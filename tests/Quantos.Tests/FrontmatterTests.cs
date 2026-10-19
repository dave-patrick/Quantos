using Quantos.Core.Security;
using Quantos.Core.Cache;
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

    [Fact] // Views sort and filter numbers as numbers, and checkbox fields read true/false.
    public void Unquoted_numbers_and_booleans_are_typed_but_text_stays_text()
    {
        var doc = FrontmatterDocument.Parse("---\nn: 1200\nneg: -3\nf: 2.5\nb: true\nB: FALSE\nnone: ~\nzip: 007\nver: 1.10\nplus: +5\nquoted: \"42\"\nwhen: 2026-10-18\ntime: 09:30\n---\nbody\n");
        Assert.Equal(1200L, doc.Get("n"));
        Assert.Equal(-3L, doc.Get("neg"));
        Assert.Equal(2.5, doc.Get("f"));
        Assert.Equal(true, doc.Get("b"));
        Assert.Equal(false, doc.Get("B"));
        Assert.Null(doc.Get("none"));
        Assert.Equal("007", doc.Get("zip"));
        Assert.Equal("1.10", doc.Get("ver"));
        Assert.Equal("+5", doc.Get("plus"));
        Assert.Equal("42", doc.Get("quoted"));
        Assert.Equal("2026-10-18", doc.Get("when"));
        Assert.Equal("09:30", doc.Get("time"));
        Assert.Equal("true", Quantos.Core.Tasks.Props.Str(doc.Get("b")));
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
    public void Search_understands_phrases_or_exclusions_and_typos()
    {
        Assert.Equal("\"launch plan\" \"apollo\"*", CacheIndex.ToFtsQuery("\"launch plan\" apollo"));
        Assert.Equal("\"sam\"* OR \"budget\"*", CacheIndex.ToFtsQuery("sam OR budget"));
        Assert.Equal("(\"apollo\"*) NOT (\"draft\")", CacheIndex.ToFtsQuery("apollo -draft"));
        Assert.Equal("", CacheIndex.ToFtsQuery("-draft \"\" ( ) *"));
        Assert.Equal("\"lau\"*", CacheIndex.ToFtsQuery("lauch", loose: true));

        using var v = new TestVault();
        var plan = v.Writes.Create(new CreateNodeRequest { Title = "Launch plan", Body = "Draft for the board.\n" }, Actor.User);
        var budget = v.Writes.Create(new CreateNodeRequest { Title = "Budget", Body = "Numbers for the launch.\n" }, Actor.User);
        Assert.Equal([plan.Id], v.Session.Cache.Search("\"launch plan\"").Select(h => h.Id));
        Assert.Equal(2, v.Session.Cache.Search("board OR numbers").Count);
        Assert.Equal([budget.Id], v.Session.Cache.Search("launch -draft").Select(h => h.Id));
        Assert.Contains(v.Session.Cache.Search("lauch"), h => h.Id == plan.Id); // typo still finds it
        Assert.Empty(v.Session.Cache.Search("zzzzzz"));
    }
    [Fact]
    public void Plain_text_shows_link_titles_without_markers()
    {
        var body = "![Cover](attachments/0c/abc.png)Met [[01JABCDEF0123456789XYZABCD|Sam]] about [[Budget]] ([brief](attachments/b.pdf)).\n- [ ] Call back <!--task:01JABCDEF0123456789XYZABCE-->\n<!--sync:01JABCDEF0123456789XYZABCF-->\nShared line ^b1\n<!--/sync-->";
        Assert.Equal("CoverMet Sam about Budget (brief).\nCall back \n\nShared line\n", Links.PlainText(body));
        Assert.Equal("Agenda\nScope for Apollo\nNote\nquoted\nFirst\ncode line\n",
            Links.PlainText("## Agenda\n- Scope for **Apollo**\n> [!note]+ Note\n> > quoted\n1. First\n```js\ncode line\n```\n").Replace("\n\n", "\n"));
    }

    [Fact] // A long note with many links must not take quadratic time to scan (it once took ~30 s to save)
    public void Finding_links_in_a_long_note_is_fast()
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < 20_000; i++) sb.Append(i % 50 == 0 ? "```\n[[NotALink]]\n```\n" : $"- item {i} [[01JABCDEF0123456789XYZABCD|Two]]\n");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var links = Links.Find(sb.ToString()).ToList();
        Assert.True(sw.ElapsedMilliseconds < 1500, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(19_600, links.Count);
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
