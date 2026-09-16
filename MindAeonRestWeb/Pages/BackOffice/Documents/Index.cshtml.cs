using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.Documents;

/// <summary>Jenerik evrak listesi — WPF DocumentListWindow birebir karşılığı.
/// ?type=&lt;DocType&gt; ile tek liste; başlık type.ToTr(); tarih aralığı + arama filtresi.
/// "+ Yeni" / "Düzenle" / "İşle" (Post/PostTransfer/PostCount tipe göre) / "İptal".
/// Ayrıca: "Yazdır" (Edit?print=1), "⇪ Kaynaktan" (Convert, SourceType() varsa), "📥 Teslim Al" (Receive, DepoTransfer).</summary>
public class IndexModel : PageModel
{
    private readonly DocumentService _docs = new();
    private readonly DocumentPostingService _posting = new();

    [BindProperty(SupportsGet = true)] public DocType Type { get; set; } = DocType.SatisFatura;
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<Document> Items { get; private set; } = new();
    public string Title => Type.ToTr();
    public string? Error { get; private set; }
    public string? Message { get; private set; }

    // Dönüşüm (Convert): bu tip bir kaynaktan (sipariş→irsaliye→fatura) oluşturulabiliyorsa buton görünür.
    public DocType? SourceType => Type.SourceType();
    public bool IsTransfer => Type == DocType.DepoTransfer;

    // Ön Muhasebe kapısı — yalnız SysAdmin/Patron + Feature.PreAccounting.
    private static bool CanAccess => (Session.IsSysAdmin || Session.HasRole("Patron")) && Session.LicenseHas(Feature.PreAccounting);

    /// <summary>WPF "İşle"nin özet etkisi (onay diyaloğu metni).</summary>
    public string PostEffect => Type switch
    {
        DocType.DepoTransfer => "kaynak depodan çıkış yapılacak",
        DocType.Sayim => "sayım kesinleşip stok farkları işlenecek",
        _ when Type.RunsCari() => "cari + stok hareketleri oluşturulacak",
        _ when Type.RunsStock() => "stok hareketleri oluşturulacak",
        _ => "onaylanacak (defter çalışmaz)"
    };

    public string PostLabel => Type == DocType.Sayim ? "Kesinleştir" : "İşle";

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadDefaults();
        Load();
        return Page();
    }

    /// <summary>Tipe göre defter işleme: Transfer→PostTransfer, Sayım→PostCount, diğer→Post.</summary>
    public IActionResult OnPostProcess(int id)
    {
        if (!CanAccess) return Forbid();
        try
        {
            if (Type == DocType.DepoTransfer) _posting.PostTransfer(id, Session.CurrentUserId);
            else if (Type == DocType.Sayim) _posting.PostCount(id, Session.CurrentUserId);
            else _posting.Post(id, Session.CurrentUserId);
            TempData["DocMessage"] = "Evrak işlendi.";
        }
        catch (Exception ex) { TempData["DocError"] = ex.Message; }
        return RedirectToPage(new { Type, From, To, Q });
    }

    /// <summary>İptal — cari + stok kayıtları geri alınır.</summary>
    public IActionResult OnPostCancelDoc(int id)
    {
        if (!CanAccess) return Forbid();
        try { _posting.Cancel(id, Session.CurrentUserId); TempData["DocMessage"] = "Evrak iptal edildi."; }
        catch (Exception ex) { TempData["DocError"] = ex.Message; }
        return RedirectToPage(new { Type, From, To, Q });
    }

    private void LoadDefaults()
    {
        From ??= DateTime.Today.AddMonths(-1);
        To ??= DateTime.Today;
        Message = TempData["DocMessage"] as string;
        Error = TempData["DocError"] as string;
    }

    private void Load() => Items = _docs.GetList(Type, From, To, null, null, Q);
}
