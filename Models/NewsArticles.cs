namespace TradingPlatform.Models;

public class NewsArticle
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? UrlToImage { get; set; }
    public DateTime PublishedAt { get; set; }
    public string? SourceName { get; set; }
}
