namespace MindAeonRest.Models;

/// <summary>Cari tipi: müşteri, tedarikçi veya her ikisi.</summary>
public enum CustomerType { Musteri = 0, Tedarikci = 1, Her = 2 }

public static class CustomerTypeExtensions
{
    public static string ToTr(this CustomerType t) => t switch
    {
        CustomerType.Tedarikci => "Tedarikçi",
        CustomerType.Her => "Müşteri + Tedarikçi",
        _ => "Müşteri"
    };
}

/// <summary>Proje kodu (evraklarda zorunlu seçilir).</summary>
public class Project : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
}

/// <summary>Gider tipi (kategori). Maaş tipi seçilirse gider fişinde personel istenir.</summary>
public class ExpenseType : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSalary { get; set; }
    public bool IsActive { get; set; } = true;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string SalaryText => IsSalary ? "Maaş (personel seçilir)" : "";
}

/// <summary>Personel kartı (login kullanıcısından bağımsız).</summary>
public class Personnel : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Title { get; set; }       // görev
    public string? Department { get; set; }
    public string? NationalId { get; set; }  // TC
    public string? Phone { get; set; }
    public int? BranchId { get; set; }
    public DateTime? HireDate { get; set; }
    public decimal Salary { get; set; }
    public bool IsActive { get; set; } = true;

    public string? BranchName { get; set; }  // gösterim (cross-db join)
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public string SalaryText => Salary.ToString("N2");
    public string HireText => HireDate?.ToString("dd.MM.yyyy") ?? "";
}

public enum LeaveType { Yillik = 0, Ucretsiz = 1, Hastalik = 2, Mazeret = 3, Diger = 9 }

public static class LeaveTypeExtensions
{
    public static string ToTr(this LeaveType t) => t switch
    {
        LeaveType.Yillik => "Yıllık İzin",
        LeaveType.Ucretsiz => "Ücretsiz İzin",
        LeaveType.Hastalik => "Hastalık (Rapor)",
        LeaveType.Mazeret => "Mazeret İzni",
        _ => "Diğer"
    };
}

/// <summary>Personel izin kaydı.</summary>
public class PersonnelLeave : AuditableEntity
{
    public int PersonnelId { get; set; }
    public LeaveType LeaveType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Days { get; set; }
    public string? Note { get; set; }

    public string? PersonnelName { get; set; }
    public string TypeText => LeaveType.ToTr();
    public string RangeText => $"{StartDate:dd.MM.yyyy} - {EndDate:dd.MM.yyyy}";
    public string DaysText => $"{Days} gün";
}
