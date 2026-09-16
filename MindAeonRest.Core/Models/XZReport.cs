namespace MindAeonRest.Models;

/// <summary>X (anlık) / Z (gün sonu) rapor verisi — bir şube + gün.</summary>
public class XZReport
{
    public DateTime Date { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public decimal SalesTotal { get; set; }
    public int FisCount { get; set; }
    public int OrderCount { get; set; }
    public decimal FireQty { get; set; }
    public decimal TreatTotal { get; set; }

    public List<ReportPay> Payments { get; set; } = new();
    public List<ReportKdv> Kdv { get; set; } = new();
    public List<CashBalance> CashBalances { get; set; } = new();
    public List<BankBalance> BankBalances { get; set; } = new();
    public List<PosBalance> PosBalances { get; set; } = new();
    public List<MealVoucherBalance> MealVoucherBalances { get; set; } = new();

    public string SalesText => $"₺{SalesTotal:N2}";
    public string TreatText => $"₺{TreatTotal:N2}";
    public string DateText => Date.ToString("dd.MM.yyyy");
}

public class ReportPay
{
    public PaymentKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string KindText => Kind.ToTr();
    public string AmountText => $"₺{Amount:N2}";
}

/// <summary>Yemek çeki defter bakiyesi (rapor/XAML için isimli model).</summary>
public class MealVoucherBalance
{
    public int AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string BalanceText => $"₺{Balance:N2}";
}

public class ReportKdv
{
    public decimal Rate { get; set; }
    public decimal Matrah { get; set; }
    public decimal Tax { get; set; }
    public string RateText => $"KDV %{Rate:0.##}";
    public string MatrahText => $"₺{Matrah:N2}";
    public string TaxText => $"₺{Tax:N2}";
}
