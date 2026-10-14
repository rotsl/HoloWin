using HoloWin.Sensors;
using HoloWin.Audio;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            Console.Error.WriteLine("HoloWin DeviceInspector requires Windows 11 22H2 or later.");
            return 1;
        }
        Console.WriteLine("HoloWin DeviceInspector");
        Console.WriteLine("=======================");
        Console.WriteLine();

        // Audio inputs
        Console.WriteLine("Audio input devices:");
        foreach (var d in AudioCaptureFactory.EnumerateInputDevices())
        {
            Console.WriteLine($"  [{d.Class}] {d.FriendlyName} (id={d.Id}, state={d.State}{(d.IsDefault ? ", default" : "")})");
        }
        Console.WriteLine();

        // Camera devices (if Vision is available)
        try
        {
            var camType = Type.GetType("HoloWin.Vision.OpenCvCameraCapture, HoloWin.Vision");
            if (camType is not null)
            {
                var enumMethod = camType.GetMethod("EnumerateDevices", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (enumMethod is not null)
                {
                    var cameras = (System.Collections.IEnumerable?)enumMethod.Invoke(null, null);
                    Console.WriteLine("Camera devices:");
                    if (cameras is null) Console.WriteLine("  (enumeration returned null)");
                    else
                    {
                        foreach (var c in cameras)
                            Console.WriteLine($"  {c}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Camera enumeration failed: {ex.Message}");
        }

        // Hardware capability manager snapshot
        var mgr = new HardwareCapabilityManager();
        await mgr.RescanAsync(default);
        Console.WriteLine();
        Console.WriteLine("HardwareCapabilityManager snapshot:");
        foreach (var d in mgr.Snapshot())
        {
            Console.WriteLine($"  [{d.Class}] {d.FriendlyName} ({d.State}){(d.IsDefault ? " [default]" : "")}");
        }
        return 0;
    }
}
