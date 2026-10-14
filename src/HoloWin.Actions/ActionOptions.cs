using System.Globalization;
using HoloWin.Core;

namespace HoloWin.Actions;

public static class ActionOptions
{
    public static int Step(IReadOnlyDictionary<string, string> parameters)
    {
        var text = parameters.GetValueOrDefault("step_percent", "5");
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var step) || step < 1 || step > 100)
            throw new ArgumentException("Step must be a whole number from 1 to 100 percentage points.");
        return step;
    }
    public static int Adjust(int current, int step, bool increase) => Math.Clamp(current + (increase ? step : -step), 0, 100);
    public static string Shortcut(ActionType type, IReadOnlyDictionary<string, string> parameters)
    {
        if (type == ActionType.FunctionKey)
        {
            var key = parameters.GetValueOrDefault("key", "F1").Trim().ToUpperInvariant();
            if (key.Length < 2 || key[0] != 'F' || !int.TryParse(key[1..], out int number) || number < 1 || number > 10)
                throw new ArgumentException("Choose a function key from F1 through F10.");
            var modifiers = parameters.GetValueOrDefault("modifiers", "").Trim();
            if (modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(m => !new[] { "Ctrl", "Alt", "Shift", "Win" }.Contains(m, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Modifiers may contain Ctrl, Alt, Shift or Win, separated by +.");
            return string.IsNullOrWhiteSpace(modifiers) ? key : modifiers.TrimEnd('+') + "+" + key;
        }
        var custom = parameters.GetValueOrDefault("shortcut", "").Trim();
        if (custom.Length > 0) return custom;
        return parameters.GetValueOrDefault("direction", "next").Trim().ToLowerInvariant() switch
        {
            "next" => "Ctrl+Tab", "previous" => "Ctrl+Shift+Tab",
            _ => throw new ArgumentException("Tab direction must be next or previous.")
        };
    }
}
