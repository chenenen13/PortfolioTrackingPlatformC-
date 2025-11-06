using System.Collections.Generic;
using System.Threading.Tasks;
using TradingPlatform.Models;

namespace TradingPlatform.Services.Abstractions
{
    public interface IEarningsService
    {
        Task<IReadOnlyList<EarningsEvent>> GetWithHistoryAsync(IEnumerable<string> symbols);
        Task<IReadOnlyList<EarningsEvent>> GetUpcomingAsync(IEnumerable<string> symbols);
    }
}
