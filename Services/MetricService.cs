using TradingPlatform.Models;
using TradingPlatform.Services.Abstractions;

namespace TradingPlatform.Services;

public class MetricsService : IMetricsService
{
    public async Task<IReadOnlyList<ReturnPoint>> ComputePortfolioReturnsAsync(
        Portfolio portfolio, DateTime start, DateTime end, IPriceProvider provider)
    {
        var tickers = portfolio.Positions
            .Select(p => p.Security.Ticker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dictSeries = new Dictionary<string, List<PriceBar>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tickers)
            dictSeries[t] = (await provider.GetDailyHistoryAsync(t, start, end)).ToList();

        var allDates = dictSeries.Values.SelectMany(s => s.Select(b => b.Date)).Distinct().OrderBy(d => d).ToList();
        if (allDates.Count < 3) return new List<ReturnPoint>();

        var qtyByTicker = portfolio.Positions.ToDictionary(p => p.Security.Ticker, p => p.Quantity, StringComparer.OrdinalIgnoreCase);
        decimal? priceOn(string t, DateTime d) => dictSeries[t].FirstOrDefault(b => b.Date == d)?.Close;

        var values = new List<(DateTime date, decimal value)>();
        foreach (var dt in allDates)
        {
            decimal total = 0m;
            foreach (var (ticker, qty) in qtyByTicker)
            {
                if (qty == 0) continue;
                var p = priceOn(ticker, dt);
                if (p.HasValue) total += qty * p.Value;
            }
            if (total > 0) values.Add((dt, total));
        }

        var rets = new List<ReturnPoint>();
        double cum = 1.0;
        for (int i = 1; i < values.Count; i++)
        {
            double r = (double)((values[i].value - values[i - 1].value) / values[i - 1].value);
            cum *= (1.0 + r);
            rets.Add(new ReturnPoint(values[i].date, r, cum - 1.0));
        }
        return rets;
    }

    public async Task<IReadOnlyList<ReturnPoint>> ComputeAssetReturnsAsync(
        string ticker, DateTime start, DateTime end, IPriceProvider provider)
    {
        var bars = await provider.GetDailyHistoryAsync(ticker, start, end);
        var rets = new List<ReturnPoint>();
        double cum = 1.0;

        for (int i = 1; i < bars.Count; i++)
        {
            double r = (double)((bars[i].Close - bars[i - 1].Close) / bars[i - 1].Close);
            cum *= (1.0 + r);
            rets.Add(new ReturnPoint(bars[i].Date, r, cum - 1.0));
        }
        return rets;
    }

    public Task<MetricsResult> ComputeMetricsAsync(
        IReadOnlyList<ReturnPoint> retsPtf,
        IReadOnlyList<ReturnPoint> retsBm,
        double riskFreeRateAnnual)
    {
        var bmDict = retsBm.ToDictionary(x => x.Date, x => x.Return);
        var paired = retsPtf
            .Where(p => bmDict.ContainsKey(p.Date))
            .Select(p => (p.Return, bmDict[p.Date]))
            .ToList();

        if (paired.Count < 10)
            return Task.FromResult(new MetricsResult(0, 0, 0, 0, 0, 0, 0, 0));

        static double Mean(IEnumerable<double> xs) => xs.Average();
        static double Var(IEnumerable<double> xs, double m)
        {
            var arr = xs.ToArray(); var n = arr.Length;
            if (n < 2) return 0;
            return arr.Select(x => (x - m) * (x - m)).Sum() / (n - 1);
        }
        static double Cov((double[] x, double[] y) v, (double mx, double my) m)
        {
            int n = v.x.Length; if (n < 2) return 0;
            double s = 0; for (int i = 0; i < n; i++) s += (v.x[i] - m.mx) * (v.y[i] - m.my);
            return s / (n - 1);
        }

        var rp = paired.Select(t => t.Item1).ToArray();
        var rb = paired.Select(t => t.Item2).ToArray();

        var mp = Mean(rp);
        var mb = Mean(rb);
        var vp = Var(rp, mp);
        var vb = Var(rb, mb);
        var cov = Cov((rp, rb), (mp, mb));

        var beta = vb > 0 ? cov / vb : 0.0;
        var alphaDaily = mp - beta * mb;
        var r2 = (cov * cov) / (vb * vp + 1e-12);

        const double D = 252.0;
        double volP = Math.Sqrt(vp) * Math.Sqrt(D);
        double alphaAnnual = Math.Pow(1.0 + alphaDaily, D) - 1.0;

        var ex = rp.Zip(rb, (x, y) => x - beta * y).ToArray();
        var mex = Mean(ex);
        var vte = Var(ex, mex);
        var teAnnual = Math.Sqrt(vte) * Math.Sqrt(D);

        double rfDaily = Math.Pow(1.0 + riskFreeRateAnnual, 1.0 / D) - 1.0;
        double sharpeP = (mp - rfDaily) / (Math.Sqrt(vp) + 1e-12) * Math.Sqrt(D);
        double sharpeB = (mb - rfDaily) / (Math.Sqrt(vb) + 1e-12) * Math.Sqrt(D);

        double ir = teAnnual > 0 ? ((mp - mb) * D) / teAnnual : 0.0;

        var res = new MetricsResult(
            AlphaAnnual: alphaAnnual,
            Beta: beta,
            RSquared: double.IsFinite(r2) ? Math.Clamp(r2, 0, 1) : 0,
            VolatilityAnnual: volP,
            TrackingErrorAnnual: teAnnual,
            InformationRatio: ir,
            SharpePortfolio: sharpeP,
            SharpeBenchmark: sharpeB
        );
        return Task.FromResult(res);
    }
}
