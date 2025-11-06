using System;

namespace TradingPlatform.Models
{
    public class PriceBar
    {
        public DateTime Date { get; set; }      // UTC date (jour)
        public decimal Open  { get; set; }
        public decimal High  { get; set; }
        public decimal Low   { get; set; }
        public decimal Close { get; set; }
        public long    Volume { get; set; }

        public override string ToString()
            => $"{Date:yyyy-MM-dd} O:{Open} H:{High} L:{Low} C:{Close} V:{Volume}";
    }
}
