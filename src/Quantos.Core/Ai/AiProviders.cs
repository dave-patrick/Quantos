namespace Quantos.Core.Ai;

/// <summary>How Quantos talks to a provider.</summary>
public enum AiApi
{
    /// <summary>Claude, through the official Anthropic SDK.</summary>
    Anthropic,
    /// <summary>The OpenAI-style /chat/completions and /embeddings endpoints (most providers and local servers).</summary>
    OpenAiCompatible,
    /// <summary>Azure OpenAI: OpenAI-style endpoints with an api-key header.</summary>
    AzureOpenAi
}

/// <summary>A ready-made provider setting. Model names are suggestions; "List models" asks the provider.</summary>
public sealed record AiPreset(
    string Id, string Name, AiApi Api, string BaseUrl, bool Local, bool NeedsKey,
    bool Chat, bool Embeddings, string? DefaultChatModel, string? DefaultEmbeddingModel, string Note);

/// <summary>
/// Release 1.1 in-app AI (BUILD-PLAN 14). Local providers keep everything on this computer; remote
/// ones send the text involved in a request (a question and the notes it draws on, or the selected
/// text) to that provider. Nothing is sent until a provider is chosen and switched on.
/// </summary>
public static class AiProviders
{
    public static readonly IReadOnlyList<AiPreset> Presets =
    [
        new("ollama", "Ollama (on this computer)", AiApi.OpenAiCompatible, "http://localhost:11434/v1", Local: true, NeedsKey: false,
            Chat: true, Embeddings: true, "llama3.2", "nomic-embed-text",
            "Runs models locally. Install Ollama, then pull a model (for example: ollama pull llama3.2 and ollama pull nomic-embed-text)."),
        new("lmstudio", "LM Studio (on this computer)", AiApi.OpenAiCompatible, "http://localhost:1234/v1", Local: true, NeedsKey: false,
            Chat: true, Embeddings: true, null, null,
            "Runs models locally. Start LM Studio's local server and load a chat model (and an embedding model for search)."),
        new("anthropic", "Anthropic Claude", AiApi.Anthropic, "https://api.anthropic.com", Local: false, NeedsKey: true,
            Chat: true, Embeddings: false, "claude-opus-5-5", null,
            "Writing and answering. Claude has no embedding model, so pick another provider for meaning search."),
        new("openai", "OpenAI", AiApi.OpenAiCompatible, "https://api.openai.com/v1", Local: false, NeedsKey: true,
            Chat: true, Embeddings: true, null, "text-embedding-3-small", "Chat and embeddings."),
        new("gemini", "Google Gemini", AiApi.OpenAiCompatible, "https://generativelanguage.googleapis.com/v1beta/openai", Local: false, NeedsKey: true,
            Chat: true, Embeddings: true, null, null, "Through Google's OpenAI-compatible endpoint. Use a Gemini API key."),
        new("mistral", "Mistral", AiApi.OpenAiCompatible, "https://api.mistral.ai/v1", Local: false, NeedsKey: true,
            Chat: true, Embeddings: true, null, "mistral-embed", "Chat and embeddings."),
        new("voyage", "Voyage AI", AiApi.OpenAiCompatible, "https://api.voyageai.com/v1", Local: false, NeedsKey: true,
            Chat: false, Embeddings: true, null, null, "Embeddings only (the models Anthropic recommends for search alongside Claude)."),
        new("openrouter", "OpenRouter", AiApi.OpenAiCompatible, "https://openrouter.ai/api/v1", Local: false, NeedsKey: true,
            Chat: true, Embeddings: false, null, null, "Many models through one key."),
        new("azure", "Azure OpenAI", AiApi.AzureOpenAi, "https://YOUR-RESOURCE.openai.azure.com/openai/v1", Local: false, NeedsKey: true,
            Chat: true, Embeddings: true, null, null, "Your organisation's deployment. Set the address to your resource and use deployment names as models."),
        new("custom", "Other (OpenAI-compatible)", AiApi.OpenAiCompatible, "http://localhost:8080/v1", Local: false, NeedsKey: false,
            Chat: true, Embeddings: true, null, null, "Any server with OpenAI-style /chat/completions and /embeddings endpoints.")
    ];

    public static AiPreset? Find(string? id) => Presets.FirstOrDefault(p => p.Id == id);
}
