using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MindAeonRest.Api.Auth;

namespace MindAeonRest.Api.Hubs;

/// <summary>Gerçek zamanlı POS hub'ı. Cihazlar bağlanınca şubelerinin grubuna girer.
/// Mutfak "Hazır" deyince / sipariş değişince sunucu bu grup üzerinden anlık push yapar.
/// Ek olarak HEDEFLİ "alarm" grupları (garson/mutfak/reyon/kullanıcı) — yalnız ilgili istemci tetiklenir.</summary>
[Authorize]
public class PosHub : Hub
{
    // MEVCUT: tüm "tazele" olayları (NewTicket/TicketReady/TableChanged/ServiceChanged) buraya gider — DOKUNULMADI.
    public static string BranchGroup(string companyCode, int branchId) => $"branch:{companyCode}:{branchId}";

    // YENİ hedefli alarm grupları (BranchGroup deseninde).
    public static string WaitersGroup(string companyCode, int branchId) => $"branch:{companyCode}:{branchId}:waiters";
    public static string KitchenGroup(string companyCode, int branchId) => $"branch:{companyCode}:{branchId}:kitchen";
    public static string KitchenReyonGroup(string companyCode, int branchId, int reyonId) => $"branch:{companyCode}:{branchId}:kitchen:reyon:{reyonId}";
    public static string UserGroup(string companyCode, int userId) => $"user:{companyCode}:{userId}";

    /// <summary>JWT bağlamından (şirket kodu, şube) çözer — OnConnected/JoinReyon ortak yardımcı. Bağlam yoksa null.</summary>
    private (string Company, int Branch)? Ctx()
    {
        var code = Context.User?.FindFirst(JwtTokenService.ClaimCompanyCode)?.Value;
        var branch = Context.User?.FindFirst(JwtTokenService.ClaimBranchId)?.Value;
        return !string.IsNullOrEmpty(code) && int.TryParse(branch, out var b) ? (code, b) : null;
    }

    public override async Task OnConnectedAsync()
    {
        if (Ctx() is (var code, var b))
        {
            // MEVCUT şube grubu — regresyon yok, tüm istemciler bununla tazeler.
            await Groups.AddToGroupAsync(Context.ConnectionId, BranchGroup(code, b));

            // YENİ: kullanıcı-özel grup (hedefli push için — userId = 'sub' claim'i, TenantMiddleware ile aynı).
            if (int.TryParse(Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) && userId > 0)
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(code, userId));

            // YENİ: rol-bazlı alarm grupları (roller ClaimTypes.Role → IsInRole çalışır).
            var u = Context.User!;
            if (u.IsInRole("Garson") || u.IsInRole("Kasiyer") || u.IsInRole("SysAdmin"))
                await Groups.AddToGroupAsync(Context.ConnectionId, WaitersGroup(code, b));
            if (u.IsInRole("Mutfak") || u.IsInRole("Kasiyer") || u.IsInRole("SysAdmin"))
                await Groups.AddToGroupAsync(Context.ConnectionId, KitchenGroup(code, b));
        }
        await base.OnConnectedAsync();
    }

    /// <summary>Reyon-özel KDS: istemci yalnız ilgilendiği reyonun "KitchenNew" alarmına abone olur.</summary>
    public Task JoinReyon(int reyonId) =>
        Ctx() is (var code, var b)
            ? Groups.AddToGroupAsync(Context.ConnectionId, KitchenReyonGroup(code, b, reyonId))
            : Task.CompletedTask;

    /// <summary>Reyon aboneliğini bırak.</summary>
    public Task LeaveReyon(int reyonId) =>
        Ctx() is (var code, var b)
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, KitchenReyonGroup(code, b, reyonId))
            : Task.CompletedTask;
}

/// <summary>Hub olaylarını (controller'lardan) tek noktadan yayınlar.</summary>
public class PosNotifier(IHubContext<PosHub> hub)
{
    // NewTicket/TableChanged: argümansız sinyal (client refetch eder) — handler imzası 0-arg ile eşleşir.
    public Task NewKitchenTicket(string companyCode, int branchId, object? ticket = null) =>
        hub.Clients.Group(PosHub.BranchGroup(companyCode, branchId)).SendAsync("NewTicket");

    // TicketReady: lineId payload'lı (garson hangi satır olduğunu bilsin) — handler 1-arg.
    public Task KitchenTicketReady(string companyCode, int branchId, object payload) =>
        hub.Clients.Group(PosHub.BranchGroup(companyCode, branchId)).SendAsync("TicketReady", payload);

    public Task TableChanged(string companyCode, int branchId, object? payload = null) =>
        hub.Clients.Group(PosHub.BranchGroup(companyCode, branchId)).SendAsync("TableChanged");

    // ServiceChanged: masa servis isteği (çağrı/temizlik/hesap) oluştu/tamamlandı — argümansız sinyal.
    public Task ServiceChanged(string companyCode, int branchId, object? payload = null) =>
        hub.Clients.Group(PosHub.BranchGroup(companyCode, branchId)).SendAsync("ServiceChanged");

    // ===== YENİ hedefli "alarm" olayları (tazele olaylarına EK; onların yerine geçmez) =====

    /// <summary>KitchenNew: yeni fiş mutfağa düştü. TÜM mutfak istemcileri (KitchenGroup) alarm alır; reyon
    /// filtresi payload'daki reyonIds ile İSTEMCİ tarafında yapılır (reyon-özel KDS yalnız kendi reyonunda çalar).
    /// Not: SignalR Clients.Groups gruplar-arası tekilleştirmediğinden reyon grubuna AYRICA yollamayız (çift ses olurdu).</summary>
    public Task KitchenNew(string companyCode, int branchId, IEnumerable<int> reyonIds, object payload)
        => hub.Clients.Group(PosHub.KitchenGroup(companyCode, branchId)).SendAsync("KitchenNew", payload);

    /// <summary>WaiterReady: kalem hazır → yalnız garson grubu. İstemci ownerUserId'yi kendi userId'siyle
    /// karşılaştırıp belirginleştirir. payload { lineId, ownerUserId, tableCode, productName }.</summary>
    public Task WaiterReady(string companyCode, int branchId, int ownerUserId, object payload) =>
        hub.Clients.Group(PosHub.WaitersGroup(companyCode, branchId)).SendAsync("WaiterReady", payload);

    /// <summary>OnlineOrder: dış platform (Yemeksepeti/Getir vb.) siparişi geldi → garson/kasa panosu alarmı.</summary>
    public Task OnlineOrderArrived(string companyCode, int branchId, object payload) =>
        hub.Clients.Group(PosHub.WaitersGroup(companyCode, branchId)).SendAsync("OnlineOrder", payload);
}
