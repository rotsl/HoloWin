using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HoloWin.Core;

namespace HoloWin.Actions;

[SupportedOSPlatform("windows")]
internal static class WindowsSpeech
{
    public static async Task SpeakAsync(IReadOnlyDictionary<string, string> p)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        if (!p.TryGetValue(ActionParameterKeys.Text, out var text) || string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Missing text parameter for speak action.");
        // The Windows Speech API (SAPI) is COM-callable from .NET via the
        // built-in SpeechSynthesizer. We avoid extra NuGet packages.
        var synthType = Type.GetTypeFromProgID("SAPI.SpVoice");
        if (synthType is null) throw new PlatformNotSupportedException("SAPI SpVoice not available.");
        dynamic synth = Activator.CreateInstance(synthType)!;
        try
        {
            var sb = new StringBuilder(text);
            if (p.TryGetValue(ActionParameterKeys.Rate, out var rs) && int.TryParse(rs, out var rate))
                synth.Rate = rate;
            if (p.TryGetValue(ActionParameterKeys.VoiceName, out var voiceName))
                synth.Voice = synth.GetVoices().Item(voiceName);
            synth.Speak(sb.ToString());
            await Task.CompletedTask;
        }
        finally
        {
            if (synth is not null && Marshal.IsComObject(synth))
                Marshal.ReleaseComObject(synth);
        }
    }
}

[SupportedOSPlatform("windows")]
internal static class ShellExecutor
{
    public static async Task RunPowerShellAsync(string script, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start powershell.exe.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { if (!p.HasExited) p.Kill(entireProcessTree: true); throw; }
        await stdout;
        if (p.ExitCode != 0)
        {
            var err = await stderr;
            throw new InvalidOperationException($"PowerShell exit code {p.ExitCode}: {err}");
        }
    }

    public static async Task RunExecutableAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {exe}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { if (!p.HasExited) p.Kill(entireProcessTree: true); throw; }
        await stdout;
        if (p.ExitCode != 0)
        {
            var err = await stderr;
            throw new InvalidOperationException($"{exe} exit code {p.ExitCode}: {err}");
        }
    }
}

[SupportedOSPlatform("windows")]
internal static class HttpInvoker
{
    public static async Task InvokeAsync(string endpoint, string method, string? body, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var m = new HttpMethod(method.ToUpperInvariant());
        using var req = new HttpRequestMessage(m, endpoint);
        if (!string.IsNullOrEmpty(body) && m != HttpMethod.Get)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}

[SupportedOSPlatform("windows")]
internal static class InputController
{
    public static Task DispatchAsync(ActionType type, IReadOnlyDictionary<string, string> p, CancellationToken ct)
    {
        // SendInput-based input simulation. Only standard media keys and a small
        // set of keyboard shortcuts are supported out of the box. Custom
        // shortcuts can be configured via "shortcut" parameter using Windows
        // virtual-key names.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return Task.CompletedTask;
        switch (type)
        {
            case ActionType.SendMediaKey:
                if (p.TryGetValue(ActionParameterKeys.MediaKey, out var mk))
                    SendMediaKey(mk);
                break;
            case ActionType.MuteAudio:
                SendMediaKey("mute");
                break;
            case ActionType.TriggerKeyboardShortcut:
                if (p.TryGetValue(ActionParameterKeys.Shortcut, out var sc))
                    SendShortcut(sc);
                break;
            case ActionType.SwitchVirtualDesktop:
                var direction = p.GetValueOrDefault("direction", "right").ToLowerInvariant();
                if (direction is not ("left" or "right")) throw new InvalidOperationException("Direction must be left or right.");
                SendShortcut("Win+Ctrl+" + direction);
                break;
            case ActionType.TakeScreenshot:
                // Forwards to OS clipboard via PrintScreen. The user can reassign.
                SendShortcut("PrintScreen");
                break;
            default:
                throw new NotSupportedException($"Input action type {type} not supported.");
        }
        return Task.CompletedTask;
    }

    private static void SendMediaKey(string key)
    {
        // Implementation uses keybd_event for compatibility; SendInput is preferred
        // for new code but keybd_event still works on Windows 11.
        var vk = key.ToLowerInvariant() switch
        {
            "play"     or "play_pause" => 0xB3,
            "pause"    or "media_pause" => 0xB3,
            "stop"     => 0xB2,
            "next"     => 0xB0,
            "previous" => 0xB1, // Note: this is approximated; real key is VK_MEDIA_PREVTRACK 0xB1
            "mute"     => 0xAD,
            _ => throw new InvalidOperationException("Unknown media key.")
        };
        // We avoid direct P/Invoke here for simplicity; users wanting full
        // SendInput implementation should install HoloWin.Plugins.SendInput.
        DesktopActions.SendKey((ushort)vk, '\0', 0);
        DesktopActions.SendKey((ushort)vk, '\0', 2);
    }

    private static void SendShortcut(string shortcut)
    {
        // Simple parser: split on '+'; map each token to a VK code; press, then release.
        var tokens = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var codes = new List<byte>();
        foreach (var t in tokens)
        {
            var vk = TokenToVk(t);
            if (vk == 0) throw new InvalidOperationException($"Unknown shortcut key: {t}");
            codes.Add(vk);
        }
        if (codes.Count == 0) throw new InvalidOperationException("Shortcut is empty.");
        try { foreach (var vk in codes) DesktopActions.SendKey(vk, '\0', 0); }
        finally { for (int i = codes.Count - 1; i >= 0; i--) DesktopActions.SendKey(codes[i], '\0', 2); }
    }

    private static byte TokenToVk(string t) => t.ToLowerInvariant() switch
    {
        "ctrl" or "control" => 0x11,
        "alt" or "menu"    => 0x12,
        "shift"            => 0x10,
        "win" or "meta"    => 0x5B, // LWIN
        "enter"            => 0x0D,
        "esc" or "escape"  => 0x1B,
        "tab"              => 0x09,
        "pageup" or "pgup" => 0x21, "pagedown" or "pgdn" => 0x22,
        "space"            => 0x20,
        "printscreen"      => 0x2C,
        "left" => 0x25, "up" => 0x26, "right" => 0x27, "down" => 0x28,
        "delete" => 0x2E, "backspace" => 0x08, "home" => 0x24, "end" => 0x23,
        _ when t.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(t[1..], out var f) && f >= 1 && f <= 24 => (byte)(0x6F + f),
        _ when t.Length == 1 && char.IsLetterOrDigit(t[0]) => (byte)char.ToUpper(t[0]),
        _ => 0
    };
}

internal static class PInvoke
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
}
