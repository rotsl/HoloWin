using HoloWin.Core;

namespace HoloWin.Actions;

public static class ActionCatalog
{
    public static readonly (ActionType Type, string Label)[] Items =
    [
        (ActionType.VisualOnly, "Feedback · Visual only"), (ActionType.PlaySound, "Feedback · Play sound"),
        (ActionType.FunctionKey, "Keyboard · Function key (F1–F10)"), (ActionType.TriggerKeyboardShortcut, "Keyboard · Custom shortcut"),
        (ActionType.SwitchTab, "Navigation · Switch tab"), (ActionType.SwitchVirtualDesktop, "Navigation · Switch virtual desktop"),
        (ActionType.FocusApplication, "Navigation · Focus application"),
        (ActionType.IncreaseVolume, "Audio · Increase volume"), (ActionType.DecreaseVolume, "Audio · Decrease volume"),
        (ActionType.MuteAudio, "Audio · Mute/unmute"), (ActionType.SendMediaKey, "Audio · Media key"),
        (ActionType.IncreaseBrightness, "Display · Increase brightness"), (ActionType.DecreaseBrightness, "Display · Decrease brightness"),
        (ActionType.TakeScreenshot, "Display · Screenshot"),
        (ActionType.OpenWebsite, "Open · Website"), (ActionType.OpenApplication, "Open · Application"), (ActionType.OpenFileOrFolder, "Open · File or folder"),
        (ActionType.CopyText, "Text · Copy"), (ActionType.TypeText, "Text · Type"), (ActionType.SpeakText, "Text · Speak"),
        (ActionType.RunMacro, "Automation · Macro"), (ActionType.RunPowerShell, "Automation · PowerShell"),
        (ActionType.RunExecutable, "Automation · Executable"), (ActionType.InvokeHttpEndpoint, "Automation · HTTP request"),
        (ActionType.SetVolume, "Legacy · Set absolute volume")
    ];
}
