using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Quantos.Core.Markdown;

/// <summary>
/// A Markdown file split into its YAML frontmatter and body. Edits replace only the lines of the
/// keys that change, so unknown keys, comments, ordering and formatting survive untouched
/// (STORAGE-SPEC section 2). Unchanged documents round-trip byte for byte.
/// </summary>
public sealed partial class FrontmatterDocument
{
    private readonly string _original;
    private readonly List<Entry> _entries = [];
    private string _closing = "---";
    private string _afterClosing = "";
    private bool _dirty;

    private FrontmatterDocument(string original) => _original = original;

    public bool HasFrontmatter { get; private set; }
    public string Bom { get; private set; } = "";
    public string Newline { get; private set; } = "\n";
    public string Body { get; private set; } = "";
    public bool BodyChanged { get; private set; }

    /// <summary>Set when the YAML block can't be parsed; the visual editor must fall back to raw source.</summary>
    public string? YamlError { get; private set; }

    public static FrontmatterDocument Parse(string text)
    {
        var doc = new FrontmatterDocument(text);
        var rest = text;
        if (rest.StartsWith('﻿')) { doc.Bom = "﻿"; rest = rest[1..]; }
        doc.Newline = rest.Contains("\r\n") ? "\r\n" : "\n";

        var firstEnd = rest.IndexOf('\n');
        var firstLine = firstEnd < 0 ? rest : rest[..firstEnd];
        if (firstEnd < 0 || firstLine.TrimEnd('\r') != "---")
        {
            doc.Body = rest;
            return doc;
        }

        // Find the closing delimiter.
        var pos = firstEnd + 1;
        var lines = new List<string>();
        while (pos <= rest.Length)
        {
            var end = rest.IndexOf('\n', pos);
            var line = end < 0 ? rest[pos..] : rest[pos..end];
            var trimmed = line.TrimEnd('\r');
            if (trimmed is "---" or "...")
            {
                doc.HasFrontmatter = true;
                doc._closing = line;
                doc._afterClosing = end < 0 ? "" : "\n";
                doc.Body = end < 0 ? "" : rest[(end + 1)..];
                foreach (var l in lines) doc.AddLine(l);
                doc.Validate();
                return doc;
            }
            lines.Add(line);
            if (end < 0) break;
            pos = end + 1;
        }

        // No closing delimiter: not frontmatter.
        doc.Body = rest;
        return doc;
    }

    public static FrontmatterDocument Create(string newline = "\n")
    {
        var doc = new FrontmatterDocument("") { HasFrontmatter = true, Newline = newline, _afterClosing = "\n", _dirty = true };
        doc._closing = "---" + (newline == "\r\n" ? "\r" : "");
        return doc;
    }

    private void AddLine(string line)
    {
        var match = KeyLine().Match(line);
        if (match.Success)
            _entries.Add(new Entry(match.Groups[1].Value, [line]));
        else if (_entries.Count > 0 && _entries[^1].Key is not null && IsContinuation(line))
            _entries[^1].Lines.Add(line);
        else
            _entries.Add(new Entry(null, [line]));
    }

    private static bool IsContinuation(string line) =>
        line.Length > 0 && (line[0] == ' ' || line[0] == '\t' || line.StartsWith("- ") || line.TrimEnd('\r') == "-");

    private void Validate()
    {
        try { ParseYaml(string.Join("\n", _entries.SelectMany(e => e.Lines).Select(l => l.TrimEnd('\r')))); YamlError = null; }
        catch (Exception ex) { YamlError = ex.Message; }
    }

    public IEnumerable<string> Keys => _entries.Where(e => e.Key is not null).Select(e => e.Key!);

    public bool ContainsKey(string key) => _entries.Any(e => e.Key == key);

    /// <summary>All values, parsed. Scalars are strings; sequences are lists; mappings are dictionaries.</summary>
    public Dictionary<string, object?> Values()
    {
        if (!HasFrontmatter || YamlError is not null) return new();
        var text = string.Join("\n", _entries.SelectMany(e => e.Lines).Select(l => l.TrimEnd('\r')));
        return ParseYaml(text);
    }

    public object? Get(string key)
    {
        var entry = _entries.FirstOrDefault(e => e.Key == key);
        if (entry is null) return null;
        try
        {
            var parsed = ParseYaml(string.Join("\n", entry.Lines.Select(l => l.TrimEnd('\r'))));
            return parsed.TryGetValue(key, out var value) ? value : null;
        }
        catch { return null; }
    }

    public string? GetString(string key) => Get(key) switch
    {
        null => null,
        string s => s,
        var other => Convert.ToString(other, CultureInfo.InvariantCulture)
    };

    public List<string> GetStringList(string key) => Get(key) switch
    {
        null => [],
        string s when s.Length == 0 => [],
        string s => [s],
        IEnumerable list => list.Cast<object?>().Where(o => o is not null).Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)!).ToList(),
        var other => [Convert.ToString(other, CultureInfo.InvariantCulture)!]
    };

    public void Set(string key, object? value)
    {
        if (!HasFrontmatter) { HasFrontmatter = true; _closing = "---" + Cr; _afterClosing = "\n"; }
        var lines = Serialize(key, value).Select(l => l + Cr).ToList();
        var entry = _entries.FirstOrDefault(e => e.Key == key);
        if (entry is not null)
        {
            if (entry.Lines.SequenceEqual(lines)) return;
            entry.Lines.Clear();
            entry.Lines.AddRange(lines);
        }
        else
        {
            // Append after the last key, before any trailing blank lines.
            var insertAt = _entries.Count;
            while (insertAt > 0 && _entries[insertAt - 1].Key is null && _entries[insertAt - 1].Lines.All(l => l.Trim().Length == 0))
                insertAt--;
            _entries.Insert(insertAt, new Entry(key, lines));
        }
        _dirty = true;
    }

    public void Remove(string key)
    {
        if (_entries.RemoveAll(e => e.Key == key) > 0) _dirty = true;
    }

    public void SetBody(string body)
    {
        if (body == Body) return;
        Body = body;
        BodyChanged = true;
    }

    private string Cr => Newline == "\r\n" ? "\r" : "";

    public override string ToString()
    {
        if (!_dirty && !BodyChanged) return _original;
        if (!HasFrontmatter) return Bom + Body;
        var sb = new StringBuilder();
        sb.Append(Bom).Append("---").Append(Newline);
        foreach (var line in _entries.SelectMany(e => e.Lines)) sb.Append(line).Append('\n');
        sb.Append(_closing).Append(_afterClosing).Append(Body);
        return sb.ToString();
    }

    // ---- YAML helpers -------------------------------------------------------------------------

    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    private static Dictionary<string, object?> ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return new();
        var result = Deserializer.Deserialize<object?>(yaml);
        if (result is null) return new();
        if (result is not IDictionary<object, object?> map) throw new FormatException("Frontmatter is not a mapping.");
        return map.ToDictionary(kv => Convert.ToString(kv.Key, CultureInfo.InvariantCulture)!, kv => Normalize(kv.Value));
    }

    private static object? Normalize(object? value) => value switch
    {
        IDictionary<object, object?> map => map.ToDictionary(kv => Convert.ToString(kv.Key, CultureInfo.InvariantCulture)!, kv => Normalize(kv.Value)),
        IList<object?> list => list.Select(Normalize).ToList(),
        _ => value
    };

    private static IEnumerable<string> Serialize(string key, object? value)
    {
        switch (value)
        {
            case null:
                yield return key + ":";
                yield break;
            case string or bool or int or long or double or decimal or DateTimeOffset or DateOnly or DateTime:
                yield return key + ": " + Scalar(value);
                yield break;
            case IDictionary dict:
                yield return key + ":";
                foreach (var line in Block(dict, "  ")) yield return line;
                yield break;
            case IEnumerable seq:
                var items = seq.Cast<object?>().ToList();
                if (items.All(IsScalar))
                {
                    yield return key + ": [" + string.Join(", ", items.Select(i => Scalar(i, flow: true))) + "]";
                    yield break;
                }
                yield return key + ":";
                foreach (var item in items)
                {
                    if (item is IDictionary d)
                    {
                        var first = true;
                        foreach (var line in Block(d, "    "))
                        {
                            yield return first ? "  - " + line.TrimStart() : line;
                            first = false;
                        }
                    }
                    else yield return "  - " + Scalar(item);
                }
                yield break;
            default:
                yield return key + ": " + Scalar(Convert.ToString(value, CultureInfo.InvariantCulture));
                yield break;
        }
    }

    private static IEnumerable<string> Block(IDictionary dict, string indent)
    {
        foreach (DictionaryEntry kv in dict)
        {
            if (kv.Value is null) continue;
            foreach (var line in Serialize(Convert.ToString(kv.Key, CultureInfo.InvariantCulture)!, kv.Value))
                yield return indent + line;
        }
    }

    private static bool IsScalar(object? o) => o is null or string or bool or int or long or double or decimal or DateTimeOffset or DateOnly or DateTime;

    public static string Scalar(object? value, bool flow = false) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        int or long or double or decimal => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        DateTimeOffset d => d.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s => QuoteIfNeeded(s, flow),
        _ => QuoteIfNeeded(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", flow)
    };

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        { "true", "false", "yes", "no", "on", "off", "null", "~", "y", "n" };

    private static string QuoteIfNeeded(string s, bool flow)
    {
        var plain = s.Length > 0 && s.Trim() == s
                    && PlainSafe().IsMatch(s)
                    && !Reserved.Contains(s)
                    && !s.Contains(": ") && !s.Contains(" #") && !s.EndsWith(':')
                    && !(flow && (s.Contains(',') || s.Contains('[') || s.Contains(']') || s.Contains('{') || s.Contains('}')))
                    && !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (plain) return s;
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => $"\\u{(int)c:x4}",
                _ => c.ToString()
            });
        }
        return sb.Append('"').ToString();
    }

    [GeneratedRegex(@"^([A-Za-z_][\w\-\.]*)\s*:(\s|$)")]
    private static partial Regex KeyLine();

    // Starts with a letter/digit/_ and contains no YAML indicators that would change meaning.
    [GeneratedRegex(@"^[\p{L}\p{N}_\(][^\n\r\t""'`{}\[\]&*!|>%@]*$")]
    private static partial Regex PlainSafe();

    private sealed record Entry(string? Key, List<string> Lines);
}
