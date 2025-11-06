using TradingPlatform.Models;

namespace TradingPlatform.Services.Abstractions;

public interface IMetricsService
{
    Task<IReadOnlyList<ReturnPoint>> ComputePortfolioReturnsAsync(
        Portfolio portfolio, DateTime start, DateTime end, IPriceProvider provider);

    Task<IReadOnlyList<ReturnPoint>> ComputeAssetReturnsAsync(
        string ticker, DateTime start, DateTime end, IPriceProvider provider);

    Task<MetricsResult> ComputeMetricsAsync(
        IReadOnlyList<ReturnPoint> retsPtf,
        IReadOnlyList<ReturnPoint> retsBm,
        double riskFreeRateAnnual);
}

/// <summary>
/// Ensemble standard de métriques de performance.
/// </summary>
public record MetricsResult(
    double AlphaAnnual,
    double Beta,
    double RSquared,
    double VolatilityAnnual,
    double TrackingErrorAnnual,
    double InformationRatio,
    double SharpePortfolio,
    double SharpeBenchmark);
