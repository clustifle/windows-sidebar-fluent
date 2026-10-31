using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace Sidebar;

internal sealed record FeedHeadline(string Title, Uri Link);
internal static class FeedReader
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const int Limit = 2 * 1024 * 1024;
    public static async Task<IReadOnlyList<FeedHeadline>> FetchAsync(Uri uri, CancellationToken token)
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
        return await Task.Run(() => Parse(memory, response.RequestMessage?.RequestUri ?? uri), token);
    }
    internal static IReadOnlyList<FeedHeadline> Parse(Stream source, Uri baseUri)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = Limit });
        var document = XDocument.Load(reader);
        var headlines = new List<FeedHeadline>();
        foreach (var entry in document.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string title = WebUtility.HtmlDecode(entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "").Trim();
            var linkNode = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && (e.Attribute("rel") is null || e.Attribute("rel")?.Value == "alternate"));
            string link = linkNode?.Attribute("href")?.Value ?? linkNode?.Value ?? "";
            if (title.Length > 0 && !string.IsNullOrWhiteSpace(link) && Uri.TryCreate(baseUri, link.Trim(), out var uri) && uri.Scheme is "http" or "https") headlines.Add(new(title, uri));
            if (headlines.Count == 5) break;
        }
        return headlines;
    }
}
