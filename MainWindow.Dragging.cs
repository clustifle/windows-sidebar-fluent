using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Sidebar;

public partial class MainWindow
{
    private const string GadgetDragKey = "WindowsSidebarFluent.Gadget";
    private string? draggingGadget;
    private int dragStartPage, hoverDirection;
    private bool dropCommitted;
    private readonly List<Border> insertionMarkers = [];
    private readonly DispatcherTimer pageHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };

    private void InitializeGadgetDragging()
    {
        foreach (string id in state.GadgetOrder)
        {
            var gadget = (Border)GadgetElement(id);
            var content = gadget.Child;
            gadget.Child = null;
            var layout = new Grid();
            layout.Children.Add(content);
            var insertion = new Border { Height = 2, Background = CpuBar.Foreground, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            insertionMarkers.Add(insertion);
            gadget.CanDrag = true;
            if (gadget.Background is null) gadget.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            AutomationProperties.SetHelpText(gadget, "Drag the card background or heading to reorder. Settings also offers Move up and Move down.");
            bool interactivePress = false;
            gadget.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
            {
                interactivePress = false;
                for (DependencyObject? node = args.OriginalSource as DependencyObject; node is not null && node != gadget; node = VisualTreeHelper.GetParent(node))
                    if (node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or TextBox or NumberBox or ComboBox or Slider or ToggleSwitch or Microsoft.UI.Xaml.Controls.Primitives.ScrollBar)
                    { interactivePress = true; break; }
            }), true);
            gadget.DragStarting += (_, args) =>
            {
                if (interactivePress) { args.Cancel = true; return; }
                draggingGadget = id; dragStartPage = state.PageIndex; dropCommitted = false;
                hideTimer.Stop(); RevealSidebar();
                args.Data.Properties[GadgetDragKey] = id;
                args.Data.RequestedOperation = DataPackageOperation.Move;
                gadget.Opacity = 0.55;
            };
            gadget.DropCompleted += (_, _) =>
            {
                gadget.Opacity = 1; draggingGadget = null; pageHoverTimer.Stop();
                foreach (var marker in insertionMarkers) marker.Visibility = Visibility.Collapsed;
                if (!dropCommitted) { state.PageIndex = dragStartPage; ApplyGadgetPage(); }
                QueueAutoHide();
            };
            gadget.Child = layout;
            layout.Children.Add(insertion);
            gadget.AllowDrop = true;
            gadget.DragOver += (_, args) =>
            {
                if (!IsGadgetDrag(args.DataView)) return;
                args.AcceptedOperation = DataPackageOperation.Move;
                args.Handled = true;
                insertion.VerticalAlignment = args.GetPosition(gadget).Y > gadget.ActualHeight / 2 ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                insertion.Visibility = draggingGadget == id ? Visibility.Collapsed : Visibility.Visible;
            };
            gadget.DragLeave += (_, _) => insertion.Visibility = Visibility.Collapsed;
            gadget.Drop += (_, args) =>
            {
                if (!IsGadgetDrag(args.DataView) || draggingGadget is not string source) return;
                args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true;
                insertion.Visibility = Visibility.Collapsed;
                if (source != id)
                {
                    bool after = args.GetPosition(gadget).Y > gadget.ActualHeight / 2;
                    state.GadgetOrder.Remove(source);
                    int index = state.GadgetOrder.IndexOf(id) + (after ? 1 : 0);
                    state.GadgetOrder.Insert(index, source);
                    ApplyState();
                }
                dropCommitted = true; Save();
            };
        }

        ConfigurePageDrop(PreviousPage, -1);
        ConfigurePageDrop(NextPage, 1);
        pageHoverTimer.Tick += (_, _) =>
        {
            pageHoverTimer.Stop();
            if (draggingGadget is null) return;
            if ((hoverDirection < 0 && PreviousPage.IsEnabled) || (hoverDirection > 0 && NextPage.IsEnabled))
            { state.PageIndex += hoverDirection; ApplyGadgetPage(); }
        };
        Closed += (_, _) => pageHoverTimer.Stop();
    }

    private bool IsGadgetDrag(DataPackageView data) => draggingGadget is not null && data.Properties.ContainsKey(GadgetDragKey);

    private void ConfigurePageDrop(Button button, int direction)
    {
        button.AllowDrop = true;
        button.DragEnter += (_, args) =>
        {
            if (!IsGadgetDrag(args.DataView)) return;
            hoverDirection = direction; pageHoverTimer.Stop(); pageHoverTimer.Start();
        };
        button.DragOver += (_, args) =>
        {
            if (IsGadgetDrag(args.DataView)) { args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true; }
        };
        button.DragLeave += (_, _) => pageHoverTimer.Stop();
    }
}
