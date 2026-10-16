# Testing

Run on Windows with the .NET 10 SDK:

```powershell
.\scripts\download-hand-model.ps1
dotnet build HoloWin.sln -c Debug
dotnet test HoloWin.sln -c Debug --no-build --no-restore
```

The test suite uses synthetic inputs. Run it after code changes and check the result for that commit.

## Test projects

| Project | Focus |
|---------|-------|
| HoloWin.Core.Tests | DSP, classifier, zones, timing, presence transitions, light rules and audio-file primitives |
| HoloWin.Audio.Tests | Acoustic pipeline contracts |
| HoloWin.Actions.Tests | Dispatcher gates, function-key/tab options and relative control validation |
| HoloWin.Fusion.Tests | Observation routing and modes |
| HoloWin.Sensors.Tests | Capability-manager behavior using test providers |
| HoloWin.Vision.Tests | Model loading, blank-frame inference, gesture stability, four swipe directions, custom finger poses and hand-loss reset |
| HoloWin.Integration.Tests | Calibration, evaluation, persistence and selected cross-module flows |

Coverage is selective. Tests do not verify every timeout, UI control, external application or device. Model and synthetic landmark tests do not establish gesture accuracy on a webcam.

## Packaging check

```powershell
.\scripts\publish-single-exe.ps1
Start-Process -FilePath .\artifacts\standalone\HoloWin.exe -ArgumentList '--extract-only' -Wait
```

The release workflow runs the EXE from a folder without companion files, verifies extraction and compares required files with the published payload. It uploads the EXE and checksum. It also runs --smoke-test, which loads a themed WinUI control and exits without profiles, hotkeys or sensors. This catches missing theme resources but does not test the full interface or physical hardware.

## Synthetic throughput tool

```powershell
dotnet run --project tools/PerformanceBenchmark/PerformanceBenchmark.csproj -c Release -- --duration=60
```

This feeds generated observations into fusion. It does not capture a microphone or measure physical impact-to-action latency.

## Manual checks

Start with profile selection, calibration save and manual zone actions. Then test microphone start/pause, physical taps, confirmation prompts and evaluation reports. Check that settings survive a restart and that closing the title bar quits the app while hiding to tray keeps it running.

For camera tests, use feedback-only actions first. Check preview stop-on-navigation, swipe direction, symbol hold/release and saved sensitivity. Test foreground shortcuts separately. With a light sensor, check threshold jitter, hold time, cooldown and disarming.

See [Hardware verification](HARDWARE_VERIFICATION_MATRIX.md) for recording physical results.

CI builds and tests Debug and Release. The release workflow runs Release tests before packaging. Automated tests do not change system volume/brightness or send function keys.

For presence checks, use a feedback action. Verify that arming establishes a baseline, then test connection/disconnection, hold time, cooldown, unknown readings and profile changes. Check presence/proximity sensors only if Windows exposes them. Confirm that Pause sensors stops monitoring. Record physical results separately from synthetic tests.
