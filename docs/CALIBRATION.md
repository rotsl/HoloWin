# Calibration and evaluation

A profile is specific to the microphone, PC position, desk and room conditions. Begin with Passive sensing. Active and Hybrid strategies are experimental and require an explicitly enabled probe; do not change sensing strategy between calibration and normal use.

## Collect calibration samples

1. Select and enable the microphone in Hardware.
2. Create a profile in Profiles, or select one and press Use selected.
3. Open Calibration and press Begin new calibration.
4. Press Prepare for the intended zone. Wait for it to arm, then make ten spaced taps within that zone.
5. Repeat for all four zones. Check the accepted counts; weak, noisy or clipped samples may be rejected.
6. Optionally collect negative examples: choose Talking, Typing or Room noise, start collection, make the relevant sounds, then stop collection. The detector must accept an event before it can be saved as a negative example.
7. Press Validate calibration for leave-one-out agreement, then Save calibration.

The microphone indicators show incoming buffer count, current peak level and detected tap candidates. A rising buffer count with zero peak suggests silent input; candidates without accepted samples need a check of the reported rejection reasons. Calibration logs accepted samples separately from normal classifier decisions.

Disarm pauses positive sample collection. Undo removes the last accepted sample. Cancel discards the current session. Actions do not run automatically during calibration.

The four zones are left rear, left front, right rear and right front. Use a consistent physical layout, and spread samples within each zone. The app cannot verify where your hand actually tapped.

## Trigger actions

Save an action and assign it to a zone in Actions. Choose Audio only in Settings, then press Start listening on Dashboard. One accepted tap triggers the assigned action. The ten samples per zone are training data, not the normal trigger sequence.

Click a Dashboard zone to test the mapping without microphone classification. Recent events shows rejected taps and action failures.

## Held-out evaluation

Begin evaluation on a calibrated profile. Arm each zone and collect fifteen new trials there, for sixty trials total. Save the completed report to retain overall/per-zone accuracy, rejected trials, the confusion matrix and median classification-processing latency.

Use new taps instead of replaying calibration samples. Actions are suspended during evaluation. The displayed processing latency does not include the complete time from physical impact to completion of an external action.

## Storage and interpretation

Profiles store feature vectors, labels and settings; debug WAV recording is separate and off by default. Leave-one-out agreement measures consistency within the calibration set and does not establish real-world accuracy. Recalibrate after moving the PC or changing the input device. If zones remain hard to separate, review the surface and background noise rather than assuming a lower threshold will fix accuracy.

See [Troubleshooting](TROUBLESHOOTING.md) and [Hardware verification](HARDWARE_VERIFICATION_MATRIX.md).
