using TradingPlatform.Models;
using TradingPlatform.Services.Abstractions;

namespace TradingPlatform.Services;

public class WatchlistService
{
    private readonly IStorageService _storage;
    private const string FilePath = "App_Data/watchlist.json";

    public List<WatchlistItem> Items { get; private set; } = new();

    public WatchlistService(IStorageService storage) => _storage = storage;

    public async Task LoadAsync() =>
        Items = await _storage.LoadAsync<List<WatchlistItem>>(FilePath) ?? new();

    public Task SaveAsync() => _storage.SaveAsync(FilePath, Items);

    public void Add(string ticker, string? note = null)
    {
        var t = SymbolHelper.Normalize(ticker);
        if (Items.Any(i => i.Ticker.Equals(t, StringComparison.OrdinalIgnoreCase))) return;
        Items.Add(new WatchlistItem { Ticker = t, Note = note });
    }

    public void Remove(string ticker) =>
        Items.RemoveAll(i => i.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));
}
