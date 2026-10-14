# Architecture

HoloWin is a WinUI 3 desktop application targeting .NET 10 on Windows 11. `AppServices` creates a shared `WorkspaceController`; all navigation destinations use its profile, device, capture, calibration, evaluation, and action state.

## Application flow

```text
WASAPI microphone -> streaming tap detector -> bounded tap queue
  -> feature extraction -> trained classifier -> accepted acoustic observation
  -> fusion engine -> profile zone assignment -> action dispatcher
```

The capture pipeline reports both accepted and rejected decisions. During calibration, features go to the guided capture session; during evaluation, decisions go to the evaluation session. Automatic action routing is suspended during both sessions. Dashboard manual zone buttons use the same assignment and action dispatcher without needing microphone input.

Selecting a profile stops capture. Starting normal listening requires saved calibration and an audio interaction mode. Closing the main window disposes capture and background services.

## Modules

| Module | Responsibility |
|--------|----------------|
| Core | DSP, classification, timing, queues, zones, profiles and action contracts |
| Audio | WASAPI microphone capture, acoustic pipeline and experimental active probe |
| Vision | OpenCV capture, palm detection, hand landmarks and desk-map geometry |
| Gestures | Timed pose recognition, directional swipes, custom finger patterns and hand-loss reset |
| Sensors | Provider contracts, capability inventory and device-change monitoring |
| SensorFusion | Observation queue, mode selection and fused decisions |
| Calibration | Guided sample collection, validation and held-out evaluation |
| Actions | Action execution, confidence/cooldown checks, approval and confirmation |
| Automation | Ordered macro execution using saved actions |
| Persistence | JSON profiles, settings, action library and evaluation reports |
| Plugins | Manifest catalog; no dynamic plugin loading |
| Diagnostics | Diagnostic data structures and bounded histories |
| Platform.Windows | Windows hardware, sensor readings, global hotkeys, tray controls, startup and paths |
| App | WinUI navigation, screens, shared controller and application lifecycle |

## Camera and sensors

Preview-only capture stops when its page is left. Hand tracking runs in a separate worker with a one-frame queue and stays on across navigation. The worker detects palms, samples oriented hand crops and predicts 21 landmarks per hand. Stable gestures route through the profile gesture map and action dispatcher only while the session is armed. Four manually selected desk corners define a separate homography; gestures do not infer physical desk contact.

The Windows provider discovers microphones, cameras, sensor availability and device interfaces. Motion and light readings require explicit enablement. Light rules use a separate session arm switch and evaluate dwell, hysteresis and cooldown before sharing the action dispatch gate. Profile changes and sensor stops disarm light actions.

PresenceMonitor separately samples Bluetooth connections, HID attachment and Windows human-presence/proximity sensors. PresenceRuleEvaluator establishes a baseline, debounces transitions and applies cooldown before using the shared action dispatch gate. These rules do not enter desk-zone fusion. Raw device-specific HID/Bluetooth/serial protocols are not implemented. Depth and smart-home integration are absent.

## Routing and actions

The controller applies zone mappings and thresholds, then calls `ActionDispatcher`. The dispatcher checks action settings and routes to the relevant Windows executor. Scripts, executables, HTTP requests, screenshots and macros require approval. Per-execution confirmation is displayed through the main window. Timeouts request cancellation; not every native operation can be interrupted immediately.

`MacroRunner` resolves steps against the saved action library. Nested macro actions are rejected. See [Automation](docs/AUTOMATION.md).

## Persistence and diagnostics

Data lives under `%LOCALAPPDATA%\HoloWin`:

- `Profiles/` and `current-profile.txt`: profile JSON and active selection.
- `settings.json` and `actions.json`: preferences, actions and macros.
- `Evaluations/`: saved evaluation reports.
- `DebugCaptures/`: optional short WAV recordings.
- `Logs/`: session events; the controller also holds a bounded recent-event history.

Profile schema version is 2. Profile and workspace saves use temporary files followed by replacement. Loading validates supported profile schemas; migration is persisted on a subsequent save. Imported profile mappings are disabled and action IDs cleared before use.

## Distribution

`scripts/publish.ps1` builds a self-contained app folder and ZIP. `scripts/publish-single-exe.ps1` embeds that ZIP into `tools/PortableLauncher`. The launcher extracts the payload into a hash-specific local cache and starts the WinUI executable. Native libraries and resources remain separate inside that cache because WinUI needs them at runtime.

The package includes camera libraries and pinned palm/hand models. Build scripts download and verify the models; the application does not download them at runtime. See [Packaging](docs/PACKAGING.md) and [Implementation status](docs/IMPLEMENTATION_STATUS.md).
