using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Microsoft.UI.Xaml.Automation;

namespace Sidebar;

public partial class MainWindow : Window
{
    private readonly string statePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicSidebar", "settings.json");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private SidebarState state = new();
    private DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool ready;
    private bool appearanceRefreshQueued;
    private bool closing;
    private DesktopAppBar? appBar;
    private TrayIcon? trayIcon;
    private bool dialogOpen;
    private Window? preferencesWindow;
    private DateTime? timerDeadline;
    private TimeSpan pausedTimer = TimeSpan.Zero;
    private readonly UISettings windowsAppearance = new();
    private ulong previousIdle, previousKernel, previousUser;
    private bool clockFaceDirty = true;
    private RotateTransform? hourRotation, minuteRotation, secondRotation;
    private DateTime clockDate;
    private DateTime nextPerformanceUpdate;

    public MainWindow()
    {
        InitializeComponent();
        try { if (System.IO.File.Exists(statePath)) state = JsonSerializer.Deserialize<SidebarState>(System.IO.File.ReadAllText(statePath)) ?? new(); }
        catch (Exception ex) when (ex is System.IO.IOException or JsonException or UnauthorizedAccessException) { SaveStatus.Text = "Could not read saved settings"; }
        NotesBox.Text = state.Notes;
        ApplyState();
        InitializeGadgetDragging();
        InitializeAutoHide();
        RefreshQuickLaunch();
        InitializeClassicGadgets();
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        // Exclude the host before its first activation, so Explorer never adds a taskbar button.
        HideFromTaskSwitchers();
        ready = true;
        windowsAppearance.ColorValuesChanged += WindowsAppearance_Changed;
        Root.ActualThemeChanged += (_, _) => QueueAppearanceRefresh();
        RenderCalendar();
        UpdateGadgets();
        timer.Tick += (_, _) => UpdateGadgets();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        UpdateTimerInterval();
        timer.Start();
        Activated += (_, _) => SynchronizeAppearance();
        Closed += (_, _) => { closing = true; preferencesWindow?.Close(); windowsAppearance.ColorValuesChanged -= WindowsAppearance_Changed; trayIcon?.Dispose(); appBar?.Dispose(); timer.Stop(); saveTimer.Stop(); Save(); };
    }

    public void AttachToDesktop()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        const int windowStyle = -16;
        long style = GetWindowLongPtr(hwnd, windowStyle).ToInt64();
        // The non-resizable presenter can restore WS_BORDER during its initial activation.
        SetWindowLongPtr(hwnd, windowStyle, new IntPtr(style & ~0x00C40000L));
        HideFromTaskSwitchers();
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        SynchronizeAppearance();
        appBar = new DesktopAppBar(hwnd, action => DispatcherQueue.TryEnqueue(() => action()));
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Sidebar.ico");
        AppWindow.SetIcon(iconPath);
        trayIcon = new TrayIcon(hwnd, iconPath, command => DispatcherQueue.TryEnqueue(() => HandleTrayCommand(command)), () => state.AlwaysOnTop);
        if (StartupPreference.Enabled) SetStartup(true);
        PositionSidebar();
        ApplyAutoHidePreference();
    }

    private void HideFromTaskSwitchers()
    {
        AppWindow.IsShownInSwitchers = false;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        const int extendedStyle = -20;
        const long toolWindow = 0x00000080;
        const long appWindow = 0x00040000;
        long style = GetWindowLongPtr(hwnd, extendedStyle).ToInt64();
        SetWindowLongPtr(hwnd, extendedStyle, new IntPtr((style | toolWindow) & ~appWindow));
        // Refresh non-client styling without moving, resizing, activating, or changing z-order.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
    }

    private void ApplyState()
    {
        SynchronizeAppearance();
        bool useMica = state.Material == "Mica";
        if (SystemBackdrop is not SidebarBackdrop backdrop || backdrop.UsesMica != useMica)
            SystemBackdrop = new SidebarBackdrop(useMica);
        ClockGadget.Visibility = state.Clock ? Visibility.Visible : Visibility.Collapsed;
        ClockView.Visibility = state.ClockStyle == "Digital" ? Visibility.Collapsed : Visibility.Visible;
        TimeText.Visibility = state.ClockStyle == "Analog" ? Visibility.Collapsed : Visibility.Visible;
        NotesBox.MinHeight = Math.Clamp(state.NoteHeight, 72, 240);
        NotesBox.MaxHeight = Math.Max(180, NotesBox.MinHeight);
        NormalizeGadgetOrder();
        GadgetStack.Children.Clear();
        foreach (var id in state.GadgetOrder) GadgetStack.Children.Add(GadgetElement(id));
        ApplyGadgetPage();
        ClockView.Width = ClockView.Height = state.CompactClock ? 80 : 120;
        ApplyClockPreferences();
        appBar?.Configure(state.Left, state.AlwaysOnTop, state.Width, state.AutoHide);
        ApplyAutoHidePreference();
        UpdateTimerInterval();
        if (ready) UpdateGadgets();
    }

    private void WindowsAppearance_Changed(UISettings sender, object args)
        => DispatcherQueue.TryEnqueue(() => { if (!closing) { SynchronizeAppearance(); QueueAppearanceRefresh(); } });

    private void QueueAppearanceRefresh()
    {
        if (!ready || closing || appearanceRefreshQueued) return;
        appearanceRefreshQueued = true;
        // Let WinUI finish walking the theme tree before replacing calendar/clock children.
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            appearanceRefreshQueued = false;
            if (closing) return;
            clockFaceDirty = true;
            RenderCalendar();
            UpdateGadgets();
        })) appearanceRefreshQueued = false;
    }

    private void SynchronizeAppearance()
    {
        var background = windowsAppearance.GetColorValue(UIColorType.Background);
        var requestedTheme = state.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => (background.R * 299 + background.G * 587 + background.B * 114) >= 128000 ? ElementTheme.Light : ElementTheme.Dark
        };
        if (Root.RequestedTheme != requestedTheme) Root.RequestedTheme = requestedTheme;
        if (preferencesWindow?.Content is FrameworkElement preferencesRoot)
        {
            if (preferencesRoot.RequestedTheme != requestedTheme) preferencesRoot.RequestedTheme = requestedTheme;
            preferencesWindow.AppWindow.TitleBar.ButtonForegroundColor = Root.RequestedTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
            preferencesWindow.AppWindow.TitleBar.ButtonInactiveForegroundColor = Root.RequestedTheme == ElementTheme.Dark ? Colors.LightGray : Colors.DimGray;
        }
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // DWM otherwise adds a light one-pixel outline around borderless windows.
        uint noBorder = 0xfffffffe; // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(uint)); // DWMWA_BORDER_COLOR
        uint squareCorners = 1; // DWMWCP_DONOTROUND: the rail meets the screen edge.
        DwmSetWindowAttribute(hwnd, 33, ref squareCorners, sizeof(uint));
    }

    private void PositionSidebar()
    {
        appBar?.Configure(state.Left, state.AlwaysOnTop, state.Width, state.AutoHide);
        ApplyAutoHidePreference();
    }

    private FrameworkElement GadgetElement(string id) => id switch
    {
        "Clock" => ClockGadget, "Calendar" => CalendarGadget, "Notes" => NotesGadget, "Performance" => SystemGadget,
        "Timer" => TimerCategory, "Storage" => StorageCategory, "Quick launch" => LaunchCategory,
        "Slide show" => SlideShowGadget, "Picture puzzle" => PuzzleGadget, "Feed headlines" => FeedsGadget,
        _ => throw new ArgumentException("Unknown gadget.", nameof(id))
    };

    private bool GadgetEnabled(string id) => id switch
    {
        "Clock" => state.Clock, "Calendar" => state.Calendar, "Notes" => state.ShowNotes, "Performance" => state.System,
        "Timer" => state.Timer, "Storage" => state.Storage, "Quick launch" => state.QuickLaunch,
        "Slide show" => state.SlideShow, "Picture puzzle" => state.PicturePuzzle, "Feed headlines" => state.FeedHeadlines, _ => false
    };

    private void SetGadgetEnabled(string id, bool enabled)
    {
        switch (id)
        {
            case "Clock": state.Clock = enabled; break;
            case "Calendar": state.Calendar = enabled; break;
            case "Notes": state.ShowNotes = enabled; break;
            case "Performance": state.System = enabled; break;
            case "Timer": state.Timer = enabled; break;
            case "Storage": state.Storage = enabled; break;
            case "Quick launch": state.QuickLaunch = enabled; break;
            case "Slide show": state.SlideShow = enabled; break;
            case "Picture puzzle": state.PicturePuzzle = enabled; break;
            case "Feed headlines": state.FeedHeadlines = enabled; break;
        }
    }

    private void NormalizeGadgetOrder()
    {
        string[] known = ["Calendar", "Clock", "Notes", "Performance", "Timer", "Storage", "Quick launch", "Slide show", "Picture puzzle", "Feed headlines"];
        bool migrate = state.LayoutVersion < 3;
        state.GadgetOrder = (state.GadgetOrder ?? []).Where(known.Contains).Concat(known).Distinct().ToList();
        if (migrate && state.GadgetOrder.Count > 1 && state.GadgetOrder[0] == "Clock")
        {
            state.GadgetOrder.RemoveAt(0);
            state.GadgetOrder.Insert(1, "Clock");
        }
        state.LayoutVersion = 3;
        if (migrate) Save();
    }

    private void ApplyGadgetPage()
    {
        foreach (var id in state.GadgetOrder) GadgetElement(id).Visibility = Visibility.Collapsed;
        var enabled = state.GadgetOrder.Where(GadgetEnabled).ToArray();
        int density = Math.Clamp(state.GadgetsPerPage, 1, 3);
        int pages = Math.Max(1, (enabled.Length + density - 1) / density);
        state.PageIndex = Math.Clamp(state.PageIndex, 0, pages - 1);
        foreach (var id in enabled.Skip(state.PageIndex * density).Take(density)) GadgetElement(id).Visibility = Visibility.Visible;
        PageIndicator.Text = enabled.Length == 0 ? "No gadgets" : $"{state.PageIndex + 1} / {pages}";
        PreviousPage.IsEnabled = state.PageIndex > 0;
        NextPage.IsEnabled = state.PageIndex < pages - 1;
        PreviousPage.Visibility = NextPage.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(PreviousPage, $"Previous page · {state.PageIndex + 1} of {pages}");
        ToolTipService.SetToolTip(NextPage, $"Next page · {state.PageIndex + 1} of {pages}");
        nextPerformanceUpdate = DateTime.MinValue;
        nextStatusUpdate = DateTime.MinValue;
        UpdateTimerInterval();
        if (ready) UpdateGadgets();
        if (ready) UpdateClassicGadgets();
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e) { state.PageIndex--; ApplyGadgetPage(); Save(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { state.PageIndex++; ApplyGadgetPage(); Save(); }

    private void UpdateGadgets()
    {
        var now = DateTime.Now;
        if (ClockGadget.Visibility == Visibility.Visible)
        {
            var clockNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, clockTimeZone).DateTime;
            string format = state.ClockTimeFormat switch { "12-hour" => state.ShowSeconds ? "h:mm:ss tt" : "h:mm tt", "24-hour" => state.ShowSeconds ? "HH:mm:ss" : "HH:mm", _ => state.ShowSeconds ? "T" : "t" };
            var time = clockNow.ToString(format);
            if (TimeText.Text != time) TimeText.Text = time;
            if (clockDate != clockNow.Date) { DateText.Text = clockNow.ToString("dddd, d MMMM"); clockDate = clockNow.Date; }
            if (ClockView.Visibility == Visibility.Visible)
            {
                if (clockFaceDirty) BuildClockFace();
                hourRotation!.Angle = (clockNow.Hour % 12 + clockNow.Minute / 60d) * 30;
                minuteRotation!.Angle = (clockNow.Minute + clockNow.Second / 60d) * 6;
                if (secondRotation is not null) secondRotation.Angle = clockNow.Second * 6;
            }
        }
        if (month.Year == now.Year && month.Month == now.Month && lastDate != now.Date) RenderCalendar();
        UpdatePerformance();
        UpdateDesktopStatus();
        UpdateClassicGadgets();
        UpdateTimerInterval();
    }

    private void BuildClockFace()
    {
        ClockFace.Children.Clear();
        var ink = TimeText.Foreground;
        var secondary = DateText.Foreground;
        for (int i = 0; i < 60; i++)
        {
            double a = i * Math.PI / 30;
            double r = i % 5 == 0 ? 63 : 68;
            ClockFace.Children.Add(new Line { X1 = 78 + Math.Sin(a) * r, Y1 = 78 - Math.Cos(a) * r, X2 = 78 + Math.Sin(a) * 72, Y2 = 78 - Math.Cos(a) * 72, Stroke = secondary, StrokeThickness = i % 5 == 0 ? 2 : 1 });
        }
        hourRotation = Hand(40, 4, ink);
        minuteRotation = Hand(57, 3, ink);
        secondRotation = state.ClockSecondHand ? Hand(61, 1.5, CpuBar.Foreground) : null;
        DrawClockNumerals(ink);
        var hub = new Ellipse { Width = 7, Height = 7, Fill = ink };
        Canvas.SetLeft(hub, 74.5); Canvas.SetTop(hub, 74.5); ClockFace.Children.Add(hub);
        clockFaceDirty = false;
    }

    private void UpdateTimerInterval()
    {
        bool active = ClockGadget.Visibility == Visibility.Visible || timerDeadline.HasValue || SystemGadget.Visibility == Visibility.Visible;
        var interval = TimeSpan.FromSeconds(active ? 1 : 30);
        if (timer.Interval != interval) timer.Interval = interval;
    }

    private void UpdatePerformance()
    {
        if (SystemGadget.Visibility != Visibility.Visible) { previousKernel = 0; return; }
        if (DateTime.UtcNow < nextPerformanceUpdate) return;
        nextPerformanceUpdate = DateTime.UtcNow.AddSeconds(2);
        if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            ulong total = kernel - previousKernel + user - previousUser;
            if (previousKernel != 0 && total > 0) { CpuBar.Value = Math.Clamp(100d * (1 - (double)(idle - previousIdle) / total), 0, 100); CpuText.Text = $"CPU  ·  {CpuBar.Value:0}%"; }
            previousIdle = idle; previousKernel = kernel; previousUser = user;
            CpuRing.Value = CpuBar.Value; CpuPercent.Text = $"{CpuBar.Value:0}%";
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            MemoryBar.Value = memory.Load;
            MemoryRing.Value = memory.Load; MemoryPercent.Text = $"{memory.Load}%";
            MemoryText.Text = $"Memory  ·  {(memory.TotalPhysical - memory.AvailablePhysical) / 1073741824d:0.0} / {memory.TotalPhysical / 1073741824d:0.0} GB";
        }
    }

    private RotateTransform Hand(double length, double thickness, Brush brush)
    {
        var rotation = new RotateTransform { CenterX = 78, CenterY = 78 };
        ClockFace.Children.Add(new Line { X1 = 78, Y1 = 78, X2 = 78, Y2 = 78 - length, RenderTransform = rotation, Stroke = brush, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        return rotation;
    }

    private DateTime lastDate;
    private void RenderCalendar()
    {
        lastDate = DateTime.Today;
        MonthText.Text = month.ToString("MMMM yyyy");
        CalendarGrid.Children.Clear(); CalendarGrid.ColumnDefinitions.Clear(); CalendarGrid.RowDefinitions.Clear();
        for (int i = 0; i < 7; i++) CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var culture = CultureInfo.CurrentCulture.DateTimeFormat;
        int first = (int)culture.FirstDayOfWeek;
        int offset = ((int)month.DayOfWeek - first + 7) % 7;
        int rows = 1 + (offset + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
        for (int i = 0; i < rows; i++) CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        for (int i = 0; i < 7; i++) AddDay(culture.GetShortestDayName((DayOfWeek)((i + first) % 7)), 0, i, false);
        for (int day = 1; day <= DateTime.DaysInMonth(month.Year, month.Month); day++)
        {
            int cell = offset + day - 1;
            AddDay(day.ToString(), cell / 7 + 1, cell % 7, month.AddDays(day - 1) == DateTime.Today);
        }
    }

    private void AddDay(string text, int row, int column, bool today)
    {
        var label = new TextBlock { Text = text, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var border = new Border { Child = label, CornerRadius = new CornerRadius(10), Width = 20, Height = 24 };
        if (today) { border.Background = CpuBar.Foreground; label.Foreground = new SolidColorBrush(Colors.White); }
        Grid.SetRow(border, row); Grid.SetColumn(border, column); CalendarGrid.Children.Add(border);
    }
    private void PreviousMonth_Click(object sender, RoutedEventArgs e) { month = month.AddMonths(-1); RenderCalendar(); }
    private void NextMonth_Click(object sender, RoutedEventArgs e) { month = month.AddMonths(1); RenderCalendar(); }
    private void Today_Click(object sender, RoutedEventArgs e) { month = new(DateTime.Today.Year, DateTime.Today.Month, 1); RenderCalendar(); }
    private void Notes_Changed(object sender, TextChangedEventArgs e) { if (!ready) return; state.Notes = NotesBox.Text; SaveStatus.Text = "Saving…"; saveTimer.Stop(); saveTimer.Start(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) { preferencesWindow?.Activate(); return; }
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Choose gadgets and arrange their page order.", TextWrapping = TextWrapping.Wrap });
        var list = new StackPanel { Spacing = 8 };
        panel.Children.Add(list);
        void RenderOrder()
        {
            list.Children.Clear();
            for (int index = 0; index < state.GadgetOrder.Count; index++)
            {
                int position = index;
                string id = state.GadgetOrder[index];
                var row = new Grid { ColumnSpacing = 4 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var check = new CheckBox { Content = id, IsChecked = GadgetEnabled(id) };
                check.Click += (_, _) => { SetGadgetEnabled(id, check.IsChecked == true); ApplyState(); Save(); };
                row.Children.Add(check);
                for (int direction = -1; direction <= 1; direction += 2)
                {
                    int step = direction;
                    var button = new Button { Content = new FontIcon { Glyph = step < 0 ? "\uE70E" : "\uE70D", FontSize = 12 }, Padding = new Thickness(8), IsEnabled = position + step >= 0 && position + step < state.GadgetOrder.Count };
                    AutomationProperties.SetName(button, $"Move {id} {(step < 0 ? "up" : "down")}");
                    ToolTipService.SetToolTip(button, step < 0 ? "Move up" : "Move down");
                    button.Click += (_, _) => { (state.GadgetOrder[position], state.GadgetOrder[position + step]) = (state.GadgetOrder[position + step], state.GadgetOrder[position]); ApplyState(); Save(); RenderOrder(); };
                    Grid.SetColumn(button, step < 0 ? 1 : 2); row.Children.Add(button);
                }
                list.Children.Add(row);
            }
        }
        RenderOrder();
        await ShowDialog("Gadgets", panel);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) { preferencesWindow?.Activate(); return; }
        var panel = new StackPanel { Spacing = 12 };
        var theme = new ComboBox { Header = "Theme", ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = state.Theme, HorizontalAlignment = HorizontalAlignment.Stretch };
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedItem is not string selected || state.Theme == selected) return;
            state.Theme = selected;
            // Appearance changes do not reparent gadgets or change appbar geometry.
            SynchronizeAppearance();
            QueueAppearanceRefresh();
            Save();
        };
        panel.Children.Add(theme);
        var material = new ComboBox { Header = "Material", ItemsSource = new[] { "Acrylic", "Mica" }, SelectedItem = state.Material, HorizontalAlignment = HorizontalAlignment.Stretch };
        material.SelectionChanged += (_, _) => { state.Material = (string)material.SelectedItem; ApplyState(); Save(); };
        panel.Children.Add(material);
        var width = new NumberBox { Header = "Width", Minimum = 220, Maximum = 360, Value = state.Width, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        width.ValueChanged += (_, args) => { if (!double.IsNaN(args.NewValue)) { state.Width = (int)args.NewValue; PositionSidebar(); UpdateClassicGadgets(); Save(); } };
        panel.Children.Add(width);
        AddToggle(panel, "Compact clock", state.CompactClock, v => { state.CompactClock = v; state.ClockSize = 0; });
        AddToggle(panel, "Auto-hide at screen edge", state.AutoHide, v => state.AutoHide = v);
        var clockStyle = new ComboBox { Header = "Clock style", ItemsSource = new[] { "Analog + digital", "Analog", "Digital" }, SelectedItem = state.ClockStyle, HorizontalAlignment = HorizontalAlignment.Stretch };
        clockStyle.SelectionChanged += (_, _) => { state.ClockStyle = (string)clockStyle.SelectedItem; ApplyState(); Save(); };
        panel.Children.Add(clockStyle);
        AddToggle(panel, "Show digital seconds", state.ShowSeconds, v => state.ShowSeconds = v);
        var density = new NumberBox { Header = "Gadgets per page", Minimum = 1, Maximum = 3, Value = state.GadgetsPerPage, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        density.ValueChanged += (_, args) => { if (!double.IsNaN(args.NewValue)) { state.GadgetsPerPage = (int)args.NewValue; ApplyGadgetPage(); Save(); } };
        panel.Children.Add(density);
        var noteHeight = new NumberBox { Header = "Note height", Minimum = 72, Maximum = 240, Value = state.NoteHeight, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        noteHeight.ValueChanged += (_, args) => { if (!double.IsNaN(args.NewValue)) { state.NoteHeight = (int)args.NewValue; ApplyState(); Save(); } };
        panel.Children.Add(noteHeight);
        var slideInterval = new NumberBox { Header = "Slide show interval (seconds)", Minimum = 5, Maximum = 120, Value = state.SlideInterval, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        slideInterval.ValueChanged += (_, args) => { if (!double.IsNaN(args.NewValue)) { state.SlideInterval = (int)args.NewValue; UpdateClassicGadgets(); Save(); } };
        panel.Children.Add(slideInterval);
        AddToggle(panel, "Start with Windows", StartupPreference.Enabled, SetStartup);
        AddToggle(panel, "Place on the left", state.Left, v => { state.Left = v; PositionSidebar(); });
        AddToggle(panel, "Keep above other windows", state.AlwaysOnTop, v => state.AlwaysOnTop = v);
        panel.Children.Add(new TextBlock { Text = "Sidebar reserves this screen edge. Maximized windows fit beside it. Closing Sidebar restores the space.", TextWrapping = TextWrapping.Wrap, MaxWidth = 250 });
        panel.Children.Add(new TextBlock { Text = "Notes and preferences are saved on this device.", TextWrapping = TextWrapping.Wrap, MaxWidth = 280 });
        await ShowDialog("Settings", panel);
    }

    private void AddToggle(StackPanel panel, string title, bool value, Action<bool> change)
    {
        var toggle = new ToggleSwitch { Header = title, IsOn = value };
        toggle.Toggled += (_, _) => { change(toggle.IsOn); ApplyState(); Save(); };
        panel.Children.Add(toggle);
    }

    private void AddCategory(StackPanel panel, string title, bool value, Action<bool> change)
    {
        var checkbox = new CheckBox { Content = title, IsChecked = value };
        checkbox.Click += (_, _) => { change(checkbox.IsChecked == true); ApplyState(); Save(); };
        panel.Children.Add(checkbox);
    }

    private async Task ShowDialog(string title, object content)
    {
        if (dialogOpen) return;
        dialogOpen = true;
        var completion = new TaskCompletionSource<bool>();
        var window = new Window { Title = $"Windows Sidebar Fluent — {title}", SystemBackdrop = new MicaBackdrop() };
        window.AppWindow.IsShownInSwitchers = false;
        preferencesWindow = window;
        var layout = new Grid { Padding = new Thickness(24), RowSpacing = 20 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var done = new Button { Content = "Done", HorizontalAlignment = HorizontalAlignment.Right };
        done.Click += (_, _) => window.Close(); Grid.SetRow(done, 2); layout.Children.Add(done);
        var windowRoot = new Grid { RequestedTheme = Root.RequestedTheme };
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleBar = new TitleBar { Title = "Windows Sidebar Fluent" };
        windowRoot.Children.Add(titleBar);
        Grid.SetRow(layout, 1); windowRoot.Children.Add(layout);
        window.Content = windowRoot;
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        window.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
        presenter.IsMinimizable = true;
        presenter.IsMaximizable = true;
        presenter.IsResizable = true;
        window.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Sidebar.ico"));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowLongPtr(hwnd, -8, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        int width = Math.Min((int)(400 * scale), area.Width - 24);
        int height = Math.Min((int)(640 * scale), area.Height - 24);
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        RevealSidebar();
        window.Closed += (_, _) => { preferencesWindow = null; dialogOpen = false; completion.TrySetResult(true); QueueAutoHide(); };
        window.Activate();
        SynchronizeAppearance();
        await completion.Task;
    }

    private void Sidebar_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        for (DependencyObject? element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is TextBox or NumberBox) return;
        var menu = new MenuFlyout();
        void Item(string title, Action action)
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Item("Categories and gadgets…", () => Add_Click(this, new RoutedEventArgs()));
        Item("Settings…", () => Settings_Click(this, new RoutedEventArgs()));
        Item("Customize clock…", () => CustomizeClock_Click(this, new RoutedEventArgs()));
        Item("Customize Quick launch…", () => CustomizeLaunch_Click(this, new RoutedEventArgs()));
        menu.Items.Add(new MenuFlyoutSeparator());
        var startup = new ToggleMenuFlyoutItem { Text = "Start with Windows", IsChecked = StartupPreference.Enabled };
        startup.Click += (_, _) => SetStartup(startup.IsChecked);
        menu.Items.Add(startup);
        var top = new ToggleMenuFlyoutItem { Text = "Always on top", IsChecked = state.AlwaysOnTop };
        top.Click += (_, _) => { state.AlwaysOnTop = top.IsChecked; ApplyState(); Save(); };
        menu.Items.Add(top);
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("Exit Windows Sidebar Fluent", Close);
        menu.Opened += (_, _) => { sidebarMenuOpen = true; hideTimer.Stop(); };
        menu.Closed += (_, _) => { sidebarMenuOpen = false; QueueAutoHide(); };
        menu.ShowAt(Root, e.GetPosition(Root));
        e.Handled = true;
    }

    private void HandleTrayCommand(int command)
    {
        RevealSidebar();
        switch (command)
        {
            case 1: AppWindow.Show(); Activate(); break;
            case 2: Activate(); Settings_Click(this, new RoutedEventArgs()); break;
            case 3: Activate(); Add_Click(this, new RoutedEventArgs()); break;
            case 4: SetStartup(!StartupPreference.Enabled); break;
            case 5: state.AlwaysOnTop = !state.AlwaysOnTop; ApplyState(); Save(); break;
            case 6: Close(); break;
        }
    }

    private void SetStartup(bool enabled)
    {
        try { StartupPreference.Enabled = enabled; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        { PanelStatus.Text = "Could not change startup preference."; PanelStatus.Visibility = Visibility.Visible; }
    }

    private async void WindowsPanel_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        string uri = tag switch
        {
            "controls" => "ms-controlcenter:",
            "network" => "ms-availablenetworks:", "sound" => "ms-settings:sound",
            "display" => "ms-settings:display", "power" => "ms-settings:powersleep",
            "calculator" => "calculator:", _ => "ms-settings:"
        };
        try
        {
            if (tag == "files")
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:MyComputerFolder") { UseShellExecute = true });
            else if (!await Launcher.LaunchUriAsync(new Uri(uri)))
            {
                string fallback = tag switch { "controls" => "ms-settings:controlcenter", "network" => "ms-settings:network", _ => "ms-settings:" };
                if (!await Launcher.LaunchUriAsync(new Uri(fallback))) throw new InvalidOperationException("Windows panel is unavailable.");
            }
            PanelStatus.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or COMException)
        { PanelStatus.Text = "Windows could not open this panel."; PanelStatus.Visibility = Visibility.Visible; }
    }

    private void Timer_Click(object sender, RoutedEventArgs e)
    {
        if (timerDeadline.HasValue)
        {
            pausedTimer = timerDeadline.Value - DateTime.UtcNow;
            timerDeadline = null; TimerStart.Content = "Resume";
        }
        else
        {
            var duration = pausedTimer > TimeSpan.Zero ? pausedTimer : TimeSpan.FromMinutes(double.IsNaN(TimerMinutes.Value) ? 5 : TimerMinutes.Value);
            timerDeadline = DateTime.UtcNow + duration; TimerStart.Content = "Pause";
        }
        UpdateTimerInterval();
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        timerDeadline = null; pausedTimer = TimeSpan.Zero; TimerStart.Content = "Start";
        UpdateTimerInterval();
        TimerText.Text = $"{(double.IsNaN(TimerMinutes.Value) ? 5 : TimerMinutes.Value):00}:00";
    }

    private DateTime nextStatusUpdate;
    private void UpdateDesktopStatus()
    {
        if (timerDeadline.HasValue)
        {
            var remaining = timerDeadline.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                TimerText.Text = "Time is up"; timerDeadline = null; pausedTimer = TimeSpan.Zero; TimerStart.Content = "Start";
                MessageBeep(0x40);
            }
            else TimerText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }
        if (StorageCategory.Visibility != Visibility.Visible || DateTime.UtcNow < nextStatusUpdate) return;
        nextStatusUpdate = DateTime.UtcNow.AddSeconds(30);
        try
        {
            var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);
            StorageText.Text = $"{drive.Name}  {drive.AvailableFreeSpace / 1073741824d:0.0} GB free of {drive.TotalSize / 1073741824d:0.0} GB";
            StorageBar.Value = 100d * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
        }
        catch (System.IO.IOException) { StorageText.Text = "Storage unavailable"; }
    }

    private void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(statePath)!);
            System.IO.File.WriteAllText(statePath + ".tmp", JsonSerializer.Serialize(state));
            System.IO.File.Move(statePath + ".tmp", statePath, true);
            SaveStatus.Text = "Saved on this device";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { SaveStatus.Text = "Could not save. Check folder access."; }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref uint value, int size);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MessageBeep(uint type);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
}

public sealed class SidebarState
{
    public string Notes { get; set; } = "";
    public string Material { get; set; } = "Acrylic";
    public string Theme { get; set; } = "System";
    public int Width { get; set; } = 240;
    public bool CompactClock { get; set; } = true;
    public bool AutoHide { get; set; }
    public string ClockStyle { get; set; } = "Analog + digital";
    public bool ShowSeconds { get; set; }
    public int NoteHeight { get; set; } = 100;
    public int GadgetsPerPage { get; set; } = 2;
    public int PageIndex { get; set; }
    public int LayoutVersion { get; set; } = 1;
    public List<string> GadgetOrder { get; set; } = ["Calendar", "Clock", "Notes", "Performance", "Timer", "Storage", "Quick launch"];
    public List<LaunchShortcut> Shortcuts { get; set; } = [new() { Name = "File Explorer", Target = "shell:MyComputerFolder" }, new() { Name = "Calculator", Target = "calculator:" }, new() { Name = "Windows Settings", Target = "ms-settings:" }];
    public bool QuickLaunch { get; set; }
    public bool Timer { get; set; }
    public bool Storage { get; set; }
    public bool Left { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool Clock { get; set; } = true;
    public bool Calendar { get; set; } = true;
    public bool ShowNotes { get; set; } = true;
    public bool System { get; set; } = true;
    public string ClockFaceStyle { get; set; } = "Minimal";
    public string ClockTimeFormat { get; set; } = "System";
    public string ClockTimeZone { get; set; } = "Local";
    public string ClockLabel { get; set; } = "";
    public int ClockSize { get; set; }
    public bool ClockSecondHand { get; set; } = true;
    public bool ClockShowDate { get; set; } = true;
    public bool SlideShow { get; set; }
    public bool PicturePuzzle { get; set; }
    public bool FeedHeadlines { get; set; }
    public string SlideFolder { get; set; } = "";
    public int SlideInterval { get; set; } = 10;
    public bool SlidePaused { get; set; }
    public string PuzzlePicture { get; set; } = "";
    public int[]? PuzzleTiles { get; set; }
    public int PuzzleMoves { get; set; }
    public string FeedUrl { get; set; } = "";
}

internal static class ResourceExtensions
{
    public static object LookupOrApplication(this ResourceDictionary resources, string key) => resources.TryGetValue(key, out var value) ? value : Application.Current.Resources[key];
}
