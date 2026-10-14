# Troubleshooting

## The app will not launch

The all-in-one EXE can run by itself. Its first launch extracts the app into `%LOCALAPPDATA%\HoloWin\releases`; allow enough time and disk space for extraction. Look for `releases\launcher-error.log` if extraction fails. App startup errors are written to `%TEMP%\HoloWin-startup.log`.

For the folder/ZIP distribution, extract all files and keep the DLLs and resource folders together. A small EXE copied from that folder is not the all-in-one package. Both published formats include runtimes; framework-dependent development builds still require installed .NET 10 desktop and Windows App SDK 1.6 runtimes.

## A calibrated tap does not run an action

1. Save calibration after collecting ten accepted taps in each zone.
2. In Profiles, press Use selected and check the active name on Dashboard.
3. Save the action in Actions, then select a zone and press Assign selected action. For a website, use a complete URL such as `https://example.com`.
4. Enable/select the microphone in Hardware and choose Audio only in Settings.
5. Save or cancel any calibration/evaluation session, then press Start listening on Dashboard.
6. Click the assigned Dashboard zone to test its mapping independently of the microphone. Try a physical tap and inspect recent events for rejections, thresholds, cooldowns or action errors.

An approved action can still be disabled, blocked by confidence, missing a parameter, or awaiting confirmation. Test action performs the actual configured operation.

## Microphone or camera access

In Windows Settings > Privacy & security, allow microphone/camera access for desktop apps. HoloWin's own controls are separate: microphone selection is in Hardware, and preview starts in Camera Studio or Desk Map. Check shutters and whether another app is using the camera. OpenCV indices may differ from device-inventory order.

A detected device is not proof of successful capture. Reconnect a lost microphone and explicitly restart listening.

## Poor tap accuracy

Keep the PC and microphone position stable and reduce competing impacts or noise. Recalibrate if the surface, input device or arrangement changes. Use Evaluation for fifteen new trials per zone. Review confusion and rejection counts; lowering thresholds can increase false triggers.

## Audible active probe

Stop capture, disable the experimental probe in Settings and select or create a Passive profile. Recalibrate that profile. Do not train using one sensing strategy and switch to another for recognition.

## Hand tracking does not detect a gesture

Press Start hand tracking in Camera Studio, check that landmarks appear, and enable assigned gesture actions for the session. Use even lighting and keep the whole hand visible. Hold the pose, then release it before repeating. If a source build reports missing models, run scripts/download-hand-model.ps1 and rebuild. Published packages include the verified models. See [Camera controls](CAMERA_SETUP.md).

## Profiles and updates

Imported profiles receive new IDs and disabled action mappings. Assign local actions explicitly and recalibrate for different hardware or desks. A profile with a newer unsupported schema requires a compatible application version. Back up `%LOCALAPPDATA%\HoloWin` before upgrading.

The all-in-one launcher uses a separate cache directory for each payload. If startup at sign-in points to an old release, disable it and re-enable it from the new version. See [Packaging](PACKAGING.md).

## Build failures

```powershell
dotnet --list-sdks
dotnet restore HoloWin.sln
dotnet build HoloWin.sln -c Debug
```

Use the .NET 10 SDK on Windows. Restore requires access to the configured NuGet source. Include the first compiler/restore error, rather than only the final build summary, in a [bug report](../CONTRIBUTING.md). No MSIX certificate installation is required.

## Swipes or finger symbols do not trigger

Show one hand and check its landmark dots before enabling actions. Swipe in the preview's coordinate direction, far enough within 700 ms, then wait at least 800 ms before another swipe. In Actions, reduce swipe travel if the movement is too small, or increase it to reduce accidental triggers. Hold custom finger symbols still; they take priority over built-in poses. Saving a custom symbol or camera sensitivity stops tracking, so restart and rearm it. Keyboard shortcuts affect the foreground app, not a specific browser or window.

## Ambient light rules do not trigger

Start sensor readings in Hardware and check that a lux reading appears. A discovered camera is not a light sensor. Select a profile, save its light rule with a saved action, then enable light rules for the session. Check the rule's enabled flag, hold time and action settings. A fired rule must cross its reset margin before it can run again. Editing rules, changing profile and stopping sensors disarm light actions. Recent events reports skipped triggers when another action is running.

## Camera preview is blank

Check the received-frame counter in Camera Studio or Desk Map. A zero count means camera delivery has not started. Startup waits up to eight seconds for the first frame and reports a timeout if none arrives. A rising count confirms frame delivery, but the frames themselves may be dark. The preview reports when its image is nearly black; check the shutter, lighting and camera index in that case. Otherwise, look for Preview errors in recent events. The image sits directly below the Start/Stop controls, with an updated-frame count. Close other camera applications and verify the selected camera index. Try Start preview before enabling tracking.

## Close or the terminal appears stuck

Use the title-bar close button or Quit in the tray. Driver cleanup runs away from the UI thread; if it does not finish within eight seconds, the app exits. The run script starts HoloWin separately and returns the terminal prompt. Ctrl+C can interrupt a build, but does not close an app already launched by the script.

## A screenshot action fails

Use an existing destination folder or a full PNG filename. Surrounding double quotes are accepted. A folder receives a unique filename for each screenshot. Check that the action is approved and assigned to the intended zone; visual-feedback-only zones do not execute it.

## Presence rules or relative controls do not run

Scan sources in Hardware and inspect their state. Save a rule for a saved action, start monitoring and arm presence actions. The first reading is a baseline; the source must change state after arming. A Bluetooth device can be paired without being connected. HID presence indicates an attached interface, not user activity. Unknown readings do not fire a rule.

Brightness actions require Windows WMI monitor brightness support. An external monitor may need its own controls. Volume steps target the default playback device. For F-key and tab actions, focus the intended application before triggering; use a custom tab shortcut if its bindings differ.
