namespace TradingPlatform.Models
{
    /// <summary>
    /// Point de rendement utilisé pour les métriques et graphiques.
    /// Return = rendement journalier (ex: 0.012 = +1.2%)
    /// ReturnCum = rendement cumulé depuis le début.
    /// </summary>
    public class ReturnPoint
    {
        public DateTime Date { get; set; }
        public double Return { get; set; }
        public double ReturnCum { get; set; }

        public ReturnPoint() { }

        public ReturnPoint(DateTime date, double ret, double cum = 0)
        {
            Date = date;
            Return = ret;
            ReturnCum = cum;
        }
    }

    /// <summary>
    /// Point de valorisation (ex: AUM, indice) à une date donnée.
    /// </summary>
    public class ValuePoint
    {
        public DateTime Date { get; set; }
        public double Value { get; set; }

        public ValuePoint() { }
        public ValuePoint(DateTime date, double val)
        {
            Date = date;
            Value = val;
        }
    }
}
