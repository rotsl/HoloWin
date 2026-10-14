# Presence and device connections

Sensor collection v2 adds rules for Bluetooth connections, HID attachment, Windows human-presence sensors and proximity sensors. Monitoring is optional and starts off.

## Set up a rule

1. Select a profile. In Hardware, press Scan sources under Sensor collection v2.
2. In Actions, create and save the action you want to run.
3. In Presence and device connection rules, press Scan / refresh sources and choose a source.
4. Name the rule, select its trigger condition, and set the hold time and cooldown. Save the rule for the selected action.
5. In Hardware, press Start monitoring, then enable presence actions for the session.

The first reading establishes a baseline. A rule fires when the source changes to its configured state and stays there for the hold time. Connecting a device before arming does not immediately run its action. Disconnect and reconnect it to test a connection rule.

Scans repeat three seconds after each monitoring cycle finishes. Device queries and running actions can make the interval longer. Hold time is checked against sampled readings; brief changes between scans may be missed. Cooldown suppresses repeated triggers. It does not queue them for later.

## What the sources mean

| Source | Signal | Limits |
|--------|--------|--------|
| Bluetooth | Windows reports a paired or connected classic Bluetooth or BLE device as connected/disconnected | A nearby device can be disconnected. Sleeping devices and radio changes can affect the signal. |
| HID | A Windows HID interface is attached/enabled or missing | One physical device may expose several interfaces. HoloWin does not read keyboard input or raw HID reports. |
| HumanPresence | A Windows human-presence sensor reports present/absent | Requires a supported sensor, driver and Windows API. Unknown readings do not trigger actions. |
| Proximity | A Windows proximity sensor reports near/far | Detection may describe an object near the sensor, rather than a person at the desk. |

HoloWin does not pair devices, scan Bluetooth advertisements or decode device-specific GATT data. Connection state does not identify a person. Use these rules for convenience actions. They cannot authenticate a person.

The implementation uses Windows [Bluetooth connection selectors](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.getdeviceselectorfromconnectionstatus), [device enumeration](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformation.findallasync), [HumanPresenceSensor](https://learn.microsoft.com/en-us/uwp/api/windows.devices.sensors.humanpresencesensor) and [ProximitySensor](https://learn.microsoft.com/en-us/uwp/api/windows.devices.sensors.proximitysensor). The app checks human-presence API availability at runtime.

## Action behavior and storage

Changing profiles, editing rules, stopping monitoring or pausing sensors disarms presence actions. Calibration and evaluation suspend dispatch. Re-arming, unknown readings and long reading gaps establish a new baseline. Query errors produce unknown state, rather than a disconnect trigger.

Rules share the action dispatcher with other inputs. Saved action enablement, cooldown, approval and confirmation apply. If another action is running, a presence trigger is skipped and logged. Several matching rules can run in sequence.

Selected source IDs, names and rule settings are stored in the local profile. Importing a profile clears presence rules because device IDs and action references belong to the original setup. Live readings stay in memory; configured rule names and action results may appear in logs. Hardware availability and physical transitions need testing on the target PC.
