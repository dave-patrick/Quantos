using System.Diagnostics;
using Quantos.Core;
using Quantos.Core.Security;
using Xunit.Abstractions;

namespace Quantos.Tests;

/// <summary>
/// Measures a realistic vault (BUILD-PLAN section 13: "set performance targets using a realistic test
/// vault; don't claim any speed until it's been measured"). Runs only with QUANTOS_PERF=1, since it
/// writes thousands of files. Results go to the test output.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    private static readonly string[] Words = "budget plan meeting supplier review launch risk policy client invoice report draft team quarter roadmap hiring audit contract product design".Split(' ');

    [Fact]
    public void Realistic_vault_timings()
    {
        if (Environment.GetEnvironmentVariable("QUANTOS_PERF") != "1") return;
        var notes = int.TryParse(Environment.GetEnvironmentVariable("QUANTOS_PERF_NOTES"), out var n) ? n : 5000;
        using var v = new TestVault(start: false);
        var rng = new Random(42);
        string Sentence() => string.Join(' ', Enumerable.Range(0, rng.Next(8, 20)).Select(_ => Words[rng.Next(Words.Length)])) + ".";

        // Write files directly, as if an existing vault were opened for the first time.
        var dir = Path.Combine(v.VaultPath, "workspaces", "default", "nodes");
        Directory.CreateDirectory(dir);
        var ids = new List<string>();
        for (var i = 0; i < notes; i++)
        {
            var id = Ulid.New();
            ids.Add(id);
            var links = i > 0 ? string.Join(' ', Enumerable.Range(0, 3).Select(_ => $"[[{ids[rng.Next(ids.Count - 1)]}|link]]")) : "";
            var body = string.Join("\n\n", Enumerable.Range(0, rng.Next(3, 12)).Select(_ => Sentence())) + "\n\n" + links + "\n- [ ] " + Sentence() + "\n";
            File.WriteAllText(Path.Combine(dir, $"note_{i:D5}.md"),
                $"---\nid: {id}\ntitle: Note {i} {Words[i % Words.Length]}\ntype: note\nworkspace: default\ntags: [{Words[rng.Next(Words.Length)]}]\nstatus: {(i % 3 == 0 ? "open" : "done")}\nschema: 1\n---\n{body}");
        }

        var sw = Stopwatch.StartNew();
        var first = v.Session.Start();
        var firstScan = sw.Elapsed;
        output.WriteLine($"Vault: {notes} notes ({first.Adopted} inline tasks bound later)");
        output.WriteLine($"First open (index everything): {firstScan.TotalSeconds:F1} s");

        sw.Restart();
        var restart = v.Restart();
        output.WriteLine($"Restart (nothing changed): {sw.Elapsed.TotalMilliseconds:F0} ms for {restart.Scanned} files");

        sw.Restart();
        for (var i = 0; i < 20; i++) v.Session.Cache.Search(Words[i % Words.Length] + " " + Words[(i + 3) % Words.Length]);
        output.WriteLine($"Full-text search: {sw.Elapsed.TotalMilliseconds / 20:F1} ms per query");

        sw.Restart();
        for (var i = 0; i < 20; i++) v.Session.Cache.Backlinks(ids[rng.Next(ids.Count)]);
        output.WriteLine($"Backlinks: {sw.Elapsed.TotalMilliseconds / 20:F2} ms per note");

        sw.Restart();
        var q = v.Session.Queries.RunText("where: status = open\nsort: title\nlimit: 200", "default");
        output.WriteLine($"Query over all notes: {sw.Elapsed.TotalMilliseconds:F0} ms ({q.Total} matches)");

        var target = v.Writes.Require(ids[notes / 2]);
        sw.Restart();
        for (var i = 0; i < 20; i++)
        {
            target = v.Writes.Require(target.Id);
            v.Writes.Save(target.Id, target.Content + $"\nedit {i}\n", target.Hash, Actor.User);
        }
        output.WriteLine($"Save (history + atomic write + index): {sw.Elapsed.TotalMilliseconds / 20:F1} ms per save");

        sw.Restart();
        v.Writes.Rename(ids[0], "Renamed hub note", Actor.User);
        output.WriteLine($"Rename with link rewrites ({v.Session.Cache.Backlinks(ids[0]).Count} referrers): {sw.Elapsed.TotalMilliseconds:F0} ms");

        sw.Restart();
        v.Session.RebuildCache();
        output.WriteLine($"Rebuild cache: {sw.Elapsed.TotalSeconds:F1} s");
    }
}
