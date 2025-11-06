namespace TradingPlatform.Models;

public class Position
{
    public Security Security { get; set; } = new();
    public int Quantity { get; set; } = 0;
    public decimal AvgPrice { get; set; } = 0m;

    public decimal MarketValue(decimal lastPrice) => Quantity * lastPrice;

    public void ApplyTrade(Trade t)
    {
        if (!string.Equals(t.Ticker, Security.Ticker, StringComparison.OrdinalIgnoreCase))
            return;

        if (t.Side == TradeSide.Buy)
        {
            var newQty = Quantity + t.Qty;
            if (newQty <= 0)
            {
                Quantity = 0;
                AvgPrice = 0m;
                return;
            }
            AvgPrice = (AvgPrice * Quantity + t.PriceClean * t.Qty) / newQty;
            Quantity = newQty;
        }
        else
        {
            Quantity = Math.Max(0, Quantity - t.Qty);
            if (Quantity == 0) AvgPrice = 0m;
        }
    }
}
