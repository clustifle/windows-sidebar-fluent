using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Sidebar;

/// <summary>Downloads and validates small, provider-hosted favicons for Website Quick Launch pins.</summary>
internal static class WebsiteIcons
{
    private const int MaxIconBytes = 512 * 1024;
    private const int MaxHtmlBytes = 256 * 1024;
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim Requests = new(2, 2);
    private static readonly ConcurrentDictionary<string, Task<string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Task<string> GetEmbeddedAsync(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(address.UserInfo))
            return Task.FromResult("");

        return Cache.GetOrAdd(address.GetLeftPart(UriPartial.Authority), _ => FetchAsync(address));
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 4,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = false
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) FluentSidebar/2.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html", 0.8));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*", 0.9));
        return client;
    }

    private static async Task<string> FetchAsync(Uri address)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await Requests.WaitAsync();
        try
        {
            Uri origin = new(address.GetLeftPart(UriPartial.Authority) + "/");
            Uri faviconBase = origin;
            Uri? declaredIcon = null;
            try
            {
                using var response = await Client.GetAsync(origin, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                faviconBase = response.RequestMessage?.RequestUri ?? origin;
                if (response.IsSuccessStatusCode &&
                    response.Content.Headers.ContentType?.MediaType is string mediaType &&
                    (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("xhtml", StringComparison.OrdinalIgnoreCase)))
                {
                    byte[] html = await ReadLimitedAsync(response.Content, MaxHtmlBytes, timeout.Token);
                    declaredIcon = FindDeclaredIcon(Encoding.UTF8.GetString(html), response.RequestMessage?.RequestUri ?? origin);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) { }

            Uri fallback = new(faviconBase, "favicon.ico");
            if (declaredIcon is not null)
            {
                string? embedded = await TryFetchIconAsync(declaredIcon, timeout.Token);
                if (!string.IsNullOrEmpty(embedded)) return embedded;
            }
            return await TryFetchIconAsync(fallback, timeout.Token) ?? "";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException or OperationCanceledException or RegexMatchTimeoutException)
        {
            return "";
        }
        finally { Requests.Release(); }
    }

    private static Uri? FindDeclaredIcon(string html, Uri document)
    {
        foreach (Match link in Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            string tag = link.Value;
            string rel = Attribute(tag, "rel");
            if (!rel.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(value =>
                value.Equals("icon", StringComparison.OrdinalIgnoreCase) || value.Equals("shortcut", StringComparison.OrdinalIgnoreCase))) continue;

            string href = WebUtility.HtmlDecode(Attribute(tag, "href")).Trim();
            if (href.Length == 0 || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(document, href, out Uri? icon) ||
                (icon.Scheme != Uri.UriSchemeHttps && icon.Scheme != Uri.UriSchemeHttp) ||
                !string.IsNullOrEmpty(icon.UserInfo) || icon.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            return icon;
        }
        return null;
    }

    private static string Attribute(string tag, string name)
    {
        Match match = Regex.Match(tag, $"\\b{Regex.Escape(name)}\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s/>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value : "";
    }

    private static async Task<string?> TryFetchIconAsync(Uri icon, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(icon, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            if (icon.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                (mediaType?.Contains("svg", StringComparison.OrdinalIgnoreCase) ?? false)) return null;

            byte[] bytes = await ReadLimitedAsync(response.Content, MaxIconBytes, cancellationToken);
            if (bytes.Length == 0) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            stream.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || decoder.PixelWidth > 4096 || decoder.PixelHeight > 4096 ||
                (ulong)decoder.PixelWidth * decoder.PixelHeight > 16_000_000) return null;
            return Convert.ToBase64String(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or FormatException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > limit) return [];
        await using Stream input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > limit) return [];
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
