# Privacy

HoloWin processes microphone signals locally to classify desk taps. It has no account requirement, telemetry upload, cloud classification, or profile synchronization.

## Sensor controls

Microphone capture starts from Dashboard, Calibration or Evaluation after microphone enablement. Use Pause or cancel the session to stop it. Preview-only capture stops when its page is left. Hand tracking starts separately in Camera Studio and stays on across navigation or while the window is hidden. Stop it in Camera Studio, Dashboard, or the tray menu. Motion and ambient readings have separate controls in Hardware. Device discovery does not itself enable continuous capture.

Windows microphone and camera privacy permissions also apply. Hand inference runs locally using bundled models. The application does not download models at runtime. Dashboard and the tray show capture status. Gesture, ambient light and presence actions each need a separate session enable switch.

Presence monitoring starts explicitly in Hardware and checks Windows connection and presence APIs. It does not collect HID keystrokes or raw reports, pair devices or scan Bluetooth advertisements. Readings stay in memory; configured rule names and results may appear in logs.

## Stored data

Application data is stored under `%LOCALAPPDATA%\HoloWin`.

| Data | Contents |
|------|----------|
| Profiles | Calibration feature vectors, labels, desk corners, device identifiers, action mappings, custom finger definitions, camera sensitivity, light rules and presence rules with selected source IDs/names |
| Settings | Device selections, preferences and enablement flags |
| Action library | Action parameters and macros, stored as plain JSON |
| Evaluation reports | Trial outcomes and accuracy/latency statistics |
| Logs | Session events, action results and error details |
| Debug captures | Short WAV recordings when explicitly enabled, capped at 100 files |
| Release cache | Extracted binaries, resources and models from the all-in-one EXE |

Raw audio recording is off by default. Camera preview is held in memory and is not recorded by the current UI. A configured screenshot action saves an image of the desktop separately from camera preview.

## Network and sensitive content

Configured websites, HTTP requests, scripts and external programs can access the network. HoloWin does not automatically upload captured audio or camera frames. Action parameters may contain sensitive text or credentials and are not encrypted. Error messages can also contain private information; inspect exported logs before sharing them.

## Deletion and removal

Delete individual profiles and reports from their pages. Dashboard can delete debug WAV recordings. Settings > Reset HoloWin data clears profiles, reports, recordings and action/settings data and disables automatic startup; it does not erase every log or cached release.

For complete removal, disable Start at sign-in, close HoloWin, delete the downloaded/extracted app, and delete `%LOCALAPPDATA%\HoloWin` if you no longer need its data. Startup diagnostics may also exist at `%TEMP%\HoloWin-startup.log`. Exported files and screenshots remain wherever you saved them.

See [Security](SECURITY.md) and [Packaging](docs/PACKAGING.md).
