using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using HoloWin.App.Services;
using HoloWin.Calibration;
using HoloWin.Actions;
using HoloWin.Gestures;
using HoloWin.Core;
using HoloWin.Persistence;
using HoloWin.Platform.Windows;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;
using Windows.Foundation;
using WinRT.Interop;

namespace HoloWin.App.Views.Pages;

public partial class WorkbenchPage : Page
{
    private readonly string _section;
    private readonly StackPanel _body = new() { Spacing = 16, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Action? _refresh;
    private bool _built;
    private int _frameQueued;
    private bool _pageVisible;
    private bool _previewErrorLogged;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui;
    protected WorkspaceController W => App.Services.Workspace;
    public WorkbenchPage(string section)
    {
        _section = section;
        _ui = DispatcherQueue;
        Content = new ScrollViewer { Content = _body, Padding = new Thickness(12), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += OnLoaded; Unloaded += OnUnloaded;
        _timer.Tick += (_, _) => _refresh?.Invoke();
    }
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _pageVisible = true;
        try
        {
            await W.Ready;
            if (!_built)
            {
                _body.Children.Add(new TextBlock { Text = _section, FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                _body.Children.Add(_message);
                switch (_section)
                {
                    case "Dashboard": Dashboard(); break;
                    case "Hardware": Hardware(); break;
                    case "Profiles": await Profiles(); break;
                    case "Calibration": Calibration(); break;
                    case "Evaluation": await Evaluation(); break;
                    case "Actions": Actions(); break;
                    case "Settings": Settings(); break;
                    case "Camera Studio": Camera(false); break;
                    case "Desk Map": Camera(true); break;
                }
                _built = true;
            }
            _timer.Start(); _refresh?.Invoke();
            if (_section is "Camera Studio" or "Desk Map")
            { W.CameraFrameAvailable -= OnCameraFrame; W.CameraFrameAvailable += OnCameraFrame; }
            if (_section == "Dashboard" && !W.Settings.SetupComplete) await WelcomeAsync();
        }
        catch (Exception ex) { _message.Text = ex.Message; }
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _pageVisible = false;
        _timer.Stop();
        if (_section is "Camera Studio" or "Desk Map")
        { W.CameraFrameAvailable -= OnCameraFrame; if (!W.TrackingRunning) _ = StopPreviewAsync(); }
    }
    private async Task StopPreviewAsync() { try { await W.StopCameraAsync(); } catch (Exception ex) { W.Log(ex.Message); } }
    private void Note(string text) => _body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private StackPanel Section(string title)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _body.Children.Add(new Border { Child = content, Padding = new Thickness(18), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.Gray) });
        return content;
    }
    private static StackPanel Row(Panel parent)
    { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; parent.Children.Add(row); return row; }
    private Button Button(Panel panel, string text, Func<Task> action)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false; _message.Text = "";
            try { await action(); _refresh?.Invoke(); }
            catch (OperationCanceledException) { _message.Text = "Cancelled."; }
            catch (Exception ex) { _message.Text = ex.Message; W.Log(ex.Message); }
            finally { button.IsEnabled = true; }
        };
        panel.Children.Add(button); return button;
    }
    private static TextBox Text(Panel panel, string header, string value = "")
    { var box = new TextBox { Header = header, Text = value, MinWidth = 220 }; panel.Children.Add(box); return box; }
    private static NumberBox Number(Panel panel, string header, double value, double min, double max)
    { var box = new NumberBox { Header = header, Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 220 }; panel.Children.Add(box); return box; }
    private static CheckBox Check(Panel panel, string label, bool selected)
    { var box = new CheckBox { Content = label, IsChecked = selected }; panel.Children.Add(box); return box; }
    private static ComboBox Choice(Panel panel, string header, IEnumerable<string> choices, int selected = 0)
    { var box = new ComboBox { Header = header, ItemsSource = choices.ToArray(), SelectedIndex = selected, MinWidth = 220 }; panel.Children.Add(box); return box; }
    private async Task<bool> Confirm(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = message,
            PrimaryButtonText = "Confirm", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task<string?> Pick(bool save, string extension, string suggested)
    {
        var handle = WindowNative.GetWindowHandle(App.MainWindow);
        if (save)
        {
            var picker = new FileSavePicker { SuggestedFileName = suggested };
            picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant() + " file", new List<string> { extension });
            InitializeWithWindow.Initialize(picker, handle); return (await picker.PickSaveFileAsync())?.Path;
        }
        else
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(extension);
            InitializeWithWindow.Initialize(picker, handle); return (await picker.PickSingleFileAsync())?.Path;
        }
    }
    private async Task WelcomeAsync()
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Label("HoloWin processes desk taps locally. Microphone capture and camera preview start only when you explicitly enable them."));
        var mic = Check(content, "Enable microphone controls (capture remains stopped)", false);
        content.Children.Add(Label("Next: select your microphone in Hardware, create a profile in Profiles, then collect ten taps per zone in Calibration. Camera gestures can be configured in Actions and started in Camera Studio."));
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Set up HoloWin", Content = content,
            PrimaryButtonText = "Get started", CloseButtonText = "Later" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        { W.Settings.MicrophoneEnabled = mic.IsChecked == true; W.Settings.SetupComplete = true; await W.SaveSettingsAsync(); }
    }
    private void Dashboard()
    {
        Note("Listen for calibrated desk taps, or trigger a zone manually. Camera gestures can be configured in Actions and started in Camera Studio.");
        var session = Section("Session"); var status = Label(""); session.Children.Add(status);
        var buttons = Row(session); Button(buttons, "Start listening", W.StartListeningAsync); Button(buttons, "Pause", W.StopAudioAsync);
        Button(buttons, "Stop camera", W.StopCameraAsync);
        Button(buttons, "Pause sensors", W.PauseInputsAsync);
        var hardware = Section("Detected hardware"); var devices = Label(""); hardware.Children.Add(devices);
        var zones = Section("Desk zones · click or touch to trigger");
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new()); zones.Children.Add(grid);
        foreach (var zone in DeskZoneExtensions.All)
        {
            var button = Button(grid, zone.DisplayName(), () => W.TriggerZoneAsync(zone));
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.MinHeight = 90;
            Grid.SetRow(button, zone.Row()); Grid.SetColumn(button, zone.Column());
        }
        var diagnostics = Section("Diagnostics"); var stats = Label(""); diagnostics.Children.Add(stats);
        var events = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 260 };
        diagnostics.Children.Add(events);
        var tools = Row(diagnostics);
        Button(tools, "Export log", async () => { if (await Pick(true, ".txt", "HoloWin-diagnostics") is { } path) await W.ExportDiagnosticsAsync(path); });
        Button(tools, "Open logs folder", () => { Process.Start(new ProcessStartInfo(W.Paths.LogsDir) { UseShellExecute = true }); return Task.CompletedTask; });
        Button(tools, "Delete debug recordings", async () => { if (await Confirm("Delete recordings?", "Delete all HoloWin debug WAV recordings?")) W.DeleteDebugCaptures(); });
        _refresh = () =>
        {
            status.Text = $"{W.Status}\nProfile: {W.Profile?.Name ?? "None selected"}\nCamera: {(W.CameraRunning ? "preview active" : "off")} · Motion sensors: {(W.MotionRunning ? "active" : "off")}\nPresence: {(W.PresenceRunning ? "monitoring" : "off")} · Presence actions: {(W.PresenceActionsEnabled ? "armed" : "disarmed")}";
            status.Text += $"\nCalibration: {(W.Profile?.CalibrationIsComplete == true ? "saved — ready to listen" : "collect and save ten taps per zone")} · Mode: {W.Settings.FusionMode}";
            status.Text += $"\n{W.AudioDiagnostics}\nCamera tracking: {W.TrackingStatus} · Gesture actions: {(W.GestureActionsEnabled ? "armed" : "disarmed")}";
            devices.Text = string.Join("\n", W.Hardware.Snapshot().Select(d => $"{d.FriendlyName} · {d.State}"));
            if (devices.Text.Length == 0) devices.Text = "No devices returned by the hardware scan. Rescan in Hardware.";
            stats.Text = $"Queue drops: {W.QueueDrops} · Noise floor: {W.NoiseFloor:F6}\nLast decision: {W.LastDecision?.Zone?.DisplayName() ?? W.LastDecision?.RejectionReason?.ToString() ?? "No taps yet"} · Confidence: {W.LastDecision?.Confidence:P0} · Processing: {W.LastDecision?.ProcessingLatencyMilliseconds:F1} ms";
            var log = string.Join(Environment.NewLine, W.RecentEvents.Take(40)); if (events.Text != log) events.Text = log;
        };
    }
    private void Hardware()
    {
        Note("Device inventory does not activate capture. HID/Bluetooth entries need a device-specific protocol before they can produce HoloWin observations.");
        var panel = Section("Microphone");
        var mic = new ComboBox { Header = "Input device", MinWidth = 320, DisplayMemberPath = "FriendlyName" }; panel.Children.Add(mic);
        void FillMicrophones()
        {
            var list = W.Hardware.Snapshot().Where(d => d.Class == DeviceClass.AudioInput && d.State == DeviceState.Available).ToList();
            mic.ItemsSource = list; mic.SelectedItem = list.FirstOrDefault(d => d.Id == W.Settings.MicrophoneId) ?? list.FirstOrDefault(d => d.IsDefault) ?? list.FirstOrDefault();
        }
        FillMicrophones(); var enabled = Check(panel, "Enable microphone", W.Settings.MicrophoneEnabled);
        Button(panel, "Apply microphone", async () =>
        { await W.StopAudioAsync(); W.Settings.MicrophoneEnabled = enabled.IsChecked == true; W.Settings.MicrophoneId = (mic.SelectedItem as DeviceCapability)?.Id; await W.SaveSettingsAsync(); _message.Text = "Saved. Start capture from Dashboard or Calibration."; });
        var controls = Row(panel);
        Button(controls, "Microphone privacy", () => OpenUri("ms-settings:privacy-microphone"));
        Button(controls, "Camera privacy", () => OpenUri("ms-settings:privacy-webcam"));
        var sensors = Section("Motion and ambient sensors"); var readings = Label(""); sensors.Children.Add(readings);
        var sensorButtons = Row(sensors);
        Button(sensorButtons, "Start sensor readings", async () => { W.SetMotionEnabled(true); await W.SaveSettingsAsync(); });
        Button(sensorButtons, "Stop sensor readings", async () => { W.SetMotionEnabled(false); await W.SaveSettingsAsync(); });
        var lightArmed = Check(sensors, "Enable light rules for this session", W.LightActionsEnabled);
        lightArmed.Checked += (_, _) =>
        {
            if (!W.MotionRunning || !W.HasLightSensor || W.Profile is null) { lightArmed.IsChecked = false; _message.Text = "Start sensor readings with an available light sensor and select a profile first."; return; }
            if (!W.LightActionsEnabled) W.LightActionsEnabled = true;
        };
        lightArmed.Unchecked += (_, _) => W.LightActionsEnabled = false;
        var refreshPresence = AddPresenceHardware();
        var inventory = Section("Device inventory"); var details = Label(""); inventory.Children.Add(details);
        Button(inventory, "Rescan devices", async () => { await W.Hardware.RescanAsync(default); FillMicrophones(); });
        _refresh = () => { refreshPresence(); lightArmed.IsChecked = W.LightActionsEnabled; readings.Text = W.SensorReading + $"\nLight rules: {(W.LightActionsEnabled ? "armed" : "disarmed")}"; details.Text = string.Join("\n\n", W.Hardware.Snapshot().Select(d => $"{d.Class} · {d.FriendlyName}\n{d.State} — {d.StateDetail ?? "Detected"}")); };
    }
    private static Task OpenUri(string uri) { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return Task.CompletedTask; }
    private async Task Profiles()
    {
        var panel = Section("Saved profiles");
        var list = new ComboBox { Header = "Profile", DisplayMemberPath = "Name", MinWidth = 300 }; panel.Children.Add(list);
        async Task Reload() { var all = await W.Profiles.ListAsync(default); list.ItemsSource = all; list.SelectedItem = all.FirstOrDefault(p => p.Id == W.Profile?.Id) ?? all.FirstOrDefault(); }
        await Reload(); var name = Text(panel, "Name", W.Profile?.Name ?? "My desk");
        var description = Text(panel, "Desk description", W.Profile?.DeskDescription ?? "");
        var strategy = Choice(panel, "Sensing strategy", Enum.GetNames<SensingStrategy>());
        var buttons = Row(panel);
        Button(buttons, "Create", async () => { await W.CreateProfileAsync(name.Text, (SensingStrategy)strategy.SelectedIndex); await Reload(); });
        Button(buttons, "Use selected", async () =>
        { if (list.SelectedItem is ProfileSummary p) { await W.SelectProfileAsync(p.Id); name.Text = W.Profile!.Name; description.Text = W.Profile.DeskDescription ?? ""; } });
        Button(buttons, "Save name and description", async () =>
        { if (W.Profile is null) throw new InvalidOperationException("Use a profile first."); if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a name."); W.Profile.Name = name.Text.Trim(); W.Profile.DeskDescription = description.Text; await W.SaveProfileAsync(); await Reload(); });
        var files = Row(panel);
        Button(files, "Import", async () => { if (await Pick(false, ".json", "") is { } path) { await W.ImportProfileAsync(path); await Reload(); } });
        Button(files, "Export current", async () => { if (await Pick(true, ".json", "HoloWin-profile") is { } path) await W.ExportProfileAsync(path); });
        Button(files, "Delete selected", async () => { if (list.SelectedItem is ProfileSummary p && await Confirm("Delete profile?", p.Name)) { await W.DeleteProfileAsync(p.Id); await Reload(); } });
        var info = Label(""); panel.Children.Add(info);
        _refresh = () => info.Text = W.Profile is { } p ? $"Active: {p.Name}\nStrategy: {p.Strategy} · Positive samples: {p.PositiveExamples.Count} · Negative samples: {p.NegativeExamples.Count}\nCalibration: {(p.CalibrationIsComplete ? "complete" : "incomplete")}" : "No active profile.";
    }
    private void Calibration()
    {
        Note("Select a profile and enable the microphone first. Prepare each zone, wait for Listening, then make ten spaced taps. Actions are suspended during calibration.");
        var panel = Section("Capture"); var buttons = Row(panel);
        Button(buttons, "Begin new calibration", W.BeginCalibrationAsync);
        Button(buttons, "Cancel", W.CancelSessionsAsync);
        var zones = Row(panel);
        foreach (var zone in DeskZoneExtensions.All) Button(zones, "Prepare " + zone.DisplayName(), () => W.ArmCalibrationAsync(zone));
        var status = Label(""); panel.Children.Add(status);
        var controls = Row(panel);
        Button(controls, "Disarm", () => { W.Calibration?.Disarm(); W.NegativeLabel = null; return Task.CompletedTask; });
        Button(controls, "Undo last tap", () => { W.Calibration?.UndoLast(); return Task.CompletedTask; });
        var negatives = Section("Negative examples");
        var category = Choice(negatives, "Sound category", new[] { "Talking", "Typing", "Room noise" });
        Button(negatives, "Start negative collection", () =>
        { if (W.Calibration is null || !W.IsRunning) throw new InvalidOperationException("Begin calibration first."); W.Calibration.Disarm(); W.NegativeLabel = category.SelectedItem.ToString(); return Task.CompletedTask; });
        Button(negatives, "Stop negative collection", () => { W.NegativeLabel = null; return Task.CompletedTask; });
        var finish = Row(panel); Button(finish, "Validate calibration", W.ValidateCalibrationAsync); Button(finish, "Save calibration", W.SaveCalibrationAsync);
        _refresh = () => status.Text = W.Calibration is { } c
            ? $"{W.AudioDiagnostics}\nState: {c.State} · Zone: {c.ActiveZone?.DisplayName() ?? "none"}\n" + string.Join(" · ", c.CountsPerZone.Select(kv => $"{kv.Key.ShortName()}: {kv.Value}/10"))
                + $"\nRejected: {c.RejectedCount} · Negative examples: {c.NegativeExamples.Count} · Collecting: {W.NegativeLabel ?? "positive taps"}\nLeave-one-out agreement: {(W.CalibrationAgreement is { } a ? a.ToString("P0") : "not run")}" : "No active calibration session.";
    }
    private async Task Evaluation()
    {
        Note("Use taps that were not part of calibration. Collect fifteen trials per zone. Actions are suspended during evaluation.");
        var panel = Section("Held-out evaluation"); var buttons = Row(panel);
        Button(buttons, "Begin evaluation", W.BeginEvaluationAsync); Button(buttons, "Cancel", W.CancelSessionsAsync);
        var zones = Row(panel);
        foreach (var zone in DeskZoneExtensions.All) Button(zones, zone.DisplayName(), () => { (W.Evaluation ?? throw new InvalidOperationException("Begin evaluation first.")).Arm(zone); return Task.CompletedTask; });
        var progress = Label(""); panel.Children.Add(progress);
        var history = Section("Saved reports"); var list = new ComboBox { Header = "Report", DisplayMemberPath = "CreatedAtUtc", MinWidth = 300 }; history.Children.Add(list);
        var reportText = Label(""); history.Children.Add(reportText);
        async Task Reload() { list.ItemsSource = await W.Reports.ListAsync(W.Profile?.Id, default); list.SelectedIndex = 0; }
        await Reload();
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is EvaluationReport r) reportText.Text = ReportText(r); };
        Button(panel, "Save completed evaluation", async () => { await W.SaveEvaluationAsync(); await Reload(); });
        var tools = Row(history);
        Button(tools, "Refresh reports", Reload);
        Button(tools, "Export report", async () =>
        { if (list.SelectedItem is EvaluationReport r && await Pick(true, ".txt", "HoloWin-evaluation") is { } path) await File.WriteAllTextAsync(path, ReportText(r)); });
        Button(tools, "Delete report", async () =>
        { if (list.SelectedItem is EvaluationReport r && await Confirm("Delete report?", r.CreatedAtUtc.ToString("g"))) { await W.Reports.DeleteAsync(r.Id, default); await Reload(); } });
        _refresh = () => progress.Text = W.Evaluation is { } e ? $"State: {e.CurrentState} · Armed: {e.ArmedZone?.DisplayName() ?? "choose a zone"}\n" + ReportText(e.ToReport()) : "No active evaluation.";
    }
    private static string ReportText(EvaluationReport r) => $"Trials: {r.TotalTaps}/60 · Correct: {r.CorrectTaps} · Rejected: {r.RejectedTaps}\nAccuracy: {r.OverallAccuracy:P1} · Median processing: {r.MedianLatencyMs:F1} ms\n"
        + string.Join("\n", DeskZoneExtensions.All.Select(z => $"{z.DisplayName()}: {r.PerZoneCorrect.GetValueOrDefault(z)}/{r.PerZoneTotal.GetValueOrDefault(z)} correct"))
        + "\nConfusion matrix (LR, LF, RR, RF):\n" + string.Join("\n", r.ConfusionMatrix.Select(row => string.Join("   ", row)));

    private void Actions()
    {
        Note("Create an action, then assign it to a desk zone. Tests execute real actions and use the same approval and confirmation rules as automatic dispatch.");
        Note("After saving and assigning an action, open Dashboard and press Start listening. One accepted tap in the assigned zone runs that action; the ten taps per zone are only for calibration.");
        var panel = Section("Action editor");
        var list = new ComboBox { Header = "Saved action", DisplayMemberPath = "DisplayName", MinWidth = 300 }; panel.Children.Add(list);
        var name = Text(panel, "Action name");
        var catalog = ActionCatalog.Items.Where(i => i.Type != ActionType.SetVolume || W.Library.Actions.Any(a => a.Type == ActionType.SetVolume)).ToArray(); var types = catalog.Select(i => i.Type).ToArray();
        var type = Choice(panel, "Action type", catalog.Select(i => i.Label));
        var parameters = new StackPanel { Spacing = 8 }; panel.Children.Add(parameters);
        panel.Children.Add(Label("Keyboard and tab actions affect the foreground app. Function keys send F1–F10, not a laptop's Fn media layer. Volume steps affect the default audio output. Brightness steps affect displays that expose Windows brightness control; many external monitors do not."));
        var fields = new Dictionary<string, TextBox>();
        HoloAction? editing = null;
        void ParameterFields()
        {
            parameters.Children.Clear(); fields.Clear();
            if (type.SelectedIndex < 0) return;
            var keys = types[type.SelectedIndex] switch
            {
                ActionType.PlaySound => new[] { ("path", "WAV file path (empty = system sound)") },
                ActionType.TypeText or ActionType.CopyText => new[] { ("text", "Text") },
                ActionType.SpeakText => new[] { ("text", "Text"), ("rate", "Speech rate (-10 to 10)") },
                ActionType.OpenWebsite => new[] { ("url", "Website URL") },
                ActionType.OpenApplication or ActionType.FocusApplication => new[] { ("app_path", "Application executable path") },
                ActionType.OpenFileOrFolder => new[] { ("path", "File or folder path") },
                ActionType.RunPowerShell => new[] { ("script", "PowerShell script") },
                ActionType.RunExecutable => new[] { ("executable", "Executable path"), ("arguments", "Arguments") },
                ActionType.InvokeHttpEndpoint => new[] { ("endpoint", "Endpoint URL"), ("method", "HTTP method"), ("body", "Request body") },
                ActionType.FunctionKey => new[] { ("key", "Function key: F1–F10"), ("modifiers", "Optional modifiers, e.g. Ctrl+Shift") },
                ActionType.SwitchTab => new[] { ("direction", "Direction: next or previous"), ("shortcut", "Optional shortcut override (empty = Ctrl+Tab / Ctrl+Shift+Tab)") },
                ActionType.IncreaseVolume or ActionType.DecreaseVolume or ActionType.IncreaseBrightness or ActionType.DecreaseBrightness => new[] { ("step_percent", "Step size (1–100 percentage points)") },
                ActionType.SetVolume => new[] { ("volume_percent", "Volume (0–100)") },
                ActionType.SendMediaKey => new[] { ("media_key", "Media key: play, pause, next, previous, stop, mute") },
                ActionType.TriggerKeyboardShortcut => new[] { ("shortcut", "Shortcut (for example Ctrl+Shift+S)") },
                ActionType.SwitchVirtualDesktop => new[] { ("direction", "Direction: left or right") },
                ActionType.TakeScreenshot => new[] { ("path", "Existing folder or PNG file path") },
                ActionType.RunMacro => new[] { ("macro_id", "Macro ID (shown below after saving a macro)") },
                _ => Array.Empty<(string, string)>()
            };
            foreach (var (key, label) in keys) fields[key] = Text(parameters, label, editing?.Parameters.GetValueOrDefault(key) ?? (key == "step_percent" ? "5" : key == "key" ? "F1" : key == "direction" && types[type.SelectedIndex] == ActionType.SwitchTab ? "next" : ""));
        }
        type.SelectionChanged += (_, _) => ParameterFields(); ParameterFields();
        var enabled = Check(panel, "Enabled", true); var approved = Check(panel, "I approve this action for automatic execution", false);
        var confirm = Check(panel, "Ask me before every execution", false);
        var options = Row(panel); var cooldown = Number(options, "Cooldown (ms)", 200, 0, 60000);
        var confidence = Number(options, "Minimum confidence", 0.36, 0, 1); var timeout = Number(options, "Timeout (seconds)", 5, 1, 300);
        panel.Children.Add(Label("Cooldown: minimum time between executions (200 ms = 0.2 seconds), to prevent repeats. Minimum confidence: required classifier score from 0 to 1; higher values reject more uncertain taps. Timeout: how long an action may run before cancellation is requested. Approval allows execution of scripts and other restricted action types; confirmation asks you each time."));
        void Reload() { list.ItemsSource = W.Library.Actions.ToArray(); }
        Reload();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not HoloAction a) return;
            editing = a; name.Text = a.DisplayName ?? ""; type.SelectedIndex = Array.IndexOf(types, a.Type); ParameterFields();
            enabled.IsChecked = a.Enabled; approved.IsChecked = a.RequiresApproval; confirm.IsChecked = a.RequireConfirmation;
            cooldown.Value = a.CooldownMilliseconds; confidence.Value = a.ConfidenceThreshold; timeout.Value = a.TimeoutSeconds;
        };
        HoloAction Read()
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter an action name.");
            var chosen = types[type.SelectedIndex];
            var values = fields.ToDictionary(kv => kv.Key, kv => kv.Value.Text);
            if (chosen is ActionType.FunctionKey or ActionType.SwitchTab) ActionOptions.Shortcut(chosen, values);
            if (chosen is ActionType.IncreaseVolume or ActionType.DecreaseVolume or ActionType.IncreaseBrightness or ActionType.DecreaseBrightness) ActionOptions.Step(values);
            return new HoloAction { Id = editing?.Id ?? Guid.NewGuid().ToString("N"), DisplayName = name.Text.Trim(), Type = types[type.SelectedIndex],
                Parameters = fields.ToDictionary(kv => kv.Key, kv => kv.Value.Text), Enabled = enabled.IsChecked == true,
                RequiresApproval = approved.IsChecked == true, RequireConfirmation = confirm.IsChecked == true,
                CooldownMilliseconds = (int)cooldown.Value, ConfidenceThreshold = confidence.Value, TimeoutSeconds = (int)timeout.Value };
        }
        var buttons = Row(panel);
        Button(buttons, "New action", () => { editing = null; list.SelectedItem = null; name.Text = ""; approved.IsChecked = false; ParameterFields(); return Task.CompletedTask; });
        Button(buttons, "Save action", async () => { var a = Read(); W.Library.Actions.RemoveAll(x => x.Id == a.Id); W.Library.Actions.Add(a); editing = a; await W.SaveLibraryAsync(); Reload(); });
        Button(buttons, "Test action", async () => { var a = Read(); if (await Confirm("Execute action now?", a.DisplayName ?? a.Type.ToString())) { var result = await W.Actions.ExecuteAsync(a, default); _message.Text = result.Succeeded ? "Action completed." : result.Error; } });
        Button(buttons, "Delete action", async () => { if (editing is { } a && await Confirm("Delete action?", a.DisplayName ?? a.Id)) { W.Library.Actions.Remove(a); editing = null; await W.SaveLibraryAsync(); Reload(); } });
        var mappings = Section("Zone assignment"); var zoneChoice = Choice(mappings, "Zone", DeskZoneExtensions.All.Select(z => z.DisplayName()));
        var mappingEnabled = Check(mappings, "Enable zone", true);
        Button(mappings, "Assign selected action", async () =>
        {
            if (W.Profile is null || editing is null) throw new InvalidOperationException("Select a profile and save/select an action first.");
            var zone = DeskZoneExtensions.All[zoneChoice.SelectedIndex]; var map = W.Profile.ActionMappings.ToDictionary(kv => kv.Key, kv => kv.Value);
            map[zone] = new ZoneActionMapping { Zone = zone, ActionId = editing.Id, Enabled = mappingEnabled.IsChecked == true,
                ConfidenceThreshold = confidence.Value, CooldownMilliseconds = (int)cooldown.Value, RequireConfirmation = confirm.IsChecked == true };
            W.Profile.ActionMappings = map; await W.SaveProfileAsync();
        });
        Button(mappings, "Use visual feedback only", async () =>
        {
            if (W.Profile is null) throw new InvalidOperationException("Select a profile first.");
            var zone = DeskZoneExtensions.All[zoneChoice.SelectedIndex]; var map = W.Profile.ActionMappings.ToDictionary(kv => kv.Key, kv => kv.Value);
            map[zone] = new ZoneActionMapping { Zone = zone, Enabled = mappingEnabled.IsChecked == true }; W.Profile.ActionMappings = map; await W.SaveProfileAsync();
        });
        var mappingText = Label(""); mappings.Children.Add(mappingText);
        var gestures = Section("Camera gesture assignment");
        gestures.Children.Add(Label("Save/select an action above, then assign a gesture. Start tracking in Camera Studio and enable gesture actions there. Hold a pose steadily; release it before repeating the same gesture."));
        var supportedGestures = new[] { GestureClass.OpenPalm, GestureClass.ClosedFist, GestureClass.Pinch, GestureClass.Pointing, GestureClass.TwoFinger, GestureClass.SwipeLeft, GestureClass.SwipeRight, GestureClass.SwipeUp, GestureClass.SwipeDown };
        var gestureChoice = Choice(gestures, "Gesture", supportedGestures.Select(g => g.DisplayName()));
        var gestureAssignments = Label(""); gestures.Children.Add(gestureAssignments);
        void RefreshGestures() => gestureAssignments.Text = W.Profile is { } profile
            ? string.Join("\n", profile.GestureActions.Select(kv => $"{kv.Key}: {W.Library.Actions.FirstOrDefault(a => a.Id == kv.Value)?.DisplayName ?? "Missing action"}")) : "Select a profile first.";
        RefreshGestures();
        Button(gestures, "Create tab actions and bind left/right swipes", async () =>
        {
            var p = W.Profile ?? throw new InvalidOperationException("Select a profile first.");
            if (p.GestureActions.ContainsKey("SwipeLeft") || p.GestureActions.ContainsKey("SwipeRight"))
                throw new InvalidOperationException("A left/right swipe is already assigned. Use the assignment controls to change it.");
            var next = new HoloAction { DisplayName = "Next tab", Type = ActionType.SwitchTab, Parameters = new Dictionary<string, string> { ["direction"] = "next" }, CooldownMilliseconds = 800 };
            var previous = new HoloAction { DisplayName = "Previous tab", Type = ActionType.SwitchTab, Parameters = new Dictionary<string, string> { ["direction"] = "previous" }, CooldownMilliseconds = 800 };
            W.GestureActionsEnabled = false;
            W.Library.Actions.Add(next); W.Library.Actions.Add(previous); await W.SaveLibraryAsync();
            p.GestureActions = new Dictionary<string, string>(p.GestureActions) { ["SwipeLeft"] = next.Id, ["SwipeRight"] = previous.Id };
            await W.SaveProfileAsync(); Reload(); RefreshGestures();
            _message.Text = "Left swipe: next tab; right swipe: previous tab. Edit either action or reassign gestures to customize. Start tracking and enable gesture actions in Camera Studio, then focus the target app.";
        });
        Button(gestures, "Assign selected action to gesture", async () =>
        {
            if (W.Profile is null || editing is null || !W.Library.Actions.Any(a => a.Id == editing.Id)) throw new InvalidOperationException("Select a profile and save the action first.");
            W.GestureActionsEnabled = false;
            var assignments = new Dictionary<string, string>(W.Profile.GestureActions);
            assignments[supportedGestures[gestureChoice.SelectedIndex].ToString()] = editing.Id; W.Profile.GestureActions = assignments;
            await W.SaveProfileAsync(); RefreshGestures();
        });
        Button(gestures, "Remove gesture assignment", async () =>
        {
            if (W.Profile is null) return; W.GestureActionsEnabled = false;
            var assignments = new Dictionary<string, string>(W.Profile.GestureActions);
            assignments.Remove(supportedGestures[gestureChoice.SelectedIndex].ToString()); W.Profile.GestureActions = assignments;
            await W.SaveProfileAsync(); RefreshGestures();
        });
        var customPanel = Section("Custom finger symbols");
        customPanel.Children.Add(Label("Choose Extended, Folded or Any for each finger. Custom symbols take priority over built-in poses; keep patterns distinct. Hold the symbol still to trigger the saved action selected above."));
        var symbolName = Text(customPanel, "Symbol name");
        var fingerChoices = new[] { "Thumb", "Index", "Middle", "Ring", "Little" }.Select(f => Choice(customPanel, f, Enum.GetNames<FingerState>())).ToArray();
        var symbols = new ComboBox { Header = "Saved symbols", DisplayMemberPath = "Name", MinWidth = 280 }; customPanel.Children.Add(symbols);
        void RefreshSymbols() { symbols.ItemsSource = W.Profile?.FingerGestures; }
        RefreshSymbols();
        symbols.SelectionChanged += (_, _) =>
        {
            if (symbols.SelectedItem is not FingerGesture symbol) return;
            symbolName.Text = symbol.Name;
            for (int i = 0; i < 5; i++) fingerChoices[i].SelectedIndex = (int)symbol.Fingers[i];
        };
        Button(customPanel, "New symbol", () => { symbols.SelectedIndex = -1; symbolName.Text = ""; foreach (var c in fingerChoices) c.SelectedIndex = 0; return Task.CompletedTask; });
        Button(customPanel, "Save symbol and assign selected action", async () =>
        {
            if (W.Profile is not { } p || editing is null || !W.Library.Actions.Any(a => a.Id == editing.Id)) throw new InvalidOperationException("Select a profile and a saved action.");
            var states = fingerChoices.Select(c => (FingerState)c.SelectedIndex).ToArray();
            if (string.IsNullOrWhiteSpace(symbolName.Text) || states.All(f => f == FingerState.Any)) throw new InvalidOperationException("Name the symbol and set at least one finger.");
            var selected = symbols.SelectedItem as FingerGesture;
            if (p.FingerGestures.Any(g => g.Id != selected?.Id && g.Fingers.Select((f, i) => f == FingerState.Any || states[i] == FingerState.Any || f == states[i]).All(v => v))) throw new InvalidOperationException("This pattern overlaps an existing symbol. Choose a distinct pattern or remove the old symbol.");
            await W.StopCameraAsync();
            var symbol = new FingerGesture { Id = selected?.Id ?? Guid.NewGuid().ToString("N"), Name = symbolName.Text.Trim(), Fingers = states };
            p.FingerGestures = p.FingerGestures.Where(g => g.Id != symbol.Id).Append(symbol).ToArray();
            p.GestureActions = new Dictionary<string, string>(p.GestureActions) { [symbol.Key] = editing.Id };
            await W.SaveProfileAsync(); RefreshSymbols(); RefreshGestures();
        });
        Button(customPanel, "Remove selected symbol", async () =>
        {
            if (W.Profile is not { } p || symbols.SelectedItem is not FingerGesture g) return;
            await W.StopCameraAsync(); p.FingerGestures = p.FingerGestures.Where(s => s.Id != g.Id).ToArray();
            var assignments = new Dictionary<string, string>(p.GestureActions); assignments.Remove(g.Key); p.GestureActions = assignments;
            await W.SaveProfileAsync(); RefreshSymbols(); RefreshGestures();
        });
        var tuning = Section("Camera sensitivity");
        tuning.Children.Add(Label("Swipe directions follow the preview image. Create keyboard shortcut actions: Ctrl+Tab for next tab, Ctrl+Shift+Tab for previous tab, Alt+Tab for next window, or Alt+Shift+Tab for previous window. Shortcuts go to the foreground app. Show only one hand."));
        var cameraConfidence = Number(tuning, "Minimum hand confidence (0.5–1)", W.Profile?.CameraMinimumConfidence ?? .8, .5, 1);
        var swipeTravel = Number(tuning, "Swipe travel (fraction of image width/height)", W.Profile?.SwipeDistance ?? .18, .08, .5);
        var poseHold = Number(tuning, "Pose hold (ms)", W.Profile?.GestureHoldMilliseconds ?? 450, 200, 3000);
        Button(tuning, "Save camera sensitivity", async () =>
        {
            if (W.Profile is not { } p) throw new InvalidOperationException("Select a profile.");
            if (!double.IsFinite(cameraConfidence.Value) || !double.IsFinite(swipeTravel.Value) || !double.IsFinite(poseHold.Value)) throw new InvalidOperationException("Enter numeric settings.");
            await W.StopCameraAsync(); p.CameraMinimumConfidence = cameraConfidence.Value; p.SwipeDistance = swipeTravel.Value; p.GestureHoldMilliseconds = (int)poseHold.Value;
            await W.SaveProfileAsync(); _message.Text = "Saved. Restart tracking and enable gesture actions in Camera Studio.";
        });
        var light = Section("Ambient light rules");
        light.Children.Add(Label("Use a Windows ambient light sensor, not camera brightness. Select a saved action above. Above/below use the lower threshold; range includes both limits. Hold is the time the condition must persist. Hysteresis is the margin beyond a limit needed to reset the rule. Cooldown is the minimum time between triggers. Rules fire once per entry; overlapping rules may run multiple actions. Start sensor readings and arm light rules in Hardware."));
        var lightName = Text(light, "Rule name");
        var condition = Choice(light, "Condition", Enum.GetNames<LightCondition>());
        var lower = Number(light, "Threshold / lower limit (lux)", 100, 0, 200000);
        var upper = Number(light, "Upper limit for range (lux)", 500, 0, 200000);
        var margin = Number(light, "Hysteresis (lux)", 10, 0, 200000);
        var dwell = Number(light, "Hold (ms)", 1000, 0, 60000);
        var lightCooldown = Number(light, "Cooldown (ms)", 3000, 0, 60000);
        var lightList = new ComboBox { Header = "Saved light rules", DisplayMemberPath = "Name", MinWidth = 280 }; light.Children.Add(lightList);
        void RefreshLight() { lightList.ItemsSource = W.Profile?.LightRules; }
        RefreshLight();
        var lightEnabled = Check(light, "Enable this rule when light actions are armed", true);
        lightList.SelectionChanged += (_, _) =>
        {
            if (lightList.SelectedItem is not LightRule rule) return;
            lightName.Text = rule.Name; condition.SelectedIndex = (int)rule.Condition; lower.Value = rule.LowerLux; upper.Value = rule.UpperLux;
            margin.Value = rule.HysteresisLux; dwell.Value = rule.HoldMilliseconds; lightCooldown.Value = rule.CooldownMilliseconds; lightEnabled.IsChecked = rule.Enabled;
        };
        Button(light, "New light rule", () => { lightList.SelectedIndex = -1; lightName.Text = ""; lightEnabled.IsChecked = true; return Task.CompletedTask; });
        Button(light, "Save rule for selected action", async () =>
        {
            if (W.Profile is not { } p || editing is null || !W.Library.Actions.Any(a => a.Id == editing.Id)) throw new InvalidOperationException("Select a profile and saved action.");
            if (string.IsNullOrWhiteSpace(lightName.Text) || !double.IsFinite(dwell.Value) || !double.IsFinite(lightCooldown.Value)) throw new InvalidOperationException("Enter a name and numeric timing values.");
            var rule = new LightRule { Id = (lightList.SelectedItem as LightRule)?.Id ?? Guid.NewGuid().ToString("N"), Enabled = lightEnabled.IsChecked == true, Name = lightName.Text.Trim(), ActionId = editing.Id, Condition = (LightCondition)condition.SelectedIndex,
                LowerLux = lower.Value, UpperLux = upper.Value, HysteresisLux = margin.Value, HoldMilliseconds = (int)dwell.Value, CooldownMilliseconds = (int)lightCooldown.Value };
            rule.Validate(); W.LightActionsEnabled = false; p.LightRules = p.LightRules.Where(r => r.Id != rule.Id).Append(rule).ToArray(); await W.SaveProfileAsync(); RefreshLight();
        });
        Button(light, "Remove selected light rule", async () =>
        {
            if (W.Profile is not { } p || lightList.SelectedItem is not LightRule rule) return;
            W.LightActionsEnabled = false; p.LightRules = p.LightRules.Where(r => r.Id != rule.Id).ToArray(); await W.SaveProfileAsync(); RefreshLight();
        });
        AddPresenceRuleEditor(() => editing);
        var macros = Section("Macros"); var macroName = Text(macros, "Macro name"); var steps = new List<MacroStep>();
        macros.Children.Add(Label("A macro runs saved actions in order, such as opening an application and then typing text. Add actions as steps, save the macro, then create a RunMacro action and assign it to a zone. A failed step stops the remaining steps."));
        var stepText = Label("No steps yet."); macros.Children.Add(stepText); var delay = Number(macros, "Delay before next step (ms)", 0, 0, 60000);
        Button(macros, "Add selected action as step", () =>
        { if (editing is null || editing.Type == ActionType.RunMacro) throw new InvalidOperationException("Select a non-macro action first."); steps.Add(new MacroStep { ActionId = editing.Id, Order = steps.Count, DelayMilliseconds = (int)delay.Value }); stepText.Text = string.Join("\n", steps.Select(s => $"{s.Order + 1}. {W.Library.Actions.FirstOrDefault(a => a.Id == s.ActionId)?.DisplayName} · delay {s.DelayMilliseconds} ms")); return Task.CompletedTask; });
        Button(macros, "Clear steps", () => { steps.Clear(); stepText.Text = "No steps yet."; return Task.CompletedTask; });
        Button(macros, "Save macro", async () =>
        { if (steps.Count == 0 || string.IsNullOrWhiteSpace(macroName.Text)) throw new InvalidOperationException("Enter a name and add steps."); W.Library.Macros.Add(new HoloMacro { DisplayName = macroName.Text.Trim(), Steps = steps.ToList() }); await W.SaveLibraryAsync(); });
        macros.Children.Add(Label("Dummy example: visual feedback step 1 → wait 500 ms → visual feedback step 2. It does not open apps, send keys or change device settings."));
        Button(macros, "Create dummy example macro", async () =>
        {
            if (W.Library.Macros.Any(m => m.Id == "example-feedback-macro")) { _message.Text = "The example macro already exists."; return; }
            var first = new HoloAction { Id = "example-feedback-1", DisplayName = "Example: feedback 1", Type = ActionType.VisualOnly };
            var second = new HoloAction { Id = "example-feedback-2", DisplayName = "Example: feedback 2", Type = ActionType.VisualOnly };
            if (!W.Library.Actions.Any(a => a.Id == first.Id)) W.Library.Actions.Add(first);
            if (!W.Library.Actions.Any(a => a.Id == second.Id)) W.Library.Actions.Add(second);
            W.Library.Macros.Add(new HoloMacro { Id = "example-feedback-macro", DisplayName = "Dummy example: two feedback steps", Steps = new()
                { new MacroStep { Order = 0, ActionId = first.Id }, new MacroStep { Order = 1, ActionId = second.Id, DelayMilliseconds = 500 } } });
            await W.SaveLibraryAsync(); Reload(); _message.Text = "Example created. Choose Automation · Macro and its ID to test it; no trigger was assigned.";
        });
        var macroText = Label(""); macros.Children.Add(macroText);
        var macroSection = (UIElement)macros.Parent;
        _body.Children.Remove(macroSection); _body.Children.Insert(_body.Children.IndexOf((UIElement)mappings.Parent), macroSection);
        _refresh = () =>
        {
            mappingText.Text = W.Profile is { } p ? string.Join("\n", p.ActionMappings.Select(kv => $"{kv.Key.DisplayName()} → {W.Library.Actions.FirstOrDefault(a => a.Id == kv.Value.ActionId)?.DisplayName ?? "Visual only"} ({(kv.Value.Enabled ? "enabled" : "disabled")})")) : "Select a profile to assign zones.";
            macroText.Text = string.Join("\n", W.Library.Macros.Select(m => $"{m.DisplayName} · {m.Steps.Count} steps · ID: {m.Id}"));
        };
    }

    private void Settings()
    {
        var panel = Section("Preferences"); var theme = Choice(panel, "Theme", new[] { "Default", "Light", "Dark" }, Array.IndexOf(new[] { "Default", "Light", "Dark" }, W.Settings.Theme));
        var startup = Check(panel, "Start HoloWin when I sign in", StartupManager.IsAutoStartEnabled);
        var mode = Choice(panel, "Interaction mode", new[] { "Audio only", "Manual / hotkeys only", "Adaptive (available sensors)" }, W.Settings.FusionMode == FusionMode.ManualOnly ? 1 : W.Settings.FusionMode == FusionMode.MultiSensorAdaptive ? 2 : 0);
        var probe = Check(panel, "Allow experimental active acoustic probe (may be audible)", W.Settings.ActiveProbeEnabled);
        var amplitude = Number(panel, "Probe amplitude (0–0.10)", W.Settings.ProbeAmplitude, 0, 0.10);
        var debug = Check(panel, "Retain short debug WAV recordings (maximum 100)", W.Settings.DebugCaptureEnabled);
        Button(panel, "Save settings", async () =>
        {
            await W.StopAudioAsync(); W.Settings.Theme = theme.SelectedItem?.ToString() ?? "Default";
            W.Settings.FusionMode = mode.SelectedIndex == 1 ? FusionMode.ManualOnly : mode.SelectedIndex == 2 ? FusionMode.MultiSensorAdaptive : FusionMode.AudioOnly;
            W.Settings.ActiveProbeEnabled = probe.IsChecked == true; W.Settings.ProbeAmplitude = amplitude.Value; W.Settings.DebugCaptureEnabled = debug.IsChecked == true;
            if (startup.IsChecked == true) StartupManager.EnableAutoStart(Environment.ProcessPath!); else StartupManager.DisableAutoStart();
            await W.SaveSettingsAsync(); if (App.MainWindow.Content is FrameworkElement root) root.RequestedTheme = Enum.Parse<ElementTheme>(W.Settings.Theme);
            _message.Text = "Preferences saved. Capture remains stopped.";
        });
        var data = Section("Application data");
        var hotkeys = Section("Global zone hotkeys");
        hotkeys.Children.Add(Label("Use a shortcut such as Ctrl+Alt+1 to run a zone action while another app has focus. Leave it blank to disable it. Hotkeys pause during calibration and evaluation."));
        var bindings = DeskZoneExtensions.All.ToDictionary(z => z, z => Text(hotkeys, z.DisplayName(), W.Settings.Hotkeys.GetValueOrDefault(z.ToString()) ?? ""));
        Button(hotkeys, "Save hotkeys", async () =>
        {
            var previous = W.Settings.Hotkeys;
            W.Settings.Hotkeys = bindings.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Text.Trim());
            try { App.Services.ApplyHotkeys(); await W.SaveSettingsAsync(); _message.Text = "Hotkeys saved."; }
            catch { W.Settings.Hotkeys = previous; App.Services.ApplyHotkeys(); throw; }
        });
        Button(hotkeys, "Hide window to tray", () => { App.MainWindow.AppWindow.Hide(); return Task.CompletedTask; });
        hotkeys.Children.Add(Label("The tray menu can show the window, pause sensors or quit. Hotkeys remain available when sensors are paused. Closing the window quits HoloWin."));
        Button(data, "Open data folder", () => OpenUri(W.Paths.AppDataRoot));
        Button(data, "Reset HoloWin data", async () => { if (await Confirm("Reset all HoloWin data?", "This deletes profiles, actions, macros, evaluations and debug recordings. It also disables automatic startup.")) { await W.ResetDataAsync(); App.Services.ApplyHotkeys(); _message.Text = "Application data reset."; } });
        Note($"HoloWin {typeof(App).Assembly.GetName().Version}\nRepository: https://github.com/rotsl/HoloWin\nCamera gestures can be configured in Actions and started in Camera Studio. Depth, smart-home and device-specific HID/Bluetooth/serial protocols are not configured on this installation.");
    }

    private Image? _preview;
    private WriteableBitmap? _previewBitmap;
    private byte[] _previewPixels = Array.Empty<byte>();
    private TextBlock? _previewNotice;
    private long _displayedFrames;
    private Canvas? _overlay;
    private TextBlock? _cameraStatus;
    private bool _mapping;
    private CheckBox? _gestureArmed;
    private int _corner;
    private int _frameWidth;
    private int _frameHeight;
    private void Camera(bool mapping)
    {
        _mapping = mapping;
        _corner = W.DeskMap.CornerCount;
        Note(mapping ? "Start preview, then click four desk corners clockwise: top-left, top-right, bottom-right, bottom-left. This saves the desk geometry for the selected profile." : "Start preview to check the image, or start hand tracking to recognize gestures locally. Gesture actions require a separate enable switch.");
        var panel = Section(mapping ? "Desk calibration" : "Camera preview");
        var index = Number(panel, "Camera index", W.Settings.CameraIndex, 0, 16);
        var buttons = Row(panel);
        Button(buttons, "Start preview", async () =>
        { W.Settings.CameraIndex = (int)index.Value; await W.SaveSettingsAsync(); W.CameraFrameAvailable -= OnCameraFrame; W.CameraFrameAvailable += OnCameraFrame; await W.StartCameraAsync(); });
        Button(buttons, "Stop preview", W.StopCameraAsync);
        _cameraStatus = Label("Camera off"); panel.Children.Add(_cameraStatus);
        _previewNotice = Label("Press Start preview. The camera image appears below."); panel.Children.Add(_previewNotice);
        var viewport = new Grid { Width = 640, Height = 480, Background = new SolidColorBrush(Colors.Black) };
        _preview = new Image { Width = 640, Height = 480, Stretch = Stretch.Fill };
        _overlay = new Canvas { Width = 640, Height = 480, Background = new SolidColorBrush(Colors.Transparent) };
        viewport.Children.Add(_preview); viewport.Children.Add(_overlay);
        panel.Children.Add(new Viewbox { Child = viewport, Stretch = Stretch.Uniform, MaxWidth = 640, MaxHeight = 320,
            HorizontalAlignment = HorizontalAlignment.Left });
        if (!mapping)
        {
            Button(buttons, "Start hand tracking", async () =>
            {
                W.Settings.CameraIndex = (int)index.Value; await W.SaveSettingsAsync();
                W.CameraFrameAvailable -= OnCameraFrame; W.CameraFrameAvailable += OnCameraFrame;
                await W.StartTrackingAsync();
            });
            var armed = Check(panel, "Enable assigned gesture actions for this session", W.GestureActionsEnabled);
            _gestureArmed = armed;
            armed.Checked += (_, _) =>
            {
                if (!W.TrackingRunning || W.Profile is null) { armed.IsChecked = false; _message.Text = "Select a profile and start tracking first."; return; }
                W.GestureActionsEnabled = true;
            };
            armed.Unchecked += (_, _) => W.GestureActionsEnabled = false;
            panel.Children.Add(Label("Tracking stays on when you leave this page or hide the window. Stop it here, on Dashboard, or with Pause sensors in the tray. Preview-only capture stops when you leave."));
        }
        if (W.CameraRunning) W.CameraFrameAvailable += OnCameraFrame;
        if (mapping)
        {
            _overlay.PointerPressed += (_, args) =>
            {
                if (!W.CameraRunning || _frameWidth == 0) { _message.Text = "Start preview before selecting corners."; return; }
                var point = args.GetCurrentPoint(_overlay).Position;
                if (_corner >= 4) { var desk = W.DeskMap.MapImagePointToDesk(point.X / 640 * _frameWidth, point.Y / 480 * _frameHeight); _message.Text = $"Desk position: {desk.DeskX:F3}, {desk.DeskY:F3}"; return; }
                W.DeskMap.SetCorner(_corner++, point.X / 640 * _frameWidth, point.Y / 480 * _frameHeight); DrawCorners();
            };
            Button(buttons, "Clear corners", () => { W.DeskMap.ClearCorners(); _corner = 0; DrawCorners(); return Task.CompletedTask; });
            Button(buttons, "Save desk map", W.SaveDeskMapAsync);
        }
        _refresh = () => { if (_gestureArmed is not null) _gestureArmed.IsChecked = W.GestureActionsEnabled; _cameraStatus.Text = $"Camera: {(W.CameraRunning ? "preview active" : "off")} · {_frameWidth} × {_frameHeight} · Received frames: {W.CameraFrameCount}" + (mapping ? $" · Corners: {W.DeskMap.CornerCount}/4 · {(W.DeskMap.IsCalibrated ? "mapping valid" : "select corners")}" : $" · {W.TrackingStatus} · Actions: {(W.GestureActionsEnabled ? "armed" : "disarmed")}"); };
    }
    private void OnCameraFrame(object? sender, HoloWin.Gestures.Frame frame)
    {
        if (Interlocked.Exchange(ref _frameQueued, 1) != 0) return;
        if (!_ui.TryEnqueue(() =>
        {
            try
            {
                if (!_pageVisible) return;
                bool firstFrame = _frameWidth == 0;
                _frameWidth = frame.Width; _frameHeight = frame.Height;
                if (_previewBitmap is null || _previewBitmap.PixelWidth != frame.Width || _previewBitmap.PixelHeight != frame.Height)
                {
                    _previewBitmap = new WriteableBitmap(frame.Width, frame.Height);
                    _previewPixels = new byte[frame.Width * frame.Height * 4];
                    _preview!.Source = _previewBitmap;
                }
                var pixels = _previewPixels;
                long brightness = 0;
                for (int y = 0; y < frame.Height; y++) for (int x = 0; x < frame.Width; x++)
                { int src = y * frame.Stride + x * 3, dst = (y * frame.Width + x) * 4; pixels[dst] = frame.RgbPixels[src + 2]; pixels[dst + 1] = frame.RgbPixels[src + 1]; pixels[dst + 2] = frame.RgbPixels[src]; pixels[dst + 3] = 255; brightness += pixels[dst] + pixels[dst + 1] + pixels[dst + 2]; }
                using (var stream = _previewBitmap.PixelBuffer.AsStream()) { stream.Position = 0; stream.Write(pixels); stream.Flush(); }
                _previewBitmap.Invalidate(); DrawCorners();
                var mean = brightness / (frame.Width * (double)frame.Height * 3);
                _displayedFrames++;
                if (_previewNotice is not null) _previewNotice.Text = mean < 3
                    ? "Frames are arriving, but the image is nearly black. Check the camera shutter, lighting and camera index."
                    : $"Live preview · Updated frames: {_displayedFrames}";
                if (firstFrame) W.Log($"{_section}: preview buffer updated {_frameWidth} x {_frameHeight}; mean image level {mean:F1}/255.");
            }
            catch (Exception ex)
            {
                _message.Text = "Preview: " + ex.Message;
                if (!_previewErrorLogged) { _previewErrorLogged = true; W.Log(_message.Text); }
            }
            finally { Interlocked.Exchange(ref _frameQueued, 0); }
        })) Interlocked.Exchange(ref _frameQueued, 0);
    }
    private void DrawCorners()
    {
        if (_overlay is null || _frameWidth == 0) return;
        _overlay.Children.Clear(); var points = new List<Point>();
        foreach (var hand in W.CurrentHands)
        {
            foreach (var landmark in hand.Landmarks)
            {
                var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Colors.Lime) };
                Canvas.SetLeft(dot, landmark.X * 640 - 3); Canvas.SetTop(dot, landmark.Y * 480 - 3); _overlay.Children.Add(dot);
            }
        }
        if (!_mapping) return;
        for (int i = 0; i < 4; i++) if (W.DeskMap.GetCorner(i) is { } c)
        {
            var point = new Point(c.ImgX / _frameWidth * 640, c.ImgY / _frameHeight * 480); points.Add(point);
            var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Colors.Cyan) }; Canvas.SetLeft(dot, point.X - 6); Canvas.SetTop(dot, point.Y - 6); _overlay.Children.Add(dot);
        }
        if (points.Count == 4)
        {
            var polygon = new Polygon { Stroke = new SolidColorBrush(Colors.Cyan), StrokeThickness = 2 };
            foreach (var point in points) polygon.Points.Add(point); _overlay.Children.Add(polygon);
            for (int i = 0; i < 2; i++)
            {
                var a = points[i]; var b = points[(i + 1) % 4]; var c = points[(i + 2) % 4]; var d = points[(i + 3) % 4];
                _overlay.Children.Add(new Line { X1 = (a.X + b.X) / 2, Y1 = (a.Y + b.Y) / 2, X2 = (c.X + d.X) / 2, Y2 = (c.Y + d.Y) / 2, Stroke = new SolidColorBrush(Colors.Cyan), StrokeThickness = 1 });
            }
        }
    }
}
