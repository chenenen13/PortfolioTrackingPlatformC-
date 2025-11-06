using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TradingPlatform.Models;
using TradingPlatform.Services.Abstractions;

namespace TradingPlatform.Services
{
    /// <summary>
    /// Provider:
    ///  - Prix & historiques: Yahoo v8 chart (anonyme)
    ///  - Fondamentaux: Alpha Vantage OVERVIEW, puis EODHD si dispo
    /// </summary>
    public sealed class HttpYahooPriceProvider : IPriceProvider, IDisposable
    {
        private static readonly Uri YahooBase = new("https://query1.finance.yahoo.com/");
        private static readonly Uri AlphaBase = new("https://www.alphavantage.co/");
        private static readonly Uri EodBase   = new("https://eodhistoricaldata.com/");

        private static readonly TimeSpan LastPriceCacheTtl = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan HistoryCacheTtl   = TimeSpan.FromMinutes(15);

        private readonly HttpClient _http;
        private readonly bool _ownsClient;

        private readonly string? _alphaKey;
        private readonly string? _eodhdToken;

        private readonly Dictionary<string, (DateTime asOfUtc, decimal price)> _lastPriceCache
            = new(StringComparer.OrdinalIgnoreCase);

        // key = $"{sym}|{interval}|{p1}|{p2}"
        private readonly Dictionary<string, (DateTime asOfUtc, List<PriceBar> bars)> _historyCache
            = new(StringComparer.OrdinalIgnoreCase);

        private const int MaxAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

        public HttpYahooPriceProvider()
        {
            _http = CreateConfiguredClient();
            _ownsClient = true;

            _alphaKey   = Environment.GetEnvironmentVariable("ALPHAVANTAGE_API_KEY");
            _eodhdToken = Environment.GetEnvironmentVariable("EODHD_API_TOKEN");
        }

        public HttpYahooPriceProvider(HttpClient httpClient, string? alphaVantageApiKey = null, string? eodhdToken = null)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (_http.BaseAddress == null) _http.BaseAddress = YahooBase;
            EnsureYahooHeaders(_http.DefaultRequestHeaders);
            _ownsClient = false;

            _alphaKey   = string.IsNullOrWhiteSpace(alphaVantageApiKey) ? Environment.GetEnvironmentVariable("ALPHAVANTAGE_API_KEY") : alphaVantageApiKey;
            _eodhdToken = string.IsNullOrWhiteSpace(eodhdToken)         ? Environment.GetEnvironmentVariable("EODHD_API_TOKEN")     : eodhdToken;
        }

        public void Dispose()
        {
            if (_ownsClient) _http.Dispose();
        }

        // ---------------- IPriceProvider ----------------

        public async Task<decimal?> GetLastPriceAsync(string ticker)
        {
            var sym = NormalizeSymbol(ticker);

            if (_lastPriceCache.TryGetValue(sym, out var hit) &&
                (DateTime.UtcNow - hit.asOfUtc) < LastPriceCacheTtl)
                return hit.price;

            // simple fallback via chart sur 7 jours
            var end = DateTime.UtcNow.Date.AddDays(1);
            var start = end.AddDays(-7);
            var (p1, p2) = ToUnixRange(start, end);
            var url = $"v8/finance/chart/{Uri.EscapeDataString(sym)}?period1={p1}&period2={p2}&interval=1d";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;

            using var s = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(s);

            if (!TryGetResultNode(doc.RootElement, out var result)) return null;

            var closes = ReadDecimalArray(result, "indicators", "quote", "0", "close") ?? Array.Empty<decimal>();
            for (int i = closes.Length - 1; i >= 0; i--)
            {
                if (closes[i] > 0)
                {
                    _lastPriceCache[sym] = (DateTime.UtcNow, closes[i]);
                    return closes[i];
                }
            }
            return null;
        }

        // === HISTORIQUE JOURNALIER AVEC OHLC ===
        public async Task<IReadOnlyList<PriceBar>> GetDailyHistoryAsync(string ticker, DateTime start, DateTime end)
        {
            var sym = NormalizeSymbol(ticker);
            if (end <= start) end = start.AddDays(1);

            var (p1, p2) = ToUnixRange(start.Date, end.Date);
            var cacheKey = $"{sym}|1d|{p1}|{p2}";
            if (_historyCache.TryGetValue(cacheKey, out var h) &&
                (DateTime.UtcNow - h.asOfUtc) < HistoryCacheTtl)
                return h.bars;

            var url = $"v8/finance/chart/{Uri.EscapeDataString(sym)}?period1={p1}&period2={p2}&interval=1d&events=div%2Csplit";
            var list = await FetchDailyOhlcBars(url);

            _historyCache[cacheKey] = (DateTime.UtcNow, list);
            return list;
        }

        // (laisse tel quel si tu utilises OhlcBar ailleurs)
        public async Task<IReadOnlyList<OhlcBar>> GetOhlcAsync(string ticker, DateTime start, DateTime end, string interval)
        {
            var sym = NormalizeSymbol(ticker);
            if (end <= start) end = start.AddDays(1);
            interval = interval is "1d" or "1wk" or "1mo" ? interval : "1d";

            var (p1, p2) = ToUnixRange(start.Date, end.Date);
            var url = $"v8/finance/chart/{Uri.EscapeDataString(sym)}?period1={p1}&period2={p2}&interval={interval}&events=div%2Csplit";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return Array.Empty<OhlcBar>();

            using var s = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(s);

            if (!TryGetResultNode(doc.RootElement, out var result)) return Array.Empty<OhlcBar>();

            var tzOffset = ReadGmtOffset(result);
            var ts = ReadTimestamps(result, tzOffset);

            var o = ReadDecimalArray(result, "indicators", "quote", "0", "open")  ?? Array.Empty<decimal>();
            var h = ReadDecimalArray(result, "indicators", "quote", "0", "high")  ?? Array.Empty<decimal>();
            var l = ReadDecimalArray(result, "indicators", "quote", "0", "low")   ?? Array.Empty<decimal>();
            var c = ReadDecimalArray(result, "indicators", "quote", "0", "close") ?? Array.Empty<decimal>();

            var list = new List<OhlcBar>();
            for (int i = 0; i < ts.Length; i++)
            {
                if (i < o.Length && i < h.Length && i < l.Length && i < c.Length)
                {
                    if (o[i] > 0 && h[i] > 0 && l[i] > 0 && c[i] > 0)
                    {
                        list.Add(new OhlcBar
                        {
                            Date  = ts[i],
                            Open  = o[i],
                            High  = h[i],
                            Low   = l[i],
                            Close = c[i]
                        });
                    }
                }
            }
            return list;
        }

        public async Task<IReadOnlyList<DividendEvent>> GetDividendsAsync(string ticker, DateTime start, DateTime end)
        {
            var sym = NormalizeSymbol(ticker);
            if (end <= start) end = start.AddDays(1);

            var (p1, p2) = ToUnixRange(start.Date, end.Date);
            var url = $"v8/finance/chart/{Uri.EscapeDataString(sym)}?period1={p1}&period2={p2}&interval=1d&events=div%2Csplit";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return Array.Empty<DividendEvent>();

            using var s = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(s);

            if (!TryGetResultNode(doc.RootElement, out var result)) return Array.Empty<DividendEvent>();
            if (!result.TryGetProperty("events", out var eventsNode) ||
                !eventsNode.TryGetProperty("dividends", out var divNode) ||
                divNode.ValueKind != JsonValueKind.Object)
                return Array.Empty<DividendEvent>();

            var list = new List<DividendEvent>();
            foreach (var prop in divNode.EnumerateObject())
            {
                var obj = prop.Value;
                if (obj.TryGetProperty("date", out var dEl) &&
                    obj.TryGetProperty("amount", out var aEl) &&
                    dEl.ValueKind == JsonValueKind.Number &&
                    aEl.ValueKind == JsonValueKind.Number)
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(dEl.GetInt64()).Date;
                    var amt = (decimal)aEl.GetDouble();
                    if (amt > 0) list.Add(new DividendEvent { ExDate = dt, Amount = amt, Ticker = sym });
                }
            }
            return list.OrderBy(x => x.ExDate).ToList();
        }

        public async Task<Fundamentals?> GetFundamentalsAsync(string ticker)
        {
            var sym = NormalizeSymbol(ticker);

#if DEBUG
            Console.WriteLine($"[Fundamentals] Query {sym}");
#endif

            // 1) Alpha Vantage OVERVIEW
            if (!string.IsNullOrWhiteSpace(_alphaKey))
            {
                var (n, s, p) = await TryAlphaOverview(sym, _alphaKey!);
#if DEBUG
                Console.WriteLine($"[AlphaVantage] name:{n ?? "-"} sector:{s ?? "-"} pe:{(p.HasValue ? p.Value.ToString("F2") : "-")}");
#endif
                if (!string.IsNullOrWhiteSpace(n) || !string.IsNullOrWhiteSpace(s) || (p.HasValue && p.Value > 0))
                    return new Fundamentals { Ticker = sym, Name = n, Sector = s, PeTtm = p };
            }
            else
            {
#if DEBUG
                Console.WriteLine("[AlphaVantage] no API key → skipping");
#endif
            }

            // 2) EODHD si token dispo
            if (!string.IsNullOrWhiteSpace(_eodhdToken))
            {
                var (n2, s2, p2) = await TryEodhdFundamentals(sym, _eodhdToken!);
#if DEBUG
                Console.WriteLine($"[EODHD] name:{n2 ?? "-"} sector:{s2 ?? "-"} pe:{(p2.HasValue ? p2.Value.ToString("F2") : "-")}");
#endif
                if (!string.IsNullOrWhiteSpace(n2) || !string.IsNullOrWhiteSpace(s2) || (p2.HasValue && p2.Value > 0))
                    return new Fundamentals { Ticker = sym, Name = n2, Sector = s2, PeTtm = p2 };
            }
            else
            {
#if DEBUG
                Console.WriteLine("[EODHD] no token → skipping");
#endif
            }

            return null;
        }

        // ---------------- Internals ----------------

        private static HttpClient CreateConfiguredClient()
        {
            var http = new HttpClient
            {
                BaseAddress = YahooBase,
                Timeout = TimeSpan.FromSeconds(20)
            };
            EnsureYahooHeaders(http.DefaultRequestHeaders);
            return http;
        }

        private static void EnsureYahooHeaders(HttpRequestHeaders h)
        {
            h.UserAgent.Clear();
            h.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0 Safari/537.36");
            h.Accept.Clear();
            h.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            h.CacheControl = new CacheControlHeaderValue { NoCache = false };
        }

        private static string NormalizeSymbol(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();

        private static (long p1, long p2) ToUnixRange(DateTime start, DateTime end)
        {
            var p1 = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var p2 = new DateTimeOffset(DateTime.SpecifyKind(end,   DateTimeKind.Utc)).ToUnixTimeSeconds();
            return (p1, p2);
        }

        // ----------- OHLC fetcher (amélioré) -----------
        private async Task<List<PriceBar>> FetchDailyOhlcBars(string url)
        {
            var attempt = 0;
            while (true)
            {
                attempt++;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

                if ((int)resp.StatusCode == 429 && attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay);
                    continue;
                }
                if (!resp.IsSuccessStatusCode) return new();

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                if (!TryGetResultNode(doc.RootElement, out var result)) return new();

                var tzOffset = ReadGmtOffset(result);
                var timestamps = ReadTimestamps(result, tzOffset);

                var o = ReadDecimalArray(result, "indicators", "quote", "0", "open")  ?? Array.Empty<decimal>();
                var h = ReadDecimalArray(result, "indicators", "quote", "0", "high")  ?? Array.Empty<decimal>();
                var l = ReadDecimalArray(result, "indicators", "quote", "0", "low")   ?? Array.Empty<decimal>();
                var c = ReadDecimalArray(result, "indicators", "quote", "0", "close") ?? Array.Empty<decimal>();
                var v = ReadLongArray   (result, "indicators", "quote", "0", "volume")?? Array.Empty<long>();

                var list = new List<PriceBar>(Math.Min(timestamps.Length, c.Length));
                for (int i = 0; i < timestamps.Length; i++)
                {
                    if (i < o.Length && i < h.Length && i < l.Length && i < c.Length)
                    {
                        var open  = o[i];
                        var high  = h[i];
                        var low   = l[i];
                        var close = c[i];

                        if (open > 0 && high > 0 && low > 0 && close > 0)
                        {
                            list.Add(new PriceBar
                            {
                                Date   = timestamps[i],
                                Open   = open,
                                High   = high,
                                Low    = low,
                                Close  = close,
                                Volume = (i < v.Length ? v[i] : 0L)
                            });
                        }
                    }
                }
                return list.OrderBy(x => x.Date).ToList();
            }
        }

        private static bool TryGetResultNode(JsonElement root, out JsonElement result)
        {
            result = default;
            if (root.TryGetProperty("chart", out var chart)
                && chart.TryGetProperty("result", out var arr)
                && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0)
            {
                result = arr[0];
                return true;
            }
            return false;
        }

        private static int ReadGmtOffset(JsonElement result)
        {
            if (result.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("gmtoffset", out var off)
                && off.ValueKind == JsonValueKind.Number)
            {
                return off.GetInt32();
            }
            return 0;
        }

        private static DateTime[] ReadTimestamps(JsonElement result, int gmtoffsetSeconds)
        {
            if (result.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Array)
            {
                return ts.EnumerateArray()
                         .Select(e =>
                         {
                             var unix = e.GetInt64();
                             var dto = DateTimeOffset.FromUnixTimeSeconds(unix + gmtoffsetSeconds);
                             return dto.UtcDateTime.Date; // on garde la date (chart quotidien)
                         })
                         .ToArray();
            }
            return Array.Empty<DateTime>();
        }

        private static decimal[]? ReadDecimalArray(JsonElement node, params string[] path)
        {
            if (!TryWalk(node, out var cur, path)) return null;
            if (cur.ValueKind != JsonValueKind.Array) return null;
            var list = new List<decimal>(cur.GetArrayLength());
            foreach (var v in cur.EnumerateArray())
                list.Add(v.ValueKind == JsonValueKind.Number ? (decimal)v.GetDouble() : 0m);
            return list.ToArray();
        }

        private static long[]? ReadLongArray(JsonElement node, params string[] path)
        {
            if (!TryWalk(node, out var cur, path)) return null;
            if (cur.ValueKind != JsonValueKind.Array) return null;
            var list = new List<long>(cur.GetArrayLength());
            foreach (var v in cur.EnumerateArray())
                list.Add(v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0L);
            return list.ToArray();
        }

        private static bool TryWalk(JsonElement cur, out JsonElement outEl, params string[] path)
        {
            outEl = cur;
            foreach (var part in path)
            {
                if (int.TryParse(part, out var idx))
                {
                    if (outEl.ValueKind != JsonValueKind.Array || outEl.GetArrayLength() <= idx) return false;
                    outEl = outEl[idx];
                }
                else
                {
                    if (!outEl.TryGetProperty(part, out outEl)) return false;
                }
            }
            return true;
        }

        // ---------------- Fundamentals providers ----------------

        private async Task<(string? name, string? sector, decimal? pe)> TryAlphaOverview(string sym, string apiKey)
        {
            try
            {
                var url = new Uri(AlphaBase, $"query?function=OVERVIEW&symbol={Uri.EscapeDataString(sym)}&apikey={Uri.EscapeDataString(apiKey)}");
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
#if DEBUG
                await DumpShort("[Alpha OVERVIEW]", resp);
#endif
                if (!resp.IsSuccessStatusCode) return (null, null, null);

                using var s = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);

                if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, null, null);

                string? name = doc.RootElement.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                string? sector = doc.RootElement.TryGetProperty("Sector", out var sec) && sec.ValueKind == JsonValueKind.String ? sec.GetString() : null;

                decimal? pe = null;
                if (doc.RootElement.TryGetProperty("PERatio", out var peEl) && peEl.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    if (peEl.ValueKind == JsonValueKind.Number) pe = (decimal)peEl.GetDouble();
                    else if (decimal.TryParse(peEl.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) pe = d;
                }

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(sector) && !pe.HasValue)
                    return (null, null, null);

                return (name, sector, pe);
            }
            catch { return (null, null, null); }
        }

        private async Task<(string? name, string? sector, decimal? pe)> TryEodhdFundamentals(string sym, string token)
        {
            try
            {
                var url = new Uri(EodBase, $"api/fundamentals/{Uri.EscapeDataString(sym)}?api_token={Uri.EscapeDataString(token)}");
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
#if DEBUG
                await DumpShort("[EODHD fundamentals]", resp);
#endif
                if (!resp.IsSuccessStatusCode) return (null, null, null);

                using var s = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);

                string? name = TryGetPathString(doc.RootElement, "General", "Name")
                            ?? TryGetPathString(doc.RootElement, "General", "Code");

                string? sector = TryGetPathString(doc.RootElement, "General", "Sector");

                decimal? pe = TryGetPathDecimal(doc.RootElement, "Valuation", "TrailingPE")
                           ?? TryGetPathDecimal(doc.RootElement, "Highlights", "PERatio");

                return (name, sector, pe);
            }
            catch { return (null, null, null); }
        }

#if DEBUG
        private static async Task DumpShort(string tag, HttpResponseMessage resp)
        {
            try
            {
                var code = (int)resp.StatusCode;
                string body = "";
                if ((code >= 400 || code == 200))
                {
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    body = Encoding.UTF8.GetString(bytes);
                    if (body.Length > 180) body = body[..180] + "...";
                }
                Console.WriteLine($"{tag} => {(int)resp.StatusCode} {resp.StatusCode} | {body}");
            }
            catch { }
        }
#endif
        private static string? TryGetPathString(JsonElement root, params string[] path)
        {
            if (!TryWalk(root, out var el, path)) return null;
            return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        }
        private static decimal? TryGetPathDecimal(JsonElement root, params string[] path)
        {
            if (!TryWalk(root, out var el, path)) return null;
            if (el.ValueKind == JsonValueKind.Number) return (decimal)el.GetDouble();
            if (el.ValueKind == JsonValueKind.String &&
                decimal.TryParse(el.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            return null;
        }
    }
}
