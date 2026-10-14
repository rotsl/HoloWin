using HoloWin.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Sensors;
using Windows.Foundation.Metadata;

namespace HoloWin.Platform.Windows;

/// <summary>Read-only device connection and proximity snapshots. Does not pair or read HID keystrokes.</summary>
public sealed class PresenceMonitor
{
    private readonly Dictionary<string, PresenceReading> _known = new();
    public string Status { get; private set; } = "Stopped";
    public async Task<IReadOnlyList<PresenceReading>> ScanAsync(IEnumerable<PresenceRule> savedRules, CancellationToken ct)
    {
        foreach (var rule in savedRules) _known.TryAdd(rule.SourceId, new(rule.SourceId, rule.SourceName, rule.Kind, null));
        var errors = new List<string>();
        foreach (var kind in Enum.GetValues<PresenceSourceKind>())
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                var found = new List<PresenceReading>();
                if (kind == PresenceSourceKind.Bluetooth)
                {
                    foreach (bool le in new[] { false, true })
                    {
                        var paired = await DeviceInformation.FindAllAsync(le ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) : BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask(timeout.Token);
                        var connected = await DeviceInformation.FindAllAsync(le ? BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected) : BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)).AsTask(timeout.Token);
                        var connectedIds = connected.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var device in paired.Concat(connected).DistinctBy(d => d.Id))
                            found.Add(new("bluetooth:" + device.Id, device.Name, kind, connectedIds.Contains(device.Id)));
                    }
                }
                else if (kind == PresenceSourceKind.Hid)
                {
                    var devices = await DeviceInformation.FindAllAsync("System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True").AsTask(timeout.Token);
                    found.AddRange(devices.Select(d => new PresenceReading("hid:" + d.Id, d.Name, kind, d.IsEnabled)));
                }
                else if (kind == PresenceSourceKind.Proximity)
                {
                    var devices = await DeviceInformation.FindAllAsync(ProximitySensor.GetDeviceSelector()).AsTask(timeout.Token);
                    foreach (var device in devices)
                    {
                        var sensor = ProximitySensor.FromId(device.Id);
                        found.Add(new("proximity:" + device.Id, device.Name, kind, sensor?.GetCurrentReading()?.IsDetected));
                    }
                }
                else if (ApiInformation.IsTypePresent("Windows.Devices.Sensors.HumanPresenceSensor"))
                {
                    var sensor = await HumanPresenceSensor.GetDefaultAsync().AsTask(timeout.Token);
                    if (sensor is not null && sensor.IsPresenceSupported)
                    {
                        var reading = sensor.GetCurrentReading();
                        bool? present = reading?.Presence switch { HumanPresence.Present => true, HumanPresence.NotPresent => false, _ => null };
                        found.Add(new("human-presence:" + sensor.DeviceId, "Windows human-presence sensor", kind, present));
                    }
                }
                var ids = found.Select(d => d.Id).ToHashSet();
                foreach (var old in _known.Values.Where(d => d.Kind == kind && !ids.Contains(d.Id)).ToArray())
                    _known[old.Id] = old with { Present = kind is PresenceSourceKind.Proximity or PresenceSourceKind.HumanPresence ? null : false };
                foreach (var reading in found) _known[reading.Id] = reading;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors.Add($"{kind}: {ex.Message}");
                foreach (var old in _known.Values.Where(d => d.Kind == kind).ToArray()) _known[old.Id] = old with { Present = null };
            }
        }
        Status = errors.Count == 0 ? "Connection and presence scan complete" : string.Join("; ", errors);
        return _known.Values.OrderBy(d => d.Kind).ThenBy(d => d.Name).ToArray();
    }
}
