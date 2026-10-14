using HoloWin.Core;
using HoloWin.Audio;
using HoloWin.Sensors;
using Windows.Devices.Enumeration;
using Windows.Devices.Sensors;

namespace HoloWin.Platform.Windows;

/// <summary>Enumerates device interfaces without opening the microphone or camera.</summary>
public sealed class WindowsHardwareProvider : IHardwareProvider
{
    public bool Supports(HoloWin.Core.DeviceClass cls) => cls is HoloWin.Core.DeviceClass.AudioInput
        or HoloWin.Core.DeviceClass.Camera or HoloWin.Core.DeviceClass.Imu or HoloWin.Core.DeviceClass.Ambient;
    public async Task<IReadOnlyList<DeviceCapability>> EnumerateAsync(CancellationToken ct)
    {
        var result = new List<DeviceCapability>(AudioCaptureFactory.EnumerateInputDevices());
        var cameras = await DeviceInformation.FindAllAsync(global::Windows.Devices.Enumeration.DeviceClass.VideoCapture).AsTask(ct);
        int index = 0;
        foreach (var camera in cameras)
            result.Add(new DeviceCapability { Id = camera.Id, Class = HoloWin.Core.DeviceClass.Camera,
                FriendlyName = camera.Name, State = camera.IsEnabled ? DeviceState.Available : DeviceState.Disconnected,
                Properties = new Dictionary<string, string> { ["camera_index_hint"] = (index++).ToString() },
                StateDetail = "Preview only. OpenCV device index may differ; choose and verify in Camera Studio." });
        AddSensor(result, "accelerometer", "Accelerometer", Accelerometer.GetDefault() is not null, HoloWin.Core.DeviceClass.Imu);
        AddSensor(result, "gyrometer", "Gyrometer", Gyrometer.GetDefault() is not null, HoloWin.Core.DeviceClass.Imu);
        AddSensor(result, "light", "Ambient light", LightSensor.GetDefault() is not null, HoloWin.Core.DeviceClass.Ambient);
        AddSensor(result, "inclinometer", "Inclinometer", Inclinometer.GetDefault() is not null, HoloWin.Core.DeviceClass.Imu);
        // Device interface classes expose connected devices without reading HID reports or pairing anything.
        foreach (var (name, guid, cls) in new[] {
            ("HID", "{4D1E55B2-F16F-11CF-88CB-001111000030}", HoloWin.Core.DeviceClass.Hid),
            ("Bluetooth", "{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}", HoloWin.Core.DeviceClass.Bluetooth),
            ("Serial", "{86E0D1E0-8089-11D0-9CE4-08003E301F73}", HoloWin.Core.DeviceClass.Serial) })
        {
            var devices = await DeviceInformation.FindAllAsync($"System.Devices.InterfaceClassGuid:=\"{guid}\"").AsTask(ct);
            foreach (var device in devices)
                result.Add(new DeviceCapability { Id = device.Id, Class = cls, FriendlyName = device.Name,
                    State = cls == HoloWin.Core.DeviceClass.Serial ? DeviceState.Unsupported : device.IsEnabled ? DeviceState.Available : DeviceState.Disconnected,
                    StateDetail = cls == HoloWin.Core.DeviceClass.Serial
                        ? "Serial device detected. No device-specific protocol configured."
                        : $"{name} interface detected. Use Sensor collection v2 for connection sources; raw sensor reports are not decoded." });
        }
        return result;
    }
    private static void AddSensor(List<DeviceCapability> list, string id, string name, bool available, HoloWin.Core.DeviceClass cls)
        => list.Add(new DeviceCapability { Id = "windows.sensor." + id, Class = cls, FriendlyName = name,
            State = available ? DeviceState.Available : DeviceState.Unsupported,
            StateDetail = available ? "Windows sensor API available; readings require explicit enablement." : "No sensor exposed by Windows." });
    public Task<bool> RequestPermissionAsync(DeviceCapability device, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var status = DeviceAccessInformation.CreateFromId(device.Id).CurrentStatus;
        return Task.FromResult(status == DeviceAccessStatus.Allowed);
    }
}

public sealed class WindowsMotionMonitor : IDisposable
{
    private Accelerometer? _accelerometer;
    private Gyrometer? _gyrometer;
    private LightSensor? _light;
    private Inclinometer? _inclinometer;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _readings = new();
    public event EventHandler<MotionObservation>? MotionAvailable;
    public event EventHandler<string>? ReadingAvailable;
    public event EventHandler<double>? LightAvailable;
    public bool HasLightSensor => _light is not null;
    public void Start()
    {
        Stop();
        _accelerometer = Accelerometer.GetDefault(); _gyrometer = Gyrometer.GetDefault(); _light = LightSensor.GetDefault();
        _inclinometer = Inclinometer.GetDefault();
        if (_accelerometer is null && _gyrometer is null && _light is null && _inclinometer is null) throw new InvalidOperationException("Windows exposes no supported sensor readings.");
        if (_accelerometer is not null) { _accelerometer.ReportInterval = Math.Max(100u, _accelerometer.MinimumReportInterval); _accelerometer.ReadingChanged += OnAcceleration; }
        if (_gyrometer is not null) { _gyrometer.ReportInterval = Math.Max(100u, _gyrometer.MinimumReportInterval); _gyrometer.ReadingChanged += OnGyro; }
        if (_light is not null) { _light.ReportInterval = Math.Max(250u, _light.MinimumReportInterval); _light.ReadingChanged += OnLight; }
        if (_inclinometer is not null) { _inclinometer.ReportInterval = Math.Max(100u, _inclinometer.MinimumReportInterval); _inclinometer.ReadingChanged += OnInclination; }
    }
    private void Report(string name, string value) { _readings[name] = value; ReadingAvailable?.Invoke(this, string.Join("\n", _readings.OrderBy(p => p.Key).Select(p => p.Value))); }
    private void OnInclination(Inclinometer sender, InclinometerReadingChangedEventArgs args)
    { var r = args.Reading; Report("inclination", $"Inclination: pitch {r.PitchDegrees:F1}, roll {r.RollDegrees:F1}, yaw {r.YawDegrees:F1} deg"); }
    private void OnAcceleration(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
    {
        var r = args.Reading;
        Report("acceleration", $"Acceleration: {r.AccelerationX:F3}, {r.AccelerationY:F3}, {r.AccelerationZ:F3} g");
        MotionAvailable?.Invoke(this, new MotionObservation(MonotonicClock.Now, "windows.sensor.accelerometer", 1, r.AccelerationX, r.AccelerationY, r.AccelerationZ));
    }
    private void OnGyro(Gyrometer sender, GyrometerReadingChangedEventArgs args)
    { var r = args.Reading; Report("rotation", $"Rotation: {r.AngularVelocityX:F1}, {r.AngularVelocityY:F1}, {r.AngularVelocityZ:F1} deg/s"); }
    private void OnLight(LightSensor sender, LightSensorReadingChangedEventArgs args)
    {
        Report("light", $"Ambient light: {args.Reading.IlluminanceInLux:F1} lux");
        LightAvailable?.Invoke(this, args.Reading.IlluminanceInLux);
    }
    public void Stop()
    {
        if (_accelerometer is not null) { _accelerometer.ReadingChanged -= OnAcceleration; _accelerometer.ReportInterval = 0; }
        if (_gyrometer is not null) { _gyrometer.ReadingChanged -= OnGyro; _gyrometer.ReportInterval = 0; }
        if (_light is not null) { _light.ReadingChanged -= OnLight; _light.ReportInterval = 0; }
        if (_inclinometer is not null) { _inclinometer.ReadingChanged -= OnInclination; _inclinometer.ReportInterval = 0; }
        _inclinometer = null; _readings.Clear();
        _accelerometer = null; _gyrometer = null; _light = null;
    }
    public void Dispose() => Stop();
}
