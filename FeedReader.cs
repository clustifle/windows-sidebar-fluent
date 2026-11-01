using System.Net;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Sidebar;

internal sealed record FeedHeadline(string Title, Uri Link, Uri? Thumbnail = null);
internal static class FeedReader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const int Limit = 2 * 1024 * 1024;
    public static async Task<IReadOnlyList<FeedHeadline>> FetchAsync(Uri uri, CancellationToken token, int count = 5)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > Limit) throw new InvalidDataException("Feed is too large.");
        using var source = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            if (memory.Length + read > Limit) throw new InvalidDataException("Feed is too large.");
            memory.Write(buffer, 0, read);
        }
        memory.Position = 0;
        return await Task.Run(() => Parse(memory, response.RequestMessage?.RequestUri ?? uri, count), token);
    }
    internal static IReadOnlyList<FeedHeadline> Parse(Stream source, Uri baseUri, int count = 5)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = Limit });
        var document = XDocument.Load(reader);
        var headlines = new List<FeedHeadline>();
        foreach (var entry in document.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string title = WebUtility.HtmlDecode(entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "").Trim();
            var linkNode = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && (e.Attribute("rel") is null || e.Attribute("rel")?.Value == "alternate"));
            string link = linkNode?.Attribute("href")?.Value ?? linkNode?.Value ?? "";
            var uri = WebUri(link, ElementBase(linkNode ?? entry, baseUri));
            if (title.Length > 0 && uri is not null) headlines.Add(new(title, uri, FindThumbnail(entry, baseUri, uri)));
            if (headlines.Count == Math.Clamp(count, 1, 10)) break;
        }
        return headlines;
    }

    internal static Uri? WebUri(string? value, Uri basis)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 && Uri.TryCreate(basis, WebUtility.HtmlDecode(value.Trim()), out var uri)
            && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;

    private static Uri ElementBase(XElement element, Uri basis)
    {
        foreach (var ancestor in element.AncestorsAndSelf().Reverse())
            if (ancestor.Attribute(XNamespace.Xml + "base") is { } attribute && WebUri(attribute.Value, basis) is { } resolved) basis = resolved;
        return basis;
    }

    private static bool VisibleImage(XElement element)
        => !(int.TryParse(element.Attribute("width")?.Value, out int width) && width <= 1)
            && !(int.TryParse(element.Attribute("height")?.Value, out int height) && height <= 1);

    private static Uri? FindThumbnail(XElement entry, Uri basis, Uri article)
    {
        bool Media(XElement e) => e.Name.NamespaceName.TrimEnd('/') == "http://search.yahoo.com/mrss";
        bool ImageType(XElement e) => e.Attribute("type")?.Value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
        var candidates = entry.Descendants().Where(e => Media(e) && e.Name.LocalName == "thumbnail")
            .Concat(entry.Descendants().Where(e => Media(e) && e.Name.LocalName == "content" && (ImageType(e) || e.Attribute("medium")?.Value == "image")))
            .Concat(entry.Elements().Where(e => e.Name.LocalName == "enclosure" && ImageType(e) || e.Name.LocalName == "link" && e.Attribute("rel")?.Value == "enclosure" && ImageType(e)));
        foreach (var element in candidates)
            if (VisibleImage(element) && WebUri(element.Attribute("url")?.Value ?? element.Attribute("href")?.Value, ElementBase(element, basis)) is { } image) return image;
        foreach (var element in entry.Elements().Where(e => e.Name.LocalName is "description" or "encoded" or "content" or "summary"))
        {
            foreach (var image in element.Descendants().Where(e => e.Name.LocalName == "img" && VisibleImage(e)))
                if (WebUri(image.Attribute("src")?.Value ?? image.Attribute("data-src")?.Value, ElementBase(image, basis)) is { } nested) return nested;
            try
            {
                // Read only image attributes; feed HTML is never rendered or executed.
                foreach (Match tag in Regex.Matches(element.Value, "<img\\b[^>]{0,8192}>", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)))
                {
                    var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match attr in Regex.Matches(tag.Value, "(?<name>[\\w-]+)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))", RegexOptions.None, TimeSpan.FromMilliseconds(50)))
                        attrs[attr.Groups["name"].Value] = attr.Groups["value"].Value;
                    if (attrs.TryGetValue("width", out var w) && int.TryParse(w, out int width) && width <= 1 || attrs.TryGetValue("height", out var h) && int.TryParse(h, out int height) && height <= 1) continue;
                    var imageBase = element.AncestorsAndSelf().Any(e => e.Attribute(XNamespace.Xml + "base") is not null) ? ElementBase(element, basis) : article;
                    if (WebUri(attrs.GetValueOrDefault("src") ?? attrs.GetValueOrDefault("data-src"), imageBase) is { } image) return image;
                }
            }
            catch (RegexMatchTimeoutException) { }
        }
        return null;
    }
}
