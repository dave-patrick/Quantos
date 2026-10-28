using System.Reflection;
using System.Text.Json;
using Quantos.Client;

namespace Quantos.Client.Tests;

public sealed class WindowAppearanceTests
{
    [Fact]
    public async Task Appearance_bridge_updates_native_edge_and_controls_and_rejects_invalid_or_popup_messages()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(new ClientPaths(Path.GetTempPath()), new ClientPrefs());
                var handler = typeof(MainForm).GetMethod("HandleMessage", BindingFlags.NonPublic | BindingFlags.Instance)!;
                void Send(string bg, bool main = true)
                {
                    using var message = JsonDocument.Parse(JsonSerializer.Serialize(new { type = "appearance", background = bg, foreground = "#26364a", text = "#0c131d", hover = "#e4eaf3", active = "#d9e4f2" }));
                    handler.Invoke(form, [message.RootElement, null, main]);
                }
                Send("#f5f7fb");
                Assert.Equal(ColorTranslator.FromHtml("#f5f7fb"), form.BackColor);
                var bar = form.Controls.OfType<Panel>().Single();
                Assert.Equal(form.BackColor, bar.BackColor);
                foreach (var button in bar.Controls.OfType<CaptionButton>()) Assert.Equal(form.BackColor, button.BackColor);
                Send("#2a2b2d");
                Assert.Equal(ColorTranslator.FromHtml("#2a2b2d"), form.BackColor);
                Send("#abcdef", main: false);
                Send("invalid");
                Assert.Equal(ColorTranslator.FromHtml("#2a2b2d"), form.BackColor);
                Send("#faf0e6");
                Assert.Equal(ColorTranslator.FromHtml("#faf0e6"), form.BackColor);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
