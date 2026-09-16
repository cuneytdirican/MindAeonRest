using MindAeonRest.Licensing;
using MindAeonRest.Models;

namespace MindAeonRest.Helpers;

/// <summary>Oturum açmış kullanıcı ve seçili şirket/şube bağlamı.
/// WPF tek kullanıcılıdır → global state. Web API çok-isteklidir → <see cref="BeginScope"/> ile
/// her istek kendi izole (AsyncLocal) bağlamını taşır; servisler değişmeden çok-kiracılı çalışır.</summary>
public static class Session
{
    private sealed class State
    {
        public User? User;
        public LicenseStatus? License;
        public Company? Company;
        public CompanyBranch? Branch;
        public int KioskNo = 1;
        public int? KioskId;
        public List<string> Roles = new();
        // Web (S3) SysAdmin kiracı geçişi — cookie claim'lerinden middleware doldurur; WPF/API'de varsayılan kalır.
        public string? SysAdminHomeCode;
        public bool Impersonating;
    }

    private static readonly State _global = new();
    private static readonly AsyncLocal<State?> _scoped = new();
    private static State S => _scoped.Value ?? _global;

    /// <summary>API: her HTTP isteği için izole bağlam başlatır. <c>using</c> ile kapsamlanır;
    /// içinde set edilen Session değerleri yalnız o isteğe (async akışına) aittir, başka isteğe sızmaz.</summary>
    public static IDisposable BeginScope()
    {
        _scoped.Value = new State();
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _scoped.Value = null;
    }

    public static User? CurrentUser { get => S.User; set => S.User = value; }

    /// <summary>Aktif şirketin lisans durumu (login'de set edilir). Modül/limit kısıtlamaları buna göre.</summary>
    public static LicenseStatus? CurrentLicense { get => S.License; set => S.License = value; }
    /// <summary>Bu özellik (modül) lisans pakette açık mı?</summary>
    public static bool LicenseHas(Feature f) => CurrentLicense?.Has(f) ?? false;
    public static Company? CurrentCompany { get => S.Company; set => S.Company = value; }
    public static CompanyBranch? CurrentBranch { get => S.Branch; set => S.Branch = value; }

    public static int? CurrentUserId => CurrentUser?.Id;

    /// <summary>Bu terminalin KIOSK numarası (appsettings.json "Kiosk:No"). Login'de set edilir, DEĞİŞTİRİLEMEZ.
    /// Tüm hareketlere (sipariş, kasa/POS hareketi, fiş) damgalanır; rapor/dashboard bu numaraya göre süzer.</summary>
    public static int CurrentKioskNo { get => S.KioskNo; set => S.KioskNo = value; }

    /// <summary>CurrentKioskNo'nun karşılık geldiği (şubedeki) Kiosk kaydının Id'si — ödeme kapsam filtresinde kullanılır.</summary>
    public static int? CurrentKioskId { get => S.KioskId; set => S.KioskId = value; }

    /// <summary>Oturumun TÜM rolleri (çoklu). Girişte doldurulur; boşsa birincil role düşülür.</summary>
    public static List<string> CurrentRoles { get => S.Roles; set => S.Roles = value; }

    /// <summary>Kullanıcının (herhangi) bu role sahip olup olmadığı.</summary>
    public static bool HasRole(string role)
    {
        if (CurrentRoles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase))) return true;
        return CurrentRoles.Count == 0 && CurrentUser?.RoleName.Equals(role, StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool IsSysAdmin => HasRole("SysAdmin");

    /// <summary>Aktif şirketin kodu (kısayol). IsSysAdmin/CurrentCompany deseniyle paralel okuma.</summary>
    public static string? CompanyCode => CurrentCompany?.Code;

    /// <summary>Web S3: SysAdmin geçiş modunda mı? (sa_imp claim'i → middleware doldurur). Aksi halde false.</summary>
    public static bool IsImpersonating { get => S.Impersonating; set => S.Impersonating = value; }

    /// <summary>Web S3: SysAdmin geçiş modundaysa ORİJİNAL (ev) şirket kodu (sa_home claim'i). "Süper Admin'e dön" bunu kullanır.</summary>
    public static string? SysAdminHomeCode { get => S.SysAdminHomeCode; set => S.SysAdminHomeCode = value; }

    public static void Clear()
    {
        CurrentUser = null;
        CurrentCompany = null;
        CurrentBranch = null;
        CurrentRoles = new();
        CurrentKioskId = null;
        CurrentLicense = null;
        SysAdminHomeCode = null;
        IsImpersonating = false;
        // CurrentKioskNo terminal kimliği — config'ten gelir, Clear ile sıfırlanmaz.
    }
}
