namespace TradingPlatform.Models;

public enum TradeSide { Buy, Sell }

public class Trade
{
    public string Ticker { get; set; } = string.Empty;
    public TradeSide Side { get; set; }
    public int Qty { get; set; }
    public decimal PriceClean { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
}
