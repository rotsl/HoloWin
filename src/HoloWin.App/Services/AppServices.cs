using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HoloWin.Core;
using HoloWin.Platform.Windows;

namespace HoloWin.App.Services;

public sealed class AppServices
{
    public WorkspaceController Workspace { get; private set; } = null!;
    private GlobalHotkeyManager? _hotkeys;
    private readonly SystemTrayController _tray = new();
    public void ApplyHotkeys() => _hotkeys?.Configure(Workspace.Settings.Hotkeys, async key =>
    {
        try
        {
            if (Workspace.Calibration is not null || Workspace.Evaluation is not null) return;
            if (Enum.TryParse<DeskZone>(key, out var zone) && DeskZoneExtensions.All.Contains(zone))
                await Workspace.TriggerZoneAsync(zone);
        }
        catch (Exception ex) { Workspace.Log("Hotkey: " + ex.Message); }
    });
    public void Initialize(Window window)
    {
        var dispatcher = window.DispatcherQueue;
        Workspace = new WorkspaceController();
        _hotkeys = new GlobalHotkeyManager();
        _tray.Initialize("HoloWin - inputs stopped", () => { window.AppWindow.Show(); window.Activate(); }, () => window.Close(),
            async () => { try { await Workspace.PauseInputsAsync(); } catch (Exception ex) { Workspace.Log(ex.Message); } });
        Workspace.Changed += (_, _) => dispatcher.TryEnqueue(() =>
            _tray.SetStatus($"HoloWin | Mic: {(Workspace.IsRunning ? "on" : "off")} | Camera: {(Workspace.CameraRunning ? "on" : "off")} | Presence: {(Workspace.PresenceRunning ? "on" : "off")}"));
        _ = RestorePreferencesAsync();
        async Task RestorePreferencesAsync()
        {
            await Workspace.Ready;
            try { ApplyHotkeys(); }
            catch (Exception ex) { Workspace.Log("Hotkeys: " + ex.Message); }
            if (window.Content is FrameworkElement root && Enum.TryParse<ElementTheme>(Workspace.Settings.Theme, out var theme)) root.RequestedTheme = theme;
        }
        Workspace.Actions.ConfirmAsync = (action, ct) =>
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var dialog = new ContentDialog
                    {
                        XamlRoot = ((FrameworkElement)window.Content).XamlRoot,
                        Title = "Run action?", Content = action.DisplayName ?? action.Type.ToString(),
                        PrimaryButtonText = "Run", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
                    };
                    using var registration = ct.Register(() => dispatcher.TryEnqueue(() => dialog.Hide()));
                    completion.TrySetResult(await dialog.ShowAsync() == ContentDialogResult.Primary && !ct.IsCancellationRequested);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            })) completion.TrySetResult(false);
            return completion.Task;
        };
        bool closing = false;
        bool shuttingDown = false;
        window.AppWindow.Closing += async (_, args) =>
        {
            if (closing) return;
            args.Cancel = true;
            if (shuttingDown) return;
            shuttingDown = true;
            _hotkeys?.Dispose(); _hotkeys = null;
            _tray.Dispose();
            try
            {
                // Native driver teardown may block before its first await.
                var cleanup = Task.Run(async () => await Workspace.DisposeAsync());
                await cleanup.WaitAsync(TimeSpan.FromSeconds(8));
            }
            catch (TimeoutException)
            {
                // Terminate only this app after the user's explicit close request.
                Environment.Exit(0);
            }
            catch (Exception ex) { Workspace.Log("Shutdown: " + ex.Message); }
            finally { _hotkeys?.Dispose(); _tray.Dispose(); closing = true; window.Close(); }
        };
    }
}
