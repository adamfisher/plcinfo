namespace PlcInfo.Parsing;

/// <summary>
/// Default implementation of the PLC parser registry.
/// </summary>
public class PlcParserRegistry : IPlcParserRegistry
{
    private readonly List<IPlcProjectParser> _parsers = new();

    /// <summary>
    /// Initializes a new instance of the PlcParserRegistry class.
    /// </summary>
    /// <param name="parsers">Collection of parsers to register.</param>
    public PlcParserRegistry(IEnumerable<IPlcProjectParser> parsers)
    {
        _parsers.AddRange(parsers);
    }

    /// <inheritdoc/>
    public IPlcProjectParser ResolveForFile(string filePath)
    {
        var parser = _parsers.FirstOrDefault(p => p.CanParse(filePath));
        if (parser == null)
        {
            throw new NotSupportedException($"No parser found for file: {filePath}");
        }
        return parser;
    }

    /// <inheritdoc/>
    public IEnumerable<IPlcProjectParser> GetAllParsers()
    {
        return _parsers.AsReadOnly();
    }
}