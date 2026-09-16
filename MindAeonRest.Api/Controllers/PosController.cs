using Microsoft.AspNetCore.Mvc;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>Ödeme enstrümanları (kasa/POS/yemek çeki hesabı) — WPF ile aynı KIOSK kapsamlaması:
/// bu KIOSK'a atanmış (Scope=Kiosk & KioskId eşleşen) araçlar; yoksa şube araçlarına düşer.</summary>
[Route("api/pos")]
public class PosController : PosControllerBase
{
    /// <summary>Ödeme türüne göre enstrümanlar. kind: 0=Nakit, 1=KrediKarti, 2=YemekCeki.</summary>
    [HttpGet("instruments")]
    public ActionResult<IEnumerable<InstrumentDto>> Instruments([FromQuery] int kind)
    {
        int b = BranchId();
        int? kioskId = new KioskService().GetByNo(b, Session.CurrentKioskNo)?.Id;

        // KIOSK bağlamında SADECE o KIOSK'un araçları; aksi halde ŞUBE araçları. Boşsa (bloklamamak için) tümüne düş.
        bool InScope(FinanceScope sc, int? kid) => kioskId is int k ? (sc == FinanceScope.Kiosk && kid == k) : (sc == FinanceScope.Sube);

        List<InstrumentDto> Scoped<T>(IEnumerable<T> all, Func<T, int> id, Func<T, string> name, Func<T, FinanceScope> sc, Func<T, int?> kid)
        {
            var list = all.ToList();
            InstrumentDto Sel(T x) => new(id(x), name(x), sc(x).ToTr());
            var scoped = list.Where(x => InScope(sc(x), kid(x))).Select(Sel).ToList();
            return scoped.Count > 0 ? scoped : list.Select(Sel).ToList();
        }

        var pk = (PaymentKind)kind;
        List<InstrumentDto> result;
        if (pk == PaymentKind.Nakit)
            result = Scoped(new CashRegisterService().GetAll().Where(x => x.IsActive && x.BranchId == b),
                x => x.Id, x => x.Name, x => x.Scope, x => x.KioskId);
        else if (pk.IsCardType())   // Kredi/Banka/Temassız/QR/Mobil/Online → POS cihazı
            result = Scoped(new PosService().GetAll().Where(x => x.IsActive && x.BranchId == b),
                x => x.Id, x => x.Name, x => x.Scope, x => x.KioskId);
        else if (pk == PaymentKind.YemekCeki)
            result = Scoped(new MealVoucherAccountService().GetAll().Where(x => x.IsActive && x.BranchId == b),
                x => x.Id, x => x.Name, x => x.Scope, x => x.KioskId);
        else if (pk == PaymentKind.HavaleEft)   // banka hesapları (firma geneli; şube/kiosk kapsamı yok)
            result = new BankAccountService().GetAll().Where(x => x.IsActive)
                .Select(x => new InstrumentDto(x.Id, x.Name, "Banka")).ToList();
        else
            result = new List<InstrumentDto>();   // Hediye(kod ile) / Puan(müşteri ile) ayrı uçlardan
        return Ok(result);
    }
}
