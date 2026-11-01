#if THEME_REGRESSION
using Microsoft.UI.Xaml;

namespace Sidebar;

public partial class MainWindow
{
    internal async void RunDesktopGadgetRegression()
    {
        string report = Path.Combine(AppContext.BaseDirectory, "desktop-gadget-test-result.txt");
        var savedBytes = File.Exists(statePath) ? File.ReadAllBytes(statePath) : null;
        var savedState = System.Text.Json.JsonSerializer.Deserialize<SidebarState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        string[] ids = ["Clock", "Calendar", "Notes", "Performance", "Timer", "Storage", "Quick launch", "Slide show", "Picture puzzle", "Feed headlines"];
        File.WriteAllText(report, "Starting independent desktop gadget checks…\n");
        try
        {
            state.LonghornMode = false; state.AppearancePreset = "Vista";
            foreach (var id in ids) SetGadgetEnabled(id, true);
            ApplyState();
            if (CalendarGadget.Height < 350) throw new Exception("Vista calendar card sizing did not expand.");
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            int columns = Math.Max(1, area.Width / 400);
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var placement = new DesktopGadgetPlacement { X = area.X + (i % columns) * 390, Y = area.Y + (i / columns) * 360, Width = 360, Height = Math.Min(340, area.Height) };
                state.DesktopGadgets[id] = placement; OpenDesktopGadget(id, placement);
                await Task.Delay(60);
            }
            if (desktopGadgets.Count != ids.Length) throw new Exception("Not all gadget windows were created.");
            foreach (var id in ids)
            {
                var detached = desktopGadgets[id];
                if (detached.Root.XamlRoot is null || ReferenceEquals(detached.Root.XamlRoot, Root.XamlRoot)) throw new Exception($"{id} did not receive an independent XamlRoot.");
                if (GadgetStack.Children.Contains(GadgetElement(id))) throw new Exception($"{id} remained in the sidebar after detaching.");
            }
            foreach (string material in new[] { "Acrylic", "Mica" })
            foreach (string theme in new[] { "Light", "Dark" })
            {
                state.Material = material; state.Theme = theme; SynchronizeAppearance(); await Task.Delay(100);
                foreach (var detached in desktopGadgets.Values)
                {
                    if (detached.Root.RequestedTheme != Root.RequestedTheme) throw new Exception($"{detached.Id} did not follow {theme} theme.");
                    if (detached.Window.SystemBackdrop is not SidebarBackdrop backdrop || backdrop.UsesMica != (material == "Mica")) throw new Exception($"{detached.Id} did not follow {material} material.");
                }
            }
            state.PageIndex = 1; ApplyGadgetPage();
            if (desktopGadgets.Count != ids.Length) throw new Exception("Page navigation closed a detached gadget.");
            foreach (var id in ids)
            {
                ReturnGadgetToSidebar(id); await Task.Delay(20);
                if (desktopGadgets.ContainsKey(id) || state.DesktopGadgets.ContainsKey(id) || !GadgetStack.Children.Contains(GadgetElement(id))) throw new Exception($"{id} did not return to the sidebar.");
            }
            var closePlacement = new DesktopGadgetPlacement { X = area.X + 30, Y = area.Y + 30, Width = 360, Height = 320 };
            state.DesktopGadgets["Clock"] = closePlacement; OpenDesktopGadget("Clock", closePlacement); await Task.Delay(100);
            if (!System.Text.Json.JsonSerializer.Serialize(state).Contains("\"DesktopGadgets\"")) throw new Exception("Detached placement was not serializable.");
            desktopGadgets["Clock"].Window.Close(); await Task.Delay(120);
            if (desktopGadgets.ContainsKey("Clock") || state.DesktopGadgets.ContainsKey("Clock") || !GadgetStack.Children.Contains(ClockGadget)) throw new Exception("Closing a detached window did not return its gadget.");
            File.AppendAllText(report, "PASS: all ten gadgets opened in independent windows, stayed separate through page and theme changes, and returned cleanly to the sidebar. Vista sizing also passed.\n");
        }
        catch (Exception ex) { File.AppendAllText(report, "FAIL: " + ex + "\n"); }
        finally
        {
            foreach (var id in state.DesktopGadgets.Keys.ToArray()) ReturnGadgetToSidebar(id);
            state = savedState; ApplyState();
            Closed += (_, _) =>
            {
                if (savedBytes is null) { if (File.Exists(statePath)) File.Delete(statePath); if (File.Exists(statePath + ".tmp")) File.Delete(statePath + ".tmp"); }
                else File.WriteAllBytes(statePath, savedBytes);
            };
            Close();
        }
    }
}
#endif
