using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Flurl.Http;
using Microsoft.Extensions.Configuration;
using TradingPlatform.Models;
using TradingPlatform.Services.Abstractions;

namespace TradingPlatform.Services
{
    public class EarningsService : IEarningsService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
        private static readonly string Ua =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:129.0) Gecko/20100101 Firefox/129.0";

        private readonly string _alphaKey;
        private readonly ConcurrentDictionary<string, (DateTime fetchedAtUtc, EarningsEvent data)> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public EarningsService(IConfiguration cfg)
        {
            // Priorité à appsettings: AlphaVantage:ApiKey
            _alphaKey =
                cfg["AlphaVantage:ApiKey"]
                ?? Environment.GetEnvironmentVariable("ALPHA_VANTAGE_KEY")
                ?? "";
        }

        // --------------- Public API ---------------

        public async Task<IReadOnlyList<EarningsEvent>> GetWithHistoryAsync(IEnumerable<string> symbols)
        {
            if (symbols == null) return Array.Empty<EarningsEvent>();

            var unique = symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unique.Length == 0) return Array.Empty<EarningsEvent>();

            var throttler = new SemaphoreSlim(6);
            var tasks = unique.Select(async symbol =>
            {
                try
                {
                    if (_cache.TryGetValue(symbol, out var cached) &&
                        (DateTime.UtcNow - cached.fetchedAtUtc) < CacheTtl)
                        return cached.data;

                    await throttler.WaitAsync();
                    try
                    {
                        EarningsEvent? ev = null;

                        // 1) Alpha Vantage
                        if (!string.IsNullOrWhiteSpace(_alphaKey))
                        {
                            ev = await FetchFromAlphaAsync(symbol);
                        }

                        // 2) Fallback Yahoo si Alpha vide
                        if (ev == null || (ev.EarningsDateUtc is null && ev.PreviousEarningsUtc.Count == 0))
                        {
                            var yahoo = await FetchFromYahooAsync(symbol);
                            if (yahoo != null)
                            {
                                // fusion minimale si Alpha a sorti quelque chose
                                if (ev == null) ev = yahoo;
                                else
                                {
                                    ev.EarningsDateUtc ??= yahoo.EarningsDateUtc;
                                    if (ev.PreviousEarningsUtc.Count == 0)
                                        ev.PreviousEarningsUtc = yahoo.PreviousEarningsUtc;
                                    ev.Notes = JoinNotes(ev.Notes, "Yahoo fallback ok");
                                }
                            }
                        }

                        // Si toujours rien
                        ev ??= new EarningsEvent
                        {
                            Symbol = symbol,
                            CompanyName = symbol,
                            Source = "Alpha/Yahoo",
                            Notes = "Aucune donnée trouvée (ni Alpha, ni Yahoo)"
                        };

                        _cache[symbol] = (DateTime.UtcNow, ev);
                        return ev;
                    }
                    finally { throttler.Release(); }
                }
                catch (Exception ex)
                {
                    return new EarningsEvent
                    {
                        Symbol = symbol,
                        CompanyName = symbol,
                        Source = "Alpha/Yahoo",
                        Notes = $"Erreur: {ex.Message}"
                    };
                }
            });

            var results = (await Task.WhenAll(tasks)).Where(x => x != null).ToList()!;
            return results
                .OrderBy(e => e.EarningsDateUtc.HasValue ? 0 : 1)
                .ThenBy(e => e.EarningsDateUtc)
                .ToList();
        }

        public async Task<IReadOnlyList<EarningsEvent>> GetUpcomingAsync(IEnumerable<string> symbols)
        {
            var all = await GetWithHistoryAsync(symbols);
            return all.Where(r => r.EarningsDateUtc.HasValue && r.EarningsDateUtc > DateTime.UtcNow.AddDays(-1))
                      .OrderBy(r => r.EarningsDateUtc)
                      .ToList();
        }

        // --------------- Alpha Vantage ---------------

        private async Task<EarningsEvent?> FetchFromAlphaAsync(string symbol)
        {
            string? companyName = null;
            DateTime? nextEarningsUtc = null;
            var prevDates = new List<DateTime>();
            var notes = new List<string>();

            // A) Prochaine date — EARNINGS_CALENDAR (dans 3 à 12 mois)
            try
            {
                // horizon=12month maximise les chances (Alpha supporte US; peu pour *.PA)
                var urlCal = $"https://www.alphavantage.co/query?function=EARNINGS_CALENDAR&symbol={symbol}&horizon=12month&apikey={_alphaKey}";
                var txt = await urlCal.WithHeader("User-Agent", Ua).GetStringAsync();

                using var doc = JsonDocument.Parse(txt);
                if (doc.RootElement.TryGetProperty("EarningsCalendar", out var ec) ||
                    doc.RootElement.TryGetProperty("earningsCalendar", out ec))
                {
                    if (ec.ValueKind == JsonValueKind.Array)
                    {
                        // chercher reportDate >= today
                        DateTime? best = null;
                        foreach (var it in ec.EnumerateArray())
                        {
                            var rdate = TryGetStr(it, "reportDate")
                                        ?? TryGetStr(it, "ReportDate");
                            if (!string.IsNullOrWhiteSpace(rdate) &&
                                TryParseDate(rdate!, out var d))
                            {
                                if (d >= DateTime.UtcNow.Date && (best == null || d < best))
                                    best = d;
                            }
                            if (companyName is null)
                                companyName = TryGetStr(it, "name") ?? TryGetStr(it, "Name") ?? symbol;
                        }
                        nextEarningsUtc = best;
                        if (best == null) notes.Add("Alpha: pas de prochaine date dans EARNINGS_CALENDAR");
                    }
                    else
                    {
                        notes.Add("Alpha: earningsCalendar non array");
                    }
                }
                else
                {
                    notes.Add("Alpha: earningsCalendar absent");
                }
            }
            catch (Exception ex)
            {
                notes.Add($"Alpha calendar err: {ex.Message}");
            }

            // B) Historique — EARNINGS (quarterlyEarnings)
            try
            {
                var urlHist = $"https://www.alphavantage.co/query?function=EARNINGS&symbol={symbol}&apikey={_alphaKey}";
                var txt = await urlHist.WithHeader("User-Agent", Ua).GetStringAsync();

                using var doc = JsonDocument.Parse(txt);
                if (doc.RootElement.TryGetProperty("quarterlyEarnings", out var q))
                {
                    if (q.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in q.EnumerateArray())
                        {
                            var s = TryGetStr(it, "reportedDate") ?? TryGetStr(it, "ReportedDate");
                            if (!string.IsNullOrWhiteSpace(s) && TryParseDate(s!, out var d))
                                prevDates.Add(d);
                        }

                        prevDates = prevDates.Where(d => d <= DateTime.UtcNow.AddDays(-1))
                                             .OrderByDescending(d => d)
                                             .Take(2)
                                             .ToList();

                        if (companyName is null)
                            companyName = symbol;
                    }
                    else
                    {
                        notes.Add("Alpha: quarterlyEarnings non array");
                    }
                }
                else
                {
                    notes.Add("Alpha: quarterlyEarnings absent");
                }
            }
            catch (Exception ex)
            {
                notes.Add($"Alpha earnings err: {ex.Message}");
            }

            if (companyName is null && nextEarningsUtc is null && prevDates.Count == 0)
                return null; // laissons Yahoo tenter

            return new EarningsEvent
            {
                Symbol = symbol,
                CompanyName = companyName ?? symbol,
                EarningsDateUtc = nextEarningsUtc,
                PreviousEarningsUtc = prevDates,
                Source = "AlphaVantage",
                Notes = notes.Count == 0 ? null : string.Join(" | ", notes)
            };
        }

        // --------------- Yahoo fallback (identique logique, condensé) ---------------

        private async Task<EarningsEvent?> FetchFromYahooAsync(string symbol)
        {
            string? companyName = null;
            DateTime? nextEarningsUtc = null;
            var prevDates = new List<DateTime>();
            var notes = new List<string>();

            // quoteSummary
            try
            {
                var url1 = $"https://query2.finance.yahoo.com/v10/finance/quoteSummary/{symbol}?modules=calendarEvents,earningsHistory,price";
                var json = await url1.WithHeader("User-Agent", Ua).GetStringAsync();
                using var doc = JsonDocument.Parse(json);

                var res = doc.RootElement.GetProperty("quoteSummary").GetProperty("result");
                if (res.ValueKind == JsonValueKind.Array && res.GetArrayLength() > 0)
                {
                    var r0 = res[0];

                    if (r0.TryGetProperty("price", out var p))
                    {
                        if (p.TryGetProperty("longName", out var ln) && ln.ValueKind == JsonValueKind.String) companyName = ln.GetString();
                        else if (p.TryGetProperty("shortName", out var sn) && sn.ValueKind == JsonValueKind.String) companyName = sn.GetString();
                    }

                    if (r0.TryGetProperty("calendarEvents", out var cal) &&
                        cal.TryGetProperty("earnings", out var earn) &&
                        earn.TryGetProperty("earningsDate", out var ed))
                    {
                        if (ed.ValueKind == JsonValueKind.Array && ed.GetArrayLength() > 0)
                        {
                            if (TryGetUnixRaw(ed[0], out var ts))
                                nextEarningsUtc = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                        }
                        else if (ed.ValueKind == JsonValueKind.Object && TryGetUnixRaw(ed, out var ts2))
                        {
                            nextEarningsUtc = DateTimeOffset.FromUnixTimeSeconds(ts2).UtcDateTime;
                        }
                    }

                    if (r0.TryGetProperty("earningsHistory", out var h) &&
                        h.TryGetProperty("history", out var hist) &&
                        hist.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in hist.EnumerateArray())
                            if (TryParseAnyDate(it, out var d)) prevDates.Add(d);

                        prevDates = prevDates.Where(d => d <= DateTime.UtcNow.AddDays(-1))
                                             .OrderByDescending(d => d)
                                             .Take(2)
                                             .ToList();
                    }
                }
            }
            catch (Exception ex) { notes.Add($"Yahoo quoteSummary err: {ex.Message}"); }

            // v7/quote fallback pour la next
            if (!nextEarningsUtc.HasValue)
            {
                try
                {
                    var url2 = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={symbol}";
                    var json2 = await url2.WithHeader("User-Agent", Ua).GetStringAsync();
                    using var doc2 = JsonDocument.Parse(json2);
                    var arr = doc2.RootElement.GetProperty("quoteResponse").GetProperty("result");
                    if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    {
                        var r = arr[0];
                        if (string.IsNullOrWhiteSpace(companyName))
                        {
                            if (r.TryGetProperty("longName", out var ln) && ln.ValueKind == JsonValueKind.String) companyName = ln.GetString();
                            else if (r.TryGetProperty("shortName", out var sn) && sn.ValueKind == JsonValueKind.String) companyName = sn.GetString();
                        }

                        if (r.TryGetProperty("earningsTimestamp", out var et) && et.ValueKind == JsonValueKind.Number)
                            nextEarningsUtc = DateTimeOffset.FromUnixTimeSeconds(et.GetInt64()).UtcDateTime;
                        else if (r.TryGetProperty("earningsTimestampStart", out var ets) && ets.ValueKind == JsonValueKind.Number)
                            nextEarningsUtc = DateTimeOffset.FromUnixTimeSeconds(ets.GetInt64()).UtcDateTime;
                        else if (r.TryGetProperty("earningsTimestampEnd", out var ete) && ete.ValueKind == JsonValueKind.Number)
                            nextEarningsUtc = DateTimeOffset.FromUnixTimeSeconds(ete.GetInt64()).UtcDateTime;
                    }
                }
                catch (Exception ex) { notes.Add($"Yahoo v7 err: {ex.Message}"); }
            }

            if (companyName is null && nextEarningsUtc is null && prevDates.Count == 0)
                return null;

            return new EarningsEvent
            {
                Symbol = symbol,
                CompanyName = companyName ?? symbol,
                EarningsDateUtc = nextEarningsUtc,
                PreviousEarningsUtc = prevDates,
                Source = "Yahoo",
                Notes = notes.Count == 0 ? null : string.Join(" | ", notes)
            };
        }

        // --------------- Helpers ---------------

        private static string JoinNotes(string? a, string b)
            => string.IsNullOrWhiteSpace(a) ? b : $"{a} | {b}";

        private static bool TryGetUnixRaw(JsonElement obj, out long unixSeconds)
        {
            unixSeconds = 0;
            return obj.ValueKind == JsonValueKind.Object
                   && obj.TryGetProperty("raw", out var raw)
                   && raw.ValueKind == JsonValueKind.Number
                   && raw.TryGetInt64(out unixSeconds);
        }

        private static string? TryGetStr(JsonElement obj, string prop)
            => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool TryParseDate(string s, out DateTime utc)
        {
            // Alpha renvoie "2025-11-21" etc. → on traite en UTC (00:00)
            var formats = new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss" };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                utc = dt;
                return true;
            }
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt2))
            {
                utc = dt2;
                return true;
            }
            utc = default;
            return false;
        }

        private static bool TryParseAnyDate(JsonElement item, out DateTime utc)
        {
            utc = default;
            string[] keys = new[] { "startdatetime", "reportedDate", "date", "period" };
            foreach (var k in keys)
            {
                if (item.TryGetProperty(k, out var val) && val.ValueKind == JsonValueKind.String)
                {
                    if (TryParseDate(val.GetString()!, out var d)) { utc = d; return true; }
                }
            }
            // quarter.raw parfois epoch
            if (item.TryGetProperty("quarter", out var q) && q.ValueKind == JsonValueKind.Object &&
                q.TryGetProperty("raw", out var qr) && qr.ValueKind == JsonValueKind.Number)
            {
                var raw = qr.GetDouble();
                if (raw > 10_000_000 && raw < 4_000_000_000)
                {
                    utc = DateTimeOffset.FromUnixTimeSeconds((long)raw).UtcDateTime;
                    return true;
                }
            }
            return false;
        }
    }
}
