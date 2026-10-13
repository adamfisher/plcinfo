using System.Globalization;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing.PlcOpen;

/// <summary>
/// Turns a PLCopen TC6 graphical body (LD, FBD, CODESYS CFC, and the FBD parts of SFC)
/// into networks with a canonical, deterministic text form.
/// </summary>
/// <remarks>
/// <para>
/// TC6 v2.01 has no rung or network element (§"Bodies"): a graphical body is a bag of objects
/// with a <c>localId</c>, wired consumer-side by <c>connectionPointIn/connection@refLocalId</c>
/// (optionally naming the producer's output pin with <c>formalParameter</c>). A network is
/// one connected group of objects. Power rails do not join groups, because one rail may feed
/// many rungs; a <c>continuation</c> joins the <c>connector</c> of the same name.
/// </para>
/// <para>
/// Networks are ordered by the position of their top-left object (y, then x), then by document
/// order. CODESYS writes every position as (0, 0), so its networks keep document order.
/// Comments, labels and CODESYS network titles are attached to the network that follows them.
/// </para>
/// <para>
/// <see cref="NetworkInfo.Text"/> has one line per output of the network, in the same order:
/// <list type="bullet">
/// <item>Ladder outputs read left to right as series elements separated by spaces. A branch that
/// joins several wires is written <c>[a , b]</c>; elements shared by every branch are moved in
/// front of the bracket, so <c>XIC(a) [XIC(b) , XIO(c)] OTE(d)</c>.</item>
/// <item>Contacts: <c>XIC</c> (normally open), <c>XIO</c> (normally closed), <c>XICP</c>/<c>XICN</c>
/// (rising/falling edge). Coils: <c>OTE</c>, <c>OTN</c> (negated), <c>OTL</c> (set), <c>OTU</c> (reset),
/// <c>OTEP</c>/<c>OTEN</c> (edge). Jumps and returns: <c>JMP(label)</c>, <c>RET</c>.</item>
/// <item>Data flow is written as assignments: <c>out := ADD(IN1 := a, IN2 := 1)</c>. A function
/// block instance, or a block whose result is used more than once, gets its own line
/// (<c>tmr(IN := XIC(a), PT := T#5s)</c>) and is referred to as <c>tmr.Q</c>; an unnamed block
/// is named <c>TYPE@localId</c> in that case. A power-rail input used as data reads <c>TRUE</c>.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class PlcOpenGraph
{
    private const int MaxRenderDepth = 256;
    private const int MaxLineLength = 100_000;

    private readonly Dictionary<ulong, Node> _nodes = new();
    private readonly List<Node> _ordered = new();
    private readonly List<Node> _annotations = new();
    private readonly Dictionary<string, Node> _connectors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ulong, int> _uses = new();
    private readonly List<string> _warnings;
    private readonly string _context;

    // Per-network rendering state.
    private readonly HashSet<ulong> _onStack = new();
    private HashSet<ulong> _recorded = new();
    private List<InstructionInfo> _instructions = new();
    private bool _truncated;

    /// <summary>
    /// Indexes the objects of a graphical body.
    /// </summary>
    /// <param name="body">The LD, FBD, CFC or SFC element.</param>
    /// <param name="context">Where the body is, for warnings (e.g. "POU 'Main'").</param>
    /// <param name="warnings">Receives recoverable problems.</param>
    public PlcOpenGraph(XElement body, string context, List<string> warnings)
    {
        _context = context;
        _warnings = warnings;

        var index = 0;
        foreach (var element in body.Elements())
        {
            var kind = element.Name.LocalName;
            if (kind is "addData" or "documentation")
                continue;

            var node = new Node(kind, element, index++, ParseId(element.Attribute("localId")?.Value));
            var position = element.Element("position");
            node.X = ParseCoordinate(position?.Attribute("x")?.Value);
            node.Y = ParseCoordinate(position?.Attribute("y")?.Value);
            ReadPins(node);

            if (node.Id is { } id)
            {
                if (!_nodes.TryAdd(id, node))
                    _warnings.Add($"{_context}: localId {id} is used by more than one element; the later one is ignored.");
            }

            if (kind is "contact" or "coil" && string.IsNullOrEmpty(node.Variable))
                _warnings.Add($"{_context}: {kind} {node.Id} has no <variable> element naming its operand.");

            if (kind is "comment" or "label" or "vendorElement")
                _annotations.Add(node);
            else
                _ordered.Add(node);

            if (kind == "connector" && !string.IsNullOrEmpty(node.Name))
                _connectors.TryAdd(node.Name, node);
        }

        foreach (var node in _nodes.Values.Where(n => n.Kind != "rightPowerRail"))
        {
            foreach (var pin in node.Inputs)
            {
                foreach (var connection in pin.Connections)
                    _uses[connection.Ref] = _uses.GetValueOrDefault(connection.Ref) + 1;
            }
        }
        foreach (var node in _nodes.Values.Where(n => n.Kind == "block"))
            _uses[node.Id!.Value] = _uses.GetValueOrDefault(node.Id!.Value) + node.OutputExpressions.Count;
    }

    /// <summary>
    /// Splits the body into networks and renders each one.
    /// </summary>
    /// <param name="language">Language name stored on each network (LD, FBD, CFC).</param>
    public IReadOnlyList<NetworkInfo> BuildNetworks(string language)
    {
        var groups = FindGroups();
        var networks = new List<NetworkInfo>();
        var pendingComments = new List<string>();
        string? pendingTitle = null, pendingLabel = null;
        var annotationIndex = 0;
        var annotations = _annotations.OrderBy(SortKey).ToList();

        foreach (var group in groups)
        {
            var first = group[0];

            // Annotations placed before this network belong to it.
            while (annotationIndex < annotations.Count && Compare(annotations[annotationIndex], first) < 0)
                Collect(annotations[annotationIndex++], pendingComments, ref pendingTitle, ref pendingLabel);

            var network = Render(group, language, networks.Count + 1);
            if (network == null)
                continue;

            ApplyAnnotations(network, pendingComments, ref pendingTitle, ref pendingLabel);
            networks.Add(network);
        }

        // Trailing annotations go to the last network.
        while (annotationIndex < annotations.Count)
            Collect(annotations[annotationIndex++], pendingComments, ref pendingTitle, ref pendingLabel);
        if (networks.Count > 0)
            ApplyAnnotations(networks[^1], pendingComments, ref pendingTitle, ref pendingLabel);

        return networks;
    }

    /// <summary>
    /// Renders the value wired to a <c>connectionPointIn</c> as data text (used for SFC transition conditions).
    /// </summary>
    public string RenderInput(XElement connectionPointIn)
    {
        var pin = ReadPin(string.Empty, connectionPointIn, negated: false, edge: null);
        ResetNetworkState();
        return DataText(RenderPin(pin, 0));
    }

    private static void Collect(Node annotation, List<string> comments, ref string? title, ref string? label)
    {
        switch (annotation.Kind)
        {
            case "comment":
                var text = PlcOpenText.Documentation(annotation.Element.Element("content"));
                if (text != null)
                    comments.Add(text);
                break;
            case "label":
                label = annotation.Element.Attribute("label")?.Value;
                break;
            case "vendorElement":
                // CODESYS writes the network title as vendorElement/addData/.../ElementType="networktitle".
                var type = annotation.Element.Descendants("ElementType").FirstOrDefault()?.Value;
                if (string.Equals(type, "networktitle", StringComparison.OrdinalIgnoreCase))
                    title = PlcOpenText.Documentation(annotation.Element.Element("alternativeText")) ?? title;
                break;
        }
    }

    private static void ApplyAnnotations(NetworkInfo network, List<string> comments, ref string? title, ref string? label)
    {
        if (comments.Count > 0)
            network.Comment = string.Join("\n", new[] { network.Comment }.Where(c => c != null).Concat(comments));
        if (title != null)
            network.Title = title;
        if (label != null)
            network.Properties["Label"] = label;
        comments.Clear();
        title = null;
        label = null;
    }

    private List<List<Node>> FindGroups()
    {
        var parent = new Dictionary<Node, Node>();
        Node Find(Node n)
        {
            while (parent.TryGetValue(n, out var p) && p != n)
            {
                if (parent.TryGetValue(p, out var gp))
                    parent[n] = gp;
                n = p;
            }
            return n;
        }
        void Union(Node a, Node b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
                parent[ra] = rb;
        }

        var members = _ordered.Where(n => !IsRail(n)).ToList();
        foreach (var node in members)
            parent[node] = node;

        foreach (var node in members)
        {
            foreach (var connection in node.Inputs.SelectMany(p => p.Connections))
            {
                if (_nodes.TryGetValue(connection.Ref, out var producer) && !IsRail(producer) && parent.ContainsKey(producer))
                    Union(node, producer);
            }
            if (node.Kind == "continuation" && !string.IsNullOrEmpty(node.Name) && _connectors.TryGetValue(node.Name, out var connector))
                Union(node, connector);
        }

        return members
            .GroupBy(Find)
            .Select(g => g.OrderBy(SortKey).ToList())
            .OrderBy(g => SortKey(g[0]))
            .ToList();
    }

    private NetworkInfo? Render(List<Node> group, string language, int number)
    {
        ResetNetworkState();
        var lines = new List<string>();

        foreach (var node in group)
        {
            foreach (var line in LinesFor(node))
            {
                if (line.Length > MaxLineLength)
                {
                    _truncated = true;
                    lines.Add(line[..MaxLineLength] + " ...");
                }
                else
                {
                    lines.Add(line);
                }
            }
        }

        if (_truncated)
            _warnings.Add($"{_context}: network {number} is too large or too deeply nested to render in full; its text is truncated.");

        if (lines.Count == 0 && _instructions.Count == 0)
            return null;

        for (var i = 0; i < _instructions.Count; i++)
            _instructions[i].Position = i;

        return new NetworkInfo
        {
            Number = number,
            Language = language,
            Text = string.Join("\n", lines),
            Instructions = _instructions
        };
    }

    private void ResetNetworkState()
    {
        _onStack.Clear();
        _recorded = new HashSet<ulong>();
        _instructions = new List<InstructionInfo>();
        _truncated = false;
    }

    private IEnumerable<string> LinesFor(Node node)
    {
        var uses = node.Id is { } id ? _uses.GetValueOrDefault(id) : 0;
        switch (node.Kind)
        {
            case "coil":
            case "contact":
                if (uses == 0)
                    yield return SeriesText(RenderNode(node, null, 0));
                break;
            case "outVariable":
                yield return $"{Negate(node.Expression ?? "?", node.Negated)} := {DataText(RenderPin(node.Input, 0))}";
                break;
            case "inOutVariable":
                if (node.Input.Connections.Count > 0 || node.Input.Expression != null)
                    yield return $"{node.Expression} := {DataText(RenderPin(node.Input, 0))}";
                break;
            case "jump":
                yield return SeriesText(RenderPin(node.Input, 0).Append($"JMP({node.Name})"));
                Record(node, "JMP", "Jump", node.Name ?? string.Empty);
                break;
            case "return":
                yield return SeriesText(RenderPin(node.Input, 0).Append("RET"));
                Record(node, "RET", "Return");
                break;
            case "connector":
                // CODESYS CFC also uses unnamed connectors as wire junctions that other objects reference directly.
                if (uses == 0 && !HasContinuation(node))
                    yield return $"CONNECTOR({node.Name}) := {DataText(RenderPin(node.Input, 0))}";
                break;
            case "block":
                if (IsMaterialized(node))
                {
                    yield return BlockCall(node, 0);
                    foreach (var (pin, expression) in node.OutputExpressions)
                        yield return $"{expression} := {BlockReference(node)}.{pin}";
                }
                else if (uses == 1 && node.OutputExpressions.Count == 1)
                {
                    var (pin, expression) = node.OutputExpressions[0];
                    yield return $"{expression} := {BlockCall(node, 0)}{PinSuffix(node, pin)}";
                }
                break;
        }
    }

    private bool HasContinuation(Node connector) =>
        !string.IsNullOrEmpty(connector.Name) &&
        _ordered.Any(n => n.Kind == "continuation" && string.Equals(n.Name, connector.Name, StringComparison.OrdinalIgnoreCase));

    private bool IsMaterialized(Node block) =>
        block.InstanceName != null || _uses.GetValueOrDefault(block.Id ?? ulong.MaxValue) != 1;

    private List<string> RenderPin(Pin pin, int depth)
    {
        List<string> result;
        if (pin.Expression != null)
        {
            result = new List<string> { pin.Expression };
        }
        else if (pin.Connections.Count == 0)
        {
            result = new List<string>();
        }
        else if (pin.Connections.Count == 1)
        {
            result = RenderReference(pin.Connections[0], depth);
        }
        else
        {
            // Several wires into one input: an OR join (parallel branches).
            var branches = pin.Connections.Select(c => RenderReference(c, depth)).ToList();
            var prefix = new List<string>();
            while (branches.All(b => b.Count > 0) && branches.All(b => b[0] == branches[0][0]))
            {
                prefix.Add(branches[0][0]);
                foreach (var branch in branches)
                    branch.RemoveAt(0);
            }
            prefix.Add("[" + string.Join(" , ", branches.Select(b => string.Join(" ", b))) + "]");
            result = prefix;
        }

        if (pin.Edge is "rising" or "falling")
            result = new List<string> { $"{(pin.Edge == "rising" ? "R_EDGE" : "F_EDGE")}({DataText(result)})" };
        if (pin.Negated)
            result = new List<string> { $"NOT({DataText(result)})" };
        return result;
    }

    private List<string> RenderReference(Connection connection, int depth)
    {
        if (!_nodes.TryGetValue(connection.Ref, out var producer))
        {
            _warnings.Add($"{_context}: a connection refers to element {connection.Ref}, which does not exist; it is shown as '?{connection.Ref}'.");
            return new List<string> { $"?{connection.Ref}" };
        }
        return RenderNode(producer, connection.Parameter, depth);
    }

    private List<string> RenderNode(Node node, string? pin, int depth)
    {
        if (depth > MaxRenderDepth)
        {
            _truncated = true;
            return new List<string> { "..." };
        }

        var id = node.Id ?? ulong.MaxValue;
        if (!_onStack.Add(id))
            return new List<string> { $"#{id}" }; // feedback loop

        try
        {
            switch (node.Kind)
            {
                case "leftPowerRail":
                    return new List<string>();
                case "contact":
                {
                    var items = RenderPin(node.Input, depth + 1);
                    var mnemonic = ContactMnemonic(node);
                    items.Add($"{mnemonic}({node.Variable})");
                    Record(node, mnemonic, "Input", node.Variable ?? string.Empty);
                    return items;
                }
                case "coil":
                {
                    var items = RenderPin(node.Input, depth + 1);
                    var mnemonic = CoilMnemonic(node);
                    items.Add($"{mnemonic}({node.Variable})");
                    Record(node, mnemonic, "Output", node.Variable ?? string.Empty);
                    return items;
                }
                case "inVariable":
                case "inOutVariable":
                    return new List<string> { Negate(node.Expression ?? "?", node.Negated) };
                case "connector":
                    return RenderPin(node.Input, depth + 1);
                case "continuation":
                    return node.Name != null && _connectors.TryGetValue(node.Name, out var connector)
                        ? RenderPin(connector.Input, depth + 1)
                        : new List<string> { node.Name ?? "?" };
                case "block":
                    if (IsMaterialized(node))
                    {
                        // Its own line renders the call; here only the result is referenced.
                        return new List<string> { $"{BlockReference(node)}.{pin.NullIfEmpty() ?? DefaultOutput(node) ?? "OUT"}" };
                    }
                    return new List<string> { BlockCall(node, depth + 1) + PinSuffix(node, pin) };
                default:
                    return new List<string> { node.Expression ?? node.Name ?? node.Kind };
            }
        }
        finally
        {
            _onStack.Remove(id);
        }
    }

    private string BlockCall(Node block, int depth)
    {
        var arguments = new List<string>();
        foreach (var pin in block.Inputs)
        {
            if (pin.Connections.Count == 0 && pin.Expression == null)
                continue;
            var text = DataText(RenderPin(pin, depth + 1));
            arguments.Add(string.IsNullOrEmpty(pin.Name) ? text : $"{pin.Name} := {text}");
        }

        var operands = new List<string>();
        if (block.InstanceName != null)
            operands.Add(block.InstanceName);
        foreach (var pin in block.Inputs)
        {
            if (pin.Expression != null)
                operands.Add(pin.Expression);
            else if (pin.Connections.Count == 1 && SourceVariable(pin.Connections[0].Ref) is { } source)
                operands.Add(source);
        }
        operands.AddRange(block.OutputExpressions.Select(o => o.Expression));
        Record(block, block.TypeName ?? "?", block.InstanceName != null ? "FunctionBlock" : "Function", operands.ToArray());

        return $"{block.InstanceName ?? block.TypeName}({string.Join(", ", arguments)})";
    }

    /// <summary>
    /// The variable or literal feeding a pin, following unnamed CFC connectors and named connector/continuation pairs.
    /// </summary>
    private string? SourceVariable(ulong id)
    {
        for (var hops = 0; hops < 16 && _nodes.TryGetValue(id, out var node); hops++)
        {
            switch (node.Kind)
            {
                case "inVariable" or "inOutVariable":
                    return node.Expression;
                case "connector" when node.Input.Connections.Count == 1:
                    id = node.Input.Connections[0].Ref;
                    break;
                case "continuation" when node.Name != null && _connectors.TryGetValue(node.Name, out var connector) && connector.Input.Connections.Count == 1:
                    id = connector.Input.Connections[0].Ref;
                    break;
                default:
                    return null;
            }
        }
        return null;
    }

    private static string BlockReference(Node block) =>
        block.InstanceName ?? $"{block.TypeName}@{block.Id}";

    private static string? DefaultOutput(Node block) =>
        block.Outputs.FirstOrDefault(o => !string.Equals(o, "ENO", StringComparison.OrdinalIgnoreCase)) ?? block.Outputs.FirstOrDefault();

    private static string PinSuffix(Node block, string? pin)
    {
        if (string.IsNullOrEmpty(pin) || string.Equals(pin, DefaultOutput(block), StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return "." + pin;
    }

    private void Record(Node node, string mnemonic, string type, params string[] operands)
    {
        if (node.Id is { } id && !_recorded.Add(id))
            return;
        _instructions.Add(new InstructionInfo
        {
            Mnemonic = mnemonic,
            Type = type,
            Operands = operands
        });
    }

    private static string ContactMnemonic(Node contact) => contact.Edge switch
    {
        "rising" => "XICP",
        "falling" => "XICN",
        _ => contact.Negated ? "XIO" : "XIC"
    };

    private static string CoilMnemonic(Node coil) => (coil.Storage, coil.Edge) switch
    {
        ("set", _) => "OTL",
        ("reset", _) => "OTU",
        (_, "rising") => "OTEP",
        (_, "falling") => "OTEN",
        _ => coil.Negated ? "OTN" : "OTE"
    };

    private static string SeriesText(IEnumerable<string> items) => string.Join(" ", items);

    private static string DataText(List<string> items) => items.Count == 0 ? "TRUE" : string.Join(" ", items);

    private static string Negate(string text, bool negated) => negated ? $"NOT({text})" : text;

    private static bool IsRail(Node node) => node.Kind is "leftPowerRail" or "rightPowerRail";

    private static (double, double, int) SortKey(Node node) => (node.Y, node.X, node.Index);

    private static int Compare(Node a, Node b) => SortKey(a).CompareTo(SortKey(b));

    private void ReadPins(Node node)
    {
        var element = node.Element;
        node.Negated = IsTrue(element.Attribute("negated")?.Value) || IsTrue(element.Attribute("negatedIn")?.Value);
        node.Edge = element.Attribute("edge")?.Value ?? element.Attribute("edgeIn")?.Value;
        node.Storage = element.Attribute("storage")?.Value ?? element.Attribute("storageIn")?.Value;
        node.Expression = element.Element("expression")?.Value.Trim();
        node.Variable = element.Element("variable")?.Value.Trim();
        node.Name = element.Attribute("name")?.Value ?? element.Attribute("label")?.Value;
        node.TypeName = element.Attribute("typeName")?.Value;
        node.InstanceName = element.Attribute("instanceName")?.Value.NullIfEmpty();

        if (node.Kind == "block")
        {
            foreach (var section in new[] { "inputVariables", "inOutVariables" })
            {
                foreach (var variable in element.Element(section)?.Elements("variable") ?? Enumerable.Empty<XElement>())
                {
                    var pinElement = variable.Element("connectionPointIn");
                    var pin = pinElement != null
                        ? ReadPin(variable.Attribute("formalParameter")?.Value ?? string.Empty, pinElement,
                            IsTrue(variable.Attribute("negated")?.Value), variable.Attribute("edge")?.Value)
                        : new Pin(variable.Attribute("formalParameter")?.Value ?? string.Empty);
                    node.Inputs.Add(pin);
                }
            }
            foreach (var variable in element.Element("outputVariables")?.Elements("variable") ?? Enumerable.Empty<XElement>())
            {
                var name = variable.Attribute("formalParameter")?.Value ?? string.Empty;
                node.Outputs.Add(name);
                // CODESYS writes an output assignment on the pin itself: <connectionPointOut><expression>x</expression>.
                var expression = variable.Element("connectionPointOut")?.Element("expression")?.Value.Trim();
                if (!string.IsNullOrEmpty(expression))
                    node.OutputExpressions.Add((name, expression));
            }
            foreach (var variable in element.Element("inOutVariables")?.Elements("variable") ?? Enumerable.Empty<XElement>())
                node.Outputs.Add(variable.Attribute("formalParameter")?.Value ?? string.Empty);
            return;
        }

        // Single-input objects; a right power rail may have several inputs.
        var inputs = element.Elements("connectionPointIn").ToList();
        if (inputs.Count > 0)
        {
            var pin = ReadPin(string.Empty, inputs[0], negated: false, edge: null);
            foreach (var extra in inputs.Skip(1))
                pin.Connections.AddRange(ReadPin(string.Empty, extra, false, null).Connections);
            node.Inputs.Add(pin);
        }
    }

    private Pin ReadPin(string name, XElement connectionPointIn, bool negated, string? edge)
    {
        var pin = new Pin(name)
        {
            Negated = negated,
            Edge = edge,
            Expression = connectionPointIn.Element("expression")?.Value.Trim()
        };
        foreach (var connection in connectionPointIn.Elements("connection"))
        {
            var id = ParseId(connection.Attribute("refLocalId")?.Value);
            if (id == null)
            {
                _warnings.Add($"{_context}: a connection has no valid refLocalId and is ignored.");
                continue;
            }
            pin.Connections.Add(new Connection(id.Value, connection.Attribute("formalParameter")?.Value));
        }
        return pin;
    }

    private static ulong? ParseId(string? value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static double ParseCoordinate(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private sealed record Connection(ulong Ref, string? Parameter);

    private sealed class Pin(string name)
    {
        public string Name { get; } = name;
        public bool Negated { get; init; }
        public string? Edge { get; init; }
        public string? Expression { get; init; }
        public List<Connection> Connections { get; } = new();
    }

    private sealed class Node(string kind, XElement element, int index, ulong? id)
    {
        public string Kind { get; } = kind;
        public XElement Element { get; } = element;
        public int Index { get; } = index;
        public ulong? Id { get; } = id;
        public double X { get; set; }
        public double Y { get; set; }
        public bool Negated { get; set; }
        public string? Edge { get; set; }
        public string? Storage { get; set; }
        public string? Expression { get; set; }
        public string? Variable { get; set; }
        public string? Name { get; set; }
        public string? TypeName { get; set; }
        public string? InstanceName { get; set; }
        public List<Pin> Inputs { get; } = new();
        public List<string> Outputs { get; } = new();
        public List<(string Pin, string Expression)> OutputExpressions { get; } = new();
        public Pin Input => Inputs.Count > 0 ? Inputs[0] : EmptyPin;
    }

    private static readonly Pin EmptyPin = new(string.Empty);
}

internal static class PlcOpenStringExtensions
{
    /// <summary>
    /// Returns null for null or empty strings.
    /// </summary>
    public static string? NullIfEmpty(this string? value) => string.IsNullOrEmpty(value) ? null : value;
}
