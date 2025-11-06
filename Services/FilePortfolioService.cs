using System.Text.Json;
using Microsoft.Extensions.Hosting;
using TradingPlatform.Models;
using TradingPlatform.Services.Abstractions;

namespace TradingPlatform.Services;

public sealed class FilePortfolioService : IPortfolioService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _io = new(1, 1);

    public Portfolio Portfolio { get; private set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public FilePortfolioService(IHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "portfolio.json");
    }

    /// <summary>
    /// Charge le portefeuille depuis App_Data/portfolio.json.
    /// S'il n'existe pas, crée un portefeuille vide et l'écrit une première fois.
    /// (Aucun seed automatique ici.)
    /// </summary>
    public async Task LoadAsync()
    {
        await _io.WaitAsync();
        try
        {
            if (File.Exists(_filePath))
            {
                using var fs = File.OpenRead(_filePath);
                var loaded = await JsonSerializer.DeserializeAsync<Portfolio>(fs, JsonOpts);
                Portfolio = loaded ?? new Portfolio();
                PruneZeroQty(Portfolio);
            }
            else
            {
                Portfolio = new Portfolio();
                await SaveUnlockedAsync();
            }
        }
        finally
        {
            _io.Release();
        }
    }

    public async Task SaveAsync()
    {
        await _io.WaitAsync();
        try
        {
            await SaveUnlockedAsync();
        }
        finally
        {
            _io.Release();
        }
    }

    /// <summary>
    /// Applique un trade en mémoire, supprime les positions à 0 et sauvegarde.
    /// Compatible avec la signature historique (void).
    /// </summary>
    public void AddTrade(Trade trade)
    {
        // On modifie en mémoire, puis on déclenche une sauvegarde fire-and-forget
        // (on ne bloque pas le thread de l'UI).
        ApplyTradeInMemory(trade);
        _ = SaveAsync(); // best effort
    }

    public Task<Dictionary<string, decimal>> GetLastPricesAsync()
        => throw new NotImplementedException("Réutilise ton implémentation existante ou injecte IPriceProvider ici.");

    // ---------- internes ----------
    private void ApplyTradeInMemory(Trade t)
    {
        var pos = Portfolio.Positions
            .FirstOrDefault(p => string.Equals(p.Security.Ticker, t.Ticker, StringComparison.OrdinalIgnoreCase));

        if (pos is null)
        {
            // Ne crée la position que pour un BUY
            if (t.Side != TradeSide.Buy) return;

            pos = new Position { Security = new Security { Ticker = t.Ticker } };
            Portfolio.Positions.Add(pos);
        }

        pos.ApplyTrade(t);
        PruneZeroQty(Portfolio);
    }

    private async Task SaveUnlockedAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        using var fs = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(fs, Portfolio, JsonOpts);
        await fs.FlushAsync();
    }

    private static void PruneZeroQty(Portfolio p)
    {
        p.Positions = p.Positions.Where(x => x.Quantity != 0).ToList();
    }
}
