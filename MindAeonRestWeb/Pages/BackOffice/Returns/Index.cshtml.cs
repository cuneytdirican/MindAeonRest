using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Returns;

/// <summary>İade (Ödeme Sonrası) — WPF IadeWindow + RefundDialog birebir karşılığı.
/// Tarihe göre KAPANMIŞ siparişler → sipariş seç (?orderId) → iade edilebilir satırlar →
/// satır iade (miktar + stoğa dön/fire + iade ödeme aracı: nakit kasa / kart POS / yemek çeki hesabı).
/// Yetki: SysAdmin veya 'İade' rolü (WPF Permissions.Refund ile aynı).</summary>
public class IndexModel : PageModel
{
    private readonly OrderService _orders = new();
    private readonly CashRegisterService _cash = new();
    private readonly PosService _pos = new();
    private readonly MealVoucherAccountService _meal = new();
    private readonly EInvoiceService _einvoice = new();
    private readonly UserService _users = new();
    private readonly UserRoleService _userRoles = new();

    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
    [BindProperty(SupportsGet = true)] public int? OrderId { get; set; }

    public List<Order> Orders { get; private set; } = new();
    public Order? SelectedOrder { get; private set; }
    public List<OrderLine> Lines { get; private set; } = new();

    // İade aracı seçenekleri (şubeye göre süzülü) — WPF RefundDialog.Method_Changed.
    public List<(int Id, string Name)> CashRegisters { get; private set; } = new();
    public List<(int Id, string Name)> Poses { get; private set; } = new();
    public List<(int Id, string Name)> MealAccounts { get; private set; } = new();

    public string? Error { get; private set; }
    public string? Message { get; private set; }

    /// <summary>Mevcut kullanıcı 'İade' yetkisine sahip değil ama giriş yapmış; sayfa süpervizör onayı moduna geçer.
    /// (WPF IadeWindow.Refund_Click: yetkisiz kullanıcı SupervisorAuthWindow ile onay alır.)</summary>
    public bool NeedsSupervisor { get; private set; }

    private int BranchId => Session.CurrentBranch?.Id ?? 0;

    // İade yetkisi — WPF Permissions.Refund: SysAdmin veya 'İade' rolü.
    private static bool CanAccess => Session.IsSysAdmin || Session.HasRole("İade");

    // Giriş yapmış herhangi bir kullanıcı (yetkisiz olsa da) sayfaya erişebilir; iade süpervizör onayı ile yapılır.
    private static bool IsLoggedIn => Session.CurrentUserId is not null;

    public IActionResult OnGet()
    {
        if (!CanAccess && !IsLoggedIn) return Forbid();
        NeedsSupervisor = !CanAccess;
        Date ??= DateTime.Today;
        Message = TempData["RefundMessage"] as string;
        Error = TempData["RefundError"] as string;
        Load();
        return Page();
    }

    /// <summary>Seçili satırı iade et — WPF RefundDialog.Ok_Click + IadeWindow.Refund_Click.
    /// Mevcut kullanıcı yetkili değilse formla gelen süpervizör kimlik bilgisi doğrulanır
    /// (WPF SupervisorAuthWindow.TryAuth). WPF birebir: RefundLine userId = onaylayan süpervizör.</summary>
    public IActionResult OnPostRefund(int lineId, decimal refundQty, bool returnToStock, int refundKind, int? instrumentId,
        string? supervisorUser, string? supervisorPassword)
    {
        if (!CanAccess && !IsLoggedIn) return Forbid();
        try
        {
            if (refundQty <= 0) throw new InvalidOperationException("Geçerli bir miktar girin.");
            if (instrumentId is null or 0) throw new InvalidOperationException("İade aracı (kasa/POS/hesap) seçin.");

            // WPF IadeWindow.Refund_Click: yetkili kullanıcı → authUserId = mevcut kullanıcı;
            // yetkisiz → süpervizör onayı → authUserId = onaylayan süpervizör (au.Id). RefundLine bu id ile damgalanır.
            // Yetkisizse doğrula; başarısızsa exception fırlar ve iade yapılmaz. (Parola loglanmaz.)
            int? authUserId = Session.CurrentUserId;
            if (!CanAccess)
                authUserId = AuthorizeSupervisor(supervisorUser, supervisorPassword).Id;

            var cl = _orders.RefundLine(lineId, refundQty, returnToStock, (PaymentKind)refundKind, instrumentId, authUserId);
            TempData["RefundMessage"] = $"İade fişi: {cl.Code} · Tutar: {cl.Total:N2} ₺";
        }
        catch (Exception ex) { TempData["RefundError"] = ex.Message; }
        return RedirectToPage(new { Date = Date?.ToString("yyyy-MM-dd"), OrderId });
    }

    /// <summary>Süpervizör kimlik doğrulama + 'İade'/SysAdmin yetki kontrolü — WPF SupervisorAuthWindow.TryAuth
    /// + IadeWindow.IsRefundAuthorized birebir. UserService.GetForLogin ile kullanıcıyı bulur,
    /// PasswordHasher.Verify ile parolayı doğrular (parola yalnız doğrulama için; loglanmaz).</summary>
    private User AuthorizeSupervisor(string? username, string? password)
    {
        username = username?.Trim() ?? string.Empty;
        password ??= string.Empty;
        if (username.Length == 0 || password.Length == 0)
            throw new InvalidOperationException("Süpervizör onayı için kullanıcı adı ve şifre girin.");

        var user = _users.GetForLogin(Session.CurrentCompany?.Id ?? 0, Session.CurrentBranch?.Id)
            .FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        if (user is null || string.IsNullOrEmpty(user.PasswordHash) || !PasswordHasher.Verify(password, user.PasswordHash))
            throw new InvalidOperationException("Süpervizör kullanıcı adı veya şifre hatalı.");
        if (!IsRefundAuthorized(user))
            throw new InvalidOperationException($"'{user.FullName}' iade için yetkili değil.");
        return user;
    }

    /// <summary>WPF IadeWindow.IsRefundAuthorized birebir: RoleName SysAdmin veya rollerinde SysAdmin/'İade'.</summary>
    private bool IsRefundAuthorized(User u)
        => string.Equals(u.RoleName, "SysAdmin", StringComparison.OrdinalIgnoreCase)
        || _userRoles.GetRoleNames(u.Id).Any(r =>
               r.Equals("SysAdmin", StringComparison.OrdinalIgnoreCase) || r.Equals("İade", StringComparison.OrdinalIgnoreCase));

    /// <summary>Seçili (iade edilen) fiş için e-Fatura gönder — WPF IadeWindow.EInvoice_Click karşılığı.</summary>
    public IActionResult OnPostSendEInvoice(int orderId)
    {
        if (!CanAccess) return Forbid();
        try
        {
            var order = _orders.GetClosedOrders(BranchId, Date ?? DateTime.Today).FirstOrDefault(o => o.Id == orderId);
            if (order is null) throw new InvalidOperationException("Sipariş bulunamadı.");

            var res = _einvoice.SendForOrder(order, Session.CurrentUserId);
            if (res.Success)
                TempData["RefundMessage"] = $"e-Fatura gönderildi. No: {res.InvoiceNo} · ETTN: {res.Ettn} {res.Message}".Trim();
            else
                TempData["RefundError"] = res.Message;
        }
        catch (Exception ex) { TempData["RefundError"] = ex.Message; }
        return RedirectToPage(new { Date = Date?.ToString("yyyy-MM-dd"), OrderId = orderId });
    }

    private void Load()
    {
        var date = Date ?? DateTime.Today;
        Orders = _orders.GetClosedOrders(BranchId, date);

        if (OrderId is int oid)
        {
            SelectedOrder = Orders.FirstOrDefault(o => o.Id == oid);
            if (SelectedOrder != null)
                Lines = _orders.GetLines(oid)
                    .Where(l => l.ParentLineId == null && l.SettledClFicheId != null
                                && l.LineStatus != OrderLineStatus.Iptal && !l.IsTreat && l.RefundableQty > 0)
                    .ToList();
        }

        // İade araçları — şube + aktif (WPF RefundDialog ile aynı süzgeç).
        CashRegisters = _cash.GetAll().Where(x => x.IsActive && x.BranchId == BranchId).Select(x => (x.Id, x.Name)).ToList();
        Poses = _pos.GetAll().Where(x => x.IsActive && x.BranchId == BranchId).Select(x => (x.Id, x.Name)).ToList();
        MealAccounts = _meal.GetAll().Where(x => x.IsActive && x.BranchId == BranchId).Select(x => (x.Id, x.Name)).ToList();
    }
}
