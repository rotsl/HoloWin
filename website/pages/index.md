# Control Windows with desk taps and hand gestures

HoloWin turns taps on four desk zones into actions on your PC. A webcam can also recognize hand poses, swipes and custom finger symbols. Processing runs locally on Windows 11.

<div class="downloads">
  <a class="download" href="https://github.com/rotsl/HoloWin/releases/latest/download/HoloWin.exe">Download HoloWin.exe</a>
  <a href="https://github.com/rotsl/HoloWin/releases/latest">Release notes and checksum</a>
</div>

Windows 11 build 22621 or later, x64. The single EXE includes its companion files and runtimes, which it extracts on first launch. No Microsoft Store installation is needed. Downloads are built by the repository's release workflow; the link becomes available after the first release finishes.

## What can it do?

- Open websites and apps, send F1–F10 or custom shortcuts, and run saved macros.
- Switch tabs with left/right hand swipes or assign your own gesture actions.
- Increase or decrease volume and supported display brightness.
- Trigger actions from light levels, Bluetooth connections, HID attachment or supported Windows presence sensors.

Choose your actions and save them in a profile. Cameras and sensors start only when you enable them. Gesture, light and presence actions also need to be armed for each session.

<img class="app-screenshot" src="assets/dashboard.png" alt="HoloWin dashboard with profile, calibration and sensor controls" width="1322" height="873" loading="lazy">

## Quick start

1. Download and run **HoloWin.exe**. Allow time for the first extraction.
2. In **Hardware**, select and enable your microphone, then press **Apply microphone**.
3. In **Profiles**, create a Passive profile or select one and press **Use selected**.
4. In **Calibration**, collect ten taps in each of the four zones and save.
5. In **Actions**, save an action and assign it to a zone.
6. Choose **Audio only** in Settings, then press **Start listening** on Dashboard. One accepted tap runs the assigned action.

Click a Dashboard zone to test its action without the microphone. For camera controls, assign a gesture in Actions, start hand tracking in Camera Studio, check the landmark dots, then enable gesture actions and focus the app you want to control.

## Before you use it

HoloWin is experimental. Tap accuracy depends on your desk, microphone and room; calibrate each setup. Camera gestures need clear lighting and a visible hand. Brightness and presence features require hardware that Windows exposes to the app.

Profiles and settings stay on your PC. There is no telemetry or cloud classification. Actions you configure, such as opening a website or running a script, can use the network.

Read the [setup guide](https://github.com/rotsl/HoloWin#quick-start), [troubleshooting notes](https://github.com/rotsl/HoloWin/blob/main/docs/TROUBLESHOOTING.md) or [privacy policy](https://github.com/rotsl/HoloWin/blob/main/PRIVACY.md).

## Support HoloWin

[Sponsor rotsl on GitHub](https://github.com/sponsors/rotsl), [report a bug](https://github.com/rotsl/HoloWin/issues) or [contribute a change](https://github.com/rotsl/HoloWin/blob/main/CONTRIBUTING.md).
