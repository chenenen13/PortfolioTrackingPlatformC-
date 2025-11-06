using System.Globalization;
using TradingPlatform.Models;
using TradingPlatform.Services;
using TradingPlatform.Services.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// 1) Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// 2) Options
builder.Services.Configure<BenchmarkOptions>(builder.Configuration.GetSection("Benchmark"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<PriceProviderOptions>(builder.Configuration.GetSection("PriceProvider"));
builder.Services.Configure<NewsOptions>(builder.Configuration.GetSection("News"));

// 3) Services techniques
builder.Services.AddSingleton<IStorageService, FileStorageService>();

// IPriceProvider (Yahoo + Fundamentals fallbacks via Alpha/EOD)
builder.Services.AddSingleton<IPriceProvider>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();

    var alphaKey = cfg["PriceProvider:AlphaVantageApiKey"];
    if (string.IsNullOrWhiteSpace(alphaKey))
        alphaKey = Environment.GetEnvironmentVariable("ALPHAVANTAGE_API_KEY");

    var eodToken = cfg["PriceProvider:EodhdApiToken"];
    if (string.IsNullOrWhiteSpace(eodToken))
        eodToken = Environment.GetEnvironmentVariable("EODHD_API_TOKEN");

#if DEBUG
    Console.WriteLine($"[Startup] Alpha key present: {(!string.IsNullOrWhiteSpace(alphaKey))}, EOD token present: {(!string.IsNullOrWhiteSpace(eodToken))}");
#endif

    var http = new HttpClient
    {
        BaseAddress = new Uri("https://query1.finance.yahoo.com/"),
        Timeout = TimeSpan.FromSeconds(20)
    };
    return new HttpYahooPriceProvider(http, alphaVantageApiKey: alphaKey, eodhdToken: eodToken);
});

// 4) Services applicatifs
builder.Services.AddSingleton<IPortfolioService, PortfolioService>();   // ta version adaptée ci-dessous
builder.Services.AddSingleton<IMetricsService, MetricsService>();
builder.Services.AddSingleton<NewsService>();
builder.Services.AddSingleton<WatchlistService>();
builder.Services.AddSingleton<IEarningsService, EarningsService>();

var app = builder.Build();

// 5) Charger le portefeuille UNE seule fois au démarrage
using (var scope = app.Services.CreateScope())
{
    var ptf = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
    await ptf.LoadAsync();
}

// Ensure final flush on shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        var svc = app.Services.GetRequiredService<IPortfolioService>();
        svc.SaveAsync().GetAwaiter().GetResult();
    }
    catch { /* ignore */ }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// 6) Culture par défaut en anglais
var culture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

app.Run();

// ---- Options records ----
public record BenchmarkOptions
{
    public string Ticker { get; init; } = "^GSPC";
    public double RiskFreeRate { get; init; } = 0.02;
}

public record StorageOptions
{
    /// <summary>Chemin du fichier JSON de portefeuille (relatif au ContentRoot si non absolu).</summary>
    public string PortfolioFile { get; init; } = "App_Data/portfolio.json";
}

public record PriceProviderOptions
{
    public string? AlphaVantageApiKey { get; init; }
    public string? EodhdApiToken { get; init; }
}

public record NewsOptions
{
    public string? ApiKey { get; init; }
}
