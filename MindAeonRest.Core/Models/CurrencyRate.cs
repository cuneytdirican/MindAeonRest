namespace MindAeonRest.Models;

/// <summary>Bir güne ait TCMB döviz kuru kaydı.</summary>
public class CurrencyRate : AuditableEntity
{
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTime RateDate { get; set; }
    public decimal? ForexBuying { get; set; }
    public decimal? ForexSelling { get; set; }
    public decimal? BanknoteBuying { get; set; }
    public decimal? BanknoteSelling { get; set; }
    public string Source { get; set; } = "TCMB";

    public string RateDateText => RateDate.ToString("dd.MM.yyyy");
}
