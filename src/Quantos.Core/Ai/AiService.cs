using System.Runtime.CompilerServices;
using System.Text;
using Quantos.Core.Markdown;
using Quantos.Core.Security;

namespace Quantos.Core.Ai;

public sealed record AiSource(string Id, string Title, string Workspace);

/// <summary>A piece of a streamed answer: the notes it drew on (first), then text as it's written.</summary>
public sealed record AiChunk(string? Text, IReadOnlyList<AiSource>? Sources = null);

/// <summary>
/// In-app AI (Release 1.1): ask your notes, rewrite or summarise selected text, and meaning search.
/// Off until switched on in Settings › AI. Only what a request needs is sent: a question and the
/// passages it draws on, or the selected text. Agents and plugins also need the Network permission.
/// </summary>
public sealed class AiService : IDisposable
{
    private readonly WriteService _writes;
    private readonly AiClient _client = new();

    public AiService(WriteService writes, string settingsFolder, string cacheFolder)
    {
        _writes = writes;
        Settings = new AiSettingsStore(settingsFolder);
        Index = new SemanticIndex(writes, Path.Combine(cacheFolder, "embeddings.sqlite"),
            () => Settings.Load() is { Enabled: true, Embeddings: { } e } ? e : null, Settings.GetKey, _client);
        Index.Refresh();
    }

    public AiSettingsStore Settings { get; }
    public SemanticIndex Index { get; }
    public AiClient Client => _client;

    private AiEndpoint ChatEndpoint(Actor actor)
    {
        if (actor.Kind is not (ActorKind.User or ActorKind.Cli)) _writes.Permissions.Demand(actor, Permission.Network, "Use the AI provider");
        var s = Settings.Load();
        if (!s.Enabled) throw new QuantosException("AI is switched off. Turn it on in Settings › AI.");
        return s.Chat ?? throw new QuantosException("Choose a provider for writing and answering in Settings › AI.");
    }

    public void SaveSettings(AiSettings settings)
    {
        Settings.Save(settings);
        Index.Refresh();
    }

    /// <summary>Answers a question from the user's notes, naming the notes it used.</summary>
    public async IAsyncEnumerable<AiChunk> AskAsync(string question, string? workspace, Actor actor, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question)) throw new QuantosException("Ask a question.");
        var endpoint = ChatEndpoint(actor);
        _writes.Permissions.Demand(actor, Permission.Read, "Read notes to answer a question");
        var passages = await Retrieve(question, workspace, ct);
        yield return new AiChunk(null, passages.Select(p => new AiSource(p.Id, p.Title, p.Workspace)).DistinctBy(s => s.Id).ToList());

        var context = new StringBuilder();
        foreach (var p in passages)
            context.Append($"<note title=\"{Escape(p.Title)}\">\n{p.Text}\n</note>\n\n");
        var system = """
            You answer questions using the user's own notes, which are given in <note> tags. Base the answer on those notes.
            When you use a note, name it as a link in double square brackets with its exact title, like [[Project plan]].
            If the notes don't contain the answer, say so plainly, and only then add general knowledge, marked as such.
            Write in plain Markdown. Be concise.
            """;
        var prompt = passages.Count == 0
            ? $"No notes matched this question.\n\nQuestion: {question}"
            : $"{context}Question: {question}";
        await foreach (var text in _client.ChatAsync(endpoint, Settings.GetKey(endpoint.Provider), system, [new AiMessage("user", prompt)], 16000, ct))
            yield return new AiChunk(text);
    }

    /// <summary>Applies an instruction to some text (rewrite, summarise, translate, continue…) and streams the result.</summary>
    public async IAsyncEnumerable<string> TransformAsync(string instruction, string text, string? noteTitle, Actor actor, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instruction)) throw new QuantosException("Say what to do with the text.");
        var endpoint = ChatEndpoint(actor);
        var system = """
            You are a writing assistant inside a notes app. Apply the user's instruction to the text they give you.
            Reply with only the resulting text in Markdown: no preamble, no explanation, no surrounding quotes or code fences.
            Keep links in double square brackets exactly as they are.
            """;
        var prompt = $"Instruction: {instruction}\n\n{(noteTitle is null ? "" : $"(From the note “{noteTitle}”.)\n\n")}<text>\n{text}\n</text>";
        await foreach (var piece in _client.ChatAsync(endpoint, Settings.GetKey(endpoint.Provider), system, [new AiMessage("user", prompt)], 16000, ct))
            yield return piece;
    }

    /// <summary>Passages for a question: by meaning when that's set up, otherwise by words.</summary>
    private async Task<List<SemanticHit>> Retrieve(string question, string? workspace, CancellationToken ct)
    {
        var s = Settings.Load();
        if (s.Embeddings is not null)
        {
            try { return (await Index.SearchAsync(question, workspace, 8, ct)).ToList(); }
            catch (QuantosException) { /* fall back to word search */ }
        }
        var hits = _writes.Cache.Search(question, workspace, 8);
        if (hits.Count == 0) hits = _writes.Cache.Search(string.Join(" OR ", question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3)), workspace, 8);
        return hits.Select(h =>
        {
            var node = _writes.Get(h.Id);
            var text = node is null ? h.Snippet : Links.PlainText(node.Body);
            return new SemanticHit(h.Id, h.Title, h.Workspace, h.Path, text.Length > 2400 ? text[..2400] + "…" : text, 0);
        }).ToList();
    }

    private static string Escape(string s) => s.Replace("\"", "'").Replace("<", "‹").Replace(">", "›");

    public void Dispose() => Index.Dispose();
}
