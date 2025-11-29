using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Interface for PLC project parsers.
/// </summary>
public interface IPlcProjectParser
{
    /// <summary>
    /// Determines if this parser can handle the specified file.
    /// </summary>
    /// <param name="filePath">Path to the file to check.</param>
    /// <returns>True if this parser can handle the file; otherwise, false.</returns>
    bool CanParse(string filePath);

    /// <summary>
    /// Parses the PLC project file asynchronously.
    /// </summary>
    /// <param name="filePath">Path to the file to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed PLC project.</returns>
    Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default);
}