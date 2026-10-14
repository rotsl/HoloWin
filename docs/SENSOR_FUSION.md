# Sensor fusion

`FusionEngine` accepts timestamped observations and emits `FusedDecision` records. It uses monotonic timestamps, a bounded queue and a default 300 ms stale cutoff at enqueue time. Its observation selection is experimental and has not been validated as a multisensor estimator.

## Application modes

| Setting | Current behavior |
|---------|------------------|
| Audio only | Accepted acoustic classifications can route to zone actions |
| Manual / hotkeys only | Acoustic observations do not trigger actions; Dashboard buttons and configured hotkeys remain available |
| Adaptive (available sensors) | Uses the engine's simple observation selection; only a decision with a zone can route to a zone action |

Camera gestures route directly through profile gesture assignments and the action dispatcher when explicitly armed. They do not use the acoustic fusion mode. Motion readings can produce observations but do not identify a desk zone by themselves. Manual zone buttons call the controller directly rather than generating touch observations.

## Source-level modes and limits

The enum also contains VisionOnly, AudioVision and DepthAssisted. Their presence does not mean those inputs are integrated. AudioVision has a basic confidence boost for accompanying gestures; adaptive/depth modes choose an available observation rather than performing calibrated sensor weighting.

Registered reliability data is not used in decision weighting. Contradiction rejection and synchronized contact estimation are not implemented. Staleness is checked when pushing an observation; this is not a guarantee on queue-to-dispatch latency. Dashboard queue-drop statistics currently describe the acoustic tap queue, not every fusion queue.

Calibration and evaluation consume features/decisions while the controller suspends automatic action routing. See [Architecture](../ARCHITECTURE.md) and [Implementation status](IMPLEMENTATION_STATUS.md).

Presence and connection rules evaluate separately from zone fusion. Bluetooth/HID connections and Windows human-presence/proximity readings do not classify desk zones. Their actions share the dispatcher with zone, camera and light actions. See [Presence](PRESENCE.md).
