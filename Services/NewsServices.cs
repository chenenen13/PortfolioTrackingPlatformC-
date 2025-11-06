using NewsAPI;                 // NewsApiClient
using NewsAPI.Models;          // EverythingRequest, Article
using NewsAPI.Constants;       // Statuses, SortBys, Languages
using TradingPlatform.Models;
using Microsoft.Extensions.Options;

namespace TradingPlatform.Services;

public class NewsService
{
    private readonly string _apiKey;
    private readonly NewsApiClient _client;

    public NewsService(IOptions<NewsOptions> opt)
    {
        _apiKey = opt.Value.ApiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("News API key is missing. Set News:ApiKey in appsettings.json.");

        _client = new NewsApiClient(_apiKey);
    }

    public async Task<List<NewsArticle>> GetNewsAsync(string keyword = "markets")
    {
        try
        {
            // The library is sync; wrap in Task.Run to keep Blazor UI responsive
            var response = await Task.Run(() =>
                _client.GetEverything(new EverythingRequest
                {
                    Q = string.IsNullOrWhiteSpace(keyword) ? "markets" : keyword,
                    Language = Languages.EN,
                    SortBy = SortBys.PublishedAt,
                    PageSize = 10
                }));

            if (response.Status == Statuses.Ok && response.Articles != null)
            {
                return response.Articles
                    .Select(a => new NewsArticle
                    {
                        Title       = a.Title,
                        Description = a.Description,
                        Url         = a.Url,
                        UrlToImage  = a.UrlToImage,
                        PublishedAt = a.PublishedAt ?? DateTime.Now,
                        SourceName  = a.Source?.Name
                    })
                    .ToList();
            }

            return new();
        }
        catch
        {
            // swallow errors → empty list (page shows "No articles found")
            return new();
        }
    }
}
