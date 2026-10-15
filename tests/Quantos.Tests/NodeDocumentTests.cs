using System.Text;
using Quantos.Core;

namespace Quantos.Tests;

/// <summary>Acceptance test "Editing and sync 6": unknown YAML and unsupported Markdown are preserved.</summary>
public class NodeDocumentTests
{
    private const string Original =
        "---\n" +
        "id: 0192f0c4-0000-7000-8000-000000000001\n" +
        "title: Plain title   # a comment the user wrote\n" +
        "custom_field:\n" +
        "  nested: [1, 2, 3]\n" +
        "aliases:\n" +
        "- one\n" +
        "- two\n" +
        "modified: 2026-01-01T00:00:00-07:00\n" +
        "---\n" +
        "# Heading\n\n" +
        "%% obsidian comment %%\n" +
        "::: custom-block\nweird syntax {{ here }}\n:::\n";

    [Fact]
    public void Unchanged_document_round_trips_byte_for_byte()
    {
        var bytes = Encoding.UTF8.GetBytes(Original);
        Assert.Equal(bytes, NodeDocument.Parse(bytes).ToBytes());
    }

    [Fact]
    public void Crlf_and_bom_are_preserved()
    {
        var crlf = Original.Replace("\n", "\r\n");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(crlf)).ToArray();
        var doc = NodeDocument.Parse(bytes);
        Assert.Equal(bytes, doc.ToBytes());

        doc.SetScalar("modified", "2026-10-15T09:00:00-07:00");
        var text = Encoding.UTF8.GetString(doc.ToBytes()[3..]);
        Assert.Equal(0xEF, doc.ToBytes()[0]);
        Assert.Contains("modified: \"2026-10-15T09:00:00-07:00\"\r\n", text);
        Assert.DoesNotContain("\n\n---", text.Replace("\r\n", "\n").Split("---")[1]);
    }

    [Fact]
    public void Setting_one_key_leaves_every_other_line_untouched()
    {
        var doc = NodeDocument.ParseText(Original);
        doc.SetScalar("modified", "2026-10-15T09:00:00-07:00");
        var expected = Original.Replace("modified: 2026-01-01T00:00:00-07:00", "modified: \"2026-10-15T09:00:00-07:00\"");
        Assert.Equal(expected, doc.ToText());
    }

    [Fact]
    public void Replacing_a_key_with_continuation_lines_replaces_the_whole_value_only()
    {
        var doc = NodeDocument.ParseText(Original);
        doc.SetRaw("aliases", "[three]");
        var text = doc.ToText();
        Assert.Contains("aliases: [three]\nmodified:", text);
        Assert.DoesNotContain("- one", text);
        Assert.Contains("custom_field:\n  nested: [1, 2, 3]\n", text);
    }

    [Fact]
    public void New_keys_are_appended_and_unknown_keys_survive_reading()
    {
        var doc = NodeDocument.ParseText(Original);
        doc.SetScalar("state", "active");
        var fields = doc.ReadFields();
        Assert.Equal("active", fields["state"]);
        Assert.True(fields.ContainsKey("custom_field"));
        Assert.EndsWith("state: active\n---\n# Heading\n\n%% obsidian comment %%\n::: custom-block\nweird syntax {{ here }}\n:::\n", doc.ToText());
    }

    [Theory]
    [InlineData("Simple title", "Simple title")]
    [InlineData("Meeting: notes", "\"Meeting: notes\"")]
    [InlineData("yes", "\"yes\"")]
    [InlineData("123", "\"123\"")]
    [InlineData("Quote \" inside", "\"Quote \\\" inside\"")]
    [InlineData("#hashtag", "\"#hashtag\"")]
    public void Scalars_are_quoted_only_when_needed(string value, string expected)
    {
        var yaml = NodeDocument.YamlScalar(value);
        Assert.Equal(expected, yaml);
        var doc = NodeDocument.ParseText("---\n---\n");
        doc.SetRaw("title", yaml);
        Assert.Equal(value, doc.ReadFields()["title"]);
    }

    [Fact]
    public void Invalid_utf8_is_refused_instead_of_corrupted()
    {
        Assert.Throws<ValidationException>(() => NodeDocument.Parse([0x2D, 0x2D, 0x2D, 0x0A, 0xFF, 0xFE]));
    }

    [Fact]
    public void Invalid_yaml_is_reported_not_rewritten()
    {
        var doc = NodeDocument.ParseText("---\ntitle: [unclosed\n---\nbody\n");
        Assert.Throws<ValidationException>(() => doc.ReadHeader());
    }

    [Fact]
    public void File_without_frontmatter_keeps_its_body()
    {
        var doc = NodeDocument.ParseText("just text\n");
        Assert.False(doc.HasFrontmatter);
        doc.SetScalar("id", "x");
        Assert.Equal("---\nid: x\n---\njust text\n", doc.ToText());
    }
}
