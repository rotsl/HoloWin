using System.Runtime.Versioning;
using System.IO;
using HoloWin.Core;

namespace HoloWin.Platform.Windows;

/// <summary>
/// Per-app Windows registry helpers for opt-in startup. Honors "do not start
/// automatically unless enabled": the registry key is only written when the
/// user explicitly enables the option in Settings.
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "HoloWin";

    public static bool IsAutoStartEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return false;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) is string s && !string.IsNullOrEmpty(s);
        }
    }

    public static void EnableAutoStart(string executablePath)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(AppName, executablePath, Microsoft.Win32.RegistryValueKind.String);
    }

    public static void DisableAutoStart()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
}

public sealed class ApplicationPaths
{
    public string AppDataRoot { get; }
    public string ProfilesDir { get; }
    public string EvaluationsDir { get; }
    public string DebugCapturesDir { get; }
    public string LogsDir { get; }
    public string ModelsDir { get; }

    public ApplicationPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppDataRoot = Path.Combine(localAppData, "HoloWin");
        ProfilesDir = Path.Combine(AppDataRoot, "Profiles");
        EvaluationsDir = Path.Combine(AppDataRoot, "Evaluations");
        DebugCapturesDir = Path.Combine(AppDataRoot, "DebugCaptures");
        LogsDir = Path.Combine(AppDataRoot, "Logs");
        ModelsDir = AppContext.BaseDirectory; // models ship next to the binary
        foreach (var d in new[] { AppDataRoot, ProfilesDir, EvaluationsDir, DebugCapturesDir, LogsDir })
            Directory.CreateDirectory(d);
    }
}

[SupportedOSPlatform("windows")]
public sealed class SystemTrayController : IDisposable
{
    private System.Windows.Forms.NotifyIcon? _icon;
    private System.Drawing.Icon? _artwork;
    private bool _disposed;

    public void Initialize(string tooltip, Action onShow, Action onQuit, Action? onPause = null)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "HoloWin.ico");
        if (System.IO.File.Exists(iconPath)) _artwork = new System.Drawing.Icon(iconPath, 32, 32);
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _artwork ?? System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = tooltip
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show HoloWin", null, (_, _) => onShow());
        if (onPause is not null) menu.Items.Add("Pause sensors", null, (_, _) => onPause());
        menu.Items.Add("-");
        menu.Items.Add("Quit", null, (_, _) => onQuit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => onShow();
    }

    public void SetStatus(string text)
    {
        if (_icon is not null) _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        _artwork?.Dispose();
    }
}

