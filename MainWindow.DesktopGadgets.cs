using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage.Streams;

namespace Sidebar;

public partial class MainWindow
{
    private readonly Dictionary<string, DesktopGadgetWindow> desktopGadgets = new(StringComparer.OrdinalIgnoreCase);

    private void RestoreDesktopGadgets()
    {
        foreach (var id in state.DesktopGadgets.Keys.ToArray())
        {
            if (GadgetEnabled(id)) OpenDesktopGadget(id, state.DesktopGadgets[id]);
            else state.DesktopGadgets.Remove(id);
        }
        Save();
    }

    private void UpdateDesktopGadgets()
    {
        foreach (var id in state.DesktopGadgets.Keys.ToArray())
            if (!GadgetEnabled(id)) ReturnGadgetToSidebar(id);
    }

    private void DetachGadgetToDesktop(string id, PointerPoint cursor, Windows.Foundation.Point grabOffset)
    {
        int dpi = Math.Max(96, (int)GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)));
        double scale = dpi / 96d;
        var area = WorkAreaFromPoint(cursor);
        int width = (int)Math.Round((state.AppearancePreset == "Vista" ? 380 : 352) * scale);
        int height = (int)Math.Round(GetDesktopGadgetHeight(id) * scale);
        var placement = new DesktopGadgetPlacement
        {
            X = cursor.X - (int)Math.Round(grabOffset.X * scale),
            Y = cursor.Y - (int)Math.Round(grabOffset.Y * scale),
            Width = width,
            Height = height
        };
        ClampDesktopGadget(placement, area);
        state.DesktopGadgets[id] = placement;
        OpenDesktopGadget(id, placement);
        dropCommitted = true;
        Save();
    }

    private int GetDesktopGadgetHeight(string id) => id switch
    {
        "Clock" => 300, "Calendar" => 420, "Notes" => 340, "Performance" => 300,
        "Timer" => 320, "Storage" => 260, "Quick launch" => 460,
        "Slide show" => 420, "Picture puzzle" => 480, "Feed headlines" => 500, _ => 360
    };

    private void OpenDesktopGadget(string id, DesktopGadgetPlacement placement)
    {
        if (closing || desktopGadgets.ContainsKey(id) || !GadgetEnabled(id)) return;
        var area = WorkAreaFromPoint(new PointerPoint { X = placement.X + Math.Max(1, placement.Width) / 2, Y = placement.Y + Math.Max(1, placement.Height) / 2 });
        ClampDesktopGadget(placement, area);
        var detached = new DesktopGadgetWindow(this, id, placement);
        desktopGadgets[id] = detached;
        LonghornClockDock.Children.Remove(GadgetElement(id));
        GadgetStack.Children.Remove(GadgetElement(id));
        GadgetElement(id).Visibility = Visibility.Collapsed;
        detached.Activate();
        ApplyState();
        Save();
    }

    private void ReturnGadgetToSidebar(string id)
    {
        state.DesktopGadgets.Remove(id);
        if (desktopGadgets.Remove(id, out var detached)) detached.CloseFromOwner();
        if (!closing)
        {
            GadgetElement(id).Margin = new Thickness(0);
            GadgetElement(id).Width = double.NaN;
            ApplyState();
            Save();
        }
    }

    private void DesktopGadgetClosed(string id, DesktopGadgetWindow window)
    {
        if (desktopGadgets.TryGetValue(id, out var current) && ReferenceEquals(current, window)) desktopGadgets.Remove(id);
        if (closing) return;
        state.DesktopGadgets.Remove(id);
        GadgetElement(id).Margin = new Thickness(0);
        GadgetElement(id).Width = double.NaN;
        ApplyState();
        Save();
    }

    private void ClampDesktopGadget(DesktopGadgetPlacement placement, RectInt32 workArea)
    {
        placement.Width = Math.Clamp(placement.Width <= 0 ? 360 : placement.Width, 300, Math.Min(600, workArea.Width));
        placement.Height = Math.Clamp(placement.Height <= 0 ? 360 : placement.Height, 220, Math.Min(900, workArea.Height));
        placement.X = Math.Clamp(placement.X, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - placement.Width));
        placement.Y = Math.Clamp(placement.Y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - placement.Height));
    }

    private RectInt32 WorkAreaFromPoint(PointerPoint point)
    {
        IntPtr monitor = MonitorFromPoint(point, 2);
        var info = new GadgetMonitorInfo { Size = (uint)Marshal.SizeOf<GadgetMonitorInfo>() };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? info.Work :
            DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
    }

    private void UpdateDetachedAppearance(ElementTheme theme)
    {
        foreach (var detached in desktopGadgets.Values) detached.UpdateTheme(theme, state.Material == "Mica");
    }

    private bool IsDesktopDropTarget(PointerPoint point)
    {
        IntPtr target = WindowFromPoint(point);
        if (target == IntPtr.Zero) return false;
        var name = new System.Text.StringBuilder(128); GetClassName(target, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32") return true;
        IntPtr parent = GetAncestor(target, 2);
        if (parent != IntPtr.Zero) { GetClassName(parent, name, name.Capacity); return name.ToString() is "Progman" or "WorkerW"; }
        return false;
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PointerPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(PointerPoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref GadgetMonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct GadgetMonitorInfo { public uint Size; public RectInt32 Monitor, Work; public uint Flags; }

    private sealed class DesktopGadgetWindow
    {
        private readonly MainWindow owner;
        private readonly string id;
        private readonly DesktopGadgetPlacement placement;
        private readonly Window window;
        private readonly Grid root;
        private readonly Border card;
        private readonly TitleBar titleBar;
        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly TextBlock primary = new();
        private readonly TextBlock secondary = new();
        private readonly Grid contentGrid = new();
        private readonly StackPanel content = new() { Spacing = 10 };
        private readonly List<Button> puzzleTiles = [];
        private readonly SlidingPuzzle? puzzle;
        private DateTime calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime selectedDate = DateTime.Today;
        private int slideIndex;
        private Image? slideImage;
        private Canvas? clockCanvas;
        private Line? hourHand, minuteHand, secondHand;
        private bool clockFaceBuilt;
        private TextBox? notes;
        private NumberBox? timerMinutes;
        private ProgressBar? storageBar;
        private Image?[]? launcherIcons;
        private StackPanel? feedList;
        private TextBlock? feedStatus;
        private TextBlock? calendarTitle;
        private string loadedSlidePath = "";
        private DateTime nextFeedRefresh;
        private DateTime nextSlideAt;
        private bool closingFromOwner, renderingCalendar, syncingNotes;

        internal DesktopGadgetWindow(MainWindow owner, string id, DesktopGadgetPlacement placement)
        {
            this.owner = owner; this.id = id; this.placement = placement;
            if (id == "Picture puzzle") { owner.picturePuzzle.Restore(owner.state.PuzzleTiles, owner.state.PuzzleMoves); puzzle = owner.picturePuzzle; }
            root = new Grid { RequestedTheme = owner.Root.RequestedTheme, RowSpacing = 12, Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
            titleBar = new TitleBar { Title = $"FluentSidebar — {id}" };
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);
            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock { Text = id, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var returnButton = new Button { Content = "Return to sidebar", MinHeight = 32, Padding = new Thickness(10, 4, 10, 4) };
            returnButton.Click += (_, _) => owner.ReturnGadgetToSidebar(id);
            Grid.SetColumn(returnButton, 1); header.Children.Add(returnButton); Grid.SetRow(header, 1); root.Children.Add(header);
            card = new Border
            {
                Background = owner.SurfaceBrush(owner.Root.RequestedTheme, owner.Root.RequestedTheme == ElementTheme.Dark ? (byte)236 : (byte)246),
                BorderBrush = new SolidColorBrush(owner.windowsAppearance.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(18)
            };
            card.Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(card, 2); root.Children.Add(card);
            window = new Window { Title = $"FluentSidebar — {id}", Content = root, SystemBackdrop = new SidebarBackdrop(owner.state.Material == "Mica") };
            window.ExtendsContentIntoTitleBar = true;
            window.SetTitleBar(titleBar);
            window.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            window.Closed += (_, _) => { refresh.Stop(); if (!closingFromOwner) owner.DesktopGadgetClosed(id, this); };
            window.AppWindow.Changed += (_, args) =>
            {
                if (args.DidPositionChange || args.DidSizeChange)
                {
                    var position = window.AppWindow.Position; var size = window.AppWindow.Size;
                    placement.X = position.X; placement.Y = position.Y; placement.Width = size.Width; placement.Height = size.Height;
                    owner.Save();
                }
            };
            window.AppWindow.Resize(new SizeInt32(placement.Width, placement.Height));
            window.AppWindow.Move(new PointInt32(placement.X, placement.Y));
            window.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Sidebar.ico"));
            try { window.AppWindow.IsShownInSwitchers = false; } catch (Exception ex) when (ex is COMException or NotImplementedException) { }
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            long exStyle = GetWindowLongPtr(handle, -20).ToInt64();
            SetWindowLongPtr(handle, -20, new IntPtr((exStyle | 0x80) & ~0x40000));
            BuildContent();
            refresh.Tick += (_, _) => UpdateContent();
            refresh.Interval = RefreshInterval();
            refresh.Start();
            UpdateContent();
        }

        internal void Activate() => window.Activate();
        internal string Id => id;
        internal Window Window => window;
        internal Grid Root => root;
        internal void CloseFromOwner() { closingFromOwner = true; refresh.Stop(); window.Close(); }
        internal void UpdateTheme(ElementTheme theme, bool mica)
        {
            root.RequestedTheme = theme;
            titleBar.RequestedTheme = theme;
            clockFaceBuilt = false;
            if (window.SystemBackdrop is not SidebarBackdrop backdrop || backdrop.UsesMica != mica)
                window.SystemBackdrop = new SidebarBackdrop(mica);
            if (window.SystemBackdrop is SidebarBackdrop current) current.SetTheme(theme);
            card.Background = owner.SurfaceBrush(theme, theme == ElementTheme.Dark ? (byte)236 : (byte)246);
        }

        private void BuildContent()
        {
            switch (id)
            {
                case "Clock":
                    clockCanvas = new Canvas { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center };
                    content.Children.Add(clockCanvas); primary.FontSize = 34; primary.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(primary);
                    secondary.FontSize = 15; secondary.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(secondary);
                    content.Children.Add(new TextBlock { Text = owner.state.ClockLabel, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
                    var customizeClock = new Button { Content = "Customize clock…", HorizontalAlignment = HorizontalAlignment.Center };
                    customizeClock.Click += (_, _) => owner.CustomizeClock_Click(customizeClock, new RoutedEventArgs()); content.Children.Add(customizeClock);
                    break;
                case "Calendar":
                    var calendarHeader = new Grid { ColumnSpacing = 8 };
                    calendarHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); calendarHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); calendarHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var previous = new Button { Content = "‹", Width = 36, Height = 36, Padding = new Thickness(0) }; previous.Click += (_, _) => { calendarMonth = calendarMonth.AddMonths(-1); RenderCalendar(); };
                    calendarTitle = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    var next = new Button { Content = "›", Width = 36, Height = 36, Padding = new Thickness(0) }; next.Click += (_, _) => { calendarMonth = calendarMonth.AddMonths(1); RenderCalendar(); };
                    calendarHeader.Children.Add(previous); Grid.SetColumn(calendarTitle, 1); calendarHeader.Children.Add(calendarTitle); Grid.SetColumn(next, 2); calendarHeader.Children.Add(next); content.Children.Add(calendarHeader);
                    contentGrid.RowSpacing = 4; contentGrid.ColumnSpacing = 4; content.Children.Add(contentGrid);
                    secondary.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(secondary);
                    RenderCalendar(); break;
                case "Notes":
                    notes = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 420, Text = owner.state.Notes, PlaceholderText = "Write a note…" };
                    notes.TextChanged += (_, _) => { if (syncingNotes) return; owner.state.Notes = notes.Text; if (owner.NotesBox.Text != notes.Text) owner.NotesBox.Text = notes.Text; owner.Save(); };
                    content.Children.Add(notes); content.Children.Add(new TextBlock { Text = "Saved on this device", FontSize = 12, Opacity = 0.7 }); break;
                case "Performance":
                    primary.FontSize = 22; primary.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; content.Children.Add(primary); content.Children.Add(new ProgressBar { IsIndeterminate = false, Maximum = 100, Tag = "cpu" });
                    secondary.FontSize = 14; content.Children.Add(secondary); content.Children.Add(new ProgressBar { IsIndeterminate = false, Maximum = 100, Tag = "memory" }); break;
                case "Timer":
                    primary.FontSize = 42; primary.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(primary);
                    timerMinutes = new NumberBox { Header = "Minutes", Minimum = 1, Maximum = 180, Value = double.IsNaN(owner.TimerMinutes.Value) ? 5 : owner.TimerMinutes.Value, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
                    timerMinutes.ValueChanged += (_, args) => { if (double.IsFinite(args.NewValue)) owner.TimerMinutes.Value = args.NewValue; };
                    content.Children.Add(timerMinutes);
                    var timerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                    var start = new Button { Content = owner.TimerStart.Content }; start.Click += (_, _) => { owner.TimerMinutes.Value = timerMinutes.Value; owner.Timer_Click(start, new RoutedEventArgs()); UpdateContent(); };
                    var reset = new Button { Content = "Reset" }; reset.Click += (_, _) => { owner.TimerMinutes.Value = timerMinutes.Value; owner.TimerReset_Click(reset, new RoutedEventArgs()); UpdateContent(); };
                    timerButtons.Children.Add(start); timerButtons.Children.Add(reset); content.Children.Add(timerButtons); break;
                case "Storage":
                    primary.TextWrapping = TextWrapping.Wrap; primary.FontSize = 20; content.Children.Add(primary); storageBar = new ProgressBar { Maximum = 100 }; content.Children.Add(storageBar); content.Children.Add(new TextBlock { Text = "Updated with the sidebar", FontSize = 12, Opacity = 0.7 }); break;
                case "Quick launch":
                    var launchList = new StackPanel { Spacing = 6 };
                    launcherIcons = new Image?[owner.state.Shortcuts.Count];
                    for (int i = 0; i < owner.state.Shortcuts.Count; i++)
                    {
                        var shortcut = owner.state.Shortcuts[i]; int index = i;
                        var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(8, 6, 8, 6) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        var icon = new Image { Width = 32, Height = 32, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform }; launcherIcons[index] = icon; row.Children.Add(icon);
                        var name = new TextBlock { Text = shortcut.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 15 }; Grid.SetColumn(name, 1); row.Children.Add(name);
                        var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(2) };
                        button.Click += async (_, _) => await owner.OpenShortcut(shortcut); launchList.Children.Add(button);
                        _ = LoadIcon(shortcut, icon);
                    }
                    content.Children.Add(launchList);
                    var edit = new Button { Content = "Customize Quick launch…", HorizontalAlignment = HorizontalAlignment.Left }; edit.Click += (_, _) => owner.CustomizeLaunch_Click(owner, new RoutedEventArgs());
                    content.Children.Add(edit); break;
                case "Slide show":
                    slideImage = new Image { Height = 280, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                    var imageFrame = new Border { Child = slideImage, CornerRadius = new CornerRadius(8), Background = owner.SurfaceBrush(root.RequestedTheme, 32) }; content.Children.Add(imageFrame);
                    primary.TextWrapping = TextWrapping.Wrap; content.Children.Add(primary);
                    var slideButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                    var prev = new Button { Content = "Previous" }; prev.Click += (_, _) => { slideIndex--; UpdateSlide(); };
                    var play = new Button { Content = owner.state.SlidePaused ? "Play" : "Pause" }; play.Click += (_, _) => { owner.SlidePlay_Click(play, new RoutedEventArgs()); play.Content = owner.state.SlidePaused ? "Play" : "Pause"; };
                    var nextSlide = new Button { Content = "Next" }; nextSlide.Click += (_, _) => { slideIndex++; UpdateSlide(); };
                    var choose = new Button { Content = "Choose folder…" }; choose.Click += (_, _) => owner.ChooseSlideFolder_Click(choose, new RoutedEventArgs());
                    slideButtons.Children.Add(prev); slideButtons.Children.Add(play); slideButtons.Children.Add(nextSlide); slideButtons.Children.Add(choose); content.Children.Add(slideButtons); break;
                case "Picture puzzle":
                    var board = new Grid { Width = 330, Height = 330, RowSpacing = 3, ColumnSpacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
                    for (int i = 0; i < 3; i++) { board.RowDefinitions.Add(new RowDefinition()); board.ColumnDefinitions.Add(new ColumnDefinition()); }
                    for (int i = 0; i < 9; i++)
                    {
                        int index = i; var tile = new Button { FontSize = 30, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(4), Height = 106, Width = 106 };
                        tile.Click += (_, _) => { if (puzzle!.Move(index)) { owner.state.PuzzleTiles = puzzle.Tiles.ToArray(); owner.state.PuzzleMoves = puzzle.Moves; owner.Save(); RenderPuzzle(board); } };
                        Grid.SetRow(tile, i / 3); Grid.SetColumn(tile, i % 3); board.Children.Add(tile); puzzleTiles.Add(tile);
                    }
                    content.Children.Add(board); secondary.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(secondary);
                    var shuffle = new Button { Content = "Shuffle" }; shuffle.Click += (_, _) => { puzzle!.Shuffle(); owner.state.PuzzleTiles = puzzle.Tiles.ToArray(); owner.state.PuzzleMoves = 0; owner.Save(); RenderPuzzle(board); }; content.Children.Add(shuffle);
                    RenderPuzzle(board); break;
                case "Feed headlines":
                    feedStatus = new TextBlock { Text = string.IsNullOrWhiteSpace(owner.state.FeedUrl) ? "Add an RSS or Atom feed in the sidebar first." : "Loading feed…", TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                    content.Children.Add(feedStatus); feedList = new StackPanel { Spacing = 8 }; content.Children.Add(feedList);
                    var refreshFeed = new Button { Content = "Refresh", HorizontalAlignment = HorizontalAlignment.Left }; refreshFeed.Click += async (_, _) => await RefreshDetachedFeed(); content.Children.Add(refreshFeed); break;
            }
        }

        private async Task LoadIcon(LaunchShortcut shortcut, Image image)
        {
            try
            {
                string data = shortcut.IconData;
                if (string.IsNullOrWhiteSpace(data)) return;
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(Convert.FromBase64String(data)); await writer.StoreAsync(); }
                stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); image.Source = bitmap;
            }
            catch (Exception ex) when (ex is FormatException or COMException or IOException) { }
        }

        private void RenderCalendar()
        {
            if (id != "Calendar" || renderingCalendar) return;
            renderingCalendar = true;
            contentGrid.Children.Clear(); contentGrid.RowDefinitions.Clear(); contentGrid.ColumnDefinitions.Clear();
            calendarTitle!.Text = calendarMonth.ToString("MMMM yyyy");
            for (int c = 0; c < 7; c++) contentGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 7; r++) contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            var culture = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
            int first = owner.state.CalendarFirstDay == "Sunday" ? 0 : owner.state.CalendarFirstDay == "Monday" ? 1 : (int)culture.FirstDayOfWeek;
            for (int c = 0; c < 7; c++)
            {
                var dayName = new TextBlock { Text = culture.GetAbbreviatedDayName((DayOfWeek)((first + c) % 7)), FontSize = 12, Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(dayName, 0); Grid.SetColumn(dayName, c); contentGrid.Children.Add(dayName);
            }
            int offset = ((int)calendarMonth.DayOfWeek - first + 7) % 7; var start = calendarMonth.AddDays(-offset);
            for (int n = 0; n < 42; n++)
            {
                var date = start.AddDays(n); if (!owner.state.CalendarAdjacentDays && date.Month != calendarMonth.Month) continue;
                bool today = date.Date == DateTime.Today; bool selected = date.Date == selectedDate.Date;
                var button = new Button { Content = date.Day.ToString(), FontSize = 13, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Opacity = date.Month == calendarMonth.Month ? 1 : 0.38 };
                if (today || selected) { button.Background = owner.SurfaceBrush(root.RequestedTheme, today ? (byte)225 : (byte)130); button.BorderBrush = new SolidColorBrush(owner.windowsAppearance.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent)); button.BorderThickness = new Thickness(1); }
                button.Click += (_, _) => { selectedDate = date; calendarMonth = new DateTime(date.Year, date.Month, 1); owner.month = calendarMonth; owner.selectedCalendarDate = selectedDate; owner.RenderCalendar(); RenderCalendar(); };
                Grid.SetRow(button, n / 7 + 1); Grid.SetColumn(button, n % 7); contentGrid.Children.Add(button);
            }
            secondary.Text = selectedDate.ToString("dddd, d MMMM");
            renderingCalendar = false;
        }

        private void UpdateContent()
        {
            if (owner.closing) return;
            refresh.Interval = RefreshInterval();
            switch (id)
            {
                case "Clock":
                    var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, owner.clockTimeZone).DateTime;
                    primary.Text = now.ToString(owner.state.ClockTimeFormat switch { "12-hour" => owner.state.ShowSeconds ? "h:mm:ss tt" : "h:mm tt", "24-hour" => owner.state.ShowSeconds ? "HH:mm:ss" : "HH:mm", _ => owner.state.ShowSeconds ? "T" : "t" });
                    secondary.Text = now.ToString("dddd, d MMMM");
                    DrawDetachedClock(now);
                    break;
                case "Performance":
                    primary.Text = owner.CpuText.Text; secondary.Text = owner.MemoryText.Text;
                    ((ProgressBar)content.Children[1]).Value = owner.CpuBar.Value; ((ProgressBar)content.Children[3]).Value = owner.MemoryBar.Value; break;
                case "Timer": primary.Text = owner.TimerText.Text; break;
                case "Storage": primary.Text = owner.StorageText.Text; storageBar!.Value = owner.StorageBar.Value; break;
                case "Notes": if (!syncingNotes && notes is not null && notes.Text != owner.state.Notes) { syncingNotes = true; notes.Text = owner.state.Notes; syncingNotes = false; } break;
                case "Slide show":
                    if (!owner.state.SlidePaused && DateTime.UtcNow >= nextSlideAt) slideIndex++;
                    UpdateSlide(); break;
                case "Feed headlines": if (DateTime.UtcNow >= nextFeedRefresh) _ = RefreshDetachedFeed(); break;
            }
        }

        private TimeSpan RefreshInterval() => id switch
        {
            "Clock" => owner.state.ShowSeconds || owner.state.ClockSecondHand ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10),
            "Timer" => owner.timerDeadline.HasValue ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(10),
            "Performance" => TimeSpan.FromSeconds(Math.Clamp(owner.state.PerformanceInterval, 1, 10)),
            "Storage" => TimeSpan.FromSeconds(30), "Slide show" when !owner.state.SlidePaused => TimeSpan.FromSeconds(Math.Clamp(owner.state.SlideInterval, 5, 120)),
            "Feed headlines" => TimeSpan.FromSeconds(5), _ => TimeSpan.FromSeconds(5)
        };

        private void DrawDetachedClock(DateTime now)
        {
            var canvas = clockCanvas ??= new Canvas { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center };
            if (!clockFaceBuilt)
            {
                canvas.Children.Clear(); hourHand = minuteHand = secondHand = null;
                canvas.Children.Add(new Ellipse { Width = 180, Height = 180, Fill = owner.SurfaceBrush(root.RequestedTheme, 120), Stroke = new SolidColorBrush(owner.windowsAppearance.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)), StrokeThickness = 1.2 });
                for (int i = 0; i < 60; i++)
                {
                    double angle = i * Math.PI / 30, r1 = i % 5 == 0 ? 70 : 76, r2 = 83;
                    canvas.Children.Add(new Line { X1 = 90 + Math.Sin(angle) * r1, Y1 = 90 - Math.Cos(angle) * r1, X2 = 90 + Math.Sin(angle) * r2, Y2 = 90 - Math.Cos(angle) * r2, Stroke = new SolidColorBrush(owner.windowsAppearance.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)), StrokeThickness = i % 5 == 0 ? 2 : 1 });
                }
                hourHand = AddHand(canvas, 0, 48, 3);
                minuteHand = AddHand(canvas, 0, 68, 2);
                if (owner.state.ClockSecondHand) secondHand = AddHand(canvas, 0, 74, 1, owner.CpuBar.Foreground);
                clockFaceBuilt = true;
            }
            SetHand(hourHand!, (now.Hour % 12 + now.Minute / 60d) * 30, 48);
            SetHand(minuteHand!, (now.Minute + now.Second / 60d) * 6, 68);
            if (owner.state.ClockSecondHand && secondHand is null) secondHand = AddHand(canvas, now.Second * 6, 74, 1, owner.CpuBar.Foreground);
            else if (!owner.state.ClockSecondHand && secondHand is not null) { canvas.Children.Remove(secondHand); secondHand = null; }
            if (secondHand is not null) SetHand(secondHand, now.Second * 6, 74);
            if (!content.Children.Contains(canvas)) content.Children.Insert(0, canvas);
        }

        private static Line AddHand(Canvas canvas, double angle, double length, double thickness, Brush? brush = null)
        {
            double radians = angle * Math.PI / 180;
            var line = new Line { X1 = 90, Y1 = 90, X2 = 90 + Math.Sin(radians) * length, Y2 = 90 - Math.Cos(radians) * length, Stroke = brush ?? new SolidColorBrush(Colors.Black), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            canvas.Children.Add(line); return line;
        }

        private static void SetHand(Line line, double angle, double length)
        { double radians = angle * Math.PI / 180; line.X2 = 90 + Math.Sin(radians) * length; line.Y2 = 90 - Math.Cos(radians) * length; }

        private void UpdateSlide()
        {
            if (slideImage is null) return;
            var files = owner.slideFiles;
            if (files.Length == 0) { primary.Text = string.IsNullOrWhiteSpace(owner.state.SlideFolder) ? "Choose a picture folder" : "No pictures in this folder"; slideImage.Source = null; return; }
            slideIndex = (slideIndex % files.Length + files.Length) % files.Length;
            if (loadedSlidePath != files[slideIndex]) { loadedSlidePath = files[slideIndex]; var bitmap = new BitmapImage { DecodePixelWidth = 900 }; bitmap.UriSource = new Uri(files[slideIndex]); slideImage.Source = bitmap; }
            primary.Text = System.IO.Path.GetFileName(files[slideIndex]);
            nextSlideAt = DateTime.UtcNow.AddSeconds(Math.Clamp(owner.state.SlideInterval, 5, 120));
        }

        private void RenderPuzzle(Grid board)
        {
            for (int i = 0; i < 9; i++)
            {
                int tile = puzzle!.Tiles[i]; var button = puzzleTiles[i];
                button.Content = tile == 0 ? null : tile.ToString();
                button.IsEnabled = tile != 0 && !puzzle.Solved;
                button.Opacity = tile == 0 ? 0.1 : 1;
                button.Background = owner.SurfaceBrush(root.RequestedTheme, tile == 0 ? (byte)48 : (byte)220);
            }
            secondary.Text = puzzle!.Solved ? $"Completed · {puzzle.Moves} moves" : $"{puzzle.Moves} {(puzzle.Moves == 1 ? "move" : "moves")}";
        }

        private async Task RefreshDetachedFeed()
        {
            if (feedList is null || feedStatus is null || !Uri.TryCreate(owner.state.FeedUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
            nextFeedRefresh = DateTime.UtcNow.AddMinutes(Math.Clamp(owner.state.FeedRefreshMinutes, 5, 120));
            feedStatus.Text = "Updating…";
            try
            {
                var headlines = await FeedReader.FetchAsync(uri, owner.classicCancellation.Token, owner.state.FeedCount);
                feedList.Children.Clear();
                foreach (var headline in headlines)
                {
                    var button = new HyperlinkButton { NavigateUri = headline.Link, Content = headline.Title, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(4) };
                    feedList.Children.Add(button);
                }
                feedStatus.Text = headlines.Count == 0 ? "No headlines found" : $"Updated {DateTime.Now:t}";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException) { feedStatus.Text = "Could not update this feed."; }
        }
    }
}
