namespace TradingPlatform.Models;

public class Portfolio
{
    public string Name { get; set; } = "My Portfolio";
    public List<Position> Positions { get; set; } = new();
    public List<Trade> Trades { get; set; } = new();

    public Position? GetPosition(string ticker) =>
        Positions.FirstOrDefault(p => p.Security.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase));

    public Position GetOrCreate(string ticker, string? name = null)
    {
        var pos = GetPosition(ticker);
        if (pos is null)
        {
            pos = new Position { Security = new Security(ticker, name ?? ticker) };
            Positions.Add(pos);
        }
        return pos;
    }

    public void Apply(Trade trade)
    {
        Trades.Add(trade);
        GetOrCreate(trade.Ticker).ApplyTrade(trade);
    }
}
