using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using MindAeonRest.Api.Auth;
using MindAeonRest.Api.Hubs;
using MindAeonRestWeb.Middleware;
using MindAeonRestWeb.Services;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// IIS in-process barındırma (subdomain: MindAeonRestWeb.mobilteg.com)
builder.WebHost.UseIIS();

// ===== FAZ 0: Yapısal loglama (Serilog) =====
// Konsol + gün-bazlı rolling dosya (App_Data/logs — Data Protection anahtarları App_Data/keys deseniyle aynı kök).
// Minimum Info; Microsoft/System gürültüsü Warning'e indirilir. appsettings.json "Serilog" bölümü (varsa) override eder.
// UseSerilog global static Log.Logger'ı da kurar → sessiz-catch loglaması (aşağıda) çalışır; host kapanışında flush edilir.
{
    var logDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs");
    try { Directory.CreateDirectory(logDir); } catch { /* dizin oluşturulamazsa Console sink'i yine çalışır */ }
    builder.Host.UseSerilog((ctx, cfg) => cfg
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logDir, "web-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 31,
            shared: true)
        // Opsiyonel override: appsettings.json "Serilog" bölümü varsa yukarıdaki kod ayarlarını genişletir/ezer.
        .ReadFrom.Configuration(ctx.Configuration));
}

// TEK UYGULAMA: back-office (cookie) + POS API /api (JWT) + SignalR /hubs + PWA /app — hepsi aynı origin.
var jwt = new JwtTokenService(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<MindAeonRest.Api.Auth.PublicTokenService>();   // anonim (QR) kısıtlı-scope token üret/doğrula
builder.Services.AddSingleton<MindAeonRest.Api.Auth.PortalTokenService>();   // FAZ 5a Sadakat Portalı: müşteri (telefon+OTP) kısıtlı-scope token
builder.Services.AddSingleton<PosNotifier>();

// Auth: yol'a göre şema seç — /api ve /hubs (veya Bearer header) → JWT; diğer her şey → cookie.
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = "smart";
        o.DefaultChallengeScheme = "smart";
    })
    .AddPolicyScheme("smart", "smart", o =>
    {
        o.ForwardDefaultSelector = ctx =>
        {
            var p = ctx.Request.Path;
            if (p.StartsWithSegments("/api") || p.StartsWithSegments("/hubs"))
                return JwtBearerDefaults.AuthenticationScheme;
            var authz = ctx.Request.Headers.Authorization.ToString();
            if (authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return JwtBearerDefaults.AuthenticationScheme;
            return CookieAuthenticationDefaults.AuthenticationScheme;
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name = "MindAeon.Web";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // Sertleştirme: ÜRETİMDE cookie yalnız HTTPS'te gönderilir (Always). Dev'de (HTTP localhost) SameAsRequest.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        // OTURUM YENİDEN-DOĞRULAMA (S4): SysAdmin bir kullanıcıyı pasifleştirdiğinde AKTİF cookie oturumu da
        // en geç ~120 sn içinde düşsün (aksi halde 12 saatlik sliding cookie yaşamaya devam ederdi). Throttle'lı:
        // her istekte değil, en fazla 2 dk'da bir sistem DB'sinden IsActive kontrolü yapılır.
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async ctx =>
            {
                var idStr = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(idStr, out var userId) || userId <= 0) return;

                const string key = "sa_lastval";
                var now = DateTimeOffset.UtcNow;
                var last = ctx.Properties.GetString(key);
                if (last != null && long.TryParse(last, out var ticks)
                    && now - new DateTimeOffset(ticks, TimeSpan.Zero) < TimeSpan.FromSeconds(120))
                    return;   // throttle penceresi içinde → tekrar sorgulama

                bool active;
                try { active = new MindAeonRest.Services.UserService().IsActive(userId); }
                catch { return; }   // DB geçici hatası → oturumu düşürme (fail-open; sonraki denemede yakalanır)

                if (!active)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }
                ctx.Properties.SetString(key, now.UtcTicks.ToString());
                ctx.ShouldRenew = true;   // properties değişti → cookie yenile
            }
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = jwt.ValidationParameters;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                // access_token query'si: SignalR (/hubs) + <img> ile yüklenen görsel uçları (/api/**/image) —
                // <img> Authorization başlığı gönderemez, bu yüzden token query'den okunur (görsel salt-okuma, düşük hassasiyet).
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/hubs") || (path.Value?.EndsWith("/image", StringComparison.OrdinalIgnoreCase) ?? false)))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// LAN cihazları (MAUI) için CORS; same-origin PWA'yı etkilemez.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// POS controller'ları (MindAeonRest.Api) + SignalR — aynı süreçte.
// Tüm DateTime'lar JSON'a UTC 'Z' son ekiyle yazılsın → istemci (web/PWA POS) 'Z'siz UTC'yi yerel sanıp
// GMT+3'te "+180 dk" gecikme göstermesin (API host'u ile aynı davranış).
static void AddUtcJson(System.Text.Json.JsonSerializerOptions o)
{
    o.Converters.Add(new MindAeonRest.Api.Json.UtcDateTimeConverter());
    o.Converters.Add(new MindAeonRest.Api.Json.NullableUtcDateTimeConverter());
}
builder.Services.AddControllers()
    .AddApplicationPart(typeof(MindAeonRest.Api.Controllers.MenuController).Assembly)
    .AddJsonOptions(o => AddUtcJson(o.JsonSerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => AddUtcJson(o.PayloadSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => AddUtcJson(o.SerializerOptions));

// FAZ 0: RFC7807 ProblemDetails — /api hataları için IProblemDetailsService (aşağıda path-bazlı exception handler kullanır).
builder.Services.AddProblemDetails();

// Data Protection KALICI (IIS app-pool geri dönüşümünde antiforgery/cookie kırılmasını önler).
var dpKeys = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(dpKeys);
builder.Services.AddDataProtection()
    .SetApplicationName("MindAeonRestWeb")
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeys));

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

builder.Services.AddScoped<WebLoginService>();
builder.Services.AddScoped<TwoFactorManager>();   // 2FA (TOTP): IDataProtector + IMemoryCache + Core servisleri
builder.Services.AddScoped<MindAeonRestWeb.Auth.PwaTokenIssuer>();
var licenceApiUrl = builder.Configuration["LicenceApiUrl"]
    ?? MindAeonRest.Helpers.AppConfig.LicenceApiUrl;
builder.Services.AddHttpClient<LicenceCatalogClient>(c => c.BaseAddress = new Uri(licenceApiUrl));

var trTR = new CultureInfo("tr-TR");
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(trTR);
    o.SupportedCultures = new List<CultureInfo> { trTR };
    o.SupportedUICultures = new List<CultureInfo> { trTR };
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Anonim public YAZMA uçları (QR sipariş oluştur vb.) — IP başına makul limit. [EnableRateLimiting("public")].
    o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Anonim public OKUMA uçları (menü + ürün/kategori görselleri, lazy-load) — CÖMERT bütçe. [EnableRateLimiting("public-read")].
    // Ayrı bölüm: müşteri menüde gezinirken görsel istekleri "public"i tüketip POST /order'ı 429'a düşürmesin.
    o.AddPolicy("public-read", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Bağlam-farkında AI Chat widget'ı (back-office /BackOffice/AiAsk?handler=Ask) — kullanıcı/IP başına makul limit.
    // Yalnız [EnableRateLimiting("ai")] taşıyan AiAskModel. Giriş yapmış kullanıcı adı varsa ona, yoksa IP'ye göre bölümle.
    o.AddPolicy("ai", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.User?.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Pricing");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/SelectBranch");
    options.Conventions.AllowAnonymousToPage("/TwoFactorChallenge");   // 2FA adımı: cookie SignIn'den ÖNCE (anon)
    options.Conventions.AllowAnonymousToPage("/Logout");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Order");   // QR masadan-sipariş müşteri sayfası (token'ı PublicController doğrular)
    options.Conventions.AllowAnonymousToPage("/Portal");   // FAZ 5a müşteri sadakat portalı (telefon+OTP; token'ı PortalController doğrular)
    options.Conventions.AllowAnonymousToPage("/Feedback");   // FAZ 5d anket müşteri sayfası (token'ı FeedbackController doğrular)
    options.Conventions.AllowAnonymousToPage("/Reservation");   // FAZ 5c online rezervasyon müşteri sayfası (token'ı ReservationPublicController doğrular)
})
// KRİTİK: sayfa kültürü tr-TR olsa da HTML type="number" girdileri değeri InvariantCulture ('.' ondalık)
// gönderir. Varsayılan binder '.'yi binlik sanıp fiyatı ×10ⁿ ölçekliyordu (125.0000 → 1.250.000). Bu binder
// decimal/double/float'ı önce Invariant, sonra tr-TR ile ayrıştırır → number girdileri + tr-TR metin ikisi de doğru.
.AddMvcOptions(o => o.ModelBinderProviders.Insert(0, new MindAeonRestWeb.Infrastructure.InvariantNumberModelBinderProvider()));

var app = builder.Build();

// Sistem DB lisans şemasını garanti et (login LicenseService kullanıyor).
// DAVRANIŞ DEĞİŞMEDİ (hata yutulur, uygulama yine başlar) — yalnız yapısal loglama eklendi.
try { MindAeonRest.Data.DatabaseInitializer.EnsureLicenseSchema(); }
catch (Exception ex)
{
    Log.Error(ex, "Lisans şeması garanti edilemedi (uygulama yine de başlatılıyor)");
    Console.Error.WriteLine("Lisans şeması garanti edilemedi: " + ex.Message);
}

// 2FA (TOTP) kolonlarını (dbo.Users additive) garanti et — login/enrollment bunları okur/yazar.
// Additive + idempotent; hata yutulur (2FA henüz kimsede etkin değilse giriş yine çalışır).
try { MindAeonRest.Data.DatabaseInitializer.EnsureTwoFactorSchema(); }
catch (Exception ex)
{
    Log.Error(ex, "2FA şeması garanti edilemedi (uygulama yine de başlatılıyor)");
    Console.Error.WriteLine("2FA şeması garanti edilemedi: " + ex.Message);
}

// ===== FAZ 0: Global hata yönetimi (path-bazlı) =====
// /api istekleri (POS/mobil) → RFC7807 ProblemDetails JSON (AddProblemDetails + parametresiz UseExceptionHandler,
// Licence.Api desenidir). /hubs (SignalR) ve back-office Razor bu daldan HARİÇ tutulur (predicate yalnız "/api").
// Tüm ortamlarda geçerli → /api hataları geliştirmede de tutarlı JSON döner.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api"),
    api => api.UseExceptionHandler());

if (!app.Environment.IsDevelopment())
{
    // Back-office (Razor) + PWA kabuğu + /hubs → mevcut HTML "/Error" sayfası KORUNUR.
    // (Yalnızca /api dışına daraltıldı ki yukarıdaki ProblemDetails dalıyla çakışmasın; HTML davranışı değişmedi.)
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/api"),
        web => web.UseExceptionHandler("/Error"));
    app.UseHsts();
}

// FAZ 0: yapısal istek loglama (method, path, status, süre) — tek satır/istek özet.
app.UseSerilogRequestLogging();

app.UseRequestLocalization();
app.UseStaticFiles();

// ===== POS PWA (statik) — /app altında servis edilir (aynı origin) =====
var pwaRoot = ResolvePwaRoot(builder.Environment.ContentRootPath);
PhysicalFileProvider? pwaProvider = null;
Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider? pwaCtp = null;
if (pwaRoot is not null)
{
    pwaProvider = new PhysicalFileProvider(pwaRoot);
    pwaCtp = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
    pwaCtp.Mappings[".webmanifest"] = "application/manifest+json";
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = pwaProvider,
        RequestPath = "/app",
        ContentTypeProvider = pwaCtp,
        ServeUnknownFileTypes = true
    });
}

app.UseRouting();
app.UseCors();
app.UseAuthentication();
// RateLimiter, Authentication'DAN SONRA: "ai" policy kimliği (cookie principal) kurulmuş olur → kullanıcı başına
// bölümlenir (öncesinde ctx.User.Identity.Name daima null olup sessizce IP'ye düşüyordu). IP-tabanlı "login"/
// "public"/"public-read" policy'leri bu konumdan etkilenmez (zaten IP kullanıyorlar).
app.UseRateLimiter();
// Yol'a göre çok-kiracılı Session: /api,/hubs → JWT claim'leri (API TenantMiddleware); diğer → cookie (WebTenantMiddleware).
app.UseWhen(ctx => IsApi(ctx.Request.Path), b => b.UseMiddleware<TenantMiddleware>());
app.UseWhen(ctx => !IsApi(ctx.Request.Path), b => b.UseMiddleware<WebTenantMiddleware>());
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapHub<PosHub>("/hubs/pos");
app.MapGet("/api/ping", () => Results.Ok(new { ok = true, service = "MindAeonRestWeb+Api", utc = DateTime.UtcNow }));

// PWA SPA fallback: /app ve /app/* (dosya olmayan) → index.html.
if (pwaProvider is not null)
{
    string CtOf(string name) => pwaCtp!.TryGetContentType(name, out var t) ? t : "application/octet-stream";
    app.MapGet("/app/{**rest}", async (HttpContext ctx, string? rest) =>
    {
        var fi = string.IsNullOrEmpty(rest) ? null : pwaProvider.GetFileInfo(rest);
        if (fi is { Exists: true, IsDirectory: false })
        {
            ctx.Response.ContentType = CtOf(fi.Name);
            await ctx.Response.SendFileAsync(fi);
            return;
        }
        var index = pwaProvider.GetFileInfo("index.html");
        if (!index.Exists) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(index);
    }).AllowAnonymous();
    app.MapGet("/app", async (HttpContext ctx) =>
    {
        var index = pwaProvider.GetFileInfo("index.html");
        if (!index.Exists) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(index);
    }).AllowAnonymous();
}

app.Run();

static bool IsApi(PathString p) => p.StartsWithSegments("/api") || p.StartsWithSegments("/hubs");

// PWA wwwroot: yayında bin'e kopyalanan "pwa" klasörü; dev'de üst klasördeki MindAeonRest.Pwa\wwwroot.
static string? ResolvePwaRoot(string contentRoot)
{
    foreach (var cand in new[]
    {
        Path.Combine(AppContext.BaseDirectory, "pwa"),
        Path.Combine(contentRoot, "pwa"),
    })
        if (Directory.Exists(cand)) return cand;

    foreach (var baseDir in new[] { contentRoot, AppContext.BaseDirectory })
    {
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "MindAeonRest.Pwa", "wwwroot");
            if (Directory.Exists(c)) return c;
        }
    }
    return null;
}
