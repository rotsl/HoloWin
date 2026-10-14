using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using HoloWin.Core;
using NAudio.CoreAudioApi;

namespace HoloWin.Actions;

internal static class DesktopActions
{
    public static Task OnStaAsync(Action action, CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { ct.ThrowIfCancellationRequested(); action(); done.TrySetResult(); }
            catch (Exception ex) { done.TrySetException(ex); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task.WaitAsync(ct);
    }
    private static string Required(HoloAction a, string key) => a.Parameters.TryGetValue(key, out var value)
        && !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"Missing {key} parameter.");
    public static Task ExecuteAsync(HoloAction a, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        switch (a.Type)
        {
            case ActionType.PlaySound:
                var sound = a.Parameters.GetValueOrDefault("path");
                if (string.IsNullOrWhiteSpace(sound)) SystemSounds.Asterisk.Play();
                else { using var player = new SoundPlayer(sound); player.Load(); player.PlaySync(); }
                break;
            case ActionType.TypeText:
                foreach (var character in Required(a, "text"))
                {
                    ct.ThrowIfCancellationRequested();
                    SendKey(0, character, 4); SendKey(0, character, 4 | 2);
                }
                break;
            case ActionType.FocusApplication:
                var processName = Path.GetFileNameWithoutExtension(Required(a, "app_path"));
                using (var process = Process.GetProcessesByName(processName).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero)
                    ?? throw new InvalidOperationException("No running window found for that application."))
                {
                    ShowWindow(process.MainWindowHandle, 9);
                    if (!SetForegroundWindow(process.MainWindowHandle)) throw new InvalidOperationException("Windows denied foreground activation.");
                }
                break;
            case ActionType.SetVolume:
                if (!float.TryParse(Required(a, "volume_percent"), CultureInfo.InvariantCulture, out var volume) || volume < 0 || volume > 100)
                    throw new InvalidOperationException("Volume must be between 0 and 100.");
                using (var devices = new MMDeviceEnumerator())
                using (var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = volume / 100;
                break;
            case ActionType.IncreaseVolume:
            case ActionType.DecreaseVolume:
                var volumeStep = ActionOptions.Step(a.Parameters);
                using (var devices = new MMDeviceEnumerator())
                using (var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    var endpoint = device.AudioEndpointVolume;
                    endpoint.MasterVolumeLevelScalar = Math.Clamp(endpoint.MasterVolumeLevelScalar
                        + (a.Type == ActionType.IncreaseVolume ? volumeStep : -volumeStep) / 100f, 0, 1);
                }
                break;
            case ActionType.IncreaseBrightness:
            case ActionType.DecreaseBrightness:
                MonitorBrightness.Adjust(ActionOptions.Step(a.Parameters), a.Type == ActionType.IncreaseBrightness, ct);
                break;
            case ActionType.TakeScreenshot:
                var path = ScreenshotDestination.Resolve(Required(a, "path"));
                var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
                using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                {
                    using var graphics = Graphics.FromImage(bitmap);
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                    bitmap.Save(path, ImageFormat.Png);
                }
                break;
        }
    }, ct);

    public static void SendKey(ushort key, char scan, uint flags)
    {
        var inputs = new[] { new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Vk = key, Scan = scan, Flags = flags } } } };
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("Windows could not deliver the input to the target application.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
}
