using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Services;

[UnitTest]
public class ServiceRegressionTests
{
    private static PlcProject Project(IReadOnlyList<TagInfo>? tags = null, IReadOnlyList<ProgramInfo>? programs = null) => new()
    {
        Vendor = "Test",
        Format = "Test",
        FilePath = "test",
        Tags = tags ?? new List<TagInfo>(),
        Programs = programs ?? new List<ProgramInfo>()
    };

    private static TagInfo Tag(string name, string type, string scope) =>
        new() { Name = name, DataType = type, Scope = scope };

    [Fact]
    public void Diff_SameTagNameInTwoScopes_DetectsChangeInSecondScope()
    {
        var before = Project(new[] { Tag("bStart", "BOOL", "GVL_A"), Tag("bStart", "BOOL", "GVL_B") });
        var after = Project(new[] { Tag("bStart", "BOOL", "GVL_A"), Tag("bStart", "INT", "GVL_B") });

        var result = new PlcDiffService().Compare(before, after);

        result.HasDifferences.Should().BeTrue();
        result.Details.Should().ContainSingle(d => d.Type == "Modified").Which.Should().BeEquivalentTo(new
        {
            Location = "Tags.GVL_B.bStart.DataType",
            Value1 = "BOOL",
            Value2 = "INT"
        });
    }

    [Fact]
    public void Diff_SameTagNameInTwoScopes_DetectsRemovalOfOne()
    {
        var before = Project(new[] { Tag("bStart", "BOOL", "GVL_A"), Tag("bStart", "BOOL", "GVL_B") });
        var after = Project(new[] { Tag("bStart", "BOOL", "GVL_A") });

        var result = new PlcDiffService().Compare(before, after);

        result.Details.Should().ContainSingle(d => d.Type == "Removed")
            .Which.Location.Should().Be("Tags.GVL_B.bStart");
    }

    [Fact]
    public void Diff_ProgramLocalTagDataTypeChanged_DetectsModification()
    {
        ProgramInfo Program(string type) => new()
        {
            Name = "Main",
            Type = "Normal",
            LocalTags = new[] { Tag("x", type, "Main") }
        };

        var result = new PlcDiffService().Compare(
            Project(programs: new[] { Program("DINT") }),
            Project(programs: new[] { Program("REAL") }));

        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" && d.Location == "Programs.Main.Tags.x.DataType" &&
            d.Value1 == "DINT" && d.Value2 == "REAL");
    }

    [Fact]
    public void Diff_OnlyInformationalChanges_SummaryMatchesHasDifferences()
    {
        var before = Project(new[] { Tag("t", "BOOL", "Global") with { Description = "old" } });
        var after = Project(new[] { Tag("t", "BOOL", "Global") with { Description = "new" } });

        var result = new PlcDiffService().Compare(before, after);

        result.HasDifferences.Should().BeFalse();
        result.Summary.Should().StartWith("No differences found");
        result.Summary.Should().Contain("1 informational");
    }

    [Fact]
    public void Diff_LargeProjects_ScalesLinearly()
    {
        // Previously O(n^2) via First() lookups inside loops (~1.2s for 20k tags).
        var tags1 = Enumerable.Range(0, 50_000).Select(i => Tag($"T{i}", "DINT", "Global")).ToList();
        var tags2 = tags1.Select((t, i) => i % 100 == 0 ? t with { DataType = "REAL" } : t).ToList();

        var stopwatch = Stopwatch.StartNew();
        var result = new PlcDiffService().Compare(Project(tags1), Project(tags2));
        stopwatch.Stop();

        result.Details.Count(d => d.Type == "Modified").Should().Be(500);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2_000);
    }

    [Fact]
    public void Metrics_ProgramLocalTags_IncludedInTotalsAndDistribution()
    {
        var project = Project(
            new[] { Tag("g", "BOOL", "Controller") },
            new[] { new ProgramInfo { Name = "P", Type = "Normal", LocalTags = new[] { Tag("l", "DINT", "P") } } });

        var metrics = new MetricsService().CalculateMetrics(project);

        metrics.TotalTags.Should().Be(2);
        metrics.GlobalTags.Should().Be(1);
        metrics.LocalTags.Should().Be(1);
        metrics.DataTypeDistribution.Should().ContainKey("DINT");
    }

    [Fact]
    public void Metrics_LanguageDistribution_DoesNotDoubleCountProgramAndRoutines()
    {
        var program = new ProgramInfo
        {
            Name = "P",
            Type = "Normal",
            Language = "RLL",
            Routines = new[]
            {
                new RoutineInfo { Name = "A", Type = "RLL", Language = "RLL" },
                new RoutineInfo { Name = "B", Type = "RLL", Language = "RLL" }
            }
        };

        var metrics = new MetricsService().CalculateMetrics(Project(programs: new[] { program }));

        metrics.LanguageDistribution.Should().Equal(new Dictionary<string, int> { ["RLL"] = 2 });
    }

    [Fact]
    public void Metrics_ArrayWithOfInsideBounds_NormalizesToBaseType()
    {
        var project = Project(new[] { Tag("a", "ARRAY[0..MAX_OFFSET] OF INT", "Global") });

        var metrics = new MetricsService().CalculateMetrics(project);

        metrics.DataTypeDistribution.Should().ContainKey("ARRAY OF INT");
    }

    [Fact]
    public void Metrics_TurkishCulture_NormalizesDataTypesInvariantly()
    {
        var project = Project(new[] { Tag("a", "int", "Global") });

        WithCulture("tr-TR", () =>
            new MetricsService().CalculateMetrics(project).DataTypeDistribution.Should().ContainKey("INT"));
    }

    [Fact]
    public void Search_TurkishCulture_LowercaseQueryMatchesUppercaseTag()
    {
        var project = Project(new[] { Tag("INPUT_1", "BOOL", "Global") });

        WithCulture("tr-TR", () =>
            new SearchService().Search(project, "input", "tag").Should().ContainSingle());
    }

    [Fact]
    public void Search_RungTextAndInstructionBothMatch_ReportsInstructionOnce()
    {
        var rung = new RungInfo
        {
            Number = 0,
            Text = "XIC(Motor_Run)OTE(Lamp);",
            Instructions = new[]
            {
                new InstructionInfo { Mnemonic = "XIC", Operands = new[] { "Motor_Run" } },
                new InstructionInfo { Mnemonic = "OTE", Operands = new[] { "Lamp" }, Position = 1 }
            }
        };
        var project = Project(programs: new[]
        {
            new ProgramInfo
            {
                Name = "P",
                Type = "Normal",
                Routines = new[] { new RoutineInfo { Name = "R", Type = "RLL", Rungs = new[] { rung } } }
            }
        });

        var results = new SearchService().Search(project, "motor", "instruction").ToList();

        results.Should().ContainSingle().Which.Name.Should().Be("XIC");
    }

    internal static void WithCulture(string culture, Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
