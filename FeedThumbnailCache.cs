using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Sidebar;

// UI-thread owned, memory-only cache. At most two bounded downloads and 16 small decoded images.
internal sealed class FeedThumbnailCache
{
    private static readonly HttpClient Client = new(new HttpClientHandler { UseCookies = false, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim slots = new(2);
    private readonly Dictionary<string, SoftwareBitmapSource> cache = new();
    private readonly LinkedList<string> recent = new();
    private readonly HttpClient client;
    internal const int DownloadLimit = 2 * 1024 * 1024;
    internal FeedThumbnailCache(HttpClient? client = null) => this.client = client ?? Client;

    internal async Task<SoftwareBitmapSource?> GetAsync(Uri uri, CancellationToken token)
    {
        string key = uri.AbsoluteUri;
        if (cache.TryGetValue(key, out var cached)) { recent.Remove(key); recent.AddLast(key); return cached; }
        await slots.WaitAsync(token);
        try
        {
            if (cache.TryGetValue(key, out cached)) return cached;
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType;
            if (response.Content.Headers.ContentLength > DownloadLimit || type is not null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && type != "application/octet-stream") return null;
            using var source = await response.Content.ReadAsStreamAsync(token);
            using var stream = new InMemoryRandomAccessStream();
            using (var output = stream.AsStreamForWrite())
            {
                byte[] buffer = new byte[8192]; int read, total = 0;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                { total += read; if (total > DownloadLimit) return null; await output.WriteAsync(buffer.AsMemory(0, read), token); }
                await output.FlushAsync(token);
                stream.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
                if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 8192 || decoder.PixelHeight > 8192 || (ulong)decoder.PixelWidth * decoder.PixelHeight > 8_000_000) return null;
                double scale = Math.Min(1, 192d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
                var transform = new BitmapTransform { ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale)), ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale)) };
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
                token.ThrowIfCancellationRequested();
                var image = new SoftwareBitmapSource(); await image.SetBitmapAsync(bitmap);
                token.ThrowIfCancellationRequested();
                cache[key] = image; recent.Remove(key); recent.AddLast(key);
                while (recent.Count > 16) { cache.Remove(recent.First!.Value); recent.RemoveFirst(); }
                return image;
            }
        }
        finally { slots.Release(); }
    }
}
