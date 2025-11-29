namespace PlcInfo.Parsing;

/// <summary>
/// Registry for PLC project parsers.
/// </summary>
public interface IPlcParserRegistry
{
    /// <summary>
    /// Resolves the appropriate parser for the given file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>The parser that can handle the file.</returns>
    /// <exception cref="NotSupportedException">Thrown when no parser can handle the file.</exception>
    IPlcProjectParser ResolveForFile(string filePath);

    /// <summary>
    /// Gets all registered parsers.
    /// </summary>
    /// <returns>All registered parsers.</returns>
    IEnumerable<IPlcProjectParser> GetAllParsers();
}