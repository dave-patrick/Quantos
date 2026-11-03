using Quantos.Client;

namespace Quantos.Client.Tests;

public class BrowserProfileTests
{
    [Fact]
    public void Reuses_a_successfully_selected_fallback_profile()
    {
        var root = Path.Combine(Path.GetTempPath(), "quantos-profiles", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new ClientPaths(root);
            var fallback = Path.Combine(root, "webview2-20261029-143745");
            Directory.CreateDirectory(fallback);
            Assert.Equal(Path.Combine(root, "webview2"), paths.ActiveWebView2);
            paths.RememberWebView2(fallback);
            Assert.Equal(fallback, new ClientPaths(root).ActiveWebView2);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
