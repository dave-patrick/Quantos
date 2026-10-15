using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Quantos.Core;

/// <summary>
/// A node file: YAML frontmatter + Markdown body.
///
/// Preservation rule (BUILD-PLAN.md 6, "Keeping Markdown intact"): the file is kept as raw lines,
/// each with its own line ending. Quantos only rewrites the frontmatter lines for keys it changes,
/// and only replaces the body when the body changes. Everything else stays byte-for-byte,
/// including comments, key order, unknown keys, unsupported Markdown and the UTF-8 BOM.
/// </summary>
public sealed partial class NodeDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    private readonly List<string> _frontmatter; // lines between the fences, each including its line ending
    private string _openFence;
    private string _closeFence;

    private NodeDocument(bool bom, string newline, string openFence, List<string> frontmatter, string closeFence, string body)
    {
        HasBom = bom;
        Newline = newline;
        _openFence = openFence;
        _frontmatter = frontmatter;
        _closeFence = closeFence;
        Body = body;
    }

    public bool HasBom { get; }
    public string Newline { get; }
    public bool HasFrontmatter => _openFence.Length > 0;
    public string Body { get; set; }

    // ---------- parsing ----------

    /// <summary>Parses file bytes. Throws <see cref="ValidationException"/> if the file isn't valid UTF-8 (it is then left untouched).</summary>
    public static NodeDocument Parse(byte[] bytes)
    {
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            throw new ValidationException("This file isn't UTF-8 text, so Quantos won't edit it.");
        }
        return ParseText(text, bom);
    }

    public static NodeDocument ParseText(string text, bool bom = false)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = SplitKeepingEndings(text);

        if (lines.Count > 0 && StripEnding(lines[0]) == "---")
        {
            for (var i = 1; i < lines.Count; i++)
            {
                var bare = StripEnding(lines[i]);
                if (bare is "---" or "...")
                {
                    var fm = lines.GetRange(1, i - 1);
                    var body = string.Concat(lines.Skip(i + 1));
                    return new NodeDocument(bom, newline, lines[0], fm, lines[i], body);
                }
            }
        }
        return new NodeDocument(bom, newline, "", [], "", text);
    }

    /// <summary>A new document with Quantos's standard fields, in a fixed order.</summary>
    public static NodeDocument CreateNew(NodeHeader header, string body, string newline = "\n")
    {
        var doc = new NodeDocument(false, newline, "---" + newline, [], "---" + newline, body);
        doc.SetScalar("id", header.Id);
        doc.SetScalar("title", header.Title);
        doc.SetScalar("workspace", header.Workspace);
        doc.SetRaw("supertags", "[]");
        doc.SetRaw("schema", header.Schema.ToString(System.Globalization.CultureInfo.InvariantCulture));
        doc.SetScalar("created", header.Created);
        doc.SetScalar("modified", header.Modified);
        doc.SetScalar("state", header.State);
        return doc;
    }

    // ---------- reading fields ----------

    public string FrontmatterText => string.Concat(_frontmatter);

    /// <summary>All frontmatter values as plain objects (strings, lists, maps). Throws if the YAML is invalid.</summary>
    public Dictionary<string, object?> ReadFields()
    {
        if (!HasFrontmatter || string.IsNullOrWhiteSpace(FrontmatterText)) return new(StringComparer.Ordinal);
        try
        {
            var parsed = Yaml.Deserialize<Dictionary<object, object?>>(FrontmatterText);
            return parsed?.ToDictionary(kv => kv.Key?.ToString() ?? "", kv => kv.Value, StringComparer.Ordinal)
                   ?? new(StringComparer.Ordinal);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new ValidationException($"The frontmatter isn't valid YAML (line {ex.Start.Line}). Fix it in a text editor; Quantos won't change this file until then.");
        }
    }

    public NodeHeader ReadHeader()
    {
        var f = ReadFields();
        string? S(string key) => f.TryGetValue(key, out var v) ? v as string : null;
        var supertags = f.TryGetValue("supertags", out var st) && st is List<object?> list
            ? list.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList()
            : [];
        _ = int.TryParse(S("schema"), out var schema);
        return new NodeHeader(
            Id: S("id") ?? "",
            Title: S("title") ?? "",
            Workspace: S("workspace") ?? "",
            Schema: schema,
            Created: S("created") ?? "",
            Modified: S("modified") ?? "",
            State: S("state") ?? "active",
            Supertags: supertags);
    }

    // ---------- editing ----------

    /// <summary>Sets a top-level key to a single-line scalar, quoting it if needed.</summary>
    public void SetScalar(string key, string value) => SetRaw(key, YamlScalar(value));

    /// <summary>
    /// Replaces the line(s) for a top-level key with "key: rawValue". Continuation lines that belong
    /// to the old value (indented lines or block-list items) are replaced too. Other lines are untouched.
    /// </summary>
    public void SetRaw(string key, string rawValue)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("Invalid frontmatter key.", nameof(key));
        if (!HasFrontmatter)
        {
            _openFence = "---" + Newline;
            _closeFence = "---" + Newline;
        }

        var newLine = $"{key}: {rawValue}{Newline}";
        var index = _frontmatter.FindIndex(l => IsKeyLine(l, key));
        if (index < 0)
        {
            // Make sure the previous last line is terminated before appending.
            if (_frontmatter.Count > 0 && StripEnding(_frontmatter[^1]) == _frontmatter[^1])
                _frontmatter[^1] += Newline;
            _frontmatter.Add(newLine);
            return;
        }

        var end = index + 1;
        while (end < _frontmatter.Count && IsContinuation(_frontmatter[end])) end++;
        var ending = LineEnding(_frontmatter[end - 1]);
        _frontmatter.RemoveRange(index, end - index);
        _frontmatter.Insert(index, $"{key}: {rawValue}{(ending.Length > 0 ? ending : Newline)}");
    }

    // ---------- output ----------

    public string ToText() =>
        HasFrontmatter ? _openFence + string.Concat(_frontmatter) + _closeFence + Body : Body;

    public byte[] ToBytes()
    {
        var content = StrictUtf8.GetBytes(ToText());
        if (!HasBom) return content;
        var withBom = new byte[content.Length + 3];
        withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
        content.CopyTo(withBom, 3);
        return withBom;
    }

    /// <summary>A node's version: the SHA-256 of its exact file bytes.</summary>
    public static string VersionOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // ---------- helpers ----------

    /// <summary>Plain YAML when unambiguous, otherwise a double-quoted (JSON-compatible) string.</summary>
    public static string YamlScalar(string value)
    {
        if (PlainSafe().IsMatch(value) && !LooksSpecial(value)) return value;
        return JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static bool LooksSpecial(string v) =>
        v.Length == 0 ||
        v is "~" or "-" ||
        new[] { "true", "false", "yes", "no", "on", "off", "null", "y", "n" }.Contains(v, StringComparer.OrdinalIgnoreCase) ||
        double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) && !TimestampLike().IsMatch(v);

    private static bool IsKeyLine(string line, string key)
    {
        if (!line.StartsWith(key, StringComparison.Ordinal)) return false;
        var rest = line.AsSpan(key.Length).TrimStart(' ');
        return rest.Length > 0 && rest[0] == ':';
    }

    private static bool IsContinuation(string line)
    {
        var bare = StripEnding(line);
        if (bare.Length == 0) return false;
        return bare[0] is ' ' or '\t' || bare == "-" || bare.StartsWith("- ", StringComparison.Ordinal);
    }

    private static List<string> SplitKeepingEndings(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    private static string StripEnding(string line) => line.TrimEnd('\n').TrimEnd('\r');
    private static string LineEnding(string line) => line[StripEnding(line).Length..];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]*$")] private static partial Regex KeyPattern();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _./+-]*(?<! )$")] private static partial Regex PlainSafe();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")] private static partial Regex TimestampLike();
}

/// <summary>The universal node fields Quantos reads from frontmatter (BUILD-PLAN.md 5).</summary>
public sealed record NodeHeader(
    string Id,
    string Title,
    string Workspace,
    int Schema,
    string Created,
    string Modified,
    string State,
    IReadOnlyList<string> Supertags);
