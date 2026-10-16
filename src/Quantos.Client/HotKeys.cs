using System.Runtime.InteropServices;

namespace Quantos.Client;

/// <summary>System-wide shortcuts (BUILD-PLAN 8E.1, 8E.9). A shortcut another app already owns is reported, not stolen.</summary>
public sealed class HotKeys : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Dictionary<int, Action> _actions = new();
    private int _next = 1;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotKeys() => CreateHandle(new CreateParams());

    /// <summary>Registers "Ctrl+Shift+Space" style shortcuts. Returns an error message, or null on success.</summary>
    public string? Register(string combo, Action action)
    {
        if (string.IsNullOrWhiteSpace(combo)) return null;
        uint mods = 0x4000; // MOD_NOREPEAT
        Keys key = Keys.None;
        foreach (var part in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= 0x2; break;
                case "alt": mods |= 0x1; break;
                case "shift": mods |= 0x4; break;
                case "win": mods |= 0x8; break;
                default:
                    if (!Enum.TryParse(part, true, out key) && !(part.Length == 1 && Enum.TryParse(part.ToUpperInvariant(), out key)))
                        return $"“{part}” in {combo} isn't a key Quantos recognises.";
                    break;
            }
        }
        if (key == Keys.None) return $"{combo} needs a key as well as modifiers.";
        var id = _next++;
        if (!RegisterHotKey(Handle, id, mods, (uint)key))
            return $"{combo} is already used by another program. Choose a different shortcut in Settings.";
        _actions[id] = action;
        return null;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action)) action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Clear();
        DestroyHandle();
    }
}
