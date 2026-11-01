using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace Sidebar;

public partial class MainWindow
{
    private bool classicInitialized, classicPickerOpen, slideLoading, slidePaused, feedLoading, puzzleRendered;
    private readonly DispatcherTimer slideTimer = new();
    private readonly SlidingPuzzle picturePuzzle = new();
    private readonly CancellationTokenSource classicCancellation = new();
    private string[] slideFiles = [];
    private string loadedSlideFolder = "";
    private int slideIndex;
    private DateTime nextFeedRefresh;

    private void InitializeClassicGadgets()
    {
        classicInitialized = true;
        slidePaused = state.SlidePaused;
        SlidePlay.Content = slidePaused ? "Play" : "Pause";
        picturePuzzle.Restore(state.PuzzleTiles, state.PuzzleMoves);
        NormalizePuzzleImageMode();
        slideTimer.Tick += (_, _) => { if (SlideShowGadget.Visibility == Visibility.Visible && slideFiles.Length > 0) { slideIndex = (slideIndex + 1) % slideFiles.Length; ShowSlide(); } };
        Closed += (_, _) => { slideTimer.Stop(); CancelFeedImages(); classicCancellation.Cancel(); classicCancellation.Dispose(); };
    }

    private void UpdateClassicGadgets()
    {
        if (!classicInitialized || sidebarClosed) return;
        if (IsGadgetOnScreen(SlideShowGadget))
        {
            if (state.SlideFolder != loadedSlideFolder && !slideLoading) _ = LoadSlideFolder();
            slideTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(state.SlideInterval, 5, 120));
            if (!slidePaused && slideFiles.Length > 1) slideTimer.Start(); else slideTimer.Stop();
        }
        else slideTimer.Stop();
        int puzzleWidth = (int)Math.Round(PuzzleGadget.ActualWidth > 30 ? PuzzleGadget.ActualWidth : SidebarWidth);
        if (IsGadgetOnScreen(PuzzleGadget) && (!puzzleRendered || puzzleRenderWidth != puzzleWidth || renderedPuzzleRail != state.LonghornMode || loadedPuzzleKey != PuzzleImageKey || renderedPuzzleLabels != state.PuzzleNumberLabels))
        {
            RenderPuzzle();
        }
        if (IsGadgetOnScreen(FeedsGadget) && !feedLoading && DateTime.UtcNow >= nextFeedRefresh && !string.IsNullOrWhiteSpace(state.FeedUrl)) _ = RefreshFeed(false);
        UpdateFeedThumbnails();
    }

    private async Task LoadSlideFolder()
    {
        slideLoading = true;
        string folder = state.SlideFolder;
        try
        {
            var files = await Task.Run(() => System.IO.Directory.Exists(folder) ? System.IO.Directory.EnumerateFiles(folder).Where(p => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())).Take(500).Order(StringComparer.CurrentCultureIgnoreCase).ToArray() : []);
            if (folder != state.SlideFolder || sidebarClosed) return;
            slideFiles = files;
            loadedSlideFolder = folder; slideIndex = 0;
            if (!sidebarClosed) { ShowSlide(); UpdateClassicGadgets(); }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { loadedSlideFolder = folder; slideFiles = []; SlideHint.Text = "Folder unavailable"; SlideHint.Visibility = Visibility.Visible; }
        finally { slideLoading = false; }
    }

    private void ShowSlide()
    {
        SlidePlay.IsEnabled = slideFiles.Length > 1;
        if (slideFiles.Length == 0) { SlideImage.Source = null; SlideName.Text = ""; SlideHint.Text = string.IsNullOrEmpty(state.SlideFolder) ? "Choose a picture folder" : "No pictures in this folder"; SlideHint.Visibility = Visibility.Visible; return; }
        var image = new BitmapImage { DecodePixelWidth = 480 };
        image.ImageOpened += (_, _) => { if (ReferenceEquals(SlideImage.Source, image)) SlideHint.Visibility = Visibility.Collapsed; };
        image.ImageFailed += (_, _) => { if (ReferenceEquals(SlideImage.Source, image)) { SlideHint.Text = "Picture unavailable"; SlideHint.Visibility = Visibility.Visible; } };
        SlideImage.Source = image; image.UriSource = new Uri(slideFiles[slideIndex]);
        SlideName.Text = System.IO.Path.GetFileName(slideFiles[slideIndex]);
    }
    private void SlidePrevious_Click(object sender, RoutedEventArgs e) { if (slideFiles.Length > 0) { slideIndex = (slideIndex - 1 + slideFiles.Length) % slideFiles.Length; ShowSlide(); } }
    private void SlideNext_Click(object sender, RoutedEventArgs e) { if (slideFiles.Length > 0) { slideIndex = (slideIndex + 1) % slideFiles.Length; ShowSlide(); } }
    private void SlidePlay_Click(object sender, RoutedEventArgs e) { slidePaused = !slidePaused; state.SlidePaused = slidePaused; SlidePlay.Content = slidePaused ? "Play" : "Pause"; UpdateClassicGadgets(); Save(); }

    private async void ChooseSlideFolder_Click(object sender, RoutedEventArgs e)
    {
        classicPickerOpen = true; RevealSidebar();
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) { state.SlideFolder = folder.Path; Save(); await LoadSlideFolder(); }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { SlideHint.Text = "Could not open folder picker"; SlideHint.Visibility = Visibility.Visible; }
        finally { classicPickerOpen = false; RevealSidebar(); QueueAutoHide(); }
    }


    private async void ConfigureFeed_Click(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) { ActivatePreferences(); return; }
        var panel = new StackPanel { Spacing = 12 };
        var url = new TextBox { Header = "RSS or Atom feed address", PlaceholderText = "https://…", Text = state.FeedUrl };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var save = new Button { Content = "Use this feed" };
        save.Click += async (_, _) =>
        {
            if (!Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) { status.Text = "Enter a complete http or https feed address."; return; }
            state.FeedUrl = uri.AbsoluteUri; Save(); status.Text = "Feed saved"; await RefreshFeed(true);
        };
        panel.Children.Add(url); panel.Children.Add(save); panel.Children.Add(status);
        panel.Children.Add(new TextBlock { Text = "Headlines refresh every 15 minutes while this gadget is visible.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        await ShowDialog("Feed headlines", panel);
    }
    private async void RefreshFeed_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(state.FeedUrl)) { ConfigureFeed_Click(sender, e); return; }
        await RefreshFeed(true);
    }
    private async Task RefreshFeed(bool force)
    {
        if (feedLoading || !Uri.TryCreate(state.FeedUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        if (!force && DateTime.UtcNow < nextFeedRefresh) return;
        feedLoading = true; FeedStatus.Text = "Updating…";
        try
        {
            var headlines = await FeedReader.FetchAsync(uri, classicCancellation.Token, state.FeedCount);
            if (sidebarClosed || state.FeedUrl != uri.AbsoluteUri) return;
            RenderFeedHeadlines(headlines);
            FeedStatus.Text = headlines.Count == 0 ? "No headlines found" : $"Updated {DateTime.Now:t}";
            nextFeedRefresh = DateTime.UtcNow.AddMinutes(Math.Clamp(state.FeedRefreshMinutes, 5, 120));
        }
        catch (OperationCanceledException) { if (!sidebarClosed) { FeedStatus.Text = "Feed request timed out"; nextFeedRefresh = DateTime.UtcNow.AddMinutes(5); } }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or System.Xml.XmlException)
        { if (!sidebarClosed) { FeedStatus.Text = "Could not update this feed"; nextFeedRefresh = DateTime.UtcNow.AddMinutes(5); } }
        finally { feedLoading = false; }
    }
}
