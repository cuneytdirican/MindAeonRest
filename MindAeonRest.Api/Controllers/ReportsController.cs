using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>Patron raporları. Açılış ekranı "patron" olan kullanıcılar içindir.
/// SysAdmin, Kasiyer ve kiracı yöneticisi (Patron/Yönetici — PWA "Patron Panel" bu uçları çağırır; provisioning ilk yöneticiyi
/// "Yönetici" rolüyle açar, aksi halde panel 403 alıyordu). Salt-okuma; JWT şirket DB'sine kapsamlı.
/// branchId şube-aşırı geçersiz kılma SADECE SysAdmin için kabul edilir; aksi halde oturum şubesine zorlanır (IDOR koruması).</summary>
[Authorize(Roles = "SysAdmin,Kasiyer,Patron,Yönetici,Yonetici")]
[Route("api/reports")]
public class ReportsController : PosControllerBase
{
    private readonly DashboardService _dashboard = new();
    private readonly ReportService _reports = new();

    /// <summary>Şube özeti/dashboard (ciro, masa, kasa, en çok satan, garson…).</summary>
    [HttpGet("dashboard")]
    public ActionResult<object> Dashboard([FromQuery] int? branchId, [FromQuery] DateTime? date)
    {
        int b = ScopedBranch(branchId);
        if (b <= 0) return BadRequest("branchId gerekli.");
        return Ok(_dashboard.Get(b, date ?? DateTime.Today));
    }

    /// <summary>Z raporları listesi (tarih aralığı; SysAdmin başka şube sorgulayabilir, aksi halde oturum şubesi).</summary>
    [HttpGet("z-list")]
    public ActionResult<object> ZList([FromQuery] int? branchId, [FromQuery] DateTime from, [FromQuery] DateTime to) =>
        Ok(_reports.GetZReportList(ScopedNullableBranch(branchId), null, from, Until(to)));

    /// <summary>Canlı masa durumu (anlık doluluk + tutar).</summary>
    [HttpGet("tables-live")]
    public ActionResult<object> TablesLive([FromQuery] int? branchId, [FromQuery] int? zoneId) =>
        Ok(_reports.GetTableLiveStatus(ScopedNullableBranch(branchId), zoneId));

    /// <summary>Fiş / adisyon raporu.</summary>
    [HttpGet("fiche")]
    public ActionResult<object> Fiche([FromQuery] int? branchId, [FromQuery] DateTime from, [FromQuery] DateTime to) =>
        Ok(_reports.GetFicheReport(ScopedBranch(branchId), from, Until(to)));

    /// <summary>İptal/iade raporu (sebeplerle).</summary>
    [HttpGet("cancellations")]
    public ActionResult<object> Cancellations([FromQuery] int? branchId, [FromQuery] DateTime from, [FromQuery] DateTime to) =>
        Ok(_reports.GetCancellationReport(ScopedBranch(branchId), from, Until(to)));

    /// <summary>KIOSK kasa anlık para durumu.</summary>
    [HttpGet("kiosk-money")]
    public ActionResult<object> KioskMoney([FromQuery] int? branchId, [FromQuery] DateTime from, [FromQuery] DateTime to) =>
        Ok(_reports.GetKioskMoney(ScopedNullableBranch(branchId), null, from, Until(to)));

    /// <summary>Yalnız SysAdmin şube-aşırı sorgulayabilir. Diğer roller için branchId yok sayılır → oturum şubesi.</summary>
    private bool CanCrossBranch => Session.IsSysAdmin;

    /// <summary>İstenen şubeyi yetkiye göre çözer: SysAdmin verdiği branchId'yi (yoksa oturum şubesi) kullanır;
    /// diğer roller HER ZAMAN oturum şubesine zorlanır (şube-aşırı IDOR engellenir).</summary>
    private int ScopedBranch(int? q) => CanCrossBranch ? (q ?? Session.CurrentBranch?.Id ?? 0) : (Session.CurrentBranch?.Id ?? 0);

    /// <summary>SysAdmin: branchId null → tüm şubeler (null), aksi halde verilen şube.
    /// Diğer roller: her zaman oturum şubesi.</summary>
    private int? ScopedNullableBranch(int? q) => CanCrossBranch ? (q ?? Session.CurrentBranch?.Id) : Session.CurrentBranch?.Id;

    private static DateTime Until(DateTime to) => to.Date == to ? to.Date.AddDays(1).AddSeconds(-1) : to;
}
