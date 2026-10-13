using System.Text.RegularExpressions;
using System.Xml;
using PlcInfo.Models;
using PlcInfo.Parsing.SimaticMl;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Siemens TIA Portal Openness exports (SimaticML XML): a single exported block,
/// PLC data type or tag table, or a folder of them (e.g. an Openness export tree or a
/// Version Control Interface workspace).
/// </summary>
/// <remarks>
/// TIA Portal project files (.apXX) and project archives (.zapXX, a zip of the project
/// folder) use a proprietary, undocumented format and contain no SimaticML (spec §6), so they
/// are recognized only to explain how to export something readable.
/// </remarks>
public class SiemensTiaPortalParser : IPlcProjectParser
{
    private const int MaxDirectoryFiles = 50_000;

    private static readonly Regex ProjectExtension = new(
        @"^\.(?<zip>z)?ap(?<major>\d{2})(?:_(?<minor>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly EnumerationOptions DirectoryScan = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Do not follow symlinks/junctions out of the export folder; skip .git and similar.
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        MatchCasing = MatchCasing.CaseInsensitive
    };

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        if (ProjectExtension.IsMatch(Path.GetExtension(filePath)))
            return true;

        if (Directory.Exists(filePath))
            return FindExports(filePath).Any();

        return Path.GetExtension(filePath).Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
               File.Exists(filePath) &&
               SimaticMlXml.IsSimaticMlFile(filePath);
    }

    /// <inheritdoc/>
    public async Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var projectFile = ProjectExtension.Match(Path.GetExtension(filePath));
        if (projectFile.Success)
            throw new NotSupportedException(DescribeProjectFile(filePath, projectFile));

        if (Directory.Exists(filePath))
            return await ParseDirectoryAsync(filePath, cancellationToken);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        if (!SimaticMlXml.IsSimaticMlFile(filePath))
            throw new InvalidOperationException(
                $"{Path.GetFileName(filePath)} is not a TIA Portal Openness (SimaticML) export: expected a <Document> root holding an SW.* object.");

        var builder = new SimaticMlProjectBuilder();
        try
        {
            var document = await SimaticMlXml.LoadAsync(filePath, cancellationToken);
            builder.Add(document, source: null, new FileInfo(filePath).Length);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"Invalid SimaticML file: {ex.Message}", ex);
        }

        return builder.Build(filePath, File.GetLastWriteTime(filePath));
    }

    private static async Task<PlcProject> ParseDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        var builder = new SimaticMlProjectBuilder();
        DateTime? lastModified = null;

        foreach (var file in FindExports(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            try
            {
                var document = await SimaticMlXml.LoadAsync(file, cancellationToken);
                builder.Add(document, relative, new FileInfo(file).Length);
                var modified = File.GetLastWriteTime(file);
                if (lastModified == null || modified > lastModified)
                    lastModified = modified;
            }
            catch (XmlException ex)
            {
                builder.Warn(relative, $"The file is not well-formed XML and was skipped ({ex.Message}).");
            }
            catch (IOException ex)
            {
                builder.Warn(relative, $"The file could not be read and was skipped ({ex.Message}).");
            }
        }

        if (builder.DocumentCount == 0)
            throw new InvalidOperationException(
                $"No TIA Portal Openness (SimaticML) exports were found in {directory}. " +
                "Export blocks, PLC data types and tag tables as XML with TIA Portal Openness or the Version Control Interface, then point plcinfo at that folder.");

        return builder.Build(directory, lastModified);
    }

    /// <summary>
    /// SimaticML files under <paramref name="directory"/>, in ordinal path order so the
    /// project (and any diff of it) does not depend on file-system enumeration order.
    /// </summary>
    private static IEnumerable<string> FindExports(string directory)
    {
        List<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directory, "*.xml", DirectoryScan)
                .Take(MaxDirectoryFiles)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<string>();
        }

        return candidates.Where(SimaticMlXml.IsSimaticMlFile);
    }

    private static string DescribeProjectFile(string filePath, Match extension)
    {
        var version = "V" + extension.Groups["major"].Value +
                      (extension.Groups["minor"].Success ? "." + extension.Groups["minor"].Value : string.Empty);
        var what = extension.Groups["zip"].Success
            ? $"a TIA Portal {version} project archive (a zip of the proprietary project folder)"
            : $"a TIA Portal {version} project file";

        return $"{Path.GetFileName(filePath)} is {what}. TIA Portal project data is stored in an " +
               "undocumented, proprietary format that plcinfo cannot read. Export the PLC software as " +
               "SimaticML XML instead (TIA Portal Openness export of blocks, PLC data types and tag tables, " +
               "or a Version Control Interface workspace) and run plcinfo on the exported .xml file or folder. " +
               "For SCL/STL blocks, \"Generate source from blocks\" .scl/.awl files are also supported.";
    }
}
