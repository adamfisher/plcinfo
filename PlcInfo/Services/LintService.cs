using PlcInfo.Models;

namespace PlcInfo.Services;

/// <summary>
/// Lint finding severity, most severe first.
/// </summary>
public enum LintSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// One lint finding.
/// </summary>
public record LintFinding
{
    /// <summary>Stable rule code, e.g. "PLC001".</summary>
    public string Code { get; init; } = string.Empty;

    public LintSeverity Severity { get; init; }

    /// <summary>Where the issue is, e.g. "MainProgram/MainRoutine Rung 4".</summary>
    public string Location { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// A lint rule's metadata (for documentation and `--help`).
/// </summary>
public record LintRule(string Code, LintSeverity Severity, string Title);

/// <summary>
/// Checks a project for common PLC programming problems.
/// </summary>
public class LintService
{
    /// <summary>All rules, in code order.</summary>
    public static readonly IReadOnlyList<LintRule> Rules = new[]
    {
        new LintRule("PLC001", LintSeverity.Error, "Output written by more than one OTE (duplicate destructive bit)"),
        new LintRule("PLC002", LintSeverity.Error, "JSR/FOR/SFR/SFP references a routine that does not exist"),
        new LintRule("PLC003", LintSeverity.Warning, "Routine is never called (not main, fault, or a JSR/FOR/SFR/SFP target)"),
        new LintRule("PLC004", LintSeverity.Warning, "AFI (Always False) disables a rung"),
        new LintRule("PLC005", LintSeverity.Warning, "Program-scoped tag is never used"),
        new LintRule("PLC006", LintSeverity.Info, "Controller-scoped tag is not used in logic (may be used by HMI or other controllers)"),
        new LintRule("PLC007", LintSeverity.Info, "Routine is empty"),
        new LintRule("PLC008", LintSeverity.Warning, "Task is inhibited or program is disabled"),
        new LintRule("PLC009", LintSeverity.Warning, "Routine has pending or test online edits")
    };

    // Instructions whose first operand names a routine: JSR and FOR call it,
    // SFR/SFP reset or pause an SFC routine.
    private static readonly HashSet<string> RoutineReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "JSR", "FOR", "SFR", "SFP"
    };

    // Routines that the controller runs on its own and that are legitimately empty.
    private static readonly HashSet<string> AoiReservedRoutines = new(StringComparer.OrdinalIgnoreCase)
    {
        "Logic", "Prescan", "Postscan", "EnableInFalse"
    };

    public IReadOnlyList<LintFinding> Analyze(PlcProject project)
    {
        var findings = new List<LintFinding>();
        var xref = new XrefService().Build(project);

        CheckDuplicateOte(project, findings);
        CheckRoutineCalls(project, findings);
        CheckAfi(project, findings);
        CheckUnusedTags(project, xref, findings);
        CheckEmptyRoutines(project, findings);
        CheckDisabled(project, findings);
        CheckPendingEdits(project, findings);

        return findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Location, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsAoi(ProgramInfo program) => program.Type == "AddOnInstruction";

    private static IEnumerable<(ProgramInfo Program, RoutineInfo Routine, RungInfo Rung)> Rungs(PlcProject project) =>
        from program in project.Programs
        from routine in program.Routines
        from rung in routine.Rungs
        where rung.EditState is null or "N"
        select (program, routine, rung);

    private static void CheckDuplicateOte(PlcProject project, List<LintFinding> findings)
    {
        // OTE is non-retentive: the last one scanned wins, so writing the same bit from
        // two places is almost always a bug. OTL/OTU pairs are intentional and excluded.
        foreach (var program in project.Programs)
        {
            var uses = Rungs(project)
                .Where(x => x.Program == program)
                .SelectMany(x => x.Rung.Instructions
                    .Where(i => i.Mnemonic.Equals("OTE", StringComparison.OrdinalIgnoreCase) && i.Operands.Count > 0)
                    .Select(i => (Operand: i.Operands[0].Trim(), Where: $"{x.Routine.Name} rung {x.Rung.Number}")))
                .GroupBy(u => u.Operand, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var group in uses)
            {
                findings.Add(new LintFinding
                {
                    Code = "PLC001",
                    Severity = LintSeverity.Error,
                    Location = $"{program.Name}",
                    Message = $"'{group.Key}' is written by OTE in {group.Count()} places ({string.Join(", ", group.Select(g => g.Where))}); only the last scanned one takes effect."
                });
            }
        }
    }

    private static void CheckRoutineCalls(PlcProject project, List<LintFinding> findings)
    {
        foreach (var program in project.Programs.Where(p => !IsAoi(p)))
        {
            var routineNames = program.Routines.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var called = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var routine in program.Routines)
            {
                foreach (var rung in routine.Rungs.Where(r => r.EditState is null or "N"))
                {
                    foreach (var call in rung.Instructions.Where(i => RoutineReferences.Contains(i.Mnemonic) && i.Operands.Count > 0))
                    {
                        var target = call.Operands[0].Trim();
                        called.Add(target);
                        if (!routineNames.Contains(target))
                        {
                            findings.Add(new LintFinding
                            {
                                Code = "PLC002",
                                Severity = LintSeverity.Error,
                                Location = $"{program.Name}/{routine.Name} rung {rung.Number}",
                                Message = $"{call.Mnemonic} references routine '{target}', which does not exist in program '{program.Name}'."
                            });
                        }
                    }
                }

                // ST routines call other routines with JSR(...) too.
                if (routine.Source != null)
                {
                    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                        XrefService.StripComments(routine.Source), @"\b(?:JSR|FOR|SFR|SFP)\s*\(\s*([A-Za-z_][A-Za-z0-9_]*)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        called.Add(match.Groups[1].Value);
                    }
                }
            }

            // Only programs that record their main routine (Rockwell) can be checked for reachability.
            if (!program.Properties.TryGetValue("MainRoutineName", out var main) || string.IsNullOrEmpty(main))
                continue;
            program.Properties.TryGetValue("FaultRoutineName", out var fault);

            foreach (var routine in program.Routines)
            {
                if (routine.Name.Equals(main, StringComparison.OrdinalIgnoreCase) ||
                    routine.Name.Equals(fault, StringComparison.OrdinalIgnoreCase) ||
                    called.Contains(routine.Name))
                    continue;

                findings.Add(new LintFinding
                {
                    Code = "PLC003",
                    Severity = LintSeverity.Warning,
                    Location = $"{program.Name}/{routine.Name}",
                    Message = $"Routine '{routine.Name}' is not the main or fault routine and nothing calls it (JSR/FOR), so it never runs."
                });
            }
        }
    }

    private static void CheckAfi(PlcProject project, List<LintFinding> findings)
    {
        foreach (var (program, routine, rung) in Rungs(project))
        {
            if (rung.Instructions.Any(i => i.Mnemonic.Equals("AFI", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new LintFinding
                {
                    Code = "PLC004",
                    Severity = LintSeverity.Warning,
                    Location = $"{program.Name}/{routine.Name} rung {rung.Number}",
                    Message = "AFI (Always False) makes the rung (or branch) always false; usually left over from testing."
                });
            }
        }
    }

    private static void CheckUnusedTags(PlcProject project, IReadOnlyList<XrefEntry> xref, List<LintFinding> findings)
    {
        var usedByScope = xref
            .Where(e => e.Scope != null)
            .Select(e => (e.Scope!, e.Tag))
            .ToHashSet(new ScopeTagComparer());

        // Aliases count as using their base tag.
        foreach (var alias in project.Tags.Concat(project.Programs.SelectMany(p => p.LocalTags)).Where(t => t.AliasFor != null))
        {
            var baseTag = XrefService.BaseTag(alias.AliasFor!);
            if (baseTag != null)
                usedByScope.Add(("*", baseTag));
        }

        bool Used(string scope, string tag) => usedByScope.Contains((scope, tag)) || usedByScope.Contains(("*", tag));

        foreach (var program in project.Programs.Where(p => !IsAoi(p)))
        {
            foreach (var tag in program.LocalTags)
            {
                // Program parameters (Input/Output/InOut/Public) are used from outside the program.
                if (!string.IsNullOrEmpty(tag.Usage) && !tag.Usage.Equals("Local", StringComparison.OrdinalIgnoreCase)
                    && !tag.Usage.StartsWith("VAR", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Used(program.Name, tag.Name))
                    continue;

                findings.Add(new LintFinding
                {
                    Code = "PLC005",
                    Severity = LintSeverity.Warning,
                    Location = $"{program.Name}.{tag.Name}",
                    Message = $"Tag '{tag.Name}' in '{program.Name}' is never used."
                });
            }
        }

        foreach (var tag in project.Tags.Where(t => !t.IsIoTag && !t.Name.Contains(':')))
        {
            if (Used(tag.Scope, tag.Name))
                continue;
            findings.Add(new LintFinding
            {
                Code = "PLC006",
                Severity = LintSeverity.Info,
                Location = $"{tag.Scope}.{tag.Name}",
                Message = $"Tag '{tag.Name}' is not used in logic (it may still be used by an HMI, message or another controller)."
            });
        }
    }

    private static void CheckEmptyRoutines(PlcProject project, List<LintFinding> findings)
    {
        foreach (var program in project.Programs)
        {
            foreach (var routine in program.Routines)
            {
                if (IsAoi(program) && AoiReservedRoutines.Contains(routine.Name))
                    continue;
                // FBD/SFC bodies are not decoded into the model, so they can't be judged empty.
                if (routine.Language is "FBD" or "SFC" or "CFC")
                    continue;
                if (routine.Rungs.Count > 0 || routine.Networks.Count > 0 || !string.IsNullOrWhiteSpace(routine.Source))
                    continue;

                findings.Add(new LintFinding
                {
                    Code = "PLC007",
                    Severity = LintSeverity.Info,
                    Location = $"{program.Name}/{routine.Name}",
                    Message = $"Routine '{routine.Name}' is empty."
                });
            }
        }
    }

    private static void CheckDisabled(PlcProject project, List<LintFinding> findings)
    {
        foreach (var task in project.Tasks.Where(t => !t.IsEnabled))
        {
            findings.Add(new LintFinding
            {
                Code = "PLC008",
                Severity = LintSeverity.Warning,
                Location = $"Task {task.Name}",
                Message = $"Task '{task.Name}' is inhibited, so its programs ({string.Join(", ", task.ProgramNames)}) do not run."
            });
        }

        foreach (var program in project.Programs)
        {
            if (program.Properties.TryGetValue("Disabled", out var disabled) && disabled.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new LintFinding
                {
                    Code = "PLC008",
                    Severity = LintSeverity.Warning,
                    Location = program.Name,
                    Message = $"Program '{program.Name}' is disabled."
                });
            }
        }
    }

    private static void CheckPendingEdits(PlcProject project, List<LintFinding> findings)
    {
        foreach (var program in project.Programs)
        {
            foreach (var routine in program.Routines)
            {
                var edited = routine.Rungs.Count(r => r.EditState is not (null or "N"));
                if (edited == 0)
                    continue;
                findings.Add(new LintFinding
                {
                    Code = "PLC009",
                    Severity = LintSeverity.Warning,
                    Location = $"{program.Name}/{routine.Name}",
                    Message = $"Routine '{routine.Name}' has {edited} rung(s) with pending or test online edits; accept or cancel them before release."
                });
            }
        }
    }

    private sealed class ScopeTagComparer : IEqualityComparer<(string Scope, string Tag)>
    {
        public bool Equals((string Scope, string Tag) x, (string Scope, string Tag) y) =>
            string.Equals(x.Scope, y.Scope, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Tag, y.Tag, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Scope, string Tag) obj) =>
            HashCode.Combine(obj.Scope.ToUpperInvariant(), obj.Tag.ToUpperInvariant());
    }
}
