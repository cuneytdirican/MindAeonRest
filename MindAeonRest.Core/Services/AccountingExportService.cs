using System.Globalization;
using System.Text;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Muhasebe aktarım biçimi (harici muhasebe programı içe-aktarım kolon düzeni).</summary>
public enum AccountingExportFormat { Genel = 0, Logo = 1, Mikro = 2 }

public static class AccountingExportFormatExtensions
{
    public static string ToTr(this AccountingExportFormat f) => f switch
    {
        AccountingExportFormat.Logo => "Logo uyumlu",
        AccountingExportFormat.Mikro => "Mikro uyumlu",
        _ => "Genel CSV"
    };
}

/// <summary>Tek bir muhasebe aktarım satırı (cari hareket + varsa fatura KDV'si).</summary>
public class AccountingExportRow
{
    public DateTime Date { get; set; }
    public string? FisNo { get; set; }
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? TaxNumber { get; set; }
    public CariDocType DocType { get; set; }
    public string? Description { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal Debt { get; set; }
    public decimal Credit { get; set; }
    /// <summary>Belge KDV tutarı (yalnız fatura kaynaklı hareketlerde; diğerlerinde 0).</summary>
    public decimal Kdv { get; set; }

    public string DocTypeText => DocType.ToTr();
}

/// <summary>
/// FAZ 4a Muhasebe Aktarım: belirli tarih aralığı için cari hareketleri (+ fatura KDV'si) harici
/// muhasebe programlarına (Logo / Mikro / genel) içe-aktarılabilir CSV'ye çevirir. Salt-okunur.
/// GetRows DB'den çeker; BuildCsv/BuildSummaryCsv SAF (DB'siz, birim testine uygun) dönüştürücülerdir.
/// </summary>
public class AccountingExportService
{
    /// <summary>Tarih aralığındaki cari hareketler + (fatura ise) belge KDV'si. from dahil, to dahil (gün sonu).</summary>
    public List<AccountingExportRow> GetRows(DateTime from, DateTime to, int? customerId = null)
    {
        var list = new List<AccountingExportRow>();
        DateTime toExclusive = to.Date.AddDays(1);
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT h.TxnDate, h.DocNo, cu.Code, cu.Name, cu.TaxNumber, h.DocType, h.Description,
       cur.Code AS CurCode, h.Debt, h.Credit,
       CASE WHEN h.DocRefType = 'Fatura' THEN ISNULL(d.TaxTotal, 0) ELSE 0 END AS Kdv
FROM dbo.CariHareketler h
JOIN dbo.Customers cu ON cu.Id = h.CustomerId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.Currencies cur ON cur.Id = h.CurrencyId
LEFT JOIN dbo.Documents d ON h.DocRefType = 'Fatura' AND d.Id = h.DocRefId
WHERE h.TxnDate >= @from AND h.TxnDate < @to
  AND (@cust IS NULL OR h.CustomerId = @cust)
ORDER BY h.TxnDate, h.Id;";
        cmd.Parameters.AddWithValue("@from", from.Date);
        cmd.Parameters.AddWithValue("@to", toExclusive);
        cmd.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AccountingExportRow
            {
                Date = r.GetDateTime(0),
                FisNo = r.IsDBNull(1) ? null : r.GetString(1),
                CustomerCode = r.GetString(2),
                CustomerName = r.GetString(3),
                TaxNumber = r.IsDBNull(4) ? null : r.GetString(4),
                DocType = (CariDocType)r.GetInt32(5),
                Description = r.IsDBNull(6) ? null : r.GetString(6),
                CurrencyCode = r.IsDBNull(7) ? "" : r.GetString(7),
                Debt = r.GetDecimal(8),
                Credit = r.GetDecimal(9),
                Kdv = r.GetDecimal(10)
            });
        return list;
    }

    // ── SAF dönüştürücüler (DB'siz; birim testine uygun) ──

    /// <summary>Satırları seçilen biçimin kolon düzeninde CSV'ye çevirir (';' ayraç, tutarlar virgül ondalık).</summary>
    public static string BuildCsv(IEnumerable<AccountingExportRow> rows, AccountingExportFormat format)
    {
        var sb = new StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        switch (format)
        {
            case AccountingExportFormat.Logo:
                // Logo (GO/Tiger) içe-aktarım deseni: tarih + fiş + hesap kodu/adı + VKN + borç/alacak + KDV + döviz.
                Line("Tarih", "Fiş No", "Hesap Kodu", "Hesap Adı", "VKN/TCKN", "Açıklama", "Borç", "Alacak", "KDV", "Döviz");
                foreach (var x in rows)
                    Line(D(x.Date), x.FisNo, x.CustomerCode, x.CustomerName, x.TaxNumber,
                         x.Description ?? x.DocTypeText, A(x.Debt), A(x.Credit), A(x.Kdv), x.CurrencyCode);
                break;

            case AccountingExportFormat.Mikro:
                // Mikro içe-aktarım deseni: evrak no + tarih + cari kod/ünvan + açıklama + borç/alacak + KDV + döviz.
                Line("Evrak No", "Tarih", "Cari Kod", "Cari Ünvan", "VKN/TCKN", "Açıklama", "Borç Tutar", "Alacak Tutar", "KDV", "Döviz Cinsi");
                foreach (var x in rows)
                    Line(x.FisNo, D(x.Date), x.CustomerCode, x.CustomerName, x.TaxNumber,
                         x.Description ?? x.DocTypeText, A(x.Debt), A(x.Credit), A(x.Kdv), x.CurrencyCode);
                break;

            default: // Genel
                Line("Fiş No", "Tarih", "Cari Kod", "Cari Ünvan", "VKN/TCKN", "Belge Tipi", "Açıklama", "Döviz", "Borç", "Alacak", "KDV");
                foreach (var x in rows)
                    Line(x.FisNo, D(x.Date), x.CustomerCode, x.CustomerName, x.TaxNumber, x.DocTypeText,
                         x.Description, x.CurrencyCode, A(x.Debt), A(x.Credit), A(x.Kdv));
                break;
        }
        return sb.ToString();
    }

    /// <summary>e-Defter benzeri özet: belge tipi bazında borç/alacak/KDV toplamı + genel toplam (opsiyonel çıktı).</summary>
    public static string BuildSummaryCsv(IEnumerable<AccountingExportRow> rows)
    {
        var sb = new StringBuilder();
        void Line(params string?[] cells) => sb.Append(string.Join(";", cells.Select(Esc))).Append("\r\n");

        Line("Belge Tipi", "Satır", "Borç", "Alacak", "KDV", "Net (Borç-Alacak)");
        decimal td = 0, tc = 0, tk = 0; int tn = 0;
        foreach (var g in rows.GroupBy(x => x.DocType).OrderBy(g => (int)g.Key))
        {
            decimal d = g.Sum(x => x.Debt), cr = g.Sum(x => x.Credit), k = g.Sum(x => x.Kdv);
            int n = g.Count();
            td += d; tc += cr; tk += k; tn += n;
            Line(g.Key.ToTr(), n.ToString(CultureInfo.InvariantCulture), A(d), A(cr), A(k), A(d - cr));
        }
        Line("TOPLAM", tn.ToString(CultureInfo.InvariantCulture), A(td), A(tc), A(tk), A(td - tc));
        return sb.ToString();
    }

    // ── Biçimlendirme yardımcıları ──
    /// <summary>Tutar: binlik ayraçsız, virgül ondalık (harici muhasebe içe-aktarımı için güvenli).</summary>
    internal static string A(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
    internal static string D(DateTime d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    // BULGU 11: CSV/formül enjeksiyonu nötrleştirme + ayraç kaçışı ortak yardımcıda (CsvHelper.Field).
    private static string Esc(string? s) => CsvHelper.Field(s);
}
