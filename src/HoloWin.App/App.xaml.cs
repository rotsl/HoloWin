
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using HoloWin.App.Services;

namespace HoloWin.App;

public partial class App : Application
{
    private static Window? _mainWindow;
    private static readonly bool SmokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test");

    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(),
        "HoloWin-startup.log");

    public static Window MainWindow => _mainWindow!;

    public static AppServices Services { get; private set; } = new();

    public App()
    {
        Log("App constructor entered");

        try
        {
            InitializeComponent();
            Log("App InitializeComponent succeeded");

            UnhandledException += OnUnhandled;
            Log("App constructor completed");
        }
        catch (Exception ex)
        {
            Log($"App constructor failed: {ex}");
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched entered");

        try
        {
            if (SmokeTest)
            {
                // Exercise WinUI resources without profiles, hotkeys or sensor services.
                var control = new Microsoft.UI.Xaml.Controls.Button { Content = "HoloWin startup check" };
                control.Loaded += (_, _) => Environment.Exit(0);
                _mainWindow = new Window { Content = control };
                _mainWindow.Activate();
                return;
            }
            Log("Creating MainWindow");

            _mainWindow = new MainWindow();

            Log("MainWindow created successfully");

            // Register application services before activating the window.
            Log("Initializing services");

            Services.Initialize(_mainWindow);

            Log("Services initialized successfully");

            // Activate the window only after services are ready.
            _mainWindow.Activate();

            Log("MainWindow activated successfully");
        }
        catch (Exception ex)
        {
            Log($"STARTUP EXCEPTION: {ex}");
            throw;
        }
    }

    private void OnUnhandled(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log($"UNHANDLED XAML EXCEPTION: {e.Exception}");
        if (SmokeTest) Environment.Exit(1);

        Debug.WriteLine(
            $"[HoloWin] Unhandled: {e.Exception}");

        // Keep unexpected exceptions visible during debugging.
        e.Handled = false;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                LogPath,
                $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never cause an additional startup failure.
        }
    }
}
