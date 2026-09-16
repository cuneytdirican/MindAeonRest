using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using MindAeonRest.Data;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// TCMB döviz kurlarını çekip MindAeonRest (sistem) DB'sindeki CurrencyRates tablosunda saklar.
/// Login sırasında çağrılır: en son kur 3 günden eskiyse son 1 hafta, değilse son 3 gün taranır.
/// Var olan günler atlanır; hafta sonu/tatil günleri TCMB'de dosya olmadığından sessizce geçilir.
/// </summary>
public class CurrencyRateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private const string BaseUrl = "https://www.tcmb.gov.tr/kurlar";

    public DateTime? GetLatestDate()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(RateDate) FROM dbo.CurrencyRates;";
        var result = command.ExecuteScalar();
        return result is DateTime d ? d : null;
    }

    /// <summary>En güncel tarihteki tüm kurlar (gösterim için).</summary>
    public List<CurrencyRate> GetLatestRates()
    {
        var list = new List<CurrencyRate>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CurrencyCode, RateDate, ForexBuying, ForexSelling, BanknoteBuying, BanknoteSelling, Source
FROM dbo.CurrencyRates
WHERE RateDate = (SELECT MAX(RateDate) FROM dbo.CurrencyRates)
ORDER BY CurrencyCode;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new CurrencyRate
            {
                CurrencyCode = reader.GetString(0),
                RateDate = reader.GetDateTime(1),
                ForexBuying = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                ForexSelling = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                BanknoteBuying = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                BanknoteSelling = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                Source = reader.GetString(6)
            });
        return list;
    }

    /// <summary>En güncel USD ve EUR satış kuru (evrak kur snapshot'ı için). Yoksa null.</summary>
    public (decimal? Usd, decimal? Eur) GetUsdEur()
    {
        decimal? usd = null, eur = null;
        foreach (var r in GetLatestRates())
        {
            var v = r.ForexSelling ?? r.ForexBuying ?? r.BanknoteSelling;
            if (r.CurrencyCode.Equals("USD", StringComparison.OrdinalIgnoreCase)) usd = v;
            else if (r.CurrencyCode.Equals("EUR", StringComparison.OrdinalIgnoreCase)) eur = v;
        }
        return (usd, eur);
    }

    /// <summary>Login'de çağrılır. İnternet/erişim hataları yutulur, uygulamayı bloklamaz.</summary>
    public void EnsureUpToDate()
    {
        try
        {
            var tracked = GetTrackedCurrencies();
            if (tracked.Count == 0) return;

            DateTime? latest = GetLatestDate();
            DateTime today = DateTime.Today;

            // En son kur 3 günden eskiyse son 1 hafta, değilse son 3 gün
            int window = (latest is null || latest.Value < today.AddDays(-3)) ? 7 : 3;

            for (int i = 0; i < window; i++)
            {
                DateTime date = today.AddDays(-i);
                if (DateExists(date)) continue;       // o güne ait kayıt varsa pas geç
                TryFetchAndStore(date, tracked);
            }
        }
        catch
        {
            // kur güncellemesi giriş akışını etkilemez
        }
    }

    private static List<string> GetTrackedCurrencies()
    {
        var codes = new List<string>();
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        // TRY baz para birimidir; takip edilmez
        command.CommandText = "SELECT Code FROM dbo.Currencies WHERE IsDeleted=0 AND Code <> N'TRY';";
        using var reader = command.ExecuteReader();
        while (reader.Read()) codes.Add(reader.GetString(0));
        return codes;
    }

    private static bool DateExists(DateTime date)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM dbo.CurrencyRates WHERE RateDate=@d;";
        command.Parameters.AddWithValue("@d", date.Date);
        return (int)command.ExecuteScalar() > 0;
    }

    private void TryFetchAndStore(DateTime date, List<string> tracked)
    {
        try
        {
            string url = $"{BaseUrl}/{date:yyyyMM}/{date:ddMMyyyy}.xml";
            using var response = Http.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return;   // tatil/hafta sonu -> dosya yok

            string xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = XDocument.Parse(xml);

            using var connection = Database.OpenConnection();
            foreach (var node in doc.Descendants("Currency"))
            {
                string? code = node.Attribute("Kod")?.Value ?? node.Attribute("CurrencyCode")?.Value;
                if (string.IsNullOrEmpty(code) || !tracked.Contains(code)) continue;

                Insert(connection, code, date,
                    Parse(node.Element("ForexBuying")?.Value),
                    Parse(node.Element("ForexSelling")?.Value),
                    Parse(node.Element("BanknoteBuying")?.Value),
                    Parse(node.Element("BanknoteSelling")?.Value));
            }
        }
        catch
        {
            // tek bir günün hatası diğerlerini etkilemesin
        }
    }

    private static void Insert(Microsoft.Data.SqlClient.SqlConnection connection, string code, DateTime date,
        decimal? forexBuy, decimal? forexSell, decimal? bankBuy, decimal? bankSell)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM dbo.CurrencyRates WHERE CurrencyCode=@Code AND RateDate=@Date)
    INSERT INTO dbo.CurrencyRates (CurrencyCode, RateDate, ForexBuying, ForexSelling, BanknoteBuying, BanknoteSelling)
    VALUES (@Code, @Date, @FB, @FS, @BB, @BS);";
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Date", date.Date);
        command.Parameters.AddWithValue("@FB", (object?)forexBuy ?? DBNull.Value);
        command.Parameters.AddWithValue("@FS", (object?)forexSell ?? DBNull.Value);
        command.Parameters.AddWithValue("@BB", (object?)bankBuy ?? DBNull.Value);
        command.Parameters.AddWithValue("@BS", (object?)bankSell ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static decimal? Parse(string? value)
        => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
}
