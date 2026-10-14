using HoloWin.Core;
using HoloWin.Persistence;
using Xunit;

namespace HoloWin.Integration.Tests;

public class InteractionProfileTests
{
    [Fact]
    public async Task ProfileRoundTripPreservesCustomGesturesAndLightRules()
    {
        var root = Path.Combine(Path.GetTempPath(), "HoloWin-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var gesture = new FingerGesture { Name = "Point", Fingers = [FingerState.Any, FingerState.Extended, FingerState.Folded, FingerState.Folded, FingerState.Folded] };
            var rule = new LightRule { Name = "Dim", ActionId = "action", Condition = LightCondition.Below, LowerLux = 50 };
            var p = new HoloProfile { FingerGestures = [gesture], LightRules = [rule], SwipeDistance = .25, GestureHoldMilliseconds = 600 };
            p.GestureActions[gesture.Key] = "action";
            p.PresenceRules = [new PresenceRule { Name = "Connected", SourceId = "test-device", SourceName = "Test", ActionId = "action", Kind = PresenceSourceKind.Bluetooth }];
            var repository = new JsonProfileRepository(root);
            await repository.SaveAsync(p, default);
            var restored = await repository.LoadAsync(p.Id, default);
            Assert.NotNull(restored);
            Assert.Equal(gesture.Fingers, Assert.Single(restored.FingerGestures).Fingers);
            Assert.Equal(rule, Assert.Single(restored.LightRules));
            Assert.Equal(p.PresenceRules[0], Assert.Single(restored.PresenceRules));
            Assert.Equal("action", restored.GestureActions[gesture.Key]);
            Assert.Equal(.25, restored.SwipeDistance);
            Assert.Equal(600, restored.GestureHoldMilliseconds);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void MigrationSuppliesDefaultsAndRejectsInvalidRules()
    {
        var p = JsonProfileRepository.MigrateProfile(new HoloProfile { SchemaVersion = 1 });
        Assert.Empty(p.LightRules); Assert.Empty(p.FingerGestures); Assert.Equal(.18, p.SwipeDistance);
        p.LightRules = [new LightRule { ActionId = "a", LowerLux = double.NaN }];
        Assert.Throws<ArgumentException>(() => JsonProfileRepository.MigrateProfile(p));
    }
}
