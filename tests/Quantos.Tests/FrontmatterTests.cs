using Quantos.Core;
using Quantos.Core.Markdown;

namespace Quantos.Tests;

public class FrontmatterTests
{
    private const string Sample = "---\r\nid: 01JABCDEF0123456789XYZABCD\r\ntitle: Plan\r\n# a comment kept as is\r\ncustom_thing:   { nested: [1, 2] }   \r\nlist:\r\n  - a\r\n  - b\r\n---\r\nBody with [[Link]] and ::unsupported:: syntax\r\n";

    [Fact]
    public void Unchanged_document_round_trips_byte_for_byte()
    {
        Assert.Equal(Sample, FrontmatterDocument.Parse(Sample).ToString());
        Assert.Equal("no frontmatter\n", FrontmatterDocument.Parse("no frontmatter\n").ToString());
        Assert.Equal("\uFEFF---\nid: x\n---\n", FrontmatterDocument.Parse("\uFEFF---\nid: x\n---\n").ToString());
    }

    [Fact]
    public void Setting_one_key_leaves_unknown_yaml_and_comments_untouched()
    {
        var doc = FrontmatterDocument.Parse(Sample);
        doc.Set("title", "New plan");
        doc.Set("status", "active");
        var text = doc.ToString();
        Assert.Contains("title: New plan\r\n", text);
        Assert.Contains("# a comment kept as is\r\n", text);
        Assert.Contains("custom_thing:   { nested: [1, 2] }   \r\n", text);
        Assert.Contains("list:\r\n  - a\r\n  - b\r\nstatus: active\r\n---\r\n", text);
        Assert.EndsWith("Body with [[Link]] and ::unsupported:: syntax\r\n", text);
    }

    [Fact]
    public void Values_parse_and_quote_safely()
    {
        var doc = FrontmatterDocument.Create();
        doc.Set("a", "yes");
        doc.Set("b", "has: colon");
        doc.Set("c", new List<string> { "x", "y,z" });
        doc.Set("d", 3);
        doc.Set("e", "  padded ");
        var parsed = FrontmatterDocument.Parse(doc.ToString());
        Assert.Equal("yes", parsed.GetString("a"));
        Assert.Equal("has: colon", parsed.GetString("b"));
        Assert.Equal(["x", "y,z"], parsed.GetStringList("c"));
        Assert.Equal("3", parsed.GetString("d"));
        Assert.Equal("  padded ", parsed.GetString("e"));
    }

    [Fact]
    public void Invalid_yaml_is_reported_not_thrown()
    {
        var doc = FrontmatterDocument.Parse("---\ntitle: [unclosed\n---\nbody");
        Assert.NotNull(doc.YamlError);
        Assert.True(Node.FromText("x.md", doc.ToString()).ReadOnly);
    }

    [Fact]
    public void Links_are_found_outside_code_only()
    {
        var body = "See [[One]] and [[01JABCDEF0123456789XYZABCD|Two]].\n```\n[[NotThis]]\n```\n`[[NorThis]]` ![[Embed#^b1]]";
        var links = Links.Find(body).ToList();
        Assert.Equal(["One", "01JABCDEF0123456789XYZABCD", "Embed"], links.Select(l => l.Target));
        Assert.True(links[1].IsResolved);
        Assert.Equal(LinkKind.Embed, links[2].Kind);
        Assert.Equal("^b1", links[2].Fragment);
    }

    [Fact]
    public void Plain_text_shows_link_titles_without_markers()
    {
        var body = "![Cover](attachments/0c/abc.png)Met [[01JABCDEF0123456789XYZABCD|Sam]] about [[Budget]] ([brief](attachments/b.pdf)).\n- [ ] Call back <!--task:01JABCDEF0123456789XYZABCE-->\n<!--sync:01JABCDEF0123456789XYZABCF-->\nShared line ^b1\n<!--/sync-->";
        Assert.Equal("CoverMet Sam about Budget (brief).\n- [ ] Call back \n\nShared line\n", Links.PlainText(body));
    }

    [Fact]
    public void Ulids_are_valid_and_increase()
    {
        var a = Ulid.New();
        var b = Ulid.New();
        Assert.True(Ulid.IsValid(a));
        Assert.True(string.CompareOrdinal(a, b) < 0);
    }
}
