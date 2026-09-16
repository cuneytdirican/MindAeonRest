using System;
using System.Collections.Generic;
using MindAeonRest.Models;
using MindAeonRest.Services;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// FAZ 4a Muhasebe Aktarım — saf (DB'siz) CSV üretim / biçimlendirme testleri. BuildCsv/BuildSummaryCsv
/// üretim koduyla aynı sınıftır (ProjectReference). Amaç: kolon düzeni + tutar biçimi + özet toplamları.
/// </summary>
public class AccountingExportTests
{
    private static List<AccountingExportRow> Sample() => new()
    {
        new AccountingExportRow
        {
            Date = new DateTime(2026, 7, 3), FisNo = "SF-001", CustomerCode = "120.001",
            CustomerName = "Acme A.Ş.", TaxNumber = "1234567890", DocType = CariDocType.SatisFaturasi,
            Description = "Satış", CurrencyCode = "TRY", Debt = 1180m, Credit = 0m, Kdv = 180m
        },
        new AccountingExportRow
        {
            Date = new DateTime(2026, 7, 4), FisNo = "TH-009", CustomerCode = "120.001",
            CustomerName = "Acme A.Ş.", DocType = CariDocType.Tahsilat,
            Description = "Nakit tahsilat", CurrencyCode = "TRY", Debt = 0m, Credit = 500m, Kdv = 0m
        },
    };

    [Fact]
    public void BuildCsv_Genel_HasHeaderAndCommaDecimalAmounts()
    {
        string csv = AccountingExportService.BuildCsv(Sample(), AccountingExportFormat.Genel);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Fiş No;Tarih;Cari Kod;Cari Ünvan;VKN/TCKN;Belge Tipi;Açıklama;Döviz;Borç;Alacak;KDV", lines[0]);
        Assert.Equal(3, lines.Length); // başlık + 2 satır
        // Tutar virgül ondalık, binlik ayraçsız; tarih dd.MM.yyyy.
        Assert.Contains("03.07.2026", lines[1]);
        Assert.Contains("1180,00", lines[1]);
        Assert.Contains("180,00", lines[1]);
    }

    [Fact]
    public void BuildCsv_LogoAndMikro_HaveDistinctColumnOrders()
    {
        var rows = Sample();
        string logo = AccountingExportService.BuildCsv(rows, AccountingExportFormat.Logo);
        string mikro = AccountingExportService.BuildCsv(rows, AccountingExportFormat.Mikro);

        Assert.StartsWith("Tarih;Fiş No;Hesap Kodu", logo);      // Logo: tarih önce
        Assert.StartsWith("Evrak No;Tarih;Cari Kod", mikro);      // Mikro: evrak no önce
        Assert.NotEqual(logo, mikro);
    }

    [Fact]
    public void BuildSummaryCsv_GroupsByDocType_AndTotalsMatch()
    {
        string csv = AccountingExportService.BuildSummaryCsv(Sample());
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // başlık + 2 belge tipi + TOPLAM
        Assert.Equal(4, lines.Length);
        var total = Array.Find(lines, l => l.StartsWith("TOPLAM"))!;
        // Borç 1180, Alacak 500, KDV 180, Net 680
        Assert.Contains("1180,00", total);
        Assert.Contains("500,00", total);
        Assert.Contains("680,00", total);
    }

    [Fact]
    public void BuildCsv_EscapesSeparatorInText()
    {
        var rows = new List<AccountingExportRow>
        {
            new() { Date = new DateTime(2026,1,1), CustomerCode = "1", CustomerName = "A;B Ltd",
                    DocType = CariDocType.Manuel, CurrencyCode = "TRY", Debt = 10m }
        };
        string csv = AccountingExportService.BuildCsv(rows, AccountingExportFormat.Genel);
        Assert.Contains("\"A;B Ltd\"", csv); // ayraç içeren metin tırnaklanır
    }

    // BULGU 11: kullanıcı-denetimli alan formül/DDE ile başlarsa Excel/Logo/Mikro'da yürümesin (tek tırnak nötrleştirme).
    [Fact]
    public void BuildCsv_NeutralizesFormulaInjectionInCustomerName()
    {
        var rows = new List<AccountingExportRow>
        {
            new() { Date = new DateTime(2026,1,1), CustomerCode = "120.001", CustomerName = "=cmd|' /C calc'!A0",
                    DocType = CariDocType.SatisFaturasi, CurrencyCode = "TRY", Debt = 100m, Kdv = 0m }
        };
        string csv = AccountingExportService.BuildCsv(rows, AccountingExportFormat.Genel);
        Assert.Contains("'=cmd", csv);          // '=' önüne tek tırnak eklendi → formül yürümez
        Assert.DoesNotContain(";=cmd", csv);    // ham '=' ile başlayan hücre KALMADI
    }
}
