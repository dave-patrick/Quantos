using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Quantos.Backend.Hosting;
using Quantos.Core;
using Quantos.Core.Ai;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.IO.Compression;
using Quantos.Core.Sync;
using Quantos.Core.Security;
using Permission = Android.Content.PM.Permission;
using Uri = Android.Net.Uri;

namespace Quantos.Mobile;

// The Android process owns one backend, across rotation and activity recreation.
internal sealed record LocalHost(WebApplication App, HttpClient Http, string Url, AccessTokens Tokens)
{
    private static readonly object Gate = new();
    private static Task<LocalHost>? running;
    public static Task<LocalHost> Start(string root)
    {
        lock (Gate) return running ??= Task.Run(async () =>
        {
            AiSettingsStore.KeyProtector = new AndroidKeyProtector();
            var paths = new QuantosPaths(Path.Combine(root, "client-data"));
            paths.EnsureCreated();
            var settings = ClientSettings.Load(paths);
            var tokens = new AccessTokens();
            var app = Quantos.Backend.Program.Build([], new BackendOptions
            {
                ClientData = paths.ClientData, Vault = settings.VaultPath ?? Path.Combine(root, "Vault"), Port = settings.Port ?? BackendOptions.DefaultPort
            }, paths, tokens);
            AndroidPdf.Map(app);
            await app.StartAsync();
            var url = app.Services.GetRequiredService<ServerAddress>().Url;
            var activeSettings = app.Services.GetRequiredService<ClientSettings>();
            activeSettings.Port = app.Services.GetRequiredService<ServerAddress>().Port;
            activeSettings.Save(paths);
            var http = new HttpClient { BaseAddress = new System.Uri(url) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueNativeToken("android"));
            return new LocalHost(app, http, url, tokens);
        });
    }
    public string SignIn(string path = "/") => Url + "auth/open?code=" + Tokens.IssueCode(SessionKind.App) + "&to=" + System.Uri.EscapeDataString(path);
}

[Activity(Label = "Quantos", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : Activity
{
    private WebView? web;
    private LocalHost? host;
    private IValueCallback? filePicker;
    private const int PickFile = 40, Microphone = 41, ExportVault = 42, ImportVault = 43;
    private PermissionRequest? audioRequest;
    private bool closing;
    private AndroidTerminal? terminal;
    private BackCallback? backCallback;
    private WebView? printView;
    private string? downloadUrl;
    private const int DownloadFile = 44;
    private const int Notifications = 45;

    protected override async void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetStatusBarColor(Android.Graphics.Color.Rgb(42, 43, 45));
            Window?.SetNavigationBarColor(Android.Graphics.Color.Rgb(42, 43, 45));
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            backCallback = new BackCallback(this);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, backCallback);
        }
        var loading = new TextView(this)
        {
            Text = "Quantos\nOpening your vault…", Gravity = GravityFlags.Center, TextSize = 24,
            Typeface = Resources!.GetFont(Resource.Font.chakra_petch)
        };
        loading.SetTextColor(Android.Graphics.Color.Rgb(237, 242, 250));
        loading.SetBackgroundColor(Android.Graphics.Color.Rgb(42, 43, 45));
        SetContentView(loading);
        try
        {
            host = await LocalHost.Start(FilesDir!.AbsolutePath);
            if (IsDestroyed) return;
            terminal = new AndroidTerminal(message => RunOnUiThread(() => Reply(message)), () => host.App.Services.GetRequiredService<VaultHost>().Require().Vault.Root);
            web = new WebView(this);
            var safeArea = new FrameLayout(this);
            safeArea.SetBackgroundColor(Android.Graphics.Color.Rgb(42, 43, 45));
            safeArea.AddView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(35)) Window?.SetDecorFitsSystemWindows(false);
                safeArea.SetOnApplyWindowInsetsListener(new SafeInsets());
            }
            web.SetBackgroundColor(Android.Graphics.Color.Rgb(42, 43, 45));
            web.Settings.JavaScriptEnabled = true;
            web.Settings.DomStorageEnabled = true;
            web.Settings.TextZoom = Math.Clamp((int)Math.Round((Resources?.Configuration?.FontScale ?? 1f) * 100), 50, 300);
            web.Settings.AllowFileAccess = false;
            web.Settings.AllowContentAccess = true;
            web.Settings.MixedContentMode = MixedContentHandling.NeverAllow;
            web.Settings.JavaScriptCanOpenWindowsAutomatically = false;
            web.Settings.SetSupportMultipleWindows(false);
            web.Settings.UserAgentString += " QuantosAndroid/" + PackageManager!.GetPackageInfo(PackageName!, PackageInfoFlags.MetaData)!.VersionName;
            web.SetWebViewClient(new LocalNavigation(this));
            web.SetWebChromeClient(new NativeChrome(this));
            web.SetDownloadListener(new DownloadHandler(this));
            SetContentView(safeArea);
            safeArea.Post(() => safeArea.RequestApplyInsets());
            var launchNode = Intent?.GetStringExtra("nodeId");
            web.LoadUrl(host.SignIn(Quantos.Core.Ulid.IsValid(launchNode) ? "/note/" + launchNode : "/"));
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("Quantos", ex.ToString());
            loading.Text = "Quantos couldn’t open\n" + ex.Message + "\nClose the app and try again. Your vault has been kept.";
            loading.TextSize = 18;
            loading.SetPadding(24, 24, 24, 24);
        }
    }

    private bool IsLocal(string? url) => host is not null && System.Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == "http" && u.Host == "127.0.0.1" && u.Port == new System.Uri(host.Url).Port;
    private bool IsAppPage(string? url) => IsLocal(url) && System.Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.AbsolutePath is "/" or "/capture" or "/scratchpad" || u.AbsolutePath.StartsWith("/note/", StringComparison.Ordinal) && Quantos.Core.Ulid.IsValid(u.AbsolutePath[6..]));

    private void Reply(object message)
    {
        if (web is null || IsDestroyed || IsFinishing) return;
        web.EvaluateJavascript("window.dispatchEvent(new CustomEvent('quantos-native',{detail:" + JsonSerializer.Serialize(message) + "}))", null);
    }

    private async void NativeMessage(string encoded)
    {
        try
        {
            using var doc = JsonDocument.Parse(encoded);
            var msg = doc.RootElement;
            var type = msg.GetProperty("type").GetString();
            switch (type)
            {
                case "copy-mcp":
                    using (var clipboardResponse = await host!.Http.PostAsJsonAsync("api/sync/mcp/connection", new { }))
                    {
                        clipboardResponse.EnsureSuccessStatusCode();
                        var connection = System.Text.Json.Nodes.JsonNode.Parse(await clipboardResponse.Content.ReadAsStringAsync());
                        var settings = new System.Text.Json.Nodes.JsonObject { ["mcpServers"] = new System.Text.Json.Nodes.JsonObject { ["quantos"] = connection } };
                        var clip = ClipData.NewPlainText("Quantos MCP connection", settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }))!;
                        if (OperatingSystem.IsAndroidVersionAtLeast(33))
                        {
                            var extras = new PersistableBundle(); extras.PutBoolean("android.content.extra.IS_SENSITIVE", true);
                            clip.Description!.Extras = extras;
                        }
                        ((ClipboardManager)GetSystemService(ClipboardService)!).PrimaryClip = clip;
                        Reply(new { type = "mcp-copied" });
                    }
                    break;
                case "notification-status": ReplyNotificationPermission(); break;
                case "request-notifications":
                    if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
                        RequestPermissions([Manifest.Permission.PostNotifications], Notifications);
                    else ReplyNotificationPermission();
                    break;
                case "notification":
                    var manager = (NotificationManager)GetSystemService(NotificationService)!;
                    if (!manager.AreNotificationsEnabled()) break;
                    manager.CreateNotificationChannel(new NotificationChannel("reminders", "Quantos reminders", NotificationImportance.Default));
                    var open = new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop)!;
                    if (msg.TryGetProperty("nodeId", out var node) && node.ValueKind == JsonValueKind.String) open.PutExtra("nodeId", node.GetString());
                    var notificationId = StringComparer.Ordinal.GetHashCode(msg.GetProperty("id").GetString()!);
                    var pending = PendingIntent.GetActivity(this, notificationId, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
                    using (var notification = new Notification.Builder(this, "reminders").SetSmallIcon(Resource.Drawable.ic_notification)!
                        .SetContentTitle(msg.GetProperty("title").GetString())!.SetContentText(msg.GetProperty("body").GetString())!.SetContentIntent(pending)!.SetAutoCancel(true)!.Build())
                        manager.Notify(notificationId, notification);
                    break;
                case "exit": closing = true; Reply(new { type = "flush" }); break;
                case "terminal":
                    var action = msg.GetProperty("action").GetString();
                    if (action == "open")
                        new AlertDialog.Builder(this)!.SetTitle("Open an Android shell?")!
                            .SetMessage("Commands run on this device with Quantos’s permissions and can change your vault. This is Android sh; Windows PowerShell commands require Windows.")!
                            .SetNegativeButton("Cancel", (_, _) => Reply(new { type = "terminal", @event = "denied" }))!
                            .SetPositiveButton("Open shell", (_, _) => terminal?.Open())!.Show();
                    else if (action == "input") terminal?.Input(msg.GetProperty("id").GetString()!, msg.GetProperty("data").GetString()!);
                    else if (action is "stop" or "close") terminal?.Close(msg.GetProperty("id").GetString()!);
                    break;
                case "export-vault":
                    StartActivityForResult(new Intent(Intent.ActionCreateDocument).AddCategory(Intent.CategoryOpenable)!
                        .SetType("application/zip")!.PutExtra(Intent.ExtraTitle, "Quantos-vault-" + DateTime.Now.ToString("yyyy-MM-dd") + ".zip"), ExportVault);
                    break;
                case "import-vault":
                    new AlertDialog.Builder(this)!.SetTitle("Import a vault copy?")!
                        .SetMessage("The ZIP opens as a separate local vault. Your current vault is kept. Sync for the current vault will pause until you open it again.")!
                        .SetNegativeButton("Cancel", (_, _) => { })!.SetPositiveButton("Choose ZIP", (_, _) =>
                            StartActivityForResult(new Intent(Intent.ActionOpenDocument).AddCategory(Intent.CategoryOpenable)!.SetType("application/zip"), ImportVault))!.Show();
                    break;
                case "reauth": web?.LoadUrl(host!.SignIn()); break;
                case "download":
                    var path = msg.GetProperty("path").GetString()!;
                    BeginDownload(host!.Url + "api/files/" + string.Join('/', path.Split('/').Select(System.Uri.EscapeDataString)), Path.GetFileName(path), "application/octet-stream");
                    break;
                case "print-note":
                    var noteId = msg.GetProperty("id").GetString()!;
                    var session = host!.App.Services.GetRequiredService<VaultHost>().Require();
                    var exported = await Task.Run(() => session.Exports.ExportNode(noteId, Quantos.Core.Export.ExportFormat.Html, Actor.User));
                    var html = await File.ReadAllTextAsync(session.Vault.Absolute(exported.Path));
                    printView?.Destroy(); printView = new WebView(this);
                    printView.Settings.JavaScriptEnabled = false;
                    printView.Settings.AllowFileAccess = false;
                    printView.SetWebViewClient(new PrintReady(this, session.Writes.Require(noteId).Title));
                    printView.LoadDataWithBaseURL(host.Url + "api/files/exports/", html, "text/html", "UTF-8", null);
                    break;
                case "pending-saves":
                    if (closing && msg.GetProperty("count").GetInt32() == 0) { closing = false; MoveTaskToBack(true); }
                    else if (closing) { closing = false; ShowMessage("Some notes haven’t saved yet. Keep Quantos open and try again."); }
                    break;
                case "approve":
                    var id = msg.GetProperty("id").GetString();
                    var response = await host!.Http.GetFromJsonAsync<JsonElement>("api/approvals/" + System.Uri.EscapeDataString(id!));
                    if (response.GetProperty("state").GetString() != "Pending") break;
                    new AlertDialog.Builder(this)!.SetTitle("Allow this action?")!
                        .SetMessage(response.GetProperty("description").GetString())!
                        .SetOnCancelListener(new Cancelled(() => { _ = Decide(id!, false); }))!
                        .SetNegativeButton("Cancel", async (_, _) => await Decide(id!, false))!
                        .SetPositiveButton("Allow", async (_, _) => await Decide(id!, true))!.Show();
                    break;
                case "print":
                    var print = (Android.Print.PrintManager)GetSystemService(PrintService)!;
                    print.Print("Quantos", web!.CreatePrintDocumentAdapter("Quantos"), null);
                    break;
                case "open-folder": ShowMessage("Use Settings › Shared vault › Android files to export a copy to another app."); break;
            }
        }
        catch (Exception ex) { ShowMessage(ex.Message); }
    }

    private async Task Decide(string id, bool approve)
    {
        try { (await host!.Http.PostAsJsonAsync("api/approvals/" + System.Uri.EscapeDataString(id), new { approve })).EnsureSuccessStatusCode(); }
        catch (Exception ex) { ShowMessage(ex.Message); }
    }
    private void ShowMessage(string message)
    {
        if (IsDestroyed || IsFinishing) return;
        new AlertDialog.Builder(this)!.SetTitle("Quantos")!.SetMessage(message)!.SetPositiveButton("OK", (_, _) => { })!.Show();
    }
    private void ReplyNotificationPermission() => Reply(new { type = "notification-permission", granted = ((NotificationManager)GetSystemService(NotificationService)!).AreNotificationsEnabled() });
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is not null) Intent = intent;
        var node = intent?.GetStringExtra("nodeId");
        if (web is not null && Quantos.Core.Ulid.IsValid(node)) Reply(new { type = "open-node", id = node });
    }

    public override void OnBackPressed()
    {
        // The shared UI dismisses overlays and navigates its workspace before leaving.
        if (web is null) MoveTaskToBack(true); else Reply(new { type = "back" });
    }

    protected override void OnPause()
    {
        Reply(new { type = "flush" });
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        filePicker?.OnReceiveValue(null);
        filePicker = null;
        audioRequest?.Deny();
        terminal?.Dispose();
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && backCallback is not null)
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(backCallback);
        var oldWeb = web; web = null; oldWeb?.Destroy();
        printView?.Destroy(); printView = null;
        base.OnDestroy();
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == DownloadFile)
        {
            var source = downloadUrl; downloadUrl = null;
            if (resultCode != Result.Ok || data?.Data is null || source is null) return;
            try
            {
                using var response = await host!.Http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                using var destination = ContentResolver!.OpenOutputStream(data.Data, "wt") ?? throw new IOException("The destination could not be opened.");
                await response.Content.CopyToAsync(destination);
                ShowMessage("The file was saved.");
            }
            catch (Exception ex) { ShowMessage("The file could not be saved: " + ex.Message); }
            return;
        }
        if (requestCode is ExportVault or ImportVault)
        {
            if (resultCode != Result.Ok || data?.Data is null) return;
            try
            {
                var uri = data.Data;
                if (requestCode == ExportVault)
                {
                    await Task.Run(() =>
                    {
                        var session = host!.App.Services.GetRequiredService<VaultHost>().Require();
                        session.Writes.Exclusive(() =>
                        {
                            using var output = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("The destination could not be opened.");
                            using var archive = new ZipArchive(output, ZipArchiveMode.Create);
                            foreach (var file in SyncFiles.Manifest(session.Writes))
                            {
                                using var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest).Open();
                                using var source = File.OpenRead(SyncFiles.Validate(session.Vault, file.Path));
                                source.CopyTo(entry);
                            }
                            return true;
                        });
                    });
                    ShowMessage("The vault copy was exported.");
                }
                else
                {
                    await Task.Run(() => ImportCopy(uri));
                    web?.LoadUrl(host!.SignIn());
                }
            }
            catch (Exception ex) { ShowMessage("The file operation did not finish: " + ex.Message + ". Your original vault has been kept."); }
            return;
        }
        if (requestCode != PickFile) return;
        var callback = filePicker; filePicker = null;
        callback?.OnReceiveValue(WebChromeClient.FileChooserParams.ParseResult((int)resultCode, data));
    }

    private void ImportCopy(Uri uri)
    {
        var staging = Path.Combine(CacheDir!.AbsolutePath, "import-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(FilesDir!.AbsolutePath, "Imported Vault " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        var target = new Quantos.Core.Storage.Vault(destination);
        Directory.CreateDirectory(staging);
        try
        {
            using var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("The ZIP could not be opened.");
            // Document streams may not seek; spool a bounded archive before parsing it.
            var zipFile = Path.Combine(staging, "archive.zip");
            using (var output = File.Create(zipFile))
            {
                var buffer = new byte[81920]; int length;
                while ((length = input.Read(buffer)) > 0)
                {
                    if (output.Length + length > 1024L * 1024 * 1024) throw new IOException("The archive exceeds 1 GB.");
                    output.Write(buffer, 0, length);
                }
            }
            var files = new List<(string Path, string Staged)>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = ZipFile.OpenRead(zipFile))
            {
                long total = 0;
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    SyncFiles.Validate(target, entry.FullName);
                    if (!names.Add(entry.FullName) || files.Count >= 100000) throw new IOException("The ZIP contains duplicate paths or too many files.");
                    if (entry.Length > SyncFiles.MaxFileBytes) throw new IOException("A vault file exceeds the import limit.");
                    var staged = Path.Combine(staging, files.Count + ".data");
                    using var source = entry.Open(); using var output = File.Create(staged);
                    var chunk = new byte[81920]; int count;
                    while ((count = source.Read(chunk)) > 0)
                    {
                        if (output.Length + count > SyncFiles.MaxFileBytes || (total += count) > 1024L * 1024 * 1024)
                            throw new IOException("The unpacked vault exceeds the import limit.");
                        output.Write(chunk, 0, count);
                    }
                    if (output.Length != entry.Length) throw new IOException("The ZIP contains an incomplete file.");
                    files.Add((entry.FullName, staged));
                }
            }
            if (!files.Any(f => f.Path.StartsWith("workspaces/"))) throw new IOException("This ZIP has no Quantos workspaces.");
            var owner = host!.App.Services.GetRequiredService<VaultHost>();
            var original = owner.Require().Vault.Root;
            try
            {
                owner.Open(destination);
                var session = owner.Require();
                foreach (var file in files)
                    session.Writes.ApplySyncFile(new(file.Path, SyncFiles.HashFile(SyncFiles.Validate(session.Vault, file.Path)), File.ReadAllBytes(file.Staged)), Path.Combine(FilesDir!.AbsolutePath, "import-recovery"), Actor.User);
                session.Reconciler.Run();
            }
            catch { owner.Open(original); throw; }
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == Notifications) { ReplyNotificationPermission(); return; }
        if (requestCode != Microphone) return;
        if (grantResults.Length > 0 && grantResults[0] == Permission.Granted) audioRequest?.Grant([PermissionRequest.ResourceAudioCapture]);
        else audioRequest?.Deny();
        audioRequest = null;
    }

    private sealed class LocalNavigation(MainActivity owner) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url?.ToString();
            if (url is null) return true;
            if (url.StartsWith("quantos://native?", StringComparison.Ordinal))
            {
                if (request!.IsForMainFrame && owner.IsAppPage(view?.Url))
                {
                    var payload = request.Url!.GetQueryParameter("message");
                    if (payload is not null && payload.Length <= 16384)
                    {
                        owner.NativeMessage(payload);
                    }
                }
                return true;
            }
            if (owner.IsLocal(url)) return false;
            if (request!.IsForMainFrame && request.HasGesture && System.Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" or "mailto")
                try { owner.StartActivity(new Intent(Intent.ActionView, Uri.Parse(url))); } catch (ActivityNotFoundException) { }
            return true;
        }
    }

    private sealed class NativeChrome(MainActivity owner) : WebChromeClient
    {
        public override bool OnShowFileChooser(WebView? view, IValueCallback? callback, FileChooserParams? parameters)
        {
            owner.filePicker?.OnReceiveValue(null);
            owner.filePicker = callback;
            try { owner.StartActivityForResult(parameters!.CreateIntent(), PickFile); }
            catch (ActivityNotFoundException) { callback?.OnReceiveValue(null); owner.filePicker = null; }
            return true;
        }
        public override void OnPermissionRequest(PermissionRequest? request)
        {
            if (request is null) return;
            if (!owner.IsAppPage(owner.web?.Url) || !owner.IsLocal(request.Origin?.ToString()) || request.GetResources()?.Any(r => r != PermissionRequest.ResourceAudioCapture) != false)
            { request.Deny(); return; }
            owner.RunOnUiThread(() =>
            {
                if (owner.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted) request.Grant([PermissionRequest.ResourceAudioCapture]);
                else { owner.audioRequest?.Deny(); owner.audioRequest = request; owner.RequestPermissions([Manifest.Permission.RecordAudio], Microphone); }
            });
        }
        public override void OnPermissionRequestCanceled(PermissionRequest? request)
        { if (owner.audioRequest == request) owner.audioRequest = null; }
    }

    private sealed class DownloadHandler(MainActivity owner) : Java.Lang.Object, IDownloadListener
    {
        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition, string? mimetype, long contentLength)
        {
            owner.RunOnUiThread(() => owner.BeginDownload(url, "Quantos-export", mimetype ?? "application/octet-stream"));
        }
    }

    private void BeginDownload(string? url, string fileName, string mime)
    {
        if (!IsLocal(url) || !System.Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            !(parsed.AbsolutePath.StartsWith("/api/files/", StringComparison.Ordinal) || parsed.AbsolutePath.StartsWith("/api/sync/conflicts/", StringComparison.Ordinal)))
        { ShowMessage("This download cannot be saved from Quantos."); return; }
        downloadUrl = url;
        StartActivityForResult(new Intent(Intent.ActionCreateDocument).AddCategory(Intent.CategoryOpenable)!.SetType(mime)!.PutExtra(Intent.ExtraTitle, fileName), DownloadFile);
    }

    private sealed class PrintReady(MainActivity owner, string title) : WebViewClient
    {
        private bool printed;
        public override void OnPageFinished(WebView? view, string? url)
        {
            if (printed || view is null) return;
            printed = true;
            var manager = (Android.Print.PrintManager)owner.GetSystemService(PrintService)!;
            manager.Print(title, view.CreatePrintDocumentAdapter(title), null);
        }
    }

    private sealed class BackCallback(MainActivity owner) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => owner.OnBackPressed();
    }
    private sealed class Cancelled(Action action) : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        public void OnCancel(IDialogInterface? dialog) => action();
    }
    private sealed class SafeInsets : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30) && insets is not null)
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout())!;
                var keyboard = insets.GetInsets(WindowInsets.Type.Ime())!;
                view?.SetPadding(bars.Left, bars.Top, bars.Right, Math.Max(bars.Bottom, keyboard.Bottom));
            }
            return insets!;
        }
    }
}
