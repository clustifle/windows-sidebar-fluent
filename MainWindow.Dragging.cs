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
            var grip = new Border
            {
                Width = 28, Height = 28, Padding = new Thickness(7), CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, CanDrag = true,
                Child = new FontIcon { Glyph = "\uE784", FontSize = 12, Opacity = 0.45, OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(100) } }
            };
            AutomationProperties.SetName(grip, $"Drag {id} gadget");
            ToolTipService.SetToolTip(grip, $"Drag {id} to reorder. Hold over a page arrow to change pages.");
            grip.PointerEntered += (_, _) =>
            {
                grip.Background = new SolidColorBrush(Root.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(24, 255, 255, 255) : Windows.UI.Color.FromArgb(18, 0, 0, 0));
                ((FontIcon)grip.Child).Opacity = 1;
            };
            grip.PointerExited += (_, _) => { grip.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); ((FontIcon)grip.Child).Opacity = 0.45; };
            grip.DragStarting += (_, args) =>
            {
                draggingGadget = id; dragStartPage = state.PageIndex; dropCommitted = false;
                hideTimer.Stop(); RevealSidebar();
                args.Data.Properties[GadgetDragKey] = id;
                args.Data.RequestedOperation = DataPackageOperation.Move;
                gadget.Opacity = 0.55;
            };
            grip.DropCompleted += (_, _) =>
            {
                gadget.Opacity = 1; draggingGadget = null; pageHoverTimer.Stop();
                foreach (var marker in insertionMarkers) marker.Visibility = Visibility.Collapsed;
                if (!dropCommitted) { state.PageIndex = dragStartPage; ApplyGadgetPage(); }
                QueueAutoHide();
            };
            layout.Children.Add(grip); gadget.Child = layout;
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
        MonthText.Margin = new Thickness(0, 0, 24, 0);
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
