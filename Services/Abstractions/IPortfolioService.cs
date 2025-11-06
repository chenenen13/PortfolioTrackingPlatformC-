using TradingPlatform.Models;

namespace TradingPlatform.Services.Abstractions;

public interface IPortfolioService
{
    Portfolio Portfolio { get; }
    Task LoadAsync();
    Task SaveAsync();

    void AddTrade(Trade trade);
    Task<Dictionary<string, decimal>> GetLastPricesAsync();
}
