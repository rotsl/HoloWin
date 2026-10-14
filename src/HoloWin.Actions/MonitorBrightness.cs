using System.Runtime.InteropServices;

namespace HoloWin.Actions;

/// <summary>Uses the display driver's Windows WMI brightness interface.</summary>
internal static class MonitorBrightness
{
    public static void Adjust(int step, bool increase, CancellationToken ct)
    {
        var owned = new List<object>();
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                ?? throw new NotSupportedException("Windows WMI brightness is unavailable.");
            dynamic locator = Activator.CreateInstance(type)!; owned.Add(locator);
            dynamic services = locator.ConnectServer(".", "root\\WMI"); owned.Add(services);
            dynamic readings = services.ExecQuery("SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE"); owned.Add(readings);
            var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (dynamic reading in readings)
            {
                owned.Add(reading); levels[(string)reading.InstanceName] = (int)reading.CurrentBrightness;
            }
            dynamic methods = services.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE"); owned.Add(methods);
            int changed = 0;
            foreach (dynamic monitor in methods)
            {
                owned.Add(monitor); ct.ThrowIfCancellationRequested();
                if (!levels.TryGetValue((string)monitor.InstanceName, out int current)) continue;
                uint result = (uint)monitor.WmiSetBrightness(0u, (byte)ActionOptions.Adjust(current, step, increase));
                if (result != 0) throw new InvalidOperationException($"The display driver rejected the brightness change ({result}).");
                changed++;
            }
            if (changed == 0) throw new NotSupportedException("No display exposes Windows brightness control. External monitors may require their own controls.");
        }
        finally
        {
            foreach (var instance in owned.AsEnumerable().Reverse())
                if (Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }
    }
}
