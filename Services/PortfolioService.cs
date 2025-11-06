using Microsoft.Extensions.Options;
using TradingPlatform.Models;
using TradingPlatform.Services.Abstractions;

namespace TradingPlatform.Services;

public class PortfolioService : IPortfolioService
{
    private readonly IStorageService _storage;
    private readonly StorageOptions _opt;
    private readonly IPriceProvider _prices;

    public Portfolio Portfolio { get; private set; } = new();

    public PortfolioService(IStorageService storage, IOptions<StorageOptions> opt, IPriceProvider prices)
    {
        _storage = storage;
        _opt = opt.Value;
        _prices = prices;
    }

    /// <summary>
    /// Charge depuis le fichier. Si absent → portefeuille vide, sauvegardé immédiatement
    /// pour créer le fichier et éviter tout "seed" implicite.
    /// </summary>
    public async Task LoadAsync()
    {
        var loaded = await _storage.LoadAsync<Portfolio>(_opt.PortfolioFile);
        if (loaded is not null)
        {
            Portfolio = loaded;
            PruneZeroQty(Portfolio);
        }
        else
        {
            Portfolio = new Portfolio();
            await SaveAsync(); // crée le fichier vide
        }
    }

    public Task SaveAsync() => _storage.SaveAsync(_opt.PortfolioFile, Portfolio);

    /// <summary>
    /// Applique le trade en mémoire, supprime les positions à 0 et sauvegarde (fire-and-forget).
    /// Signature HISTORIQUE conservée (void).
    /// </summary>
    public void AddTrade(Trade trade)
    {
        Portfolio.Apply(trade);
        PruneZeroQty(Portfolio);
        _ = SaveAsync(); // on ne bloque pas l’UI
    }

    /// <summary>
    /// Derniers prix pour les tickers détenus actuellement (utilise IPriceProvider).
    /// </summary>
    public async Task<Dictionary<string, decimal>> GetLastPricesAsync()
    {
        var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Portfolio.Positions)
        {
            var last = await _prices.GetLastPriceAsync(p.Security.Ticker);
            if (last is decimal d) dict[p.Security.Ticker] = d;
        }
        return dict;
    }

    private static void PruneZeroQty(Portfolio p)
    {
        if (p.Positions.Count == 0) return;
        p.Positions = p.Positions.Where(x => x.Quantity != 0).ToList();
    }
}
