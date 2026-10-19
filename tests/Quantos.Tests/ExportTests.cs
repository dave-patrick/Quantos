using System.IO.Compression;
using Quantos.Core;
using Quantos.Core.Export;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>Stage 7: export, attachments and the attachment auditor.</summary>
public class ExportTests
{
    [Fact] // 8D.14: query results frozen as static content
    public void Exports_freeze_queries_inline_embeds_and_use_readable_links()
    {
        using var v = new TestVault();
        var a = v.Writes.Create(new CreateNodeRequest { Title = "Alpha", Body = "Alpha body ^b1\n", Properties = new Dictionary<string, object?> { ["status"] = "open" } }, Actor.User);
        var report = v.Writes.Create(new CreateNodeRequest
        {
            Title = "Report",
            Body = $"See [[Alpha]].\n\n![[{a.Id}#^b1]]\n\n```query\nwhere: status = open\ncolumns: [title, status]\n```\n- [ ] Follow up\n"
        }, Actor.User);
        var md = v.Session.Exports.Prepare(v.Writes.Require(report.Id), freeze: true, depth: 0);
        Assert.Contains("See Alpha.", md);
        Assert.Contains("> **Alpha**", md);
        Assert.Contains("| title | status |", md);
        Assert.Contains("| Alpha | open |", md);
        Assert.DoesNotContain("<!--task:", md);
        Assert.DoesNotContain("```query", md);

        var html = v.Session.Exports.ExportNode(report.Id, ExportFormat.Html, Actor.User);
        var text = File.ReadAllText(v.Abs(html.Path));
        Assert.Contains("<table>", text);
        Assert.StartsWith("exports/report-", html.Path);
        // Exporting never changes the note.
        Assert.Equal(report.Hash, v.Writes.Require(report.Id).Hash);
    }

    [Fact]
    public void Folder_export_zips_every_note()
    {
        using var v = new TestVault();
        v.Writes.Create(new CreateNodeRequest { Title = "One" }, Actor.User);
        v.Writes.Create(new CreateNodeRequest { Title = "Two" }, Actor.User);
        var result = v.Session.Exports.ExportFolder("workspaces/default/nodes", ExportFormat.Markdown, Actor.User);
        using var zip = ZipFile.OpenRead(v.Abs(result.Path));
        Assert.Equal(["one.md", "two.md"], zip.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public void Exports_keep_properties_and_images()
    {
        using var v = new TestVault();
        var (image, _) = v.Session.Attachments.Add(new MemoryStream([1, 2, 3]), "chart.png", Actor.User);
        var sam = v.Writes.Create(new CreateNodeRequest { Title = "Sam" }, Actor.User);
        var note = v.Writes.Create(new CreateNodeRequest
        {
            Title = "Plan", Body = $"![chart]({image})\n",
            Properties = new Dictionary<string, object?> { ["status"] = "Active", ["owner"] = $"[[{sam.Id}|Sam]]", ["series"] = "internal" }
        }, Actor.User);

        var single = File.ReadAllText(v.Abs(v.Session.Exports.ExportNode(note.Id, ExportFormat.Markdown, Actor.User).Path));
        Assert.Contains("- **Status:** Active", single);
        Assert.Contains("- **Owner:** Sam", single);
        Assert.DoesNotContain("Series", single);
        Assert.Contains($"](../{image})", single);   // exports/ sits beside attachments/

        var result = v.Session.Exports.ExportFolder("workspaces/default", ExportFormat.Markdown, Actor.User);
        using var zip = ZipFile.OpenRead(v.Abs(result.Path));
        Assert.NotNull(zip.GetEntry(image));
        using var reader = new StreamReader(zip.GetEntry("nodes/plan.md")!.Open());
        Assert.Contains($"](../{image})", reader.ReadToEnd());
    }
    [Fact] // 8D.6 and 8D.9
    public void Attachments_are_stored_once_and_orphans_go_to_the_trash()
    {
        using var v = new TestVault();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var (first, existed1) = v.Session.Attachments.Add(new MemoryStream(bytes), "photo.png", Actor.User);
        var (second, existed2) = v.Session.Attachments.Add(new MemoryStream(bytes), "same-photo.png", Actor.User);
        Assert.Equal(first, second);
        Assert.False(existed1);
        Assert.True(existed2);
        var (other, _) = v.Session.Attachments.Add(new MemoryStream([9, 9]), "orphan.pdf", Actor.User);

        v.Writes.Create(new CreateNodeRequest { Title = "Uses it", Body = $"![photo]({first})\n" }, Actor.User);
        var audit = v.Session.Attachments.Audit();
        Assert.Equal(1, audit.Single(x => x.Path == first).References);
        Assert.Equal(0, audit.Single(x => x.Path == other).References);

        Assert.Throws<QuantosException>(() => v.Session.Attachments.TrashOrphan(first, Actor.User));
        var entry = v.Session.Attachments.TrashOrphan(other, Actor.User);
        Assert.False(File.Exists(v.Abs(other)));
        v.Writes.Restore(entry.EntryId, Actor.User);
        Assert.True(File.Exists(v.Abs(other)));
        Assert.Empty(v.Writes.TrashBin.List());
    }

    [Fact]
    public void Pdf_export_uses_edge_when_available()
    {
        using var v = new TestVault();
        var note = v.Writes.Create(new CreateNodeRequest { Title = "Printable", Body = "Hello PDF\n" }, Actor.User);
        var result = v.Session.Exports.ExportNode(note.Id, ExportFormat.Pdf, Actor.User);
        var edgeInstalled = File.Exists(Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"))
            || File.Exists(Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"));
        if (!edgeInstalled) { Assert.NotNull(result.Note); return; } // falls back to HTML and says why
        Assert.True(result.Format == ExportFormat.Pdf, result.Note); // the note says why Edge failed
        var bytes = File.ReadAllBytes(v.Abs(result.Path));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Contains("%%EOF", System.Text.Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 64), Math.Min(64, bytes.Length)));
    }
}
