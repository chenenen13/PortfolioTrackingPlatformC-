using TradingPlatform.Models;

namespace TradingPlatform.Services.Abstractions;

public interface IPriceProvider
{
    // Historical data (daily close or adjclose)
    Task<IReadOnlyList<PriceBar>> GetDailyHistoryAsync(string ticker, DateTime start, DateTime end);

    // Last traded price
    Task<decimal?> GetLastPriceAsync(string ticker);

    // Candlestick OHLC data ("1d", "1wk", "1mo")
    Task<IReadOnlyList<OhlcBar>> GetOhlcAsync(string ticker, DateTime start, DateTime end, string interval);

    // Dividend events (ex-date + amount)
    Task<IReadOnlyList<DividendEvent>> GetDividendsAsync(string ticker, DateTime start, DateTime end);

    // Company fundamentals (name, sector, P/E ttm)
    Task<Fundamentals?> GetFundamentalsAsync(string ticker);
}
