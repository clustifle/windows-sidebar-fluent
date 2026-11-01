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
    private Windows.Foundation.Point dragGrabOffset;
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
            RegisterGadgetHeading(id, content);
            gadget.Child = null;
            var layout = new Grid();
            // Content scrolls inside a stable tile instead of resizing the rail.
            layout.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Auto });
            var insertion = new Border { Height = 2, Background = CpuBar.Foreground, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            insertionMarkers.Add(insertion);
            gadget.CanDrag = true;
            if (gadget.Background is null) gadget.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            AutomationProperties.SetHelpText(gadget, "Drag the card background or heading to reorder. Settings also offers Move up and Move down.");
            bool interactivePress = false;
            gadget.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
            {
                dragGrabOffset = args.GetCurrentPoint(gadget).Position;
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
            gadget.DropCompleted += (_, args) =>
            {
                string? source = draggingGadget;
                if (source is not null && !dropCommitted && args.DropResult == DataPackageOperation.None && GetCursorPos(out var point) && IsDesktopDropTarget(point))
                    DetachGadgetToDesktop(source, point, dragGrabOffset);
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
                    if (state.LonghornMode && source == "Clock") state.LonghornBottomClock = false;
                    ActiveGadgetOrder.Remove(source);
                    int index = ActiveGadgetOrder.IndexOf(id) + (after ? 1 : 0);
                    ActiveGadgetOrder.Insert(index, source);
                    ApplyState();
                }
                dropCommitted = true; Save();
            };
        }

        ConfigurePageDrop(PreviousPage, -1);
        ConfigurePageDrop(NextPage, 1);
        GadgetStack.AllowDrop = true;
        GadgetStack.DragOver += (_, args) =>
        {
            if (!IsGadgetDrag(args.DataView)) return;
            args.AcceptedOperation = DataPackageOperation.Move;
        };
        GadgetStack.Drop += (_, args) =>
        {
            if (args.Handled || !IsGadgetDrag(args.DataView) || draggingGadget is not string source) return;
            args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true;
            if (state.LonghornMode && source == "Clock") state.LonghornBottomClock = false;
            ActiveGadgetOrder.Remove(source); ActiveGadgetOrder.Add(source);
            dropCommitted = true;
            ApplyState();
            Save();
        };
        Root.AllowDrop = true;
        Root.DragOver += (_, args) => { if (IsGadgetDrag(args.DataView)) { args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true; } };
        Root.Drop += (_, args) =>
        {
            if (!IsGadgetDrag(args.DataView) || draggingGadget is not string source || args.Handled) return;
            args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true;
            ActiveGadgetOrder.Remove(source); ActiveGadgetOrder.Add(source);
            dropCommitted = true;
            ApplyState();
            Save();
        };
        LonghornClockDock.DragOver += (_, args) =>
        {
            if (!state.LonghornMode || draggingGadget != "Clock" || !IsGadgetDrag(args.DataView)) return;
            args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true;
            LonghornClockDock.Background = SurfaceBrush(Root.RequestedTheme, 90);
        };
        LonghornClockDock.DragLeave += (_, _) => LonghornClockDock.Background = null;
        LonghornClockDock.Drop += (_, args) =>
        {
            LonghornClockDock.Background = null;
            if (!state.LonghornMode || draggingGadget != "Clock" || !IsGadgetDrag(args.DataView)) return;
            args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true;
            state.LonghornBottomClock = true; dropCommitted = true;
            ApplyState();
            Save();
        };
        pageHoverTimer.Tick += (_, _) =>
        {
            pageHoverTimer.Stop();
            if (draggingGadget is null) return;
            if ((hoverDirection < 0 && PreviousPage.IsEnabled) || (hoverDirection > 0 && NextPage.IsEnabled))
            { state.PageIndex += hoverDirection; ApplyGadgetPage(); }
        };
        Closed += (_, _) => pageHoverTimer.Stop();
        ApplyGadgetSizes();
        ApplyCardCustomizations();
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
        button.Drop += (_, args) =>
        {
            if (!IsGadgetDrag(args.DataView) || draggingGadget is not string source) return;
            args.AcceptedOperation = DataPackageOperation.Move; args.Handled = true; dropCommitted = true;
            state.PageIndex = Math.Clamp(state.PageIndex + direction, 0, Math.Max(0, (ActiveGadgetOrder.Count - 1) / Math.Clamp(state.GadgetsPerPage, 1, 3))); ApplyGadgetPage();
            Save();
        };
        button.DragLeave += (_, _) => pageHoverTimer.Stop();
    }
}
