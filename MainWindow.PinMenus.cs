using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Sidebar;

public partial class MainWindow
{
    private MenuFlyout BuildPinMenu(LaunchShortcut app, Action? changed = null)
    {
        var menu = new MenuFlyout();
        int index = state.Shortcuts.FindIndex(s => s.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase));
        void Persist() { RefreshQuickLaunch(); Save(); changed?.Invoke(); }
        void Item(string text, Action action, bool enabled = true)
        { var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        if (index < 0)
            Item("Pin to Quick launch", () => { if (!state.Shortcuts.Any(s => s.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase))) state.Shortcuts.Add(app); Persist(); });
        else
        {
            Item("Open", async () => await OpenShortcut(state.Shortcuts.FirstOrDefault(s => s.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase)) ?? app));
            void Move(int step)
            {
                int position = state.Shortcuts.FindIndex(s => s.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase));
                if (position >= 0 && position + step >= 0 && position + step < state.Shortcuts.Count) (state.Shortcuts[position], state.Shortcuts[position + step]) = (state.Shortcuts[position + step], state.Shortcuts[position]); Persist();
            }
            Item("Move earlier", () => Move(-1), index > 0);
            Item("Move later", () => Move(1), index < state.Shortcuts.Count - 1);
            Item("Unpin", () => { state.Shortcuts.RemoveAll(s => s.Target.Equals(app.Target, StringComparison.OrdinalIgnoreCase)); Persist(); });
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Customize Quick launch…", () => { launcherFlyout?.Hide(); CustomizeLaunch_Click(this, new RoutedEventArgs()); });
        return menu;
    }
}
