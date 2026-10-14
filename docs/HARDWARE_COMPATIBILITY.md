# Hardware compatibility

HoloWin targets Windows 11 build 22621 or later. Published packages target x64. ARM64 configuration exists in source but native dependencies and behavior remain unverified.

| Device class | Current integration | Limits |
|--------------|---------------------|--------|
| Microphone | WASAPI input, selectable in Hardware | Calibrate each setup; device/driver format support varies |
| Microphone array | Per-channel features when exposed | An array may be exposed as one aggregate channel |
| Speakers | Experimental active probe through the default output | Passive sensing does not require probe output |
| RGB camera | Preview, hand tracking, gestures and manual desk map | Index order can differ from inventory |
| Accelerometer / gyrometer | Windows API discovery and opt-in readings | Requires a sensor exposed by the driver |
| Ambient light | Opt-in lux readings and above/below/range action rules | Requires a Windows light sensor; camera brightness is not used |
| Inclinometer | Windows API discovery and live pitch/roll/yaw | Requires driver support |
| Touchscreen / mouse | Dashboard zone buttons | No physical-desk touch sensing |
| HID / Bluetooth | Attachment/connection rules | No raw HID report or GATT decoding |
| Human presence / proximity | Windows sensor readings and action rules | Requires supported hardware, drivers and API |
| Serial | Device-interface discovery | No configured protocol |
| Depth / IR | No provider | Not an available input mode |
| Smart home | No configured integration | Pairing a Bluetooth device does not establish smart-home support |

## Detection versus capture

Hardware and Dashboard share one device inventory. Available means an API/device was found; it does not prove successful capture or useful classification. Microphone capture, camera preview and motion readings must be started separately. Reconnect a lost input device and explicitly restart capture.

Keep the microphone and PC position stable, then use Evaluation to measure accuracy with new taps. Do not treat advertised microphone-array channel counts or device detection as evidence of zone accuracy.

See [Hardware verification](HARDWARE_VERIFICATION_MATRIX.md) for evidence limits and [Troubleshooting](TROUBLESHOOTING.md) for common failures.
