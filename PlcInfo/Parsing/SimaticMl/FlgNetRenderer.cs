using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing.SimaticMl;

/// <summary>
/// Turns a LAD/FBD <c>FlgNet</c> graph (Parts + Wires) into deterministic text.
/// </summary>
/// <remarks>
/// Spec §3: Parts holds operands (<c>Access</c>), instructions (<c>Part</c>) and block calls
/// (<c>Call</c>), keyed by a network-local <c>UId</c>. Each <c>Wire</c> has one source (its first
/// child: <c>Powerrail</c>, <c>IdentCon</c> or an output <c>NameCon</c>) and one or more sinks.
/// Parallel branches are explicit <c>O</c> parts (in1..inN), and a wire with several sinks
/// fans one signal out. The power flow is rebuilt as a boolean expression per input pin and
/// each element that does not feed another element becomes one line:
/// <code>
/// Coil(#Run) := #Start AND NOT #Stop
/// Rs(operand := #Alarm, r := #Reset, s1 := TON&lt;Time&gt;[#T1](IN := #Fault, PT := T#5S).Q)
/// Move(en := #Go, in := 0, out1 =&gt; #Count)
/// </code>
/// UIds never appear in the text, so re-exports that renumber them do not show as changes.
/// </remarks>
internal sealed class FlgNetRenderer
{
    private static readonly Dictionary<string, string> CompareOperators = new(StringComparer.Ordinal)
    {
        ["Eq"] = "==", ["Ne"] = "<>", ["Gt"] = ">", ["Ge"] = ">=", ["Lt"] = "<", ["Le"] = "<="
    };

    private static readonly Regex PinParts = new("^(?<p>.*?)(?<n>\\d*)$", RegexOptions.CultureInvariant);

    private readonly List<XElement> _elements = new();                      // Part/Call in document order
    private readonly Dictionary<string, XElement> _byUid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _operands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, List<Source>>> _inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, List<string>>> _outputTargets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _feedsOtherParts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _parameterOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), Expr> _pinCache = new();
    private readonly HashSet<(string, string)> _evaluating = new();
    private readonly Dictionary<string, string> _boxCache = new(StringComparer.Ordinal);

    private FlgNetRenderer(XElement flgNet)
    {
        foreach (var item in flgNet.Child("Parts")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var uid = (string?)item.Attribute("UId");
            if (uid == null)
                continue;
            if (item.Name.LocalName == "Access")
            {
                _operands[uid] = SimaticMlOperand.Render(item);
            }
            else
            {
                _byUid[uid] = item;
                _elements.Add(item);
                var callInfo = item.Child("CallInfo");
                if (callInfo != null)
                    _parameterOrder[uid] = callInfo.Children("Parameter")
                        .Select(p => (string?)p.Attribute("Name") ?? string.Empty).ToList();
            }
        }

        foreach (var wire in flgNet.Child("Wires").Children("Wire"))
        {
            var ends = wire.Elements().ToList();
            if (ends.Count < 2)
                continue;

            var source = ToSource(ends[0]);
            if (source == null)
                continue;

            foreach (var sink in ends.Skip(1))
            {
                var sinkUid = (string?)sink.Attribute("UId");
                switch (sink.Name.LocalName)
                {
                    case "NameCon" when sinkUid != null:
                        var pin = (string?)sink.Attribute("Name") ?? string.Empty;
                        GetList(_inputs, sinkUid, pin).Add(source);
                        if (source.Kind == SourceKind.Pin)
                            _feedsOtherParts.Add(source.Uid!);
                        break;
                    case "IdentCon" when sinkUid != null && source.Kind == SourceKind.Pin:
                        if (_operands.TryGetValue(sinkUid, out var target))
                            GetList(_outputTargets, source.Uid!, source.Pin!).Add(target);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Renders <paramref name="flgNet"/> to canonical text and the list of instructions it contains.
    /// </summary>
    internal static (string Text, List<InstructionInfo> Instructions) Render(XElement flgNet)
    {
        var renderer = new FlgNetRenderer(flgNet);
        return (renderer.BuildText(), renderer.BuildInstructions());
    }

    private string BuildText()
    {
        var lines = new List<string>();
        foreach (var element in _elements)
        {
            var uid = (string)element.Attribute("UId")!;
            if (_feedsOtherParts.Contains(uid))
                continue;
            lines.Add(Statement(element, uid));
        }
        return string.Join("\n", lines);
    }

    private string Statement(XElement element, string uid)
    {
        var name = ElementName(element);
        if (element.Name.LocalName == "Part")
        {
            if (name.EndsWith("Coil", StringComparison.Ordinal) &&
                InputPins(uid).All(p => p is "in" or "operand"))
            {
                var power = Input(uid, element, "in")?.Render(0) ?? "TRUE";
                var operand = Input(uid, element, "operand");
                return operand == null ? $"{name} := {power}" : $"{name}({operand.Render(0)}) := {power}";
            }

            if (IsSeriesElement(element, name) || name is "O" or "A" or "X" or "Not")
                return Output(uid, element, "out").Render(0);
        }

        return BoxText(element, uid);
    }

    private List<InstructionInfo> BuildInstructions()
    {
        var result = new List<InstructionInfo>();
        for (int i = 0; i < _elements.Count; i++)
        {
            var element = _elements[i];
            var uid = (string)element.Attribute("UId")!;
            var name = ElementName(element);

            var operands = new List<string>();
            foreach (var pin in InputPins(uid))
            {
                foreach (var source in _inputs[uid][pin].Where(s => s.Kind == SourceKind.Operand))
                    operands.Add(source.Operand!);
            }
            if (_outputTargets.TryGetValue(uid, out var outputs))
            {
                foreach (var pin in SortPins(uid, outputs.Keys))
                    operands.AddRange(outputs[pin]);
            }

            var info = new InstructionInfo
            {
                Mnemonic = element.Name.LocalName == "Call" ? Unquoted(name) : name,
                Type = InstructionType(element, name),
                Operands = operands,
                Position = i,
                Properties = new Dictionary<string, string>()
            };

            var negated = element.Children("Negated").Select(n => (string?)n.Attribute("Name")).Where(n => n != null).ToList();
            if (negated.Count > 0)
                info.Properties["Negated"] = string.Join(",", negated);
            var instance = InstanceText(element);
            if (instance != null)
                info.Properties["Instance"] = instance;
            foreach (var template in TemplateValues(element))
                info.Properties[template.Name] = template.Value;
            var callInfo = element.Child("CallInfo");
            if (callInfo != null)
                info.Properties["BlockType"] = (string?)callInfo.Attribute("BlockType") ?? string.Empty;

            result.Add(info);
        }
        return result;
    }

    private static string InstructionType(XElement element, string name)
    {
        if (element.Name.LocalName == "Call")
            return "Call";
        if (name.EndsWith("Contact", StringComparison.Ordinal))
            return "Input";
        if (name.EndsWith("Coil", StringComparison.Ordinal))
            return "Output";
        if (name is "O" or "A" or "X" or "Not")
            return "Logic";
        return "Box";
    }

    // ---- expression evaluation -------------------------------------------------

    private Expr? Input(string uid, XElement element, string pin)
    {
        if (!_inputs.TryGetValue(uid, out var pins) || !pins.TryGetValue(pin, out var sources))
            return null;

        var values = sources.Select(SourceValue).ToList();
        var value = values.Count == 1 ? values[0] : Expr.Or(values);

        var negated = element.Children("Negated").Any(n => (string?)n.Attribute("Name") == pin);
        return negated ? Expr.Not(value) : value;
    }

    private Expr SourceValue(Source source) => source.Kind switch
    {
        SourceKind.Powerrail => Expr.True,
        SourceKind.Operand => Expr.Atom(source.Operand!),
        _ => _byUid.TryGetValue(source.Uid!, out var element)
            ? Output(source.Uid!, element, source.Pin!)
            : Expr.Atom("?")
    };

    private Expr Output(string uid, XElement element, string pin)
    {
        var key = (uid, pin);
        if (_pinCache.TryGetValue(key, out var cached))
            return cached;
        if (!_evaluating.Add(key))
            return Expr.Atom("<loop>");

        var result = EvaluateOutput(uid, element, pin);
        _evaluating.Remove(key);
        _pinCache[key] = result;
        return result;
    }

    private Expr EvaluateOutput(string uid, XElement element, string pin)
    {
        var name = ElementName(element);
        if (element.Name.LocalName == "Part")
        {
            switch (name)
            {
                case "O":
                    return Expr.Or(LogicInputs(uid, element));
                case "A":
                    return Expr.And(LogicInputs(uid, element));
                case "X":
                    return Expr.Xor(LogicInputs(uid, element));
                case "Not":
                    return Expr.Not(Input(uid, element, "in") ?? Expr.True);
            }

            if (name.EndsWith("Coil", StringComparison.Ordinal) && pin == "out")
                return Input(uid, element, "in") ?? Expr.True;

            if (IsSeriesElement(element, name) && pin == "out")
            {
                var power = Input(uid, element, "in") ?? Input(uid, element, "pre") ?? Expr.True;
                return Expr.And(new[] { power, SeriesTerm(element, uid, name) });
            }
        }

        return Expr.Atom($"{BoxText(element, uid)}.{pin}");
    }

    private List<Expr> LogicInputs(string uid, XElement element) =>
        InputPins(uid).Select(p => Input(uid, element, p)!).ToList();

    /// <summary>
    /// Contacts and inline comparators sit in series on the power flow: out = (in|pre) AND term.
    /// </summary>
    private bool IsSeriesElement(XElement element, string name)
    {
        if (element.Name.LocalName != "Part")
            return false;
        if (name.EndsWith("Contact", StringComparison.Ordinal))
            return true;
        var uid = (string)element.Attribute("UId")!;
        return InputPins(uid).Contains("pre");
    }

    private Expr SeriesTerm(XElement element, string uid, string name)
    {
        if (name == "Contact")
            return Input(uid, element, "operand") ?? Expr.Atom("?");

        if (CompareOperators.TryGetValue(name, out var op))
        {
            var left = Input(uid, element, "in1")?.Render(Expr.AtomPrecedence) ?? "?";
            var right = Input(uid, element, "in2")?.Render(Expr.AtomPrecedence) ?? "?";
            return Expr.Atom($"{left} {op} {right}", Expr.ComparePrecedence);
        }

        return Expr.Atom(BoxText(element, uid, excludePins: new[] { "in", "pre" }));
    }

    // ---- box text ----------------------------------------------------------------

    private string BoxText(XElement element, string uid, IReadOnlyCollection<string>? excludePins = null)
    {
        // A box output referenced from several places is rendered once.
        if (excludePins == null && _boxCache.TryGetValue(uid, out var cached))
            return cached;
        var text = BuildBoxText(element, uid, excludePins);
        if (excludePins == null)
            _boxCache[uid] = text;
        return text;
    }

    private string BuildBoxText(XElement element, string uid, IReadOnlyCollection<string>? excludePins)
    {
        var sb = new StringBuilder(ElementName(element));

        var types = TemplateValues(element).Where(t => t.Type == "Type").Select(t => t.Value).ToList();
        if (types.Count > 0)
            sb.Append('<').Append(string.Join(", ", types)).Append('>');

        var instance = InstanceText(element);
        if (instance != null)
            sb.Append('[').Append(instance).Append(']');

        var args = new List<string>();
        foreach (var pin in InputPins(uid))
        {
            if (excludePins != null && excludePins.Contains(pin))
                continue;
            var value = Input(uid, element, pin);
            if (value == null || (pin == "en" && value.IsTrue))
                continue;
            args.Add($"{pin} := {value.Render(0)}");
        }

        if (_outputTargets.TryGetValue(uid, out var outputs))
        {
            foreach (var pin in SortPins(uid, outputs.Keys))
                args.Add($"{pin} => {string.Join(", ", outputs[pin])}");
        }

        sb.Append('(').Append(string.Join(", ", args)).Append(')');
        return sb.ToString();
    }

    private IEnumerable<string> InputPins(string uid) =>
        _inputs.TryGetValue(uid, out var pins) ? SortPins(uid, pins.Keys) : Enumerable.Empty<string>();

    /// <summary>
    /// Block call pins follow the callee's parameter declaration order (from <c>CallInfo</c>);
    /// instruction pins use <see cref="PinKey"/>.
    /// </summary>
    private IEnumerable<string> SortPins(string uid, IEnumerable<string> pins)
    {
        if (!_parameterOrder.TryGetValue(uid, out var order))
            return pins.OrderBy(PinKey, PinComparer.Instance);
        return pins
            .OrderBy(p => p == "en" ? -1 : order.IndexOf(p) is var i and >= 0 ? i : int.MaxValue)
            .ThenBy(PinKey, PinComparer.Instance);
    }

    private static string ElementName(XElement element)
    {
        if (element.Name.LocalName == "Call")
        {
            var name = (string?)element.Child("CallInfo")?.Attribute("Name") ?? "?";
            return $"\"{name}\"";
        }
        return (string?)element.Attribute("Name") ?? element.Name.LocalName;
    }

    private static string Unquoted(string name) => SimaticMlXml.Unquote(name);

    private static string? InstanceText(XElement element)
    {
        var instance = element.Child("Instance") ?? element.Child("CallInfo").Child("Instance");
        if (instance == null)
            return null;
        // A single-instance DB is a global symbol; multi-instances are local statics.
        return SimaticMlOperand.Render(instance);
    }

    private static IEnumerable<(string Name, string Type, string Value)> TemplateValues(XElement element) =>
        element.Children("TemplateValue").Select(t => (
            (string?)t.Attribute("Name") ?? string.Empty,
            (string?)t.Attribute("Type") ?? string.Empty,
            t.Value.Trim()));

    private Source? ToSource(XElement end)
    {
        var uid = (string?)end.Attribute("UId");
        return end.Name.LocalName switch
        {
            "Powerrail" => new Source(SourceKind.Powerrail, null, null, null),
            "IdentCon" when uid != null => new Source(SourceKind.Operand, null, null,
                _operands.TryGetValue(uid, out var operand) ? operand : "?"),
            "NameCon" when uid != null => new Source(SourceKind.Pin, uid, (string?)end.Attribute("Name") ?? string.Empty, null),
            _ => null
        };
    }

    private static List<TValue> GetList<TValue>(Dictionary<string, Dictionary<string, List<TValue>>> map, string uid, string pin)
    {
        if (!map.TryGetValue(uid, out var pins))
            map[uid] = pins = new Dictionary<string, List<TValue>>(StringComparer.Ordinal);
        if (!pins.TryGetValue(pin, out var list))
            pins[pin] = list = new List<TValue>();
        return list;
    }

    /// <summary>
    /// Pin sort key: enable first, then by name with numeric suffixes in numeric order (in2 before in10).
    /// </summary>
    private static (int, string, int) PinKey(string pin)
    {
        if (pin == "en")
            return (0, string.Empty, 0);
        var match = PinParts.Match(pin);
        var number = match.Groups["n"].Value.Length > 0 &&
                     int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
        return (1, match.Groups["p"].Value, number);
    }

    private sealed class PinComparer : IComparer<(int, string, int)>
    {
        public static readonly PinComparer Instance = new();

        public int Compare((int, string, int) x, (int, string, int) y)
        {
            var c = x.Item1.CompareTo(y.Item1);
            if (c != 0) return c;
            c = string.CompareOrdinal(x.Item2, y.Item2);
            return c != 0 ? c : x.Item3.CompareTo(y.Item3);
        }
    }

    private enum SourceKind { Powerrail, Operand, Pin }

    private sealed record Source(SourceKind Kind, string? Uid, string? Pin, string? Operand);
}

/// <summary>
/// A small boolean expression tree used to print power flow with minimal parentheses.
/// </summary>
internal abstract class Expr
{
    internal const int OrPrecedence = 1;
    internal const int XorPrecedence = 2;
    internal const int AndPrecedence = 3;
    internal const int NotPrecedence = 4;
    internal const int ComparePrecedence = 4;
    internal const int AtomPrecedence = 5;

    internal static readonly Expr True = new AtomExpr("TRUE", AtomPrecedence);

    internal bool IsTrue => ReferenceEquals(this, True);

    protected abstract int Precedence { get; }

    protected abstract string RenderInner();

    /// <summary>
    /// Renders the expression, parenthesized when it binds looser than its context.
    /// </summary>
    internal string Render(int contextPrecedence)
    {
        var text = RenderInner();
        return Precedence < contextPrecedence ? $"({text})" : text;
    }

    internal static Expr Atom(string text, int precedence = AtomPrecedence) => new AtomExpr(text, precedence);

    internal static Expr Not(Expr operand) => operand switch
    {
        NotExpr not => not.Operand,
        _ => new NotExpr(operand)
    };

    internal static Expr And(IEnumerable<Expr> items)
    {
        var flat = new List<Expr>();
        foreach (var item in items)
        {
            if (item.IsTrue) continue;
            if (item is NaryExpr { Op: "AND" } and) flat.AddRange(and.Items);
            else flat.Add(item);
        }
        return flat.Count switch { 0 => True, 1 => flat[0], _ => new NaryExpr("AND", AndPrecedence, flat) };
    }

    internal static Expr Or(IEnumerable<Expr> items)
    {
        var flat = new List<Expr>();
        foreach (var item in items)
        {
            if (item is NaryExpr { Op: "OR" } or) flat.AddRange(or.Items);
            else flat.Add(item);
        }
        if (flat.Count == 1)
            return flat[0];
        return FactorCommonPrefix(flat) ?? new NaryExpr("OR", OrPrecedence, flat);
    }

    /// <summary>
    /// LAD parallel branches that split after a shared series section reach the OR junction
    /// as <c>a AND b AND c</c> / <c>a AND b AND d</c>, where <c>a AND b</c> is the very same
    /// sub-expression (one wire fanning out). Factor it back out so the text keeps the
    /// diagram's shape: <c>a AND b AND (c OR d)</c>.
    /// </summary>
    private static Expr? FactorCommonPrefix(List<Expr> branches)
    {
        var lists = branches.Select(b => b is NaryExpr { Op: "AND" } and ? and.Items : new List<Expr> { b }).ToList();
        var prefix = 0;
        while (lists.All(l => l.Count > prefix + 1) &&
               lists.All(l => ReferenceEquals(l[prefix], lists[0][prefix])))
            prefix++;
        if (prefix == 0)
            return null;

        var rest = lists.Select(l => And(l.Skip(prefix))).ToList();
        return And(lists[0].Take(prefix).Append(Or(rest)));
    }

    internal static Expr Xor(IEnumerable<Expr> items)
    {
        var list = items.ToList();
        return list.Count == 1 ? list[0] : new NaryExpr("XOR", XorPrecedence, list);
    }

    private sealed class AtomExpr : Expr
    {
        private readonly string _text;
        private readonly int _precedence;

        public AtomExpr(string text, int precedence)
        {
            _text = text;
            _precedence = precedence;
        }

        protected override int Precedence => _precedence;

        protected override string RenderInner() => _text;
    }

    private sealed class NotExpr : Expr
    {
        public NotExpr(Expr operand) => Operand = operand;

        public Expr Operand { get; }

        protected override int Precedence => NotPrecedence;

        protected override string RenderInner() => "NOT " + Operand.Render(AtomPrecedence);
    }

    private sealed class NaryExpr : Expr
    {
        public NaryExpr(string op, int precedence, List<Expr> items)
        {
            Op = op;
            _precedence = precedence;
            Items = items;
        }

        private readonly int _precedence;

        public string Op { get; }

        public List<Expr> Items { get; }

        protected override int Precedence => _precedence;

        // Same-operator children are flattened, so a child that binds equally loose
        // (e.g. XOR inside XOR from nested boxes) still gets parentheses.
        protected override string RenderInner() =>
            string.Join($" {Op} ", Items.Select(i => i.Render(_precedence + 1)));
    }
}
