using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using MindAeonRest.Api.Auth;
using MindAeonRest.Api.Hubs;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Api.Controllers;

/// <summary>ANONİM (müşteri) erişim katmanı — QR masadan-sipariş (Faz 2 PWA), public menü.
/// Tüm uçlar <see cref="AllowAnonymousAttribute"/>; kimlik yerine query'deki kısıtlı-scope public token
/// (<see cref="PublicTokenService"/>) ELLE doğrulanır. Token'a ŞUBE+MASA gömülüdür → müşteri yalnız kendi
/// masasının menüsünü görür / kendi masasına sipariş verir; başka şube/masaya erişemez (IDOR imkânsız).
/// Normal /api [Authorize] uçları ETKİLENMEZ: public token ayrı audience taşır, JwtBearer onu reddeder.
/// Rate-limit ("public" politikası, IP başına) kötüye kullanımı sınırlar.</summary>
// Rate-limit AYRIMI (uç bazlı): salt-okuma görsel/menü trafiği "public-read" (cömert ~240/dk) ile sipariş
// bütçesini TÜKETMEZ; yazma ucu "order" ayrı "public" (makul ~30-60/dk) politikasına bağlıdır. Sınıf düzeyinde
// tek politika yoktu → menüde görseller lazy-load olurken kota dolup POST /order 429 dönüyordu (sipariş oluşmuyordu).
[ApiController]
[AllowAnonymous]
[Route("api/public")]
public class PublicController(PublicTokenService tokens, PosNotifier notifier) : ControllerBase
{
    private readonly PosCatalogService _catalog = new();
    private readonly OrderService _orders = new();
    private readonly TableService _tables = new();
    private readonly CustomerService _customers = new();
    private readonly ProductService _products = new();
    private readonly ProductCategoryService _cats = new();
    private readonly ServiceRequestService _svc = new();

    // ===== Idempotency (QR müşteri çift-tık / kaybolan-yanıt emici katman) =====
    // Kötü bağlantıda müşteri "Sipariş Ver"e tekrar basarsa (ya da yanıt kaybolup istemci yeniden denerse)
    // aynı 'Idempotency-Key' ile POST'lar gelir → ikinci istek İLK isteğin sonucunu döner; AddLine+SendToKitchen
    // TEKRAR ÇALIŞMAZ → çift mutfak fişi / çift stok düşüşü YOK. OrdersController deseni: process-içi statik
    // MemoryCache (tek-süreç host). Key şirket+şube+MASA ile izole (bir masanın key'i başka masayı etkilemez).
    private static readonly IMemoryCache _idem = new MemoryCache(new MemoryCacheOptions());
    private static readonly TimeSpan IdemTtl = TimeSpan.FromHours(24);
    private sealed record IdemEntry(int Status, object? Body);

    private string? IdemKey(PublicContext ctx)
        => Request.Headers.TryGetValue("Idempotency-Key", out var v) && !string.IsNullOrWhiteSpace(v)
            ? $"pubidem:{ctx.CompanyCode}:{ctx.BranchId}:{ctx.TableId}:{v}" : null;

    // Yalnız BAŞARILI (2xx) yanıtı önbelleğe alır (hata/boş-sipariş tekrar denenebilsin).
    private async Task<IActionResult> IdempotentAsync(string? ck, Func<Task<IActionResult>> action)
    {
        if (ck is null) return await action();
        if (_idem.TryGetValue(ck, out IdemEntry? hit) && hit is not null)
            return new ObjectResult(hit.Body) { StatusCode = hit.Status };
        var r = await action();
        if (r is ObjectResult obj)
        {
            int status = obj.StatusCode ?? 200;
            if (status is >= 200 and < 300) _idem.Set(ck, new IdemEntry(status, obj.Value), IdemTtl);
        }
        return r;
    }

    /// <summary>Token'daki şirket/şube bağlamını bu istek için (izole) Session kapsamına yazar.
    /// TenantMiddleware anonim isteklerde çalışmaz → bağlamı burada kurar. KioskNo public/QR için sanal '1'.</summary>
    private static void ApplyContext(PublicContext ctx)
    {
        Session.CurrentCompany = new Company { Code = ctx.CompanyCode, Name = ctx.CompanyCode, DatabaseName = ctx.DbName };
        MindAeonRest.Data.CompanyDatabase.EnsureSchemaOnce(ctx.DbName);
        Session.CurrentBranch = new CompanyBranch { Id = ctx.BranchId };
        Session.CurrentKioskNo = 1;
    }

    /// <summary>QR menüsü: token'daki şubenin kategorileri + satış ürünleri (fiyat/KDV çözülü, salt-okuma).</summary>
    [HttpGet("menu")]
    [EnableRateLimiting("public-read")]
    public IActionResult Menu([FromQuery] string t)
    {
        var ctx = tokens.Validate(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            var categories = _catalog.GetCategories()
                .Select(c => new CategoryDto(c.Id, c.Name, c.Image is { Length: > 0 }));
            var products = _catalog.GetProducts(ctx.BranchId)
                .Where(p => !p.IsSalesStopped)   // satışa kapalı ürünler müşteriye gösterilmez
                .Select(p => new MenuProductDto(p.Id, p.Code, p.Name, p.Description, p.Price, p.TaxRate,
                    p.IsSalesStopped, p.IsMenu, p.Image is { Length: > 0 }, p.CategoryIds.ToList()))
                .ToList();
            return Ok(new { table = ctx.TableId, categories, products });
        }
    }

    /// <summary>QR menü ürün görseli (anonim, token'lı) — MenuController.Image'in public eşleniği.
    /// Görsel yalnız token'daki şube bağlamında servis edilir; salt-okuma binary olduğundan fiyat/sipariş
    /// güvenliğini etkilemez. Geçersiz token → 401, görsel yoksa → 404 (istemci isim/emoji'ye düşer).</summary>
    [HttpGet("product-image/{id:int}")]
    [ResponseCache(Duration = 3600)]
    [EnableRateLimiting("public-read")]
    public IActionResult ProductImage(int id, [FromQuery] string t)
    {
        var ctx = tokens.Validate(t);
        if (ctx is null) return Unauthorized();
        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            var img = _products.GetImages(id).FirstOrDefault();
            if (img?.Image is not { Length: > 0 }) return NotFound();
            return File(img.Image, "image/png");
        }
    }

    /// <summary>QR menü kategori görseli (anonim, token'lı) — MenuController.CategoryImage'in public eşleniği.</summary>
    [HttpGet("category-image/{id:int}")]
    [ResponseCache(Duration = 3600)]
    [EnableRateLimiting("public-read")]
    public IActionResult CategoryImage(int id, [FromQuery] string t)
    {
        var ctx = tokens.Validate(t);
        if (ctx is null) return Unauthorized();
        using (Session.BeginScope())
        {
            ApplyContext(ctx);
            var img = _cats.GetImage(id);
            if (img is not { Length: > 0 }) return NotFound();
            return File(img, "image/png");
        }
    }

    /// <summary>QR sipariş: token'daki masanın açık adisyonuna ekler (yoksa açar), sonra mutfağa gönderir.
    /// Fiyat/KDV SUNUCUDAN (menü) çözülür — istemci fiyat gönderemez. Yalnız token'daki masa/şube (IDOR imkânsız).</summary>
    [HttpPost("order")]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> Order([FromQuery] string t, [FromBody] PublicOrderRequest req)
    {
        var ctx = tokens.Validate(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });
        if (req?.Items is null || req.Items.Count == 0)
            return BadRequest(new { message = "Sipariş boş olamaz." });

        // Idempotency-Key varsa: aynı key ile ikinci istek İLK sonucu döner → çift sipariş yok (aşağıdaki
        // AddLine+SendToKitchen bir kez çalışır). Header yoksa davranış eskisi gibi (additive).
        return await IdempotentAsync(IdemKey(ctx), async () =>
        {
        using (Session.BeginScope())
        {
            ApplyContext(ctx);

            // Savunma derinliği: token'daki masa gerçekten token'daki şubeye mi ait? (token güvenli olsa da doğrula)
            var table = _tables.GetById(ctx.TableId);
            if (table is null || table.BranchId != ctx.BranchId)
                return NotFound(new { message = "Masa bulunamadı." });

            // Menüyü bir kez çöz — sipariş edilen her ürünü fiyat/KDV/satış-durumu için buradan doğrula.
            var menu = _catalog.GetProducts(ctx.BranchId).ToDictionary(p => p.Id);

            // Açık adisyon yoksa PA (perakende) cari ile aç.
            var order = _orders.GetOpenByTable(ctx.TableId);
            if (order is null)
            {
                var pa = EnsurePaCustomer();
                if (pa is null) return BadRequest(new { message = "Varsayılan 'PA' (perakende) cari kartı bulunamadı." });
                order = _orders.CreateOrder(ctx.BranchId, table.ZoneId, pa.Id, new[] { ctx.TableId }, null);
            }

            int added = 0;
            foreach (var item in req.Items)
            {
                if (item.Qty <= 0) continue;
                if (!menu.TryGetValue(item.ProductId, out var prod)) continue;   // menüde olmayan ürün yok sayılır
                if (prod.IsSalesStopped) continue;                                // satışa kapalı ürün eklenmez
                _orders.AddLine(order.Id, order.ZoneId, prod.Id, item.Qty, prod.Price, prod.TaxRate, item.Note, 0, null);
                added++;
            }
            if (added == 0) return BadRequest(new { message = "Sipariş edilebilir geçerli ürün yok." });

            // Kalemleri mutfağa gönder → KDS'e anlık bildirim + masa panosu tazele.
            var tickets = _orders.SendToKitchen(order.Id, null);
            await notifier.NewKitchenTicket(ctx.CompanyCode, ctx.BranchId, tickets);
            // HEDEFLİ alarm: mutfak-geneli + reyon-özel KDS'ler (QR sipariş). Mevcut NewTicket tazele KORUNUR.
            await notifier.KitchenNew(ctx.CompanyCode, ctx.BranchId, tickets.Select(t => t.ReyonId).Distinct(),
                new { tableCode = tickets.FirstOrDefault()?.TableCodes, reyonIds = tickets.Select(t => t.ReyonId).Distinct() });
            await notifier.TableChanged(ctx.CompanyCode, ctx.BranchId);

            return Ok(new { orderId = order.Id, order.Code, added, lines = _orders.GetLines(order.Id) });
        }
        });
    }

    /// <summary>QR "Garson Çağır" / "Hesap İste": token'daki masa için servis isteği açar → personele anlık push.
    /// Kimlik YOK; token'daki şube/masa bağlamı zorlanır (IDOR imkânsız). Yalnız CagriGarson(0)/Hesap(2) tipleri
    /// beyaz-listede; başka tip enjekte edilemez. Create dedup'lı: aynı masa+tip için bekleyen varsa yenisini
    /// açmaz → çift çağrı spam'i olmaz (ekstra idempotency gerekmez). KioskNo public/QR için sanal '1'.</summary>
    [HttpPost("call-waiter")]
    [EnableRateLimiting("public")]
    public async Task<IActionResult> CallWaiter([FromQuery] string t, [FromBody] PublicCallRequest? req)
    {
        var ctx = tokens.Validate(t);
        if (ctx is null) return Unauthorized(new { message = "Geçersiz veya süresi dolmuş bağlantı." });

        using (Session.BeginScope())
        {
            ApplyContext(ctx);

            // Savunma derinliği: token'daki masa gerçekten token'daki şubeye mi ait?
            var table = _tables.GetById(ctx.TableId);
            if (table is null || table.BranchId != ctx.BranchId)
                return NotFound(new { message = "Masa bulunamadı." });

            // Tip beyaz-listesi: yalnız Garson Çağır (0) veya Hesap (2); geri kalan her şey CagriGarson'a düşer.
            var type = req?.Type == (int)ServiceRequestType.Hesap
                ? ServiceRequestType.Hesap
                : ServiceRequestType.CagriGarson;
            var note = string.IsNullOrWhiteSpace(req?.Note) ? null : req!.Note!.Trim();

            _svc.Create(new ServiceRequest
            {
                BranchId = ctx.BranchId,
                TableId = ctx.TableId,
                ZoneId = table.ZoneId,
                Type = type,
                KioskNo = 1,
                Note = note
            }, null);

            await notifier.ServiceChanged(ctx.CompanyCode, ctx.BranchId);
            return Ok(new { ok = true });
        }
    }

    /// <summary>Güvenlik ağı: sabit "PA" (perakende / "Müşterilerimiz") cari kartını SAĞLAR.
    /// SeedDefaults (CompanyDatabase) bunu her DB'ye idempotent eker; ancak eski/geç-migrate tenant'larda
    /// (ör. şema süreç-başına-bir-kez guard'ı seed'den ÖNCE işaretlendiğinde) PA eksik kalabilir → QR sipariş
    /// tümden kırılırdı. Yoksa mevcut CustomerService ile SeedDefaults paritesinde (PA + varsayılan adres)
    /// oluşturur. Session bağlamı çağıran (Order) tarafından ApplyContext ile kurulmuştur. Başarısızsa null →
    /// çağıran net BadRequest döndürür (yeni dosya/servis metoduna gerek YOK).</summary>
    private Customer? EnsurePaCustomer()
    {
        var pa = _customers.GetAll().FirstOrDefault(c => c.Code == "PA");
        if (pa is not null) return pa;
        try
        {
            return _customers.Save(
                new Customer { Code = "PA", Name = "Müşterilerimiz", IsActive = true },
                new[] { new CustomerAddress { Title = "Varsayılan Adres", IsDefault = true } },
                null);
        }
        catch (Exception ex)
        {
            // Eşzamanlı ilk-sipariş yarışı olabilir → tekrar oku; hâlâ yoksa çağırana null bildir.
            System.Console.Error.WriteLine($"[PublicController] PA cari güvenlik-ağı oluşturma hatası: {ex.Message}");
            return _customers.GetAll().FirstOrDefault(c => c.Code == "PA");
        }
    }
}
