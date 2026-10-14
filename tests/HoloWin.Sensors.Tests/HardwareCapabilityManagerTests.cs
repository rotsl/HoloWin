using HoloWin.Core;
using HoloWin.Sensors;
using Xunit;

namespace HoloWin.Sensors.Tests;

public sealed class FakeHardwareProvider : IHardwareProvider
{
    public DeviceClass SupportedClass { get; set; }
    public List<DeviceCapability> Devices { get; } = new();
    public bool Supports(DeviceClass cls) => cls == SupportedClass;
    public Task<IReadOnlyList<DeviceCapability>> EnumerateAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DeviceCapability>>(Devices.ToList());
    public Task<bool> RequestPermissionAsync(DeviceCapability device, CancellationToken ct) => Task.FromResult(true);
}

public class HardwareCapabilityManagerTests
{
    [Fact]
    public async Task Rescan_FindsDevicesFromProviders()
    {
        var provider = new FakeHardwareProvider { SupportedClass = DeviceClass.AudioInput };
        provider.Devices.Add(new DeviceCapability
        {
            Id = "mic1",
            Class = DeviceClass.AudioInput,
            FriendlyName = "Test Mic",
            State = DeviceState.Available
        });
        var mgr = new HardwareCapabilityManager(new[] { provider });
        await mgr.RescanAsync(default);
        var snapshot = mgr.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("mic1", snapshot[0].Id);
    }

    [Fact]
    public async Task Rescan_MarksMissingDevicesAsDisconnected()
    {
        var provider = new FakeHardwareProvider { SupportedClass = DeviceClass.AudioInput };
        provider.Devices.Add(new DeviceCapability { Id = "mic1", Class = DeviceClass.AudioInput, FriendlyName = "M", State = DeviceState.Available });
        var mgr = new HardwareCapabilityManager(new[] { provider });
        await mgr.RescanAsync(default);
        provider.Devices.Clear();
        await mgr.RescanAsync(default);
        Assert.Empty(mgr.Snapshot());
    }

    [Fact]
    public async Task SetPreferredDevice_RoundTrips()
    {
        var mgr = new HardwareCapabilityManager();
        mgr.SetPreferredDevice(DeviceClass.AudioInput, "preferred-mic");
        Assert.Equal("preferred-mic", mgr.GetPreferredDevice(DeviceClass.AudioInput));
    }
}
