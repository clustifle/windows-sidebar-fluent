using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace Sidebar;

public partial class MainWindow
{
    private ImageSource? puzzlePicture, renderedPuzzlePicture;
    private SvgImageSource? samplePuzzlePicture;
    private string? loadedSampleId;
    private bool puzzleSampleReady, renderedPuzzleRail, renderedPuzzleLabels;
    private int puzzleRenderWidth;
    private double puzzleCellSize;
    private string? loadedPuzzleKey, puzzleImageError;
    private FlyoutBase? puzzlePopup;
    private readonly Dictionary<int, Button> puzzleButtons = new();
    private string PuzzleImageKey => state.PuzzleImageMode + "\n" + state.PuzzleSampleId + "\n" + (state.PuzzleImageMode == "Custom" ? state.PuzzlePicture : "");

    private void NormalizePuzzleImageMode()
    {
        if (state.PuzzleImageMode is not ("Sample" or "Custom" or "Numbers"))
            state.PuzzleImageMode = string.IsNullOrWhiteSpace(state.PuzzlePicture) ? "Sample" : "Custom";
        state.PuzzleSampleId = PuzzleSamples.Find(state.PuzzleSampleId).Id;
    }

    private ImageSource? SamplePuzzlePicture()
    {
        var selected = PuzzleSamples.Find(state.PuzzleSampleId);
        if (samplePuzzlePicture is not null && loadedSampleId == selected.Id) return samplePuzzlePicture;
        if (!System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", selected.FileName)))
        { puzzleImageError = "Sample picture unavailable"; return null; }
        var sample = new SvgImageSource { RasterizePixelWidth = 480, RasterizePixelHeight = 480 };
        sample.Opened += (_, _) =>
        {
            if (!ReferenceEquals(samplePuzzlePicture, sample)) return;
            puzzleSampleReady = true;
            if (!sidebarClosed && !closing && ReferenceEquals(puzzlePicture, sample)) PuzzlePreview.IsEnabled = true;
        };
        sample.OpenFailed += (_, _) =>
        {
            if (sidebarClosed || closing || !ReferenceEquals(puzzlePicture, sample)) return;
            puzzlePicture = null; puzzleImageError = "Sample picture unavailable"; RenderPuzzle();
        };
        samplePuzzlePicture = sample; loadedSampleId = selected.Id; puzzleSampleReady = false;
        sample.UriSource = new Uri("ms-appx:///Assets/" + selected.FileName);
        return sample;
    }

    private void EnsurePuzzlePicture()
    {
        NormalizePuzzleImageMode();
        if (loadedPuzzleKey == PuzzleImageKey) return;
        loadedPuzzleKey = PuzzleImageKey; puzzleImageError = null;
        if (state.PuzzleImageMode == "Numbers") puzzlePicture = null;
        else if (state.PuzzleImageMode == "Custom" && System.IO.File.Exists(state.PuzzlePicture)) LoadPuzzleImage(state.PuzzlePicture);
        else
        {
            puzzlePicture = SamplePuzzlePicture();
            if (state.PuzzleImageMode == "Custom") puzzleImageError = "Picture unavailable · using sample";
        }
    }

    private void SetPuzzleImageMode(string mode)
    {
        state.PuzzleImageMode = mode; loadedPuzzleKey = null;
        RenderPuzzle(); Save();
    }
    private void SelectPuzzleSample(string id)
    { state.PuzzleSampleId = PuzzleSamples.Find(id).Id; SetPuzzleImageMode("Sample"); }

    private void PuzzleFrame_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (classicInitialized && ready && !sidebarClosed && Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 0.5 && IsGadgetOnScreen(PuzzleGadget)) RenderPuzzle();
    }

    private FrameworkElement PuzzleImageTile(int tile, double size, bool labels = true)
    {
        var crop = new Grid { Width = size, Height = size, Clip = new RectangleGeometry { Rect = new Rect(0, 0, size, size) } };
        var canvas = new Canvas { Width = size, Height = size };
        var image = new Image
        {
            Source = puzzlePicture, Width = size * 3, Height = size * 3, Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        // Canvas keeps the full image's layout slot; Grid would clip it before translation.
        Canvas.SetLeft(image, -((tile - 1) % 3) * size); Canvas.SetTop(image, -((tile - 1) / 3) * size);
        canvas.Children.Add(image); crop.Children.Add(canvas);
        if (labels && state.PuzzleNumberLabels && tile != 9)
            crop.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(2), Padding = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(190, 0, 0, 0)),
                Child = new TextBlock { Text = tile.ToString(), FontSize = 10, Foreground = new SolidColorBrush(Colors.White) }
            });
        return crop;
    }

    private void RenderPuzzle()
    {
        if (sidebarClosed || closing) return;
        EnsurePuzzlePicture();
        int cardWidth = (int)Math.Round(PuzzleGadget.ActualWidth > 30 ? PuzzleGadget.ActualWidth : SidebarWidth);
        puzzleRenderWidth = cardWidth; renderedPuzzleRail = state.LonghornMode;
        double available = Math.Clamp(cardWidth - (state.LonghornMode ? 24 : 48), 100, 320);
        if (PuzzleFrame.ActualWidth > 30) available = Math.Min(available, PuzzleFrame.ActualWidth - 6);
        double scale = Root.XamlRoot?.RasterizationScale ?? 1;
        double cell = Math.Max(8, Math.Floor((available - 4) / 3 * scale) / scale), size = cell * 3 + 4;
        bool rebuild = !puzzleRendered || Math.Abs(puzzleCellSize - cell) > 0.01 || !ReferenceEquals(renderedPuzzlePicture, puzzlePicture) || renderedPuzzleLabels != state.PuzzleNumberLabels;
        if (rebuild)
        {
            PuzzleBoard.Children.Clear(); PuzzleBoard.RowDefinitions.Clear(); PuzzleBoard.ColumnDefinitions.Clear(); puzzleButtons.Clear();
            PuzzleBoard.Children.Add(PuzzleGap);
            for (int index = 0; index < 3; index++) { PuzzleBoard.RowDefinitions.Add(new RowDefinition()); PuzzleBoard.ColumnDefinitions.Add(new ColumnDefinition()); }
            puzzleCellSize = cell; renderedPuzzlePicture = puzzlePicture; renderedPuzzleLabels = state.PuzzleNumberLabels;
        }
        puzzleRendered = true; PuzzleBoard.Width = PuzzleBoard.Height = PuzzleStage.Width = PuzzleStage.Height = size;
        int blank = Array.IndexOf(picturePuzzle.Tiles, 0);
        Grid.SetRow(PuzzleGap, blank / 3); Grid.SetColumn(PuzzleGap, blank % 3);
        AutomationProperties.SetName(PuzzleGap, "Empty space");
        for (int index = 0; index < 9; index++)
        {
            int tile = picturePuzzle.Tiles[index];
            if (tile == 0) continue;
            if (!puzzleButtons.TryGetValue(tile, out var button))
            {
                button = new Button
                {
                    Content = puzzlePicture is null ? new TextBlock { Text = tile.ToString(), FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } : PuzzleImageTile(tile, Math.Max(6, cell - 2)),
                    Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
                };
                int number = tile;
                button.Click += (_, _) => MovePuzzleTile(Array.IndexOf(picturePuzzle.Tiles, number));
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(button, true);
                puzzleButtons[tile] = button; PuzzleBoard.Children.Add(button);
            }
            int oldRow = Grid.GetRow(button), oldColumn = Grid.GetColumn(button);
            bool movable = !picturePuzzle.Solved && Math.Abs(index / 3 - blank / 3) + Math.Abs(index % 3 - blank % 3) == 1;
            button.BorderBrush = movable ? CpuBar.Foreground : new SolidColorBrush(Colors.Transparent);
            button.IsTabStop = movable; button.Opacity = puzzlePicture is not null || movable ? 1 : 0.8;
            AutomationProperties.SetName(button, $"Puzzle tile {tile}{(movable ? ", can move" : "")}");
            AutomationProperties.SetHelpText(button, "Arrow keys move the empty space. Enter moves this tile.");
            ToolTipService.SetToolTip(button, movable ? $"Move tile {tile}" : $"Tile {tile}");
            Grid.SetRow(button, index / 3); Grid.SetColumn(button, index % 3);
            if (!rebuild && windowsAppearance.AnimationsEnabled && (oldRow != index / 3 || oldColumn != index % 3))
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(button);
                var move = visual.Compositor.CreateVector3KeyFrameAnimation();
                move.InsertKeyFrame(0, new System.Numerics.Vector3((float)((oldColumn - index % 3) * (cell + 2)), (float)((oldRow - index / 3) * (cell + 2)), 0));
                move.InsertKeyFrame(1, System.Numerics.Vector3.Zero, visual.Compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0), new System.Numerics.Vector2(0, 1)));
                move.Duration = TimeSpan.FromMilliseconds(150); visual.StartAnimation("Translation", move);
            }
        }
        UpdatePuzzleCompletion(size, cell);
        PuzzleStatus.Text = puzzleImageError ?? (picturePuzzle.Solved ? $"Completed · {picturePuzzle.Moves} moves" : $"{picturePuzzle.Moves} {(picturePuzzle.Moves == 1 ? "move" : "moves")}");
        PuzzleStatus.Visibility = state.PuzzleShowMoves || picturePuzzle.Solved || puzzleImageError is not null ? Visibility.Visible : Visibility.Collapsed;
        PuzzleShuffle.Content = picturePuzzle.Solved ? "Play again" : "Shuffle";
        PuzzlePreview.IsEnabled = puzzlePicture is not null && (!ReferenceEquals(puzzlePicture, samplePuzzlePicture) || puzzleSampleReady);
        ToolTipService.SetToolTip(PuzzleBoard, "Slide an outlined tile into the empty space. Arrow keys also move the empty space.");
    }

    private void MovePuzzleTile(int index)
    { if (!picturePuzzle.Solved && picturePuzzle.Move(index)) { PersistPuzzle(); RenderPuzzle(); } }
    private void PersistPuzzle() { state.PuzzleTiles = picturePuzzle.Tiles.ToArray(); state.PuzzleMoves = picturePuzzle.Moves; Save(); }
    private void PuzzleShuffle_Click(object sender, RoutedEventArgs args)
    {
        if (state.PuzzleImageMode == "Sample" && state.PuzzleRotateSamples) state.PuzzleSampleId = PuzzleSamples.Different(state.PuzzleSampleId).Id;
        picturePuzzle.Shuffle(); PersistPuzzle(); RenderPuzzle();
    }

    private void PuzzleBoard_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (picturePuzzle.Solved) return;
        int blank = Array.IndexOf(picturePuzzle.Tiles, 0);
        int target = args.Key switch
        {
            Windows.System.VirtualKey.Left when blank % 3 > 0 => blank - 1,
            Windows.System.VirtualKey.Right when blank % 3 < 2 => blank + 1,
            Windows.System.VirtualKey.Up when blank >= 3 => blank - 3,
            Windows.System.VirtualKey.Down when blank < 6 => blank + 3,
            _ => -1
        };
        if (args.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
        { args.Handled = true; if (target >= 0) MovePuzzleTile(target); }
    }

    private void HoldPuzzlePopup(FlyoutBase popup)
    {
        puzzlePopup?.Hide(); puzzlePopup = popup;
        popup.Opened += (_, _) => hideTimer.Stop();
        popup.Closed += (_, _) => { if (ReferenceEquals(puzzlePopup, popup)) puzzlePopup = null; QueueAutoHide(); };
    }

    private void PuzzlePictureOptions_Click(object sender, RoutedEventArgs args)
    {
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        void Source(string title, string mode)
        {
            var item = new ToggleMenuFlyoutItem { Text = title, IsChecked = state.PuzzleImageMode == mode };
            item.Click += (_, _) => SetPuzzleImageMode(mode); menu.Items.Add(item);
        }
        Source("Sample picture", "Sample");
        var samples = new MenuFlyoutSubItem { Text = "Nature pictures" };
        foreach (var sample in PuzzleSamples.All)
        {
            var item = new ToggleMenuFlyoutItem { Text = sample.Name, IsChecked = state.PuzzleImageMode == "Sample" && state.PuzzleSampleId == sample.Id };
            item.Click += (_, _) => SelectPuzzleSample(sample.Id); samples.Items.Add(item);
        }
        menu.Items.Add(samples);
        if (!string.IsNullOrWhiteSpace(state.PuzzlePicture)) Source("Your picture", "Custom");
        Source("Numbered tiles", "Numbers");
        var rotate = new ToggleMenuFlyoutItem { Text = "Change sample on shuffle", IsChecked = state.PuzzleRotateSamples };
        rotate.Click += (_, _) => { state.PuzzleRotateSamples = rotate.IsChecked; Save(); }; menu.Items.Add(rotate);
        menu.Items.Add(new MenuFlyoutSeparator());
        var choose = new MenuFlyoutItem { Text = "Choose picture…" }; choose.Click += (_, _) => ChoosePuzzlePicture_Click(sender, args); menu.Items.Add(choose);
        HoldPuzzlePopup(menu); menu.ShowAt((FrameworkElement)sender);
    }

    private void PreviewPuzzle_Click(object sender, RoutedEventArgs args)
    {
        EnsurePuzzlePicture(); if (puzzlePicture is null) return;
        var panel = new StackPanel { Width = 220, Spacing = 8, RequestedTheme = Root.RequestedTheme };
        panel.Children.Add(new TextBlock { Text = "Reference picture", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new Image { Source = puzzlePicture, Width = 220, Height = 220, Stretch = Stretch.UniformToFill });
        panel.Children.Add(new TextBlock { Text = state.PuzzleImageMode == "Custom" && puzzleImageError is null ? System.IO.Path.GetFileName(state.PuzzlePicture) : PuzzleSamples.Find(state.PuzzleSampleId).Name, FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
        var style = new Style { TargetType = typeof(FlyoutPresenter) };
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, Root.RequestedTheme));
        style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush(Root.RequestedTheme, 245)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Root.RequestedTheme == ElementTheme.Dark ? Colors.White : Colors.Black)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 244d)); style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 244d));
        var flyout = new Flyout { Content = panel, FlyoutPresenterStyle = style, ShouldConstrainToRootBounds = false, Placement = state.Left ? FlyoutPlacementMode.Right : FlyoutPlacementMode.Left };
        HoldPuzzlePopup(flyout); flyout.ShowAt(PuzzlePreview);
    }

    private async void ChoosePuzzlePicture_Click(object sender, RoutedEventArgs args)
    {
        classicPickerOpen = true; RevealSidebar();
        try
        {
            var picker = new FileOpenPicker(); foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is not null) { state.PuzzlePicture = file.Path; SetPuzzleImageMode("Custom"); }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { PuzzleStatus.Text = "Could not open picture picker"; PuzzleStatus.Visibility = Visibility.Visible; }
        finally { classicPickerOpen = false; RevealSidebar(); QueueAutoHide(); }
    }

    private void LoadPuzzleImage(string path)
    {
        var image = new BitmapImage { DecodePixelWidth = 480 };
        image.ImageFailed += (_, _) =>
        {
            if (sidebarClosed || closing || !ReferenceEquals(puzzlePicture, image)) return;
            puzzlePicture = SamplePuzzlePicture(); puzzleImageError = "Picture unavailable · using sample"; RenderPuzzle();
        };
        puzzlePicture = image; image.UriSource = new Uri(path);
    }
}
