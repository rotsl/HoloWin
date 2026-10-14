# Hardware verification

Detection, successful capture and useful interaction are separate checks. Report the result you measured, along with the application version and test method. Keep private device identifiers and personal paths out of public reports.

| Area | Automated coverage | Manual check needed |
|------|--------------------|---------------------|
| Desk taps | DSP, classifier and calibration fixtures | Held-out zone accuracy and false triggers |
| Camera | Model loading, blank input, timed poses, swipes and custom symbols | Landmark alignment, swipe directions, foreground shortcuts and finger symbols on real frames |
| Hotkeys | Build validation | Registration conflicts, focus behavior and shutdown cleanup |
| Tray | Build validation | Show, Pause sensors and Quit |
| Sensors | Capability-manager fixtures; light-rule dwell, reset and cooldown | Live readings and lux-triggered actions near thresholds |
| Single EXE | Extraction and file-integrity workflow | Clean-machine launch and upgrade |

For acoustic testing, save a sixty-trial evaluation and record rejected trials as well as correct ones. Check action execution separately. For camera testing, start with gesture actions disarmed and verify the landmarks before assigning an action.

`PerformanceBenchmark` feeds synthetic observations into fusion. It is not a live microphone or camera soak test. No physical accuracy or end-to-end latency target is claimed.

## Presence and relative controls

Verify Bluetooth connect/disconnect and HID attach/detach rules with feedback-only actions. Record whether Windows exposes a human-presence or proximity sensor and whether its transitions match the physical setup. Check baseline, hold time, disarming and scan failures. Synthetic tests cannot verify these physical transitions. Test volume and brightness steps on supported outputs before marking them verified.
