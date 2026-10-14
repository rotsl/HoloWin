using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HoloWin.Platform.Windows;

/// <summary>Owns a message-only window on the UI thread for registered zone keys.</summary>
public sealed class GlobalHotkeyManager : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _callbacks = new();
    public GlobalHotkeyManager() => CreateHandle(new CreateParams { Caption = "HoloWin hotkeys", Parent = new IntPtr(-3) });

    public void Configure(IReadOnlyDictionary<string, string> bindings, Action<string> trigger)
    {
        Clear();
        try
        {
            int id = 1;
            foreach (var (zone, shortcut) in bindings)
            {
                if (string.IsNullOrWhiteSpace(shortcut)) continue;
                var tokens = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                uint modifiers = 0x4000; // MOD_NOREPEAT
                foreach (var token in tokens.SkipLast(1))
                    modifiers |= token.ToUpperInvariant() switch
                    {
                        "CTRL" or "CONTROL" => 2u, "ALT" => 1u, "SHIFT" => 4u, "WIN" => 8u,
                        _ => throw new ArgumentException("Unknown hotkey modifier: " + token)
                    };
                if (tokens.Length < 2 || (modifiers & 11) == 0)
                    throw new ArgumentException("Use Ctrl, Alt or Win with a key, for example Ctrl+Alt+1.");
                string keyName = tokens[^1];
                if (keyName.Length == 1 && char.IsDigit(keyName[0])) keyName = "D" + keyName;
                if (!Enum.TryParse<Keys>(keyName, true, out var key) || (int)key <= 0 || (int)key > 255)
                    throw new ArgumentException("Unknown hotkey key: " + tokens[^1]);
                if (!RegisterHotKey(Handle, id, modifiers, (uint)key))
                    throw new InvalidOperationException($"Hotkey {shortcut} is already in use or unavailable.");
                string captured = zone;
                _callbacks[id++] = () => trigger(captured);
            }
        }
        catch { Clear(); throw; }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && _callbacks.TryGetValue(message.WParam.ToInt32(), out var callback)) callback();
        base.WndProc(ref message);
    }
    private void Clear() { foreach (int id in _callbacks.Keys) UnregisterHotKey(Handle, id); _callbacks.Clear(); }
    public void Dispose() { Clear(); DestroyHandle(); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
