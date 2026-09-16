namespace MindAeonRest.Models;

/// <summary>Hesap fişi içeriği (önizleme ve baskıda aynı kullanılır).</summary>
public class ReceiptData
{
    public byte[]? Logo { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string? BranchAddress { get; set; }
    public string? BranchTax { get; set; }
    public string? BranchPhone { get; set; }

    public string OrderCode { get; set; } = string.Empty;
    public string? TableCodes { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public DateTime Date { get; set; }

    public List<ReceiptItem> Items { get; set; } = new();
    public List<ReceiptKdv> Kdv { get; set; } = new();
    public decimal Total { get; set; }
    public string? Footer { get; set; }

    /// <summary>Ödeme dökümü — yalnız ödeme-sonrası fişte doldurulur. Boşsa (borç önizleme) ödeme bölümü basılmaz.</summary>
    public List<ReceiptPayment> Payments { get; set; } = new();
    /// <summary>Toplam ödenen tutar (opsiyonel; ödeme-sonrası fişte).</summary>
    public decimal? Paid { get; set; }
    /// <summary>Para üstü (opsiyonel; ödeme-sonrası fişte).</summary>
    public decimal? Change { get; set; }

    public string TotalText => $"₺{Total:N2}";
    public string DateText => Date.ToString("dd.MM.yyyy HH:mm");
    public string PaidText => $"₺{Paid ?? 0:N2}";
    public string ChangeText => $"₺{Change ?? 0:N2}";
}

/// <summary>Fiş ödeme dökümü satırı (yöntem adı + tutar).</summary>
public class ReceiptPayment
{
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AmountText => $"₺{Amount:N2}";
}

public class ReceiptItem
{
    public string? Name { get; set; }
    public decimal Qty { get; set; }
    public decimal Amount { get; set; }
    public bool IsComponent { get; set; }
    public string QtyText => $"{Qty:0.##}x";
    public string AmountText => IsComponent ? "" : $"{Amount:N2}";
}

public class ReceiptKdv
{
    public decimal Rate { get; set; }
    public decimal Matrah { get; set; }
    public decimal Tax { get; set; }
    public string RateText => $"KDV %{Rate:0.##}";
    public string MatrahText => $"{Matrah:N2}";
    public string TaxText => $"{Tax:N2}";
}
