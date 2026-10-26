using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Quantos.Core.Cache;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Structure;
using Quantos.Core.Storage;
using Quantos.Core.Tasks;

namespace Quantos.Core.Export;

public enum ExportFormat { Markdown, Html, Pdf, Csv, Site }

public sealed record ExportResult(string Path, ExportFormat Format, string? Note);

/// <summary>
/// Exports (BUILD-PLAN 8D.13-14) to the vault's exports folder. Links become readable titles,
/// embeds are inlined, and query results are frozen as static tables so the export reads the same
/// anywhere. Exports are copies; they never change the notes.
/// </summary>
public sealed partial class ExportService(WriteService writes, QueryService queries)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    /// <summary>A table (a view or query's rows, made into CSV by the page) saved to Vault/exports.</summary>
    public ExportResult ExportTable(string name, string csv, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, $"Export {name}");
        if (csv.Length > 50_000_000) throw new QuantosException("That table is too large to export.");
        Directory.CreateDirectory(writes.Vault.Exports);
        var stem = Storage.Vault.Slug(string.IsNullOrWhiteSpace(name) ? "table" : name) + "-" + writes.Clock.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var path = Unique(Path.Combine(writes.Vault.Exports, stem + ".csv"));
        AtomicFile.WriteAllText(path, csv);
        return new ExportResult(writes.Vault.Relative(path), ExportFormat.Csv, null);
    }

    public ExportResult ExportNode(string id, ExportFormat format, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, $"Export {id}");
        var node = writes.Require(id);
        // Exports sit in Vault/exports, so attachment paths point one folder up.
        var markdown = PropertyList(node) + Prepare(node, freeze: true, depth: 0, attachmentBase: "../");
        Directory.CreateDirectory(writes.Vault.Exports);
        var stem = Storage.Vault.Slug(node.Title) + "-" + writes.Clock.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        switch (format)
        {
            case ExportFormat.Markdown:
            {
                var path = Unique(Path.Combine(writes.Vault.Exports, stem + ".md"));
                AtomicFile.WriteAllText(path, $"# {node.Title}\n\n" + markdown);
                return new ExportResult(writes.Vault.Relative(path), format, null);
            }
            case ExportFormat.Html:
            {
                var path = Unique(Path.Combine(writes.Vault.Exports, stem + ".html"));
                AtomicFile.WriteAllText(path, Html(node.Title, markdown));
                return new ExportResult(writes.Vault.Relative(path), format, null);
            }
            default:
            {
                var html = Unique(Path.Combine(writes.Vault.Exports, stem + ".html"));
                AtomicFile.WriteAllText(html, Html(node.Title, markdown));
                var pdf = Unique(Path.Combine(writes.Vault.Exports, stem + ".pdf"));
                var error = PrintToPdf(html, pdf);
                if (error is not null) return new ExportResult(writes.Vault.Relative(html), ExportFormat.Html, error + " Saved as HTML instead; print it to PDF from your browser.");
                File.Delete(html);
                return new ExportResult(writes.Vault.Relative(pdf), format, null);
            }
        }
    }

    /// <summary>Exports every note in a folder (and its subfolders) as a zip of Markdown or HTML files.</summary>
    public ExportResult ExportFolder(string folderRelative, ExportFormat format, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, $"Export {folderRelative}");
        var folder = writes.Vault.Absolute(folderRelative);
        if (!Directory.Exists(folder)) throw new QuantosException("That folder doesn't exist.", 404);
        Directory.CreateDirectory(writes.Vault.Exports);
        var zipPath = Unique(Path.Combine(writes.Vault.Exports, Storage.Vault.Slug(Path.GetFileName(folder)) + "-" + writes.Clock.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".zip"));
        var count = 0;
        var attachments = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories).Order())
            {
                var node = Node.FromText(writes.Vault.Relative(file), AtomicFile.ReadAllText(file));
                var rel = Path.GetRelativePath(folder, file);
                // The zip carries its notes' attachments at its root; point each note at them.
                var up = string.Concat(Enumerable.Repeat("../", rel.Count(ch => ch is '\\' or '/')));
                var md = PropertyList(node) + Prepare(node, freeze: true, depth: 0, attachmentBase: up);
                foreach (var a in Attachments.Find(node.Body)) attachments.Add(a);
                var entryName = format == ExportFormat.Html ? Path.ChangeExtension(rel, ".html") : rel;
                var entry = zip.CreateEntry(entryName.Replace('\\', '/'));
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(format == ExportFormat.Html ? Html(node.Title, md) : $"# {node.Title}\n\n{md}");
                count++;
            }
            foreach (var a in attachments)
            {
                var source = writes.Vault.Absolute(a);
                if (FileBoundary.ContainsFile(writes.Vault.Attachments, source) && File.Exists(source)) zip.CreateEntryFromFile(source, a.Replace('\\', '/'), CompressionLevel.Optimal);
            }
        }
        return new ExportResult(writes.Vault.Relative(zipPath), format, $"{count} note(s)");
    }

    /// <summary>
    /// Publishes a scope (a workspace, or a folder linked in from elsewhere) as a small website: a
    /// page per note with its links and backlinks, an index that can be searched, and a graph of
    /// how the notes connect. The site is a copy; the notes are not touched.
    /// </summary>
    public ExportResult Publish(string scope, Actor actor)
    {
        writes.Permissions.Demand(actor, Permission.Read, $"Publish {scope}");
        var vault = writes.Vault;
        var summaries = writes.Cache.List(scope, null, includeArchived: false, limit: 100_000)
            .Where(n => !Storage.Vault.IsArchivedPath(n.Path))
            .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (summaries.Count == 0) throw new QuantosException("There are no notes in that folder to publish.");

        // One page per note; the file name comes from the title, with a number if two notes share it.
        var pages = new Dictionary<string, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in summaries)
        {
            var stem = Storage.Vault.Slug(n.Title);
            var page = stem + ".html";
            for (var i = 2; !used.Add(page); i++) page = stem + "-" + i + ".html";
            pages[n.Id] = page;
        }
        var titles = summaries.ToDictionary(n => n.Id, n => n.Title);

        var dir = Path.Combine(vault.Exports, Storage.Vault.Slug(scope) + "-" + writes.Clock.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture));
        if (Directory.Exists(dir)) dir = dir + "-2";
        Directory.CreateDirectory(dir);

        // The attachments the notes use, copied in beside the pages.
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var written = 0;
        foreach (var n in summaries)
        {
            var node = writes.Get(n.Id);
            if (node is null) continue;
            foreach (var a in Attachments.Find(node.Body))
            {
                var source = vault.Absolute(a);
                if (!FileBoundary.ContainsFile(vault.Attachments, source) || !File.Exists(source)) continue;
                var name = Path.GetFileName(source);
                var target = assets.TryGetValue(a, out var known) ? known : name;
                assets[a] = target;
                var dest = Path.Combine(dir, "assets", target);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                try { File.Copy(source, dest, overwrite: true); } catch (IOException) { }
            }
            var html = SitePage(node, pages, titles, assets, summaries);
            AtomicFile.WriteAllText(Path.Combine(dir, pages[n.Id]), html);
            written++;
        }

        AtomicFile.WriteAllText(Path.Combine(dir, "index.html"), SiteIndex(scope, summaries, pages));
        AtomicFile.WriteAllText(Path.Combine(dir, "graph.html"), SiteGraph(scope, summaries, pages));
        AtomicFile.WriteAllText(Path.Combine(dir, "style.css"), SiteCss());
        return new ExportResult(vault.Relative(dir), ExportFormat.Site, $"{written} note(s)");
    }

    /// <summary>A note as a page: the note, the notes that link here, and the ones it links to.</summary>
    private string SitePage(Node node, Dictionary<string, string> pages, Dictionary<string, string> titles, Dictionary<string, string> assets, List<NodeSummary> all)
    {
        var body = SiteBody(node, pages, titles, assets);
        var backlinks = writes.Cache.Backlinks(node.Id)
            .Where(b => pages.ContainsKey(b.SourceId))
            .OrderBy(b => b.SourceTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var links = Links.Find(node.Body)
            .Where(l => l.IsResolved && pages.ContainsKey(l.Target))
            .Select(l => (Title: titles.GetValueOrDefault(l.Target) ?? l.TitleTarget.Title, Page: pages[l.Target]))
            .DistinctBy(l => l.Page)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{WebUtility.HtmlEncode(node.Title)}</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<link rel=\"stylesheet\" href=\"style.css\"></head><body>");
        sb.Append("<nav><a href=\"index.html\">All notes</a><span>·</span><a href=\"graph.html\">Graph</a></nav>");
        sb.Append($"<article><h1>{WebUtility.HtmlEncode(node.Title)}</h1>");
        var meta = new List<string>();
        if (node.Type.Length > 0) meta.Add(WebUtility.HtmlEncode(node.Type));
        if (node.Modified is not null) meta.Add("updated " + node.Modified);
        if (meta.Count > 0) sb.Append($"<p class=\"meta\">{string.Join(" · ", meta)}</p>");
        sb.Append(Markdig.Markdown.ToHtml(PropertyList(node) + body, Pipeline));
        if (backlinks.Count > 0)
        {
            sb.Append("<section class=\"links\"><h2>Links to this note</h2><ul>");
            foreach (var b in backlinks) sb.Append($"<li><a href=\"{pages[b.SourceId]}\">{WebUtility.HtmlEncode(b.SourceTitle)}</a></li>");
            sb.Append("</ul></section>");
        }
        if (links.Count > 0)
        {
            sb.Append("<section class=\"links\"><h2>Links from this note</h2><ul>");
            foreach (var l in links) sb.Append($"<li><a href=\"{l.Page}\">{WebUtility.HtmlEncode(l.Title)}</a></li>");
            sb.Append("</ul></section>");
        }
        sb.Append("</article></body></html>");
        return sb.ToString();
    }

    /// <summary>The body for a page: links point at the other pages, attachments at the copied files.</summary>
    private string SiteBody(Node node, Dictionary<string, string> pages, Dictionary<string, string> titles, Dictionary<string, string> assets)
    {
        var body = node.Body;
        body = SyncMarker().Replace(body, "");
        body = TaskMarker().Replace(body, "");
        // A query becomes the table it would show now, so the page reads the same anywhere.
        body = QueryFence().Replace(body, m => Freeze(m.Groups["q"].Value, node.Workspace));
        body = EmbedLine().Replace(body, m =>
        {
            var target = writes.Get(m.Groups["id"].Value);
            if (target is null) return "> *(embedded note not found)*";
            var frag = m.Groups["frag"].Success ? m.Groups["frag"].Value : null;
            var text = frag is null ? SiteBody(target, pages, titles, assets) : Links.Fragment(target.Body, frag) ?? "";
            var quoted = string.Join("\n", text.TrimEnd().Split('\n').Select(l => "> " + l));
            return $"> **{target.Title}**\n>\n{quoted}";
        });
        body = Links.Rewrite(body, l =>
        {
            var title = l.Display ?? titles.GetValueOrDefault(l.Target) ?? l.TitleTarget.Title;
            return pages.TryGetValue(l.Target, out var page) ? $"[{title}]({page})" : title;
        });
        // The attachments sit in the site's own assets folder.
        body = AttachmentPath().Replace(body, m => "assets/" + Path.GetFileName(m.Groups["file"].Value));
        return body.Trim() + "\n";
    }

    /// <summary>The home page: every note, and a box to search them.</summary>
    private string SiteIndex(string scope, List<NodeSummary> notes, Dictionary<string, string> pages)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{WebUtility.HtmlEncode(scope)}</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<link rel=\"stylesheet\" href=\"style.css\"></head><body>");
        sb.Append($"<h1>{WebUtility.HtmlEncode(scope)}</h1>");
        sb.Append($"<p class=\"meta\">{notes.Count} note(s) · <a href=\"graph.html\">graph</a></p>");
        sb.Append("<input id=\"q\" type=\"search\" placeholder=\"Search these notes\" aria-label=\"Search these notes\">");
        sb.Append("<ul id=\"notes\">");
        foreach (var n in notes)
        {
            var meta = n.Type.Length > 0 ? n.Type : "note";
            if (n.Modified is not null) meta += " · " + n.Modified;
            sb.Append($"<li data-t=\"{WebUtility.HtmlEncode(n.Title.ToLowerInvariant())}\"><a href=\"{pages[n.Id]}\">{WebUtility.HtmlEncode(n.Title)}</a> <span class=\"meta\">{WebUtility.HtmlEncode(meta)}</span></li>");
        }
        sb.Append("</ul>");
        sb.Append("<script>const q=document.getElementById('q');q.addEventListener('input',()=>{const v=q.value.trim().toLowerCase();for(const li of document.getElementById('notes').children)li.style.display=!v||li.dataset.t.includes(v)?'':'none';});</script>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>The notes as a graph: a circle of notes, and a line for every link between them.</summary>
    private string SiteGraph(string scope, List<NodeSummary> notes, Dictionary<string, string> pages)
    {
        const double size = 900, radius = 360, dot = 7;
        var centre = size / 2;
        var at = new Dictionary<string, (double X, double Y)>();
        for (var i = 0; i < notes.Count; i++)
        {
            var angle = 2 * Math.PI * i / Math.Max(1, notes.Count) - Math.PI / 2;
            at[notes[i].Id] = (centre + radius * Math.Cos(angle), centre + radius * Math.Sin(angle));
        }
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{WebUtility.HtmlEncode(scope)} — graph</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<link rel=\"stylesheet\" href=\"style.css\"></head><body>");
        sb.Append("<nav><a href=\"index.html\">All notes</a></nav>");
        sb.Append($"<h1>{WebUtility.HtmlEncode(scope)}</h1>");
        sb.Append($"<svg viewBox=\"0 0 {size} {size}\" role=\"img\" aria-label=\"How the notes link to each other\">");
        foreach (var n in notes)
        {
            var body = writes.Get(n.Id)?.Body ?? "";
            foreach (var l in Links.Find(body).Where(l => l.IsResolved && at.ContainsKey(l.Target)))
            {
                var a = at[n.Id];
                var b = at[l.Target];
                sb.Append($"<line x1=\"{a.X:F1}\" y1=\"{a.Y:F1}\" x2=\"{b.X:F1}\" y2=\"{b.Y:F1}\" class=\"edge\" />");
            }
        }
        foreach (var n in notes)
        {
            var p = at[n.Id];
            sb.Append($"<a href=\"{pages[n.Id]}\"><circle cx=\"{p.X:F1}\" cy=\"{p.Y:F1}\" r=\"{dot}\" class=\"dot\" /><title>{WebUtility.HtmlEncode(n.Title)}</title></a>");
        }
        sb.Append("</svg></body></html>");
        return sb.ToString();
    }

    /// <summary>One stylesheet for the whole site, in the app's own colours.</summary>
    private static string SiteCss() =>
        """
        :root { color-scheme: light dark; }
        body { max-width: 760px; margin: 40px auto; padding: 0 24px; font: 16px/1.65 "Source Sans 3", "Segoe UI", system-ui, sans-serif; color: #0c131d; background: #ffffff; }
        h1, h2, h3 { line-height: 1.25; } h1 { font-size: 2em; border-bottom: 2px solid #78b6ff; padding-bottom: .2em; }
        h2 { font-size: 1.15em; margin-top: 2em; } a { color: #1f6fd1; }
        code, pre { font-family: "IBM Plex Mono", Consolas, monospace; font-size: .9em; }
        pre { background: #f2f5f9; padding: 12px; border-radius: 6px; overflow: auto; }
        blockquote { margin: 1em 0; padding: .2em 1em; border-left: 3px solid #78b6ff; background: #f5f8fc; }
        table { border-collapse: collapse; width: 100%; font-size: .92em; } th, td { border: 1px solid #d6dde8; padding: 6px 8px; text-align: left; }
        th { background: #eef2f8; } img { max-width: 100%; }
        nav { display: flex; gap: 10px; font-size: .9em; margin-bottom: 18px; }
        .meta { color: #6c7a8c; font-size: .88em; }
        .links ul { list-style: none; padding: 0; } .links li { padding: 2px 0; }
        #q { width: 100%; padding: 10px 12px; border-radius: 8px; border: 1px solid #d6dde8; font: inherit; margin-bottom: 14px; }
        #notes { list-style: none; padding: 0; } #notes li { padding: 5px 0; border-bottom: 1px solid #eef1f6; }
        svg { width: 100%; height: auto; } .edge { stroke: #c3cede; stroke-width: 1; } .dot { fill: #1f6fd1; }
        @media (prefers-color-scheme: dark) {
          body { color: #e6ecf5; background: #10151d; } a { color: #78b6ff; }
          pre { background: #1a2130; } blockquote { background: #161d28; border-left-color: #78b6ff; }
          th { background: #1a2130; } th, td { border-color: #2a3446; } #notes li { border-color: #1c2431; }
          .edge { stroke: #2c3648; } .meta { color: #8b98ab; }
          #q { background: #161d28; border-color: #2a3446; color: #e6ecf5; }
        }
        """;

    /// <summary>The body made self-contained: readable links, inlined embeds, frozen queries, no hidden markers.</summary>
    public string Prepare(Node node, bool freeze, int depth, string attachmentBase = "")
    {
        var body = node.Body;
        if (attachmentBase.Length > 0) body = AttachmentRef().Replace(body, m => m.Groups["open"].Value + attachmentBase + "attachments/");
        body = SyncMarker().Replace(body, "");
        body = TaskMarker().Replace(body, "");
        if (freeze) body = QueryFence().Replace(body, m => Freeze(m.Groups["q"].Value, node.Workspace));
        body = EmbedLine().Replace(body, m =>
        {
            if (depth >= 2) return m.Value;
            var target = writes.Get(m.Groups["id"].Value);
            if (target is null) return "> *(embedded note not found)*";
            var frag = m.Groups["frag"].Success ? m.Groups["frag"].Value : null;
            var text = frag is null ? Prepare(target, freeze, depth + 1, attachmentBase) : Links.Fragment(target.Body, frag) ?? "";
            var quoted = string.Join("\n", text.TrimEnd().Split('\n').Select(l => "> " + l));
            return $"> **{target.Title}**\n>\n{quoted}";
        });
        body = Links.Rewrite(body, l => l.Display ?? (l.IsResolved ? writes.Cache.Get(l.Target)?.Title : l.TitleTarget.Title) ?? l.Target);
        return body.Trim() + "\n";
    }

    // Bookkeeping that means nothing outside Quantos.
    private static readonly HashSet<string> Internal = new(StringComparer.OrdinalIgnoreCase)
        { "series", "series_day", "occurrence", "reminders", "source", "tz", "my_day", "my_day_order", "time_block", "completed", "parent", "cover" };

    /// <summary>The note's supertags and properties as a short list under the title, with links shown as titles.</summary>
    public static string PropertyList(Node node)
    {
        var lines = new List<string>();
        if (node.Tags.Count > 0) lines.Add("- **Tags:** " + string.Join(", ", node.Tags.Select(t => "#" + t)));
        foreach (var (key, value) in node.Properties)
        {
            if (Internal.Contains(key) || value is null || value is IDictionary<object, object> || value is IDictionary<string, object?>) continue;
            var list = Props.List(node.Properties, key);
            var text = list.Count > 1 ? string.Join(", ", list) : Props.Str(value) ?? "";
            text = LinkText().Replace(text, "$1").Trim();
            if (text.Length == 0) continue;
            var label = key.Replace('_', ' ');
            lines.Add($"- **{char.ToUpperInvariant(label[0]) + label[1..]}:** {text}");
        }
        return lines.Count == 0 ? "" : string.Join("\n", lines) + "\n\n";
    }

    private string Freeze(string query, string workspace)
    {
        var result = queries.RunText(query, workspace.Length > 0 ? workspace : Storage.Vault.DefaultWorkspace);
        if (result.Error is not null) return $"*Query: {result.Error}*\n";
        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", result.Columns.Select(Cell)) + " |");
        sb.AppendLine("|" + string.Concat(result.Columns.Select(_ => " --- |")));
        foreach (var r in result.Rows)
        {
            var values = result.Columns.Select(c => c switch
            {
                "title" => r.Title, "type" => r.Type, "workspace" => r.Workspace, "created" => r.Created ?? "", "modified" => r.Modified ?? "", "path" => r.Path,
                "tags" => string.Join(", ", r.Tags),
                _ => Props.List(r.Properties, c) is { Count: > 1 } list ? string.Join(", ", list) : Props.Str(r.Properties.GetValueOrDefault(c)) ?? ""
            });
            sb.AppendLine("| " + string.Join(" | ", values.Select(v => Cell(LinkText().Replace(v, "$1")))) + " |");
        }
        sb.AppendLine($"\n*{result.Total} result(s) as of {writes.Clock.Now:yyyy-MM-dd HH:mm}*");
        return sb.ToString();
    }

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    public static string Html(string title, string markdown)
    {
        var body = Markdig.Markdown.ToHtml(markdown, Pipeline);
        return $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(title)}}</title>
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>
              body { max-width: 760px; margin: 40px auto; padding: 0 24px; font: 16px/1.65 "Source Sans 3", "Segoe UI", system-ui, sans-serif; color: #0c131d; }
              h1, h2, h3 { line-height: 1.25; } h1 { font-size: 2em; border-bottom: 2px solid #78b6ff; padding-bottom: .2em; }
              a { color: #1f6fd1; } code, pre { font-family: "IBM Plex Mono", Consolas, monospace; font-size: .9em; }
              pre { background: #f2f5f9; padding: 12px; border-radius: 6px; overflow: auto; }
              blockquote { margin: 1em 0; padding: .2em 1em; border-left: 3px solid #78b6ff; background: #f5f8fc; }
              table { border-collapse: collapse; width: 100%; font-size: .92em; } th, td { border: 1px solid #d6dde8; padding: 6px 8px; text-align: left; }
              th { background: #eef2f8; } img { max-width: 100%; }
              footer { margin-top: 3em; font-size: .8em; color: #7d8ba0; }
              @media print { body { margin: 0; max-width: none; } a { color: inherit; } }
            </style></head>
            <body><h1>{{WebUtility.HtmlEncode(title)}}</h1>
            {{body}}
            <footer>Exported from Quantos</footer></body></html>
            """;
    }

    /// <summary>Prints HTML to PDF with Microsoft Edge in headless mode (installed with Windows; no extra install).</summary>
    private static string? PrintToPdf(string html, string pdf)
    {
        var edge = new[]
        {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\Edge\Application\msedge.exe")
        }.FirstOrDefault(File.Exists);
        if (edge is null) return "PDF export needs Microsoft Edge, which wasn't found.";
        var profile = Path.Combine(Path.GetTempPath(), "quantos-pdf-" + Guid.NewGuid().ToString("N"));
        var psi = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--no-pdf-header-footer", $"--user-data-dir={profile}", $"--print-to-pdf={pdf}", new Uri(html).AbsoluteUri })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            // Wait for the file, not the process: msedge.exe may hand the job to another Edge process
            // and exit at once, and Edge writes the PDF well before it finishes shutting down.
            var started = DateTime.UtcNow;
            var deadline = started.AddSeconds(60);
            long lastSize = -1;
            while (DateTime.UtcNow < deadline)
            {
                var exited = p.WaitForExit(250);
                var size = File.Exists(pdf) ? new FileInfo(pdf).Length : 0;
                if (size > 0 && size == lastSize) break;
                lastSize = size;
                if (exited && size == 0 && DateTime.UtcNow - started > TimeSpan.FromSeconds(15)) break; // nothing is coming
            }
            if (!p.HasExited) { try { p.Kill(true); } catch (InvalidOperationException) { } }
            if (!File.Exists(pdf) || new FileInfo(pdf).Length == 0) return DateTime.UtcNow >= deadline ? "PDF export timed out." : "Edge didn't produce a PDF.";
            return null;
        }
        catch (Exception ex) { return "PDF export failed: " + ex.Message; }
        finally { DeleteSoon(profile); }
    }

    /// <summary>Edge's helper processes can hold the temporary profile for a moment after it's closed.</summary>
    private static void DeleteSoon(string folder) => _ = Task.Run(async () =>
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(folder); attempt++)
        {
            try { Directory.Delete(folder, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(500); }
        }
    });

    private static string Unique(string path)
    {
        var dir = Path.GetDirectoryName(path)!; var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(dir, $"{stem}-{n}{ext}");
        return path;
    }

    [GeneratedRegex(@"<!--/?sync(:[0-9A-HJKMNP-TV-Z]{26})?-->\r?\n?")]
    private static partial Regex SyncMarker();

    [GeneratedRegex(@"[ \t]*<!--task:[0-9A-HJKMNP-TV-Z]{26}-->")]
    private static partial Regex TaskMarker();

    [GeneratedRegex(@"^```\s*query\s*\r?\n(?<q>[\s\S]*?)^```\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex QueryFence();

    [GeneratedRegex(@"^\s*!\[\[(?<id>[0-9A-HJKMNP-TV-Z]{26})(?:#(?<frag>[^\]|]+))?(?:\|[^\]]+)?\]\]\s*$", RegexOptions.Multiline)]
    private static partial Regex EmbedLine();

    [GeneratedRegex(@"\[\[[0-9A-HJKMNP-TV-Z]{26}(?:#[^\]|]+)?\|([^\]]+)\]\]")]
    private static partial Regex LinkText();

    [GeneratedRegex(@"(?<open>\]\(<?)attachments/", RegexOptions.IgnoreCase)]
    private static partial Regex AttachmentRef();

    [GeneratedRegex(@"attachments/(?<file>[^)\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AttachmentPath();
}
