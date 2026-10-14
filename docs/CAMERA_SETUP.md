# Camera controls

Camera Studio has two start buttons. Start preview shows the image without inference. Start hand tracking detects palms and estimates 21 landmarks per hand on the CPU. Both require Windows camera permission. Startup succeeds only after a frame arrives, with an eight-second limit. Capture opens, reads and releases the camera on one background thread, trying DirectShow first and Media Foundation if it cannot open. The received-frame counter helps distinguish camera delivery from preview rendering problems.

## Use a gesture action

1. Select a profile and save an action in Actions.
2. Under Camera gesture assignment, choose a gesture and assign that action.
3. Open Camera Studio, choose the camera index and press Start hand tracking.
4. Check the landmark dots and status before enabling assigned gesture actions for the session.
5. Hold the gesture steadily. Release it or move the hand out of view before repeating the same gesture.

Built-in poses are open palm, closed fist, pinch, pointing and two fingers. A pose must stay steady for the configured hold time (450 ms by default). Keeping it held does not repeat the action. Show one hand: multiple detected hands suspend recognition.

## Swipes and custom symbols

Assign Swipe left/right/up/down under Camera gesture assignment. Move your hand in the direction shown in the preview, covering at least the configured fraction of the image within 700 ms. The default travel is 0.18 (18% of the image width or height). After a swipe, wait at least 800 ms before another. Sudden position jumps, stale frames and hand switches reset the movement history.

For browser tabs, create keyboard shortcut actions with `Ctrl+Tab` and `Ctrl+Shift+Tab`. For windows, use `Alt+Tab` and `Alt+Shift+Tab`. Assign them to your preferred swipes. Shortcuts go to the foreground application, so enable camera actions and then focus the application you want to control.

Under Custom finger symbols, name a symbol and set each finger to Extended, Folded or Any. Select a saved action above, then save the symbol. Custom patterns take priority over built-in poses. Overlapping custom patterns are rejected in the editor. Select a saved symbol to change it, or use New symbol to start another.

Camera sensitivity controls the accepted hand confidence, swipe travel and pose hold time for the current profile. Lower confidence admits more uncertain detections; it does not change the model's own detection limits. Saving sensitivity or a custom symbol stops tracking. Restart it and arm actions in Camera Studio.

Tracking stays on when you leave the page or hide the window. Stop it in Camera Studio, Dashboard, or the tray's Pause sensors command. Preview-only capture stops when its page is left. Starting tracking again disarms gesture actions. Changing profile stops the camera.

## Models

The build uses the OpenCV Zoo palm detector and hand-pose model from revision `47534e27c9851bb1128ccc0102f1145e27f23f98`. The download script verifies both SHA-256 hashes before publishing. Models run locally; the app makes no runtime model download.

For source builds, run:

```powershell
.\scripts\download-hand-model.ps1
dotnet build HoloWin.sln -c Debug
```

The models use the Apache-2.0 license. Their license and notice are included in the package. The hand-pose model estimates 21 landmarks for each detected palm. See the [OpenCV model documentation](https://github.com/opencv/opencv_zoo/tree/47534e27c9851bb1128ccc0102f1145e27f23f98/models/handpose_estimation_mediapipe).

## Desk mapping

Select a profile, open Desk Map and start preview. Click top-left, top-right, bottom-right and bottom-left corners in that order, then save. Clear corners before replacing a saved map. This geometry does not detect contact with the desk; gesture actions work independently of it.

## Limits

Keep the whole hand in view and use even lighting. Camera indices may differ from inventory order. A shutter, another app using the camera or a driver issue can prevent capture. Gestures are experimental and need a physical check before you assign actions with significant effects. Blank-frame and synthetic tests do not establish real-camera accuracy.

## Tab swipe preset

In Actions, press Create tab actions and bind left/right swipes. Left selects the next tab and right selects the previous tab using Ctrl+Tab and Ctrl+Shift+Tab. The preset requires both assignments to be empty. Edit the saved Navigation · Switch tab actions to override shortcuts or reassign gestures manually. Start tracking and arm gesture actions in Camera Studio, then focus the target application.
