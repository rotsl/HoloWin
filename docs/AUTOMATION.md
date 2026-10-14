# Actions and macros

Actions are local saved configurations. A profile's zone mapping references an action by ID. Saving an action does not automatically assign it to a zone.

## Configure and test

1. In Actions, create an action, choose its type and fill in the parameters.
2. Save it, choose a desk zone, enable that zone and press Assign selected action.
3. Use Test action to execute the action immediately, or click the assigned zone on Dashboard to test the mapping.
4. Start listening on Dashboard to use calibrated microphone taps.

Use a complete URL for websites, such as `https://example.com`. Missing files, invalid parameters and blocked actions are reported in the UI or recent events.

| Control | Meaning |
|---------|---------|
| Enabled | Allows the action or zone to execute |
| Cooldown | Minimum interval between runs; 200 ms equals 0.2 seconds |
| Minimum confidence | Required classifier score from 0 to 1 |
| Timeout | Time before cancellation is requested |
| Approval | Required opt-in for restricted action types |
| Confirmation | Prompt before each execution |

Both zone and action thresholds can affect dispatch. Raising confidence rejects more uncertain taps; it does not improve the underlying classifier. Timeout/cancellation support depends on the executor, and completed side effects are not undone.

## Available actions

| Types | Parameters/behavior | Approval required |
|-------|---------------------|-------------------|
| VisualOnly | Event feedback without an external action | No |
| PlaySound | WAV path, or system sound when empty | No |
| CopyText / TypeText / SpeakText | Text; speech also accepts a rate | No |
| OpenWebsite | Website URL | No |
| OpenFileOrFolder | Local path | No |
| OpenApplication / FocusApplication | Executable path | No |
| TriggerKeyboardShortcut | Shortcut such as Ctrl+Shift+S | No |
| FunctionKey | F1–F10; optional Ctrl, Alt, Shift or Win modifiers | No |
| SwitchTab | Next/previous; optional custom shortcut | No |
| IncreaseVolume / DecreaseVolume | Step percentage, 1–100; default 5 | No |
| IncreaseBrightness / DecreaseBrightness | Step percentage, 1–100; default 5 | No |
| SendMediaKey / MuteAudio | Media command or mute toggle | No |
| SwitchVirtualDesktop | Left or right | No |
| TakeScreenshot | Existing folder or PNG file path; folders receive unique filenames | Yes |
| RunPowerShell | Script | Yes |
| RunExecutable | Executable path and arguments | Yes |
| InvokeHttpEndpoint | URL, method and optional body | Yes |
| RunMacro | Saved macro ID | Yes |

Smart-home actions are not available in the editor. Keyboard and text actions depend on the currently focused application. Configured scripts and programs run with the user's permissions.

Volume steps apply to the default playback endpoint. Brightness steps apply to active displays exposed through Windows WMI monitor brightness controls, commonly an internal laptop display. Unsupported external monitors produce an error; DDC/CI control is not implemented. Both levels are clamped to 0–100%. Legacy SetVolume actions remain editable when present in the saved library.

Function keys send standard F-key input. Laptop Fn hardware keys are separate. Tab switching defaults to Ctrl+Tab or Ctrl+Shift+Tab. Supply a custom shortcut for applications with different bindings. Keyboard actions target the foreground application.

## Macros

Press Create dummy example macro to save two visual feedback actions and a macro that runs them 500 ms apart. Copy its displayed ID into an Automation · Macro action, approve that action and test it. The example does not press keys or change system settings. The button does not assign a trigger or overwrite an existing example.

A macro runs saved actions in order. Select a non-macro action, add it as a step, set the delay, and repeat as needed. Enter a macro name and save. Create a RunMacro action with the displayed macro ID, approve it, and assign it to a zone.

Each step uses the current saved version of its action. Disabled, missing or failed actions stop the macro. Nested macro steps are not supported. Global zone hotkeys are configured in Settings. Dynamic plugin execution is not connected.

See [Security](../SECURITY.md) and [Calibration](CALIBRATION.md).

## Camera gestures, hotkeys and tray controls

Create tab actions and bind left/right swipes saves next/previous tab actions with an 800 ms cooldown. Left runs next tab and right runs previous tab. Shortcuts, cooldown and assignments remain editable. The preset refuses to replace existing left/right assignments and leaves gesture actions disarmed.

Assign a built-in pose, directional swipe or custom finger symbol to a saved action in Actions. See [Camera controls](CAMERA_SETUP.md) for sensitivity and tab/window shortcut examples. In Camera Studio, press Start hand tracking and inspect the landmark dots before enabling gesture actions. The enable switch applies only to the current session. Tracking stays on across navigation and while the window is hidden; Stop camera or Pause sensors turns it off.

Settings lets you bind shortcuts such as Ctrl+Alt+1 to desk zones. Empty bindings are disabled. Windows rejects shortcuts that another application owns. Hotkeys pause during calibration and evaluation; they remain active when sensors are paused.

Hide window to tray keeps HoloWin running. The tray menu has Show HoloWin, Pause sensors and Quit. Closing the main window quits the application.

## Ambient light rules

In Actions, select a saved action, then create a named ambient light rule. Above and Below use the threshold field. Range includes both limits. Select an existing rule to edit it, or use New light rule to create another. Saving a rule assigns the action currently selected in the action editor.

A rule fires once after its condition has held for the configured time. It must leave the condition by the hysteresis margin before it can fire again. Cooldown sets the minimum time between runs. For example, Above 100 lux with 10 lux hysteresis resets at 90 lux or lower. A range of 100–500 lux with a 10 lux margin resets below 90 or above 510 lux. Above/below comparisons are strict; range endpoints are included.

Start sensor readings in Hardware, then enable light rules for the session. A Windows ambient light sensor is required; camera brightness is not used as lux. Arming while a condition is already true can fire after the hold time. Missing readings for more than two seconds reset the hold; returning readings begin a new entry.

Rules start disarmed. Changing profile, editing rules, stopping sensor readings, pausing sensors or quitting disarms them. Calibration and evaluation suspend light dispatch. Overlapping rules can run multiple actions in order. If another action is running, a light trigger is skipped and logged rather than queued. Saved action enablement, cooldown, approval and confirmation still apply. Importing a profile removes its light rules to avoid retaining action references from another library.

## Presence and connection rules

Bluetooth connections, HID attachment and supported Windows presence/proximity readings can trigger saved actions. Rules belong to the active profile and need separate monitoring and session arming in Hardware. See [Presence and device connections](PRESENCE.md) for setup, timing and device limits.
