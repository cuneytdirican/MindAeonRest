using MindAeonRest.Models;
using Xunit;

namespace MindAeonRest.Core.Tests;

/// <summary>
/// Ödeme-sonrası fiş snapshot'ının ödeme-dökümü modeli (ReceiptData.Payments/Paid/Change).
/// Saf POCO davranışı: DB/servis yok. Fiş görselinin "ÖDEME" bölümünü basıp basmama kararı
/// Payments listesinin dolu olup olmamasına bağlıdır (ReceiptVisual.Build gating'i ile aynı koşul).
/// </summary>
public class ReceiptSnapshotTests
{
    /// <summary>Borç önizleme (açık-masa): ödeme yöntemi eklenmez → ÖDEME bölümü basılmaz.</summary>
    [Fact]
    public void PreviewReceipt_HasNoPayments()
    {
        var d = new ReceiptData { Total = 100m };
        Assert.Empty(d.Payments);
        Assert.Null(d.Paid);
        Assert.Null(d.Change);
        // ReceiptVisual.Build koşulu: Payments boşsa bölüm eklenmez.
        Assert.False(d.Payments is { Count: > 0 });
    }

    /// <summary>Ödeme-sonrası fiş: her yöntem + Ödenen + Para Üstü doğru doldurulur/biçimlenir.</summary>
    [Fact]
    public void PostPaymentReceipt_FillsPaymentBreakdown()
    {
        // Complete_Click'teki doldurma mantığının aynısı (saf simülasyon).
        decimal total = 250m, alreadyPaid = 0m;
        var collectedLines = new (PaymentKind Kind, decimal Amount)[]
        {
            (PaymentKind.Nakit, 100m),
            (PaymentKind.KrediKarti, 150m),
        };
        decimal remaining = total - alreadyPaid;
        decimal collected = 0m;

        var d = new ReceiptData { Total = total };
        foreach (var p in collectedLines)
        {
            d.Payments.Add(new ReceiptPayment { Label = p.Kind.ToTr(), Amount = p.Amount });
            collected += p.Amount;
        }
        d.Paid = collected;
        d.Change = System.Math.Max(0, collected - remaining);

        Assert.True(d.Payments is { Count: > 0 });      // ÖDEME bölümü basılır
        Assert.Equal(2, d.Payments.Count);
        Assert.Equal("Nakit", d.Payments[0].Label);
        Assert.Equal("Kredi Kartı", d.Payments[1].Label);
        Assert.Equal($"₺{100m:N2}", d.Payments[0].AmountText);   // kültürden bağımsız beklenen biçim
        Assert.Equal(250m, d.Paid);
        Assert.Equal(0m, d.Change);                     // tam ödeme → para üstü yok
    }

    /// <summary>Para üstü: toplanan kalanı aşarsa Change pozitif olur.</summary>
    [Fact]
    public void PostPaymentReceipt_ComputesChange_WhenOverpaid()
    {
        decimal total = 90m, alreadyPaid = 0m, collected = 100m;
        decimal remaining = total - alreadyPaid;

        var d = new ReceiptData
        {
            Total = total,
            Paid = collected,
            Change = System.Math.Max(0, collected - remaining),
        };
        d.Payments.Add(new ReceiptPayment { Label = PaymentKind.Nakit.ToTr(), Amount = collected });

        Assert.Equal(10m, d.Change);
        Assert.True(d.Change is > 0.005m);   // ReceiptVisual "Para Üstü" satırını gösterir
    }
}
