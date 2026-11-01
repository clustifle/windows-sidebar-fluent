#if THEME_REGRESSION
using System.Text;
namespace Sidebar;
public partial class MainWindow
{
    private sealed class ThumbnailFixtureHandler : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var content = new ByteArrayContent(request.RequestUri!.AbsolutePath == "/valid" ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aG2kAAAAASUVORK5CYII=") : new byte[10]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.RequestUri.AbsolutePath == "/html" ? "text/html" : "image/png");
            if (request.RequestUri.AbsolutePath == "/large") content.Headers.ContentLength = FeedThumbnailCache.DownloadLimit + 1;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    private static async Task VerifyThumbnailDownloads()
    {
        using var handler = new ThumbnailFixtureHandler(); using var client = new HttpClient(handler);
        var cache = new FeedThumbnailCache(client);
        var uri = new Uri("https://fixture.example.test/valid");
        var source = await cache.GetAsync(uri, CancellationToken.None);
        if (source is null || !ReferenceEquals(source, await cache.GetAsync(uri, CancellationToken.None)) || handler.Requests != 1) throw new Exception("Thumbnail decode/cache failed");
        foreach (string path in new[] { "/html", "/large" })
            if (await cache.GetAsync(new Uri(uri, path), CancellationToken.None) is not null) throw new Exception("Unsupported or oversized thumbnail accepted");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await cache.GetAsync(new Uri(uri, "/canceled"), cancellation.Token); throw new Exception("Canceled thumbnail was downloaded"); }
        catch (OperationCanceledException) { }
    }

    private async Task VerifyFeedCardAsync()
    {
        state.LonghornMode = false; state.Width = 240; state.Theme = "Light"; state.FeedHeadlines = false; state.FeedUrl = "";
        ApplyState();
        RenderFeedHeadlines([new("A quiet morning beside the lake", new Uri("https://example.test/one"), new Uri("https://example.test/image")), new("A headline without a supplied image stays text-only", new Uri("https://example.test/two"))]);
        // Use our own sample as a deterministic visual fixture; no external request is made.
        foreach (var row in feedImageRows) { row.Finished = true; row.Image.Source = samplePuzzlePicture; row.Frame.Visibility = Microsoft.UI.Xaml.Visibility.Visible; }
        state.FeedHeadlines = true; state.GadgetOrder.Remove("Feed headlines"); state.GadgetOrder.Insert(0, "Feed headlines");
        state.PageIndex = 0; await UpdateLonghornAsync(); GadgetScroller.ChangeView(null, 0, null); await Task.Delay(300);
        if (feedImageRows.Count != 1 || feedImageRows[0].Frame.ActualWidth != 44 || FeedItems.ActualWidth > FeedsGadget.ActualWidth) throw new Exception("Feed thumbnail card layout overflowed");
        await CaptureRailPreviewAsync("feed-thumbnails.png", FeedsGadget);
        state.FeedShowThumbnails = false; RenderFeedHeadlines(displayedHeadlines);
        if (feedImageRows.Count != 0 || FeedItems.Children.Count != 2) throw new Exception("Feed image setting removed headlines");
    }

    private static void VerifyFeedThumbnails()
    {
        void Check(string xml, string? expected)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            var row = FeedReader.Parse(stream, new Uri("https://feeds.example.test/rss.xml")).Single();
            if (row.Thumbnail?.AbsoluteUri != expected) throw new Exception("Feed thumbnail resolution failed: " + row.Thumbnail);
        }
        string Rss(string image) => "<rss xmlns:media='http://search.yahoo.com/mrss/'><channel><item><title>Article</title><link>https://news.example.test/posts/story</link>" + image + "</item></channel></rss>";
        Check(Rss("<media:thumbnail url='/photo.jpg'/ >".Replace("/ >", "/>")), "https://feeds.example.test/photo.jpg");
        Check(Rss("<media:content medium='image' url='photo.png'/>"), "https://feeds.example.test/photo.png");
        Check(Rss("<enclosure type='image/jpeg' url='https://cdn.example.test/a.jpg'/>"), "https://cdn.example.test/a.jpg");
        Check(Rss("<description><![CDATA[<img width='1' height='1' src='tracker.gif'><img src='../nature.jpg'>]]></description>"), "https://news.example.test/nature.jpg");
        Check(Rss("<media:thumbnail url='file:///C:/secret'/><description><![CDATA[<img src='javascript:alert(1)'>]]></description>"), null);
        Check("<feed xmlns='http://www.w3.org/2005/Atom' xml:base='https://news.example.test/base/'><entry><title>Atom</title><link href='article'/><link rel='enclosure' type='image/png' href='picture.png'/></entry></feed>", "https://news.example.test/base/picture.png");
        Check(Rss("<description xml:base='https://cdn.example.test/images/'><![CDATA[<img src='photo.jpg'>]]></description>"), "https://cdn.example.test/images/photo.jpg");
        Check(Rss(""), null);
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///C:/secret'>]><rss/>"));
            FeedReader.Parse(stream, new Uri("https://example.test/")); throw new Exception("DTD was accepted");
        }
        catch (System.Xml.XmlException) { }
    }
}
#endif
