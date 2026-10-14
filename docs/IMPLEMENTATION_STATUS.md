# Implementation status

HoloWin supports calibrated desk taps, camera gestures and saved actions on Windows 11 x64. Features that depend on hardware still need a check on the PC where they will run.

## Available controls

- Microphone calibration uses ten taps per zone. Evaluation collects fifteen new trials per zone.
- Camera Studio recognizes poses, directional swipes and custom finger patterns. Gesture actions need session arming.
- Actions include F1–F10, customizable tab switching, volume and brightness steps, text, applications and macros. A feedback-only macro example is included.
- Global hotkeys run zone assignments. The tray can show the window, pause sensors or quit.
- Hardware can show acceleration, rotation, light and inclinometer readings when Windows exposes them.
- Light rules use lux thresholds. Presence rules use Bluetooth connections, HID attachment or supported Windows presence/proximity sensors. Both need session arming.

Preview-only camera capture stops on navigation. Hand tracking continues until stopped. Calibration and evaluation suspend automatic actions.

## Verification

Tests cover DSP, classification, persistence, model loading, synthetic gestures, action options and rule timing. They do not measure real-camera accuracy or physical desk performance. Hotkey conflicts, sensor transitions and display brightness need manual checks. See [Testing](TESTING.md) and [Hardware verification](HARDWARE_VERIFICATION_MATRIX.md).

The release workflow tests Release, bundles runtimes and camera models, and verifies extraction and file hashes. The desktop EXE and standalone launcher use the same HoloWin icon. The workflow uploads an EXE and checksum; publishes them on a GitHub Release, and leaves the binary unsigned. Clean-machine GUI deployment remains unverified.

## Unavailable integrations

Depth, smart-home integration, raw device-specific HID/Bluetooth/serial decoding and dynamic plugin loading are not implemented. Active acoustic probing and camera gesture recognition are experimental. ARM64 has not been validated.

See [Camera controls](CAMERA_SETUP.md), [Presence](PRESENCE.md) and [Packaging](PACKAGING.md).
