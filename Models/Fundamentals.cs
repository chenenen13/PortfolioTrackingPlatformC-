namespace TradingPlatform.Models;

public class Fundamentals
{
    public string Ticker { get; set; } = "";
    public string? Name { get; set; }
    public string? Sector { get; set; }
    public decimal? PeTtm { get; set; }
}
