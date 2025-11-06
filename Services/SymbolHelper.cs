namespace TradingPlatform.Services;

public static class SymbolHelper
{
    private static readonly HashSet<string> DefaultParis =
        new(StringComparer.OrdinalIgnoreCase) { "AIR","OR","MC","EL","DG","BNP","ACA","GLE","RMS","SAN","SU","AI" };

    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var s = raw.Trim().ToUpperInvariant();

        if (s is "FCHI") return "^FCHI";
        if (s is "GSPC") return "^GSPC";

        if (s.Contains('.') || s.StartsWith("^")) return s;

        if (DefaultParis.Contains(s)) return s + ".PA";

        return s;
    }
}
