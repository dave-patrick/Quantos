using Quantos.Client;

namespace Quantos.Client.Tests;

public sealed class ScriptConfirmationTests
{
    [Theory]
    [InlineData(2000, false)]
    [InlineData(100000, false)]
    [InlineData(2000, true)]
    public async Task Native_review_preserves_the_complete_script_and_requires_an_explicit_run(int padding, bool approve)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var code = "# " + new string('x', padding) + "\r\nWrite-Output 'END-OF-SCRIPT'";
                using var owner = new Form { ShowInTaskbar = false };
                using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                Exception? inspectionFailure = null;
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var dialog = Application.OpenForms.Cast<Form>().Single(f => f.Text == "Run this PowerShell code?");
                    try
                    {
                        var preview = Assert.IsType<TextBox>(dialog.Controls.Find("ScriptPreview", true).Single());
                        Assert.Equal(code, preview.Text);
                        Assert.True(preview.ReadOnly);
                        Assert.Equal(ScrollBars.Both, preview.ScrollBars);
                        Assert.Equal(DialogResult.Cancel, Assert.IsType<Button>(dialog.AcceptButton).DialogResult);
                        Assert.Equal(DialogResult.Cancel, Assert.IsType<Button>(dialog.CancelButton).DialogResult);
                        preview.SelectionStart = preview.TextLength;
                        preview.ScrollToCaret();
                        Assert.EndsWith("Write-Output 'END-OF-SCRIPT'", preview.Text);
                    }
                    catch (Exception ex) { inspectionFailure = ex; }
                    finally
                    {
                        var buttons = dialog.Controls.Cast<Control>().SelectMany(Descendants).OfType<Button>();
                        buttons.Single(b => b.DialogResult == (approve ? DialogResult.OK : DialogResult.Cancel)).PerformClick();
                    }
                };
                owner.Show();
                timer.Start();
                Assert.Equal(approve, ScriptConfirmation.Show(owner, code, @"C:\review-marker"));
                if (inspectionFailure is not null) throw inspectionFailure;
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        foreach (Control child in control.Controls)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
