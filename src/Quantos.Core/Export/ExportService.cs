using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Quantos.Core.Markdown;
using Quantos.Core.Security;
using Quantos.Core.Structure;
using Quantos.Core.Storage;
using Quantos.Core.Tasks;

namespace Quantos.Core.Export;

public enum ExportFormat { Markdown, Html, Pdf }

public sealed record ExportResult(string Path, ExportFormat Format, string? Note);

/// <summary>
/// Exports (BUILD-PLAN 8D.13-14) to the vault's exports folder. Links become readable titles,
/// embeds are inlined, and query results are frozen as static tables so the export reads the same
/// anywhere. Exports are copies; they never change the notes.
/// </summary>
public sealed partial class ExportService(WriteService writes, QueryService queries)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

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
                if (File.Exists(source)) zip.CreateEntryFromFile(source, a.Replace('\\', '/'), CompressionLevel.Optimal);
            }
        }
        return new ExportResult(writes.Vault.Relative(zipPath), format, $"{count} note(s)");
    }

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
}
