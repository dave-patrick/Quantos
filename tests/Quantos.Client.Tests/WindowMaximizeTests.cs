using System.Reflection;
using Quantos.Client;

namespace Quantos.Client.Tests;

public sealed class WindowMaximizeTests
{
    [Fact]
    public async Task Maximizing_fits_monitor_working_area_and_offscreen_fallback_stays_within_working_area()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tempDir = Path.Combine(Path.GetTempPath(), "quantos-max-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var thread = new Thread(() =>
        {
            try
            {
                // Saved bounds completely outside all current monitors (regression for off-screen fallback).
                var prefs = new ClientPrefs { X = 99999, Y = -99999, Width = 6000, Height = 4000, Maximized = true };
                using var form = new MainForm(new ClientPaths(tempDir), prefs);
                form.ShowInTaskbar = false;
                form.Opacity = 0;

                var toggle = typeof(MainForm).GetMethod("ToggleMaximized", BindingFlags.NonPublic | BindingFlags.Instance)!;
                form.Shown += (_, _) =>
                {
                    try
                    {
                        var work = Screen.FromControl(form).WorkingArea;
                        Assert.Equal(FormWindowState.Maximized, form.WindowState);
                        Assert.Equal(work, form.Bounds);

                        toggle.Invoke(form, null);
                        Assert.Equal(FormWindowState.Normal, form.WindowState);
                        Assert.True(work.Contains(form.Bounds), $"Restored fallback bounds {form.Bounds} should fit inside {work}.");

                        toggle.Invoke(form, null);
                        work = Screen.FromControl(form).WorkingArea;
                        Assert.Equal(FormWindowState.Maximized, form.WindowState);
                        Assert.Equal(work, form.Bounds);

                        completion.SetResult();
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        Application.ExitThread();
                    }
                };

                Application.Run(form);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Maximizing_from_normal_state_fits_current_screen_working_area()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tempDir = Path.Combine(Path.GetTempPath(), "quantos-max-screen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var thread = new Thread(() =>
        {
            try
            {
                foreach (var screen in Screen.AllScreens)
                {
                    var area = screen.WorkingArea;
                    var width = Math.Min(area.Width - 100, 1000);
                    var height = Math.Min(area.Height - 100, 700);
                    var prefs = new ClientPrefs
                    {
                        X = area.Left + 40,
                        Y = area.Top + 40,
                        Width = Math.Max(720, width),
                        Height = Math.Max(480, height),
                        Maximized = false
                    };
                    using var form = new MainForm(new ClientPaths(tempDir), prefs);
                    form.ShowInTaskbar = false;
                    form.Opacity = 0;

                    var toggle = typeof(MainForm).GetMethod("ToggleMaximized", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    form.Shown += (_, _) =>
                    {
                        try
                        {
                            toggle.Invoke(form, null);
                            var expected = Screen.FromControl(form).WorkingArea;
                            Assert.Equal(FormWindowState.Maximized, form.WindowState);
                            Assert.Equal(expected, form.Bounds);
                            toggle.Invoke(form, null);
                            Assert.Equal(FormWindowState.Normal, form.WindowState);
                        }
                        finally
                        {
                            Application.ExitThread();
                        }
                    };

                    Application.Run(form);
                }
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
