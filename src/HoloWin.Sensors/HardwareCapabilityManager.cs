using System.Collections.Concurrent;
using HoloWin.Core;

namespace HoloWin.Sensors;

public sealed class HardwareCapabilityManager : IHardwareCapabilityManager, IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<DeviceClass, string> _preferredDevices = new();
    private readonly ConcurrentDictionary<string, DeviceCapability> _current = new();
    private readonly List<IHardwareProvider> _providers = new();
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private bool _disposed;

    public event EventHandler<DeviceCapability>? DeviceChanged;

    public HardwareCapabilityManager(IEnumerable<IHardwareProvider>? providers = null)
    {
        if (providers is not null) _providers.AddRange(providers);
    }

    public void RegisterProvider(IHardwareProvider provider) { lock (_providers) _providers.Add(provider); }

    public IReadOnlyList<DeviceCapability> Snapshot()
    {
        return _current.Values.OrderBy(c => c.Class.ToString()).ThenBy(c => c.FriendlyName).ToList();
    }

    public async Task RescanAsync(CancellationToken ct)
    {
        var seen = new HashSet<string>();
        IHardwareProvider[] providersCopy;
        lock (_providers) providersCopy = _providers.ToArray();
        foreach (var provider in providersCopy)
        {
            try
            {
                var devices = await provider.EnumerateAsync(ct);
                foreach (var d in devices)
                {
                    seen.Add(d.Id);
                    _current[d.Id] = d;
                    DeviceChanged?.Invoke(this, d);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var errCap = new DeviceCapability
                {
                    Id = $"{provider.GetType().Name}.error",
                    Class = DeviceClass.Other,
                    FriendlyName = $"{provider.GetType().Name} enumeration error",
                    State = DeviceState.Error,
                    StateDetail = ex.Message
                };
                _current[errCap.Id] = errCap;
                DeviceChanged?.Invoke(this, errCap);
            }
        }
        // Drop devices that are no longer reported.
        var missing = _current.Keys.Where(k => !seen.Contains(k) && !_current[k].Id.EndsWith(".error")).ToList();
        foreach (var id in missing)
        {
            if (_current.TryGetValue(id, out var cap))
            {
                var disc = cap with { State = DeviceState.Disconnected };
                _current[id] = disc;
                DeviceChanged?.Invoke(this, disc);
                _current.TryRemove(id, out _);
            }
        }
    }

    public Task<bool> RequestPermissionAsync(DeviceCapability device, CancellationToken ct)
    {
        // On Windows 11, microphone and camera access is granted via system-level
        // privacy settings and per-app runtime permissions. We cannot programmatically
        // grant them. We can only prompt the user by attempting to use the device.
        // The implementer of IHardwareProvider for the device class is responsible
        // for surfacing the OS prompt.
        var providersCopy = _providers.ToArray();
        foreach (var p in providersCopy)
        {
            if (p.Supports(device.Class))
            {
                try { return p.RequestPermissionAsync(device, ct); }
                catch { /* continue */ }
            }
        }
        return Task.FromResult(false);
    }

    public void SetPreferredDevice(DeviceClass cls, string id)
    {
        _preferredDevices[cls] = id;
    }

    public string? GetPreferredDevice(DeviceClass cls)
    {
        return _preferredDevices.TryGetValue(cls, out var id) ? id : null;
    }

    public void StartHotPlugMonitoring(TimeSpan interval)
    {
        if (_monitorTask is not null) return;
        _monitorCts = new CancellationTokenSource();
        _monitorTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(_monitorCts.Token).ConfigureAwait(false))
            {
                try { await RescanAsync(_monitorCts.Token); }
                catch (OperationCanceledException) { break; }
                catch { /* swallow to keep monitoring */ }
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _monitorCts?.Cancel();
        if (_monitorTask is not null)
        {
            try { await _monitorTask; } catch { /* ignore */ }
        }
        _monitorCts?.Dispose();
    }
}

public interface IHardwareProvider
{
    bool Supports(DeviceClass cls);
    Task<IReadOnlyList<DeviceCapability>> EnumerateAsync(CancellationToken ct);
    Task<bool> RequestPermissionAsync(DeviceCapability device, CancellationToken ct);
}
