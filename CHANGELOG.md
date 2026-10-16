# Changelog

## v0.1.1

- Fixed silent startup failure in the GitHub-built EXE by rebuilding the WinUI resource index with self-contained settings.
- Added a check for the WinUI theme resource and a packaged startup smoke test before release uploads.
- The launcher now reports early application crashes instead of exiting silently.
- Added the dashboard screenshot to README and the website.
- Updated packaging and troubleshooting instructions.

## v0.1.0

Initial experimental release for Windows 11 x64.

### Inputs and calibration

- Local microphone recognition for four desk zones, with ten-tap-per-zone calibration and separate fifteen-trial-per-zone evaluation.
- Calibration validation, negative examples, rejected-tap reasons and microphone capture counters.
- Camera preview, local palm detection and 21-point hand tracking, with bundled models.
- Directional swipes, custom finger symbols and per-profile camera sensitivity.
- Ambient light rules for above, below and range conditions, with hold time, reset margin and cooldown.
- Bluetooth connection, HID attachment and supported Windows human-presence/proximity rules with session arming.
- Live Windows acceleration, rotation, light and inclinometer readings when supported.
- Global zone hotkeys and manual Dashboard zone buttons.

### Actions and interface

- Saved actions, zone and gesture assignments, confidence thresholds, cooldowns, approval and confirmation.
- F1–F10 with modifiers, customizable tab switching and a left/right swipe preset.
- Increase/decrease volume and brightness with configurable steps. Existing absolute-volume actions remain supported.
- Ordered macros and a feedback-only example.
- Text, website, file, application, media, screenshot, virtual-desktop, script, executable and HTTP actions.
- Actions grouped by purpose; sidebar sections for setup, camera and automation.
- Dashboard profile and sensor status, recent events and diagnostics export.
- Tray controls and native minimize, maximize and close buttons.
- Camera startup checks frame delivery and reports preview errors. Driver shutdown has a bounded wait.
- Adaptive fusion preserves actionable observations. Screenshot destinations accept folders and quoted paths.

### Storage and distribution

- Local JSON profiles, settings, actions and evaluation reports; optional debug WAV capture and startup at sign-in.
- Portable ZIP and all-in-one EXE packages with runtimes, camera libraries, models and model notices.
- Matching HoloWin artwork in README, both EXEs, the desktop window and tray.
- Debug/Release CI tests and a release workflow that tests Release, verifies extraction and uploads the standalone EXE with its SHA-256 checksum.
- Public documentation revised for setup, troubleshooting and hardware limits. Project attribution is kept in README.
- A MkDocs GitHub Pages site with the logo, quick-start instructions and a direct release download.
- GitHub sponsorship for rotsl and workflow publication of the EXE and checksum as GitHub Release assets.

Depth, smart-home integrations, raw device-specific sensor decoding and dynamic plugins are not implemented. Automated tests do not establish physical desk accuracy, real-camera accuracy or clean-machine compatibility.
