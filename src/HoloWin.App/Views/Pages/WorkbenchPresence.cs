using HoloWin.Core;
using Microsoft.UI.Xaml.Controls;

namespace HoloWin.App.Views.Pages;

public partial class WorkbenchPage
{
    private Action AddPresenceHardware()
    {
        var panel = Section("Sensor collection v2 · Presence and connections");
        panel.Children.Add(Label("Bluetooth shows connection state for paired or connected devices. HID shows attachment of a device interface; no keystrokes or raw reports are collected. Windows human-presence sensors report present/absent; proximity sensors report near/far. Availability depends on Windows and your hardware. These signals do not identify a person."));
        var buttons = Row(panel);
        Button(buttons, "Scan sources", W.ScanPresenceAsync);
        Button(buttons, "Start monitoring", W.StartPresenceAsync);
        Button(buttons, "Stop monitoring", W.StopPresenceAsync);
        var armed = Check(panel, "Enable presence actions for this session", W.PresenceActionsEnabled);
        armed.Checked += (_, _) =>
        {
            if (!W.PresenceRunning || W.Profile is null) { armed.IsChecked = false; _message.Text = "Select a profile and start monitoring first."; return; }
            W.PresenceActionsEnabled = true;
        };
        armed.Unchecked += (_, _) => W.PresenceActionsEnabled = false;
        var status = Label(""); panel.Children.Add(status);
        return () =>
        {
            armed.IsChecked = W.PresenceActionsEnabled;
            status.Text = $"{W.PresenceStatus}\nMonitoring: {(W.PresenceRunning ? "on" : "off")} · Actions: {(W.PresenceActionsEnabled ? "armed" : "disarmed")}\n"
                + (W.PresenceReadings.Count == 0 ? "No sources found yet. Scan sources to check availability." : string.Join("\n", W.PresenceReadings.Select(r => r.DisplayName)));
        };
    }
    private void AddPresenceRuleEditor(Func<HoloAction?> selectedAction)
    {
        var panel = Section("Presence and device connection rules");
        panel.Children.Add(Label("Select a saved action in the editor, then a source below. Rules fire on a stable change, not when monitoring first starts. A Bluetooth connection is not proof that a person is nearby. Configure hold and cooldown, then start monitoring and enable presence actions in Hardware."));
        var source = new ComboBox { Header = "Source", DisplayMemberPath = "DisplayName", MinWidth = 300, MaxWidth = 650 };
        panel.Children.Add(source);
        void Sources() { source.ItemsSource = W.PresenceReadings; }
        Sources();
        Button(panel, "Scan / refresh sources", async () => { await W.ScanPresenceAsync(); Sources(); });
        var list = new ComboBox { Header = "Saved presence rules", DisplayMemberPath = "Name", MinWidth = 300 }; panel.Children.Add(list);
        void Reload() { list.ItemsSource = W.Profile?.PresenceRules; }
        Reload();
        var name = Text(panel, "Rule name");
        var condition = Choice(panel, "Trigger", new[] { "Connected / present / near", "Disconnected / absent / far" });
        var hold = Number(panel, "Stable condition before triggering (ms)", 2000, 0, 60000);
        var cooldown = Number(panel, "Cooldown (ms)", 5000, 0, 600000);
        var enabled = Check(panel, "Enable this rule when presence actions are armed", true);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not PresenceRule rule) return;
            name.Text = rule.Name; condition.SelectedIndex = rule.WhenPresent ? 0 : 1;
            hold.Value = rule.HoldMilliseconds; cooldown.Value = rule.CooldownMilliseconds; enabled.IsChecked = rule.Enabled;
            var choices = W.PresenceReadings.ToList();
            if (!choices.Any(r => r.Id == rule.SourceId)) choices.Add(new(rule.SourceId, rule.SourceName, rule.Kind, null));
            source.ItemsSource = choices; source.SelectedItem = choices.First(r => r.Id == rule.SourceId);
        };
        Button(panel, "New presence rule", () => { list.SelectedIndex = -1; name.Text = ""; enabled.IsChecked = true; return Task.CompletedTask; });
        Button(panel, "Save rule for selected action", async () =>
        {
            var action = selectedAction();
            if (W.Profile is not { } profile || action is null || !W.Library.Actions.Any(a => a.Id == action.Id) || source.SelectedItem is not PresenceReading reading)
                throw new InvalidOperationException("Select a profile, a saved action and a presence source.");
            if (!double.IsFinite(hold.Value) || !double.IsFinite(cooldown.Value)) throw new InvalidOperationException("Enter numeric timing values.");
            var rule = new PresenceRule { Id = (list.SelectedItem as PresenceRule)?.Id ?? Guid.NewGuid().ToString("N"), Name = name.Text.Trim(),
                SourceId = reading.Id, SourceName = reading.Name, Kind = reading.Kind, WhenPresent = condition.SelectedIndex == 0,
                ActionId = action.Id, Enabled = enabled.IsChecked == true, HoldMilliseconds = (int)hold.Value, CooldownMilliseconds = (int)cooldown.Value };
            rule.Validate(); W.PresenceActionsEnabled = false;
            profile.PresenceRules = profile.PresenceRules.Where(r => r.Id != rule.Id).Append(rule).ToArray();
            await W.SaveProfileAsync(); Reload();
        });
        Button(panel, "Remove selected presence rule", async () =>
        {
            if (W.Profile is not { } profile || list.SelectedItem is not PresenceRule rule) return;
            W.PresenceActionsEnabled = false; profile.PresenceRules = profile.PresenceRules.Where(r => r.Id != rule.Id).ToArray();
            await W.SaveProfileAsync(); Reload();
        });
    }
}
