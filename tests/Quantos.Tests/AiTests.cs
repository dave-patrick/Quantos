using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Quantos.Core;
using Quantos.Core.Ai;
using Quantos.Core.Security;

namespace Quantos.Tests;

/// <summary>
/// Release 1.1 AI against a fake OpenAI-style provider on localhost: no account or network needed.
/// Its embeddings count a few topic words, so meaning search has something real to rank by.
/// </summary>
public sealed class AiTests : IDisposable
{
    private static readonly string[] Topics = ["budget", "money", "cost", "garden", "plant", "flower", "launch", "rocket"];
    private HttpListener _server;
    private readonly string _url;
    private readonly CancellationTokenSource _stop = new();
    public List<string> Prompts { get; } = [];

    public AiTests()
    {
        // Any free port: try a few in case one is taken.
        for (var attempt = 0; ; attempt++)
        {
            var port = 40000 + Random.Shared.Next(20000);
            try
            {
                _server = new HttpListener();
                _server.Prefixes.Add($"http://127.0.0.1:{port}/");
                _server.Start();
                _url = $"http://127.0.0.1:{port}/v1";
                break;
            }
            catch (HttpListenerException) when (attempt < 10) { _server.Close(); }
        }
        _ = Task.Run(Serve);
    }

    private async Task Serve()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _server.GetContextAsync(); } catch { return; }
            var body = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
            var path = ctx.Request.Url!.AbsolutePath;
            byte[] reply;
            if (path.EndsWith("/embeddings"))
            {
                var inputs = JsonNode.Parse(body)!["input"]!.AsArray().Select(i => i!.GetValue<string>().ToLowerInvariant()).ToList();
                var data = new JsonArray(inputs.Select((text, i) => (JsonNode)new JsonObject
                {
                    ["index"] = i,
                    // Synonyms share a dimension: budget/money/cost, garden/plant/flower, launch/rocket.
                    ["embedding"] = new JsonArray(
                        Count(text, "budget", "money", "cost"), Count(text, "garden", "plant", "flower"), Count(text, "launch", "rocket"), 0.01)
                }).ToArray());
                reply = Encoding.UTF8.GetBytes(new JsonObject { ["data"] = data }.ToJsonString());
                ctx.Response.ContentType = "application/json";
            }
            else if (path.EndsWith("/chat/completions"))
            {
                lock (Prompts) Prompts.Add(body);
                ctx.Response.ContentType = "text/event-stream";
                var sb = new StringBuilder();
                foreach (var word in new[] { "From ", "[[Garden plan]]", ": water daily." })
                    sb.Append("data: ").Append(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["delta"] = new JsonObject { ["content"] = word } }) }.ToJsonString()).Append("\n\n");
                sb.Append("data: [DONE]\n\n");
                reply = Encoding.UTF8.GetBytes(sb.ToString());
            }
            else if (path.EndsWith("/models"))
            {
                reply = Encoding.UTF8.GetBytes("""{"data":[{"id":"small-chat"},{"id":"tiny-embed"}]}""");
                ctx.Response.ContentType = "application/json";
            }
            else { ctx.Response.StatusCode = 404; reply = []; }
            await ctx.Response.OutputStream.WriteAsync(reply);
            ctx.Response.Close();
        }
    }

    private static double Count(string text, params string[] words) => words.Sum(w => (text.Length - text.Replace(w, "").Length) / (double)w.Length);

    private AiSettings Settings(bool embeddings = true) => new()
    {
        Enabled = true,
        Chat = new AiEndpoint("custom", _url, "small-chat"),
        Embeddings = embeddings ? new AiEndpoint("custom", _url, "tiny-embed") : null
    };

    private static async Task WaitForIndex(VaultSession s, int notes)
    {
        for (var i = 0; i < 100 && (s.Ai.Index.Status() is var st && (st.Indexed < notes || st.Working)); i++) await Task.Delay(100);
    }

    [Fact]
    public async Task Meaning_search_finds_notes_by_topic_not_words()
    {
        using var v = new TestVault();
        v.Writes.Create(new CreateNodeRequest { Title = "Garden plan", Body = "Plant tomatoes and water the flower beds.\n" }, Actor.User);
        v.Writes.Create(new CreateNodeRequest { Title = "Q4 numbers", Body = "The budget is tight; cut costs where we can.\n" }, Actor.User);
        v.Session.Ai.SaveSettings(Settings());
        await WaitForIndex(v.Session, 2);

        var hits = await v.Session.Ai.Index.SearchAsync("how much money is left", null, 5);
        Assert.Equal("Q4 numbers", hits[0].Title); // "money" never appears in the note
        Assert.Equal("Garden plan", (await v.Session.Ai.Index.SearchAsync("flowers", null, 5))[0].Title);

        // Editing a note re-indexes only that note.
        var garden = v.Session.Cache.List().Single(n => n.Title == "Garden plan");
        var current = v.Writes.Require(garden.Id);
        v.Writes.Save(garden.Id, current.Content.Replace("Plant tomatoes", "Rocket launch day"), current.Hash, Actor.User);
        await Task.Delay(2600);
        await WaitForIndex(v.Session, 2);
        Assert.Equal("Garden plan", (await v.Session.Ai.Index.SearchAsync("rocket", null, 5))[0].Title);
    }

    [Fact]
    public async Task Ask_streams_an_answer_from_the_notes_it_names()
    {
        using var v = new TestVault();
        v.Writes.Create(new CreateNodeRequest { Title = "Garden plan", Body = "Water the flower beds daily.\n" }, Actor.User);
        v.Session.Ai.SaveSettings(Settings());
        await WaitForIndex(v.Session, 1);

        var sources = new List<AiSource>();
        var text = new StringBuilder();
        await foreach (var chunk in v.Session.Ai.AskAsync("How often do the flowers need water?", null, Actor.User))
        {
            if (chunk.Sources is not null) sources.AddRange(chunk.Sources);
            text.Append(chunk.Text);
        }
        Assert.Equal("From [[Garden plan]]: water daily.", text.ToString());
        Assert.Contains(sources, s => s.Title == "Garden plan");
        // The provider got the question and the note's text, nothing else from the vault.
        var prompt = Prompts.Last();
        Assert.Contains("Water the flower beds daily.", prompt);
        Assert.Contains("How often do the flowers need water?", prompt);
    }

    [Fact]
    public async Task Ai_is_off_until_switched_on_and_agents_need_the_network_permission()
    {
        using var v = new TestVault();
        var off = await Assert.ThrowsAsync<QuantosException>(async () => { await foreach (var _ in v.Session.Ai.TransformAsync("Shorten", "Some text", null, Actor.User)) { } });
        Assert.Contains("switched off", off.Message);

        v.Session.Ai.SaveSettings(Settings(embeddings: false));
        var agent = new Actor(ActorKind.Agent, "helper");
        await Assert.ThrowsAnyAsync<Exception>(async () => { await foreach (var _ in v.Session.Ai.TransformAsync("Shorten", "Some text", null, agent)) { } });

        var result = new StringBuilder();
        await foreach (var piece in v.Session.Ai.TransformAsync("Shorten", "Some text", null, Actor.User)) result.Append(piece);
        Assert.NotEmpty(result.ToString());
        Assert.Equal(["small-chat", "tiny-embed"], await v.Session.Ai.Client.ListModelsAsync(new AiEndpoint("custom", _url, null), null));
    }

    [Fact]
    public void Keys_are_encrypted_at_rest_and_never_in_the_settings_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var v = new TestVault();
        v.Session.Ai.Settings.SetKey("openai", "sk-test-1234567890");
        Assert.Equal("sk-test-1234567890", v.Session.Ai.Settings.GetKey("openai"));
        var onDisk = File.ReadAllText(Path.Combine(v.ClientData, "settings", "ai-keys.json"));
        Assert.DoesNotContain("sk-test", onDisk);
        v.Session.Ai.Settings.SetKey("openai", null);
        Assert.False(v.Session.Ai.Settings.HasKey("openai"));
    }

    [Fact]
    public async Task Unreachable_local_provider_says_so_plainly()
    {
        var ai = new AiClient();
        var ex = await Assert.ThrowsAnyAsync<QuantosException>(() => ai.EmbedAsync(new AiEndpoint("ollama", "http://127.0.0.1:9/v1", "nomic-embed-text"), null, ["x"]));
        Assert.Contains("Is it running?", ex.Message);
    }

    [Fact]
    public void Claude_requests_ask_for_the_default_fallback()
    {
        var request = AiClient.ClaudeRequest("claude-opus-5-5", "Be brief.", [new AiMessage("user", "Hi")], 1000);
        var json = request.ToString();
        Assert.Contains("\"fallbacks\"", json);
        Assert.Contains("default", json);
    }

    [Fact]
    public void Notes_are_cut_into_titled_passages()
    {
        var body = string.Join("\n\n", Enumerable.Range(0, 12).Select(i => $"Paragraph {i} " + new string('x', 150)));
        var chunks = SemanticIndex.Chunk("Long note", body);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.StartsWith("Long note\n\n", c));
        Assert.All(chunks, c => Assert.True(c.Length <= 800 + 20));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _server.Stop();
        _server.Close();
    }
}
