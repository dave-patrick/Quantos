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
        var markdown = Prepare(node, freeze: format != ExportFormat.Markdown || true, depth: 0);
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
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories).Order())
            {
                var node = Node.FromText(writes.Vault.Relative(file), AtomicFile.ReadAllText(file));
                var md = Prepare(node, freeze: true, depth: 0);
                var rel = Path.GetRelativePath(folder, file);
                var entryName = format == ExportFormat.Html ? Path.ChangeExtension(rel, ".html") : rel;
                var entry = zip.CreateEntry(entryName.Replace('\\', '/'));
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(format == ExportFormat.Html ? Html(node.Title, md) : $"# {node.Title}\n\n{md}");
                count++;
            }
        }
        return new ExportResult(writes.Vault.Relative(zipPath), format, $"{count} note(s)");
    }

    /// <summary>The body made self-contained: readable links, inlined embeds, frozen queries, no hidden markers.</summary>
    public string Prepare(Node node, bool freeze, int depth)
    {
        var body = node.Body;
        body = SyncMarker().Replace(body, "");
        body = TaskMarker().Replace(body, "");
        if (freeze) body = QueryFence().Replace(body, m => Freeze(m.Groups["q"].Value, node.Workspace));
        body = EmbedLine().Replace(body, m =>
        {
            if (depth >= 2) return m.Value;
            var target = writes.Get(m.Groups["id"].Value);
            if (target is null) return "> *(embedded note not found)*";
            var frag = m.Groups["frag"].Success ? m.Groups["frag"].Value : null;
            var text = frag is null ? Prepare(target, freeze, depth + 1) : Links.Fragment(target.Body, frag) ?? "";
            var quoted = string.Join("\n", text.TrimEnd().Split('\n').Select(l => "> " + l));
            return $"> **{target.Title}**\n>\n{quoted}";
        });
        body = Links.Rewrite(body, l => l.Display ?? (l.IsResolved ? writes.Cache.Get(l.Target)?.Title : l.TitleTarget.Title) ?? l.Target);
        return body.Trim() + "\n";
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
        foreach (var a in new[] { "--headless", "--disable-gpu", "--no-first-run", "--no-pdf-header-footer", $"--user-data-dir={profile}", $"--print-to-pdf={pdf}", new Uri(html).AbsoluteUri })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(60_000)) { p.Kill(true); return "PDF export timed out."; }
            return File.Exists(pdf) && new FileInfo(pdf).Length > 0 ? null : "Edge didn't produce a PDF.";
        }
        catch (Exception ex) { return "PDF export failed: " + ex.Message; }
        finally { try { Directory.Delete(profile, true); } catch { } }
    }

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
}
