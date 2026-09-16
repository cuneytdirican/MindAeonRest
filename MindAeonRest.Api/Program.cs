using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using MindAeonRest.Api.Auth;
using MindAeonRest.Api.Hubs;
using MindAeonRest.Api.Json;

var builder = WebApplication.CreateBuilder(args);

// Windows Service olarak çalışabilsin (SCM başlattığında); konsol/normal çalıştırmada etkisiz.
builder.Services.AddWindowsService(o => o.ServiceName = "MindAeonRestApi");

var jwt = new JwtTokenService(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<PublicTokenService>();   // anonim (QR) kısıtlı-scope token üret/doğrula
builder.Services.AddSingleton<PortalTokenService>();   // FAZ 5a Sadakat Portalı: müşteri (telefon+OTP) kısıtlı-scope token
builder.Services.AddSingleton<PosNotifier>();

// Tüm DateTime'lar JSON'a UTC 'Z' son ekiyle yazılsın → istemci (PWA/MAUI) 'Z'siz UTC'yi yerel sanıp
// "+180 dk" (GMT+3) gecikme göstermesin. Controllers + SignalR + minimal-API için ayrı ayrı kaydedilir.
static void AddUtc(System.Text.Json.JsonSerializerOptions o)
{
    o.Converters.Add(new UtcDateTimeConverter());
    o.Converters.Add(new NullableUtcDateTimeConverter());
}
builder.Services.AddControllers().AddJsonOptions(o => AddUtc(o.JsonSerializerOptions));
builder.Services.AddSignalR().AddJsonProtocol(o => AddUtc(o.PayloadSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(o => AddUtc(o.SerializerOptions));   // minimal-API (MapGet/MapPost) çıktıları

// LAN'da tüm cihazlardan erişim (kapalı ağ)
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = jwt.ValidationParameters;
        // SignalR (WebSocket) token'ı header yerine query string'te taşır
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

// Anti-abuse: anonim public uçlar (QR menü/sipariş) için IP başına makul limit.
// Normal /api (Authorize) uçları bu politikadan ETKİLENMEZ — yalnız [EnableRateLimiting("public")] taşıyan PublicController.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Anonim public YAZMA uçları (QR sipariş oluştur vb.) — [EnableRateLimiting("public")].
    options.AddPolicy("public", ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,                      // IP başına dakikada 60 istek (yazma uçları için makul)
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Anonim public OKUMA uçları (menü + ürün/kategori görselleri, lazy-load) — [EnableRateLimiting("public-read")].
    // CÖMERT + ayrı bölüm: görsel istekleri "public"i tüketip POST /order'ı 429'a düşürmesin (QR sipariş kök nedeni).
    options.AddPolicy("public-read", ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,                     // IP başına dakikada 240 istek (menü + görsel lazy-load için bol)
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MindAeonRest API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

var app = builder.Build();

// Sistem DB lisans şemasını garanti et (terminal-bazlı migration) — login LicenseService kullanıyor.
try { MindAeonRest.Data.DatabaseInitializer.EnsureLicenseSchema(); }
catch (Exception ex) { Console.Error.WriteLine("Lisans şeması garanti edilemedi: " + ex.Message); }

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();   // JWT → Session (AsyncLocal) çok-kiracılı bağlam
app.UseAuthorization();
app.UseRateLimiter();                     // "public" politikası PublicController'a uygulanır

app.MapGet("/api/ping", () => Results.Ok(new { ok = true, service = "MindAeonRest.Api", utc = DateTime.UtcNow }));
app.MapControllers();
app.MapHub<PosHub>("/hubs/pos");

// ===== Garson PWA (statik) — /app altında servis edilir =====
// Pwa wwwroot'u: çalışma dizini (bin/Debug veya yayın) fark etmeksizin üst klasörlerde ara.
string? pwaRoot = null;
foreach (var baseDir in new[] { builder.Environment.ContentRootPath, AppContext.BaseDirectory })
{
    var dir = new DirectoryInfo(baseDir);
    for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
    {
        var cand = Path.Combine(dir.FullName, "MindAeonRest.Pwa", "wwwroot");
        if (Directory.Exists(cand)) { pwaRoot = cand; break; }
    }
    if (pwaRoot is not null) break;
}
if (pwaRoot is not null)
{
    var pwaProvider = new PhysicalFileProvider(pwaRoot);
    // Doğru MIME tipleri: css→text/css, js→text/javascript (yoksa tarayıcı stylesheet'i UYGULAMAZ → sayfa stilsiz kalır).
    var ctp = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
    ctp.Mappings[".webmanifest"] = "application/manifest+json";
    string CtOf(string name) => ctp.TryGetContentType(name, out var t) ? t : "application/octet-stream";
    // /app/* için statik dosyalar (manifest, js, css, ikonlar, service-worker)
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = pwaProvider,
        RequestPath = "/app",
        ContentTypeProvider = ctp,
        ServeUnknownFileTypes = true   // .webmanifest vb. için
    });
    // /app ve /app/* (dosya olmayan) istekleri index.html'e düşür (SPA routing).
    app.MapGet("/app/{**rest}", async (HttpContext ctx, string? rest) =>
    {
        var fi = string.IsNullOrEmpty(rest) ? null : pwaProvider.GetFileInfo(rest);
        if (fi is { Exists: true, IsDirectory: false })
        {
            ctx.Response.ContentType = CtOf(fi.Name);   // gerçek tip (css/js/png…), sabit octet-stream DEĞİL
            await ctx.Response.SendFileAsync(fi);
            return;
        }
        var index = pwaProvider.GetFileInfo("index.html");
        if (!index.Exists) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(index);
    });
    // Çıplak /app (slash'sız) → index.html doğrudan servis (redirect döngüsü olmasın; <base href="/app/"> göreli yolları çözer).
    app.MapGet("/app", async (HttpContext ctx) =>
    {
        var index = pwaProvider.GetFileInfo("index.html");
        if (!index.Exists) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(index);
    });
}

app.Run();
