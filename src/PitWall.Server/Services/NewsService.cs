using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace PitWall.Server.Services;

/// <param name="Published">Null when the feed gives no date (formula1.com omits them).</param>
public sealed record NewsItem(string Title, string Url, string Source, DateTimeOffset? Published, string? Summary, string? Image)
{
    /// <summary>Undated items keep their feed order by pretending to be spaced out behind the fetch time.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset SortKey { get; init; }
}

public sealed record NewsFeed(string Source, string Url);

/// <summary>
/// Headlines from public RSS/Atom feeds. Parsed by hand with LINQ to XML because real-world
/// feeds break strict parsers (non-RFC dates, stray HTML in descriptions).
/// </summary>
public sealed partial class NewsService
{
    public static readonly NewsFeed[] DefaultFeeds =
    [
        new("Formula 1", "https://www.formula1.com/en/latest/all.xml"),
        new("Autosport", "https://www.autosport.com/rss/f1/news/"),
        new("Motorsport.com", "https://www.motorsport.com/rss/f1/news/"),
        new("BBC Sport", "https://feeds.bbci.co.uk/sport/formula1/rss.xml"),
        new("RaceFans", "https://www.racefans.net/feed/"),
    ];

    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private readonly HttpClient _http;
    private readonly ILogger<NewsService> _logger;
    private readonly CachedValue<IReadOnlyList<NewsItem>> _cache;

    public NewsService(HttpClient http, ILogger<NewsService> logger)
    {
        _http = http;
        _logger = logger;
        _cache = new CachedValue<IReadOnlyList<NewsItem>>(TimeSpan.FromMinutes(10), LoadAsync);
    }

    public async Task<IReadOnlyList<NewsItem>> GetAsync(CancellationToken cancellation) =>
        await _cache.GetAsync(cancellation) ?? [];

    private async Task<IReadOnlyList<NewsItem>> LoadAsync(CancellationToken cancellation)
    {
        var results = await Task.WhenAll(DefaultFeeds.Select(feed => ReadFeedAsync(feed, cancellation)));
        var items = results
            .SelectMany(r => r)
            .GroupBy(i => Normalise(i.Title))
            .Select(g => g.OrderByDescending(i => i.Image is not null).First())
            .OrderByDescending(i => i.SortKey)
            .Take(80)
            .ToList();

        if (items.Count == 0)
        {
            throw new InvalidOperationException("No news feed could be read.");
        }
        return items;
    }

    private async Task<IReadOnlyList<NewsItem>> ReadFeedAsync(NewsFeed feed, CancellationToken cancellation)
    {
        try
        {
            await using var stream = await _http.GetStreamAsync(feed.Url, cancellation);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, Async = true, XmlResolver = null };
            using var reader = XmlReader.Create(stream, settings);
            var doc = await XDocument.LoadAsync(reader, LoadOptions.None, cancellation);
            return Parse(doc, feed.Source).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException)
        {
            _logger.LogWarning("News feed {Source} unavailable: {Message}", feed.Source, ex.Message);
            return [];
        }
    }

    internal static IEnumerable<NewsItem> Parse(XDocument doc, string source)
    {
        var now = DateTimeOffset.UtcNow;
        var order = 0;
        var rssItems = doc.Descendants("item");
        foreach (var item in rssItems)
        {
            var title = Clean(item.Element("title")?.Value);
            var link = item.Element("link")?.Value?.Trim();
            if (title is null || !IsWebUrl(link))
            {
                continue;
            }

            var image = item.Elements(Media + "content").Select(e => (string?)e.Attribute("url")).FirstOrDefault(IsWebUrl)
                ?? item.Elements(Media + "thumbnail").Select(e => (string?)e.Attribute("url")).FirstOrDefault(IsWebUrl)
                ?? item.Elements(Media + "group").Elements(Media + "content").Select(e => (string?)e.Attribute("url")).FirstOrDefault(IsWebUrl)
                ?? item.Elements("enclosure")
                    .Where(e => ((string?)e.Attribute("type"))?.StartsWith("image", StringComparison.OrdinalIgnoreCase) ?? true)
                    .Select(e => (string?)e.Attribute("url")).FirstOrDefault(IsWebUrl);

            var published = ParseDate(item.Element("pubDate")?.Value);
            yield return new NewsItem(title, link!, source, published, Summarise(item.Element("description")?.Value), image)
            {
                SortKey = published ?? now - TimeSpan.FromMinutes(30 * order),
            };
            order++;
        }

        foreach (var entry in doc.Descendants(Atom + "entry"))
        {
            var title = Clean(entry.Element(Atom + "title")?.Value);
            var link = entry.Elements(Atom + "link")
                .Where(l => (string?)l.Attribute("rel") is null or "alternate")
                .Select(l => (string?)l.Attribute("href")).FirstOrDefault(IsWebUrl);
            if (title is null || link is null)
            {
                continue;
            }
            var published = ParseDate(entry.Element(Atom + "published")?.Value ?? entry.Element(Atom + "updated")?.Value);
            yield return new NewsItem(title, link, source, published, Summarise(entry.Element(Atom + "summary")?.Value), null)
            {
                SortKey = published ?? now - TimeSpan.FromMinutes(30 * order),
            };
            order++;
        }
    }

    private static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var stripped = WebUtility.HtmlDecode(Tags().Replace(text, " "));
        return Spaces().Replace(stripped, " ").Trim();
    }

    private static string? Summarise(string? html)
    {
        var text = Clean(html);
        if (text is null)
        {
            return null;
        }
        if (text.Length <= 240)
        {
            return text;
        }
        var cut = text.LastIndexOf(' ', 237);
        return text[..(cut < 120 ? 237 : cut)] + "…";
    }

    private static string Normalise(string title) => Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]", "");

    private static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var value = text.Trim().Replace(" GMT", " +0000").Replace(" UT", " +0000");
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        // "Fri, 11 Sep 2026 13:14:07 +0100": drop the weekday, which some feeds get wrong.
        var comma = value.IndexOf(',');
        return comma > 0 && DateTimeOffset.TryParseExact(value[(comma + 1)..].Trim(), "d MMM yyyy HH:mm:ss zzz",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
            ? parsed
            : null;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
