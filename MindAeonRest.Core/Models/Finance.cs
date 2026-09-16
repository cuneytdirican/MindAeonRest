namespace MindAeonRest.Models;

/// <summary>Ödeme aracı kapsamı: şubeye mi yoksa bir KIOSK'a mı ait.</summary>
public enum FinanceScope { Sube = 0, Kiosk = 1 }

public static class FinanceScopeExtensions
{
    public static string ToTr(this FinanceScope s) => s == FinanceScope.Kiosk ? "KIOSK Kasası" : "Şube Kasası";
}

/// <summary>KIOSK tanımı (şube bazlı self-servis cihaz). Kasa/POS/Çek hesapları KIOSK'a atanabilir.</summary>
public class Kiosk : AuditableEntity
{
    public int BranchId { get; set; }
    public string? BranchName { get; set; }
    public int No { get; set; } = 1;   // KIOSKNO — appsettings.json'daki numara ile eşleşir (şube içinde tekil)
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ReceiptPrinterName { get; set; }   // Hesap/fiş yazıcısı (bu KIOSK terminalinin)
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"#{No} · {Name} ({Code})";
}

/// <summary>Banka (şirket lookup).</summary>
public class Bank : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}

/// <summary>Banka Hesabı (döviz zorunlu).</summary>
public class BankAccount : AuditableEntity
{
    public int BankId { get; set; }
    public string? BankName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}

/// <summary>Kredi Kartı POS (şube + banka + döviz).</summary>
public class CreditCardPos : AuditableEntity
{
    public int BranchId { get; set; }
    public string? BranchName { get; set; }
    public int BankId { get; set; }
    public string? BankName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public FinanceScope Scope { get; set; } = FinanceScope.Sube;
    public int? KioskId { get; set; }
    public string? KioskName { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string ScopeText => Scope == FinanceScope.Kiosk ? $"KIOSK: {KioskName}" : "Şube";
    public string Display => $"{Name} ({Code})";
}

/// <summary>Nakit Kasa (şube + döviz). Kapsam: Şube veya KIOSK kasası.</summary>
public class CashRegister : AuditableEntity
{
    public int BranchId { get; set; }
    public string? BranchName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public FinanceScope Scope { get; set; } = FinanceScope.Sube;
    public int? KioskId { get; set; }
    public string? KioskName { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string ScopeText => Scope == FinanceScope.Kiosk ? $"KIOSK: {KioskName}" : "Şube";
    public string Display => $"{Name} ({Code})";
}

/// <summary>Yemek Çeki firması (şirket lookup).</summary>
public class MealVoucher : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string Display => $"{Name} ({Code})";
}

/// <summary>Yemek Çek Hesabı (şube + yemek çeki + döviz).</summary>
public class MealVoucherAccount : AuditableEntity
{
    public int BranchId { get; set; }
    public string? BranchName { get; set; }
    public int MealVoucherId { get; set; }
    public string? MealVoucherName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public FinanceScope Scope { get; set; } = FinanceScope.Sube;
    public int? KioskId { get; set; }
    public string? KioskName { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string ScopeText => Scope == FinanceScope.Kiosk ? $"KIOSK: {KioskName}" : "Şube";
    public string Display => $"{Name} ({Code})";
}
