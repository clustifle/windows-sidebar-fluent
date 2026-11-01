using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Sidebar;

public partial class MainWindow
{
    private sealed class FeedImageRow(Uri uri, Image image, Border frame)
    {
        internal readonly Uri Uri = uri;
        internal readonly Image Image = image;
        internal readonly Border Frame = frame;
        internal bool Loading, Finished;
    }
    private readonly FeedThumbnailCache feedThumbnailCache = new();
    private readonly List<FeedImageRow> feedImageRows = new();
    private IReadOnlyList<FeedHeadline> displayedHeadlines = [];
    private CancellationTokenSource? feedImageCancellation;

    private void CancelFeedImages()
    { feedImageCancellation?.Cancel(); feedImageCancellation?.Dispose(); feedImageCancellation = null; }

    private void RenderFeedHeadlines(IReadOnlyList<FeedHeadline> headlines)
    {
        CancelFeedImages(); displayedHeadlines = headlines; feedImageRows.Clear(); FeedItems.Children.Clear();
        foreach (var headline in headlines)
        {
            var row = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (state.FeedShowThumbnails && headline.Thumbnail is { } uri)
            {
                var image = new Image { Width = 44, Height = 44, Stretch = Stretch.UniformToFill };
                AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                var frame = new Border { Child = image, Width = 44, Height = 44, CornerRadius = new CornerRadius(4), Visibility = Visibility.Collapsed };
                row.Children.Add(frame); feedImageRows.Add(new(uri, image, frame));
            }
            var title = new TextBlock { Text = headline.Title, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1); row.Children.Add(title);
            var button = new HyperlinkButton { NavigateUri = headline.Link, Content = row, Padding = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, headline.Title); FeedItems.Children.Add(button);
        }
        UpdateFeedThumbnails();
    }

    private void UpdateFeedThumbnails()
    {
        if (sidebarClosed || closing || !state.FeedShowThumbnails || !IsGadgetOnScreen(FeedsGadget)) { CancelFeedImages(); return; }
        feedImageCancellation ??= CancellationTokenSource.CreateLinkedTokenSource(classicCancellation.Token);
        foreach (var row in feedImageRows)
            if (!row.Loading && !row.Finished) _ = LoadFeedThumbnail(row, feedImageCancellation.Token);
    }

    private async Task LoadFeedThumbnail(FeedImageRow row, CancellationToken token)
    {
        row.Loading = true;
        try
        {
            var source = await feedThumbnailCache.GetAsync(row.Uri, token);
            if (token.IsCancellationRequested || sidebarClosed || closing || !feedImageRows.Contains(row)) return;
            if (source is not null) { row.Image.Source = source; row.Frame.Visibility = Visibility.Visible; }
            row.Finished = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or System.Runtime.InteropServices.COMException or ArgumentException)
        { row.Finished = true; }
        finally { row.Loading = false; }
    }
}
