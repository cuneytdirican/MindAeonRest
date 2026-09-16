namespace MindAeonRest.Helpers;

/// <summary>Rol bazlı POS yetkileri. Roller: SysAdmin, Garson, Kasiyer, Mutfak.</summary>
public static class Permissions
{
    private static bool Admin => Session.IsSysAdmin;
    private static bool Has(string role) => Session.HasRole(role);

    /// <summary>Sipariş alma (adisyon, paket, kiosk).</summary>
    public static bool Orders => Admin || Has("Garson") || Has("Kasiyer");
    /// <summary>Ödeme alma / adisyon kapatma.</summary>
    public static bool Payment => Admin || Has("Kasiyer");
    /// <summary>İade / geri ödeme yetkisi (yalnız 'İade' rolü olan kasiyer ya da yönetici; yoksa başka yetkili onaylar).</summary>
    public static bool Refund => Admin || Has("İade");
    /// <summary>Mutfak (KDS) ekranı.</summary>
    public static bool Kitchen => Admin || Has("Mutfak") || Has("Kasiyer");
    /// <summary>Kasa işlemleri (giriş/çıkış/transfer).</summary>
    public static bool Cash => Admin || Has("Kasiyer");
    /// <summary>X/Z raporları, dashboard.</summary>
    public static bool Reports => Admin || Has("Kasiyer");
    /// <summary>Z (gün sonu) raporu ALMA: yalnız yönetici veya 'Z_Rapor' rolü.</summary>
    public static bool ZReport => Admin || Has("Z_Rapor");
    /// <summary>AI Hub kullanımı (atanmış rollerden HERHANGİ biri yetkiliyse; SysAdmin her zaman).</summary>
    public static bool Ai
    {
        get
        {
            if (Admin) return true;
            if (Session.CurrentUser is null) return false;
            var svc = new Services.RoleService();
            var roles = Session.CurrentRoles.Count > 0 ? Session.CurrentRoles : new List<string> { Session.CurrentUser.RoleName };
            return roles.Any(r => svc.CanUseAi(r));
        }
    }
}
