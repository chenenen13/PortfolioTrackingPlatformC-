namespace TradingPlatform.Models;

public class DividendEvent
{
    public DateTime ExDate { get; set; }      // ex-dividend date
    public decimal Amount { get; set; }       // cash dividend per share
    public string Ticker { get; set; } = "";
}
