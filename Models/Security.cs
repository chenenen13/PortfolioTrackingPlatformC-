namespace TradingPlatform.Models;

public class Security
{
    public string Ticker { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Security() { }
    public Security(string ticker, string name = "")
    {
        Ticker = ticker.ToUpperInvariant();
        Name = string.IsNullOrWhiteSpace(name) ? ticker.ToUpperInvariant() : name;
    }

    public override string ToString() => $"{Name} ({Ticker})";
}
