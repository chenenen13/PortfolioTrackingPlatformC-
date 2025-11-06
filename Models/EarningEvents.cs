using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingPlatform.Models
{
    public class EarningsEvent
    {
        public string Symbol { get; set; } = "";
        public string? CompanyName { get; set; }

        /// Prochaine date (UTC)
        public DateTime? EarningsDateUtc { get; set; }

        /// Deux dernières dates passées (UTC), triées décroissant
        public List<DateTime> PreviousEarningsUtc { get; set; } = new();

        public string Source { get; set; } = "";
        public string? Notes { get; set; }

        public DateTime? Prev1Utc => PreviousEarningsUtc.Count > 0 ? PreviousEarningsUtc[0] : (DateTime?)null;
        public DateTime? Prev2Utc => PreviousEarningsUtc.Count > 1 ? PreviousEarningsUtc[1] : (DateTime?)null;

        public bool IsUpcoming => EarningsDateUtc.HasValue && EarningsDateUtc.Value > DateTime.UtcNow.AddDays(-1);

        public override string ToString()
            => $"{Symbol} | next: {EarningsDateUtc:yyyy-MM-dd HH:mm} | prev: {string.Join(", ", PreviousEarningsUtc.Select(d => d.ToString("yyyy-MM-dd")))}";
    }
}
