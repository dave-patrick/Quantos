using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantos.Core.GoogleTasks;

/// <summary>The Google Tasks REST API over HttpClient. A 401 refreshes the token once; every call has a 30 s timeout.</summary>
public sealed class GoogleTasksApi(HttpClient http, IGoogleAccessTokens tokens, Uri? baseUri = null) : IGoogleTasksApi
{
    private static readonly Uri DefaultBase = new("https://tasks.googleapis.com/tasks/v1/");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly Uri _base = baseUri ?? DefaultBase;

    public async Task<IReadOnlyList<GTaskList>> ListTaskLists(CancellationToken ct)
    {
        var lists = new List<GTaskList>();
        string? page = null;
        do
        {
            var url = "users/@me/lists?maxResults=100" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var doc = await Send(HttpMethod.Get, url, null, ct);
            page = Parse(() =>
            {
                var root = doc!.RootElement;
                if (root.TryGetProperty("items", out var items))
                    foreach (var item in items.EnumerateArray())
                        lists.Add(new GTaskList(item.GetProperty("id").GetString()!, Str(item, "title") ?? ""));
                return Str(root, "nextPageToken");
            });
        } while (page is not null);
        return lists;
    }

    public async Task<GTaskList> CreateTaskList(string title, CancellationToken ct)
    {
        using var doc = await Send(HttpMethod.Post, "users/@me/lists", new JsonObject { ["title"] = title }, ct);
        return Parse(() => new GTaskList(doc!.RootElement.GetProperty("id").GetString()!, Str(doc.RootElement, "title") ?? title));
    }

    public async Task<GTaskPage> ListTasks(string listId, DateTimeOffset? updatedMin, string? pageToken, CancellationToken ct)
    {
        var url = $"lists/{Uri.EscapeDataString(listId)}/tasks?maxResults=100&showCompleted=true&showDeleted=true&showHidden=true&showAssigned=true";
        if (updatedMin is { } min) url += "&updatedMin=" + Uri.EscapeDataString(min.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        if (pageToken is not null) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
        using var doc = await Send(HttpMethod.Get, url, null, ct);
        return Parse(() =>
        {
            var items = new List<GTask>();
            if (doc!.RootElement.TryGetProperty("items", out var array))
                foreach (var item in array.EnumerateArray()) items.Add(Read(item));
            return new GTaskPage(items, Str(doc.RootElement, "nextPageToken"));
        });
    }

    public async Task<GTask?> Get(string listId, string id, CancellationToken ct)
    {
        try
        {
            using var doc = await Send(HttpMethod.Get, $"lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(id)}", null, ct);
            return Parse(() => Read(doc!.RootElement));
        }
        catch (GoogleApiException ex) when (ex.Status is 404 or 410) { return null; }
    }

    public async Task<GTask> Insert(string listId, GTaskWrite write, string? parent, CancellationToken ct)
    {
        var url = $"lists/{Uri.EscapeDataString(listId)}/tasks" + (parent is null ? "" : "?parent=" + Uri.EscapeDataString(parent));
        using var doc = await Send(HttpMethod.Post, url, Body(write), ct);
        return Parse(() => Read(doc!.RootElement));
    }

    public async Task<GTask> Patch(string listId, string id, GTaskWrite write, CancellationToken ct)
    {
        using var doc = await Send(HttpMethod.Patch, $"lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(id)}", Body(write), ct);
        return Parse(() => Read(doc!.RootElement));
    }

    public async Task<GTask> Move(string listId, string id, string? parent, CancellationToken ct)
    {
        var url = $"lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(id)}/move" + (parent is null ? "" : "?parent=" + Uri.EscapeDataString(parent));
        using var doc = await Send(HttpMethod.Post, url, null, ct);
        return Parse(() => Read(doc!.RootElement));
    }

    public async Task Delete(string listId, string id, CancellationToken ct)
    {
        try { (await Send(HttpMethod.Delete, $"lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(id)}", null, ct))?.Dispose(); }
        catch (GoogleApiException ex) when (ex.Status is 404 or 410) { }
    }

    // ---- wire format --------------------------------------------------------------------------

    /// <summary>The JSON for a write: absent fields are not sent; an explicit null is a JSON null. Reopening also clears `completed`.</summary>
    public static JsonObject Body(GTaskWrite w)
    {
        var o = new JsonObject();
        if (w.Title.HasValue) o["title"] = w.Title.Value;
        if (w.Notes.HasValue) o["notes"] = w.Notes.Value;
        if (w.Due.HasValue) o["due"] = w.Due.Value is { } d ? ToWireDue(d) : null;
        if (w.Completed.HasValue)
        {
            o["status"] = w.Completed.Value ? "completed" : "needsAction";
            if (!w.Completed.Value) o["completed"] = null;
        }
        return o;
    }

    /// <summary>yyyy-MM-dd to the midnight-UTC timestamp Google stores.</summary>
    public static string ToWireDue(string date) => date[..10] + "T00:00:00.000Z";

    private static GTask Read(JsonElement e)
    {
        var due = Str(e, "due");
        return new GTask(e.GetProperty("id").GetString()!, Str(e, "title") ?? "", Str(e, "notes"), due is { Length: >= 10 } ? due[..10] : null,
            Str(e, "status") == "completed", Str(e, "completed"), Str(e, "parent"), Str(e, "updated") ?? "", Str(e, "etag"),
            Bool(e, "deleted"), Bool(e, "hidden"), e.TryGetProperty("assignmentInfo", out var a) && a.ValueKind == JsonValueKind.Object);
    }

    /// <summary>A reply that is not the JSON Google documents (a missing id, the wrong shape) is a Google problem, not a crash.</summary>
    private static T Parse<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new GoogleApiException(502, "badReply", "Google sent a reply Quantos could not read."); }
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private async Task<JsonDocument?> Send(HttpMethod method, string relative, JsonNode? body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await tokens.Get(forceRefresh: attempt > 0, ct);
            using var request = new HttpRequestMessage(method, new Uri(_base, relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GoogleApiException(503, "timeout", "Google did not respond in time."); }
            catch (HttpRequestException ex) { throw new GoogleApiException(503, "network", "Can't reach Google: " + GoogleOAuth.Describe(ex)); }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (attempt == 0) continue;
                    throw new GoogleAuthException("Google no longer accepts this sign-in. Connect again.");
                }
                var text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode) throw Failure(response, text);
                try { return text.Length == 0 ? JsonDocument.Parse("{}") : JsonDocument.Parse(text); }
                catch (JsonException) { throw new GoogleApiException(502, "badReply", "Google sent a reply Quantos could not read."); }
            }
        }
    }

    private static GoogleApiException Failure(HttpResponseMessage response, string text)
    {
        string? reason = null, message = null;
        try
        {
            var error = JsonDocument.Parse(text).RootElement.GetProperty("error");
            message = Str(error, "message");
            if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0) reason = Str(errors[0], "reason");
            reason ??= Str(error, "status");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
        TimeSpan? retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } at ? at - DateTimeOffset.UtcNow : null);
        return new GoogleApiException((int)response.StatusCode, reason, message ?? $"Google returned {(int)response.StatusCode}.", retry);
    }
}
