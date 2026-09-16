using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MindAeonRest.Licence.Api.Auth;
using MindAeonRest.Licence.Api.Services;
using MindAeonRest.Licence.Api.Tenant;
using MindAeonRest.Licence.Data;

var builder = WebApplication.CreateBuilder(args);

// Kiracı bağlamı (HttpContext'ten JWT claim'leriyle dolar) — DbContext + denetim için Scoped.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantInfo, TenantContext>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

// DbContext Scoped; tenant'a bağlı query filter + denetim interceptor ile.
builder.Services.AddDbContext<LicenceDbContext>((sp, o) =>
{
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    o.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
    // Dealer.Code kolonu EF migration yerine startup raw-SQL ile garanti ediliyor → bekleyen-model uyarısını bastır.
    o.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddIdentityCore<ApplicationUser>(opt =>
{
    opt.Password.RequiredLength = 6;
    opt.Password.RequireNonAlphanumeric = false;
    opt.User.RequireUniqueEmail = true;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<LicenceDbContext>()
    .AddSignInManager();

// JWT imza anahtarı: ENV/config'teki güçlü anahtar kullanılır; placeholder/zayıf/eksikse makineye özel
// kalıcı güçlü anahtar üretilir (repodaki sabit anahtarla token sahteciliği engellenir). İmza+doğrulama aynı anahtar.
var key = MindAeonRest.Licence.Api.Auth.JwtKeyProvider.Resolve(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<ILicenseSigner, LicenseSigner>();
builder.Services.AddProblemDetails();

// Data Protection: ödeme sağlayıcı sırlarını (PaymentProviderConfig) şifrelemek için kullanılır.
// Anahtar halkası kalıcı bir klasörde tutulur ve DİSKTE DPAPI ile şifrelenir (Windows) → app-pool
// geri dönüşümü/yeniden başlatma sonrası eski şifreli kayıtlar çözülür ama anahtar dosyaları korunur.
{
    var keysDir = builder.Configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(keysDir))
        keysDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                               "MindAeon", "Licence", "dp-keys");
    try { Directory.CreateDirectory(keysDir); } catch { }
    var dp = builder.Services.AddDataProtection()
        .SetApplicationName("MindAeonRest.Licence")
        .PersistKeysToFileSystem(new DirectoryInfo(keysDir));
    if (OperatingSystem.IsWindows())
        dp.ProtectKeysWithDpapi(protectToLocalMachine: true);   // anahtarlar diskte düz metin durmaz
}

builder.Services.AddControllers();

// CORS: yalnız TAM (exact) yapılandırılmış origin'lere izin. Tarayıcı yalnız portaldan çağırır;
// cihaz uçları tarayıcı dışıdır (CORS uygulanmaz). AllowAnyOrigin ve wildcard-subdomain KALDIRILDI
// (wildcard-subdomain, ele geçirilmiş bir alt alan adından çağrıya kapı aralardı).
// ÜRETİM: Cors:AllowedOrigins ZORUNLUDUR; eksikse strict/boş politika uygulanır (hiçbir origin'e izin yok)
// — geliştirme dışında sessiz localhost fallback YAPILMAZ. Geliştirmede kolaylık için localhost'a izin verilir.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();
allowedOrigins = allowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
if (allowedOrigins.Length == 0 && builder.Environment.IsDevelopment())
    allowedOrigins = new[] { "https://localhost", "http://localhost" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length == 0)
        return; // ÜretimDE AllowedOrigins yoksa: hiçbir origin'e izin verme (strict).
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

// Brute-force/abuse koruması: kimlik doğrulama uçları (login) IP başına dar oran sınırı; cihaz uçları
// (activate/validate/usage) IP + HardwareId başına ayrı, daha geniş ama yine de korumalı oran sınırı.
// HardwareId gövdede gelir (limiter gövdeyi buffer'lamaz); cihaz isteğe bağlı X-Hardware-Id başlığı
// gönderirse partition o cihaza incelir, yoksa IP bazına düşer (yine de tek IP'den enumerasyon/brute sınırlanır).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var path = ctx.Request.Path.Value ?? "";
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (path.Contains("/auth/", StringComparison.OrdinalIgnoreCase))
            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: "auth:" + ip,
                factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });

        // Anonim public uçlar (tanıtım sitesi fiyat kataloğu) → IP başına dar pencere (kötüye kullanım/scraping koruması).
        if (path.Contains("/public/", StringComparison.OrdinalIgnoreCase))
            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: "public:" + ip,
                factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });

        // Cihaz uçları: activate/validate/usage → IP (+ varsa HardwareId) başına dar pencere.
        bool isDevice =
            path.Contains("/licenses/activate", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/licenses/validate", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/usage", StringComparison.OrdinalIgnoreCase);
        if (isDevice)
        {
            var hw = ctx.Request.Headers["X-Hardware-Id"].ToString();
            var partKey = "device:" + ip + ":" + (string.IsNullOrWhiteSpace(hw) ? "_" : hw);
            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partKey,
                factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        }

        return System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("open");
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// DB oluştur/migrate + rol & admin seed
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<LicenceDbContext>();
    db.Database.Migrate();

    // ŞEMA STRATEJİSİ (bilinçli): MaxKiosks/AddOnFeatures/Extra*/AddOnPrices ve Dealers.Code kolonları
    // EF migration ile DEĞİL, startup'ta İDEMPOTENT raw-SQL ile garanti edilir. Gerekçe: canlı DB zaten
    // bu yolla migrate edildi; bu kolonlar için sonradan üretilecek gerçek bir EF migration canlıyı bozma
    // (drop/recreate) riski taşır. Bu nedenle idempotent ensure korunur AMA aşağıda FAIL-CLOSED doğrulama
    // yapılır: ensure sonrası kritik kolon/tablo hâlâ yoksa istisna SESSİZCE YUTULMAZ — uygulama DURUR.
    try
    {
        db.Database.ExecuteSqlRaw("IF COL_LENGTH('dbo.Dealers','Code') IS NULL ALTER TABLE dbo.Dealers ADD Code NVARCHAR(40) NOT NULL CONSTRAINT DF_Dealers_Code DEFAULT('');");
        db.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Dealers_Code') CREATE UNIQUE INDEX UX_Dealers_Code ON dbo.Dealers(Code) WHERE Code <> '' AND IsDeleted = 0;");
        // Lisans: terminal + add-on + ekstra sayı kolonları (EF migration yerine idempotent).
        foreach (var col in new[] {
            "MaxKiosks INT NOT NULL CONSTRAINT DF_Lic_MaxKiosks DEFAULT(1)",
            "AddOnFeatures BIGINT NOT NULL CONSTRAINT DF_Lic_AddOn DEFAULT(0)",
            "ExtraUsers INT NOT NULL CONSTRAINT DF_Lic_ExUsers DEFAULT(0)",
            "ExtraKiosks INT NOT NULL CONSTRAINT DF_Lic_ExKiosks DEFAULT(0)",
            "ExtraBranches INT NOT NULL CONSTRAINT DF_Lic_ExBranches DEFAULT(0)" })
        {
            var name = col.Split(' ')[0];
            db.Database.ExecuteSqlRaw($"IF COL_LENGTH('dbo.Licenses','{name}') IS NULL ALTER TABLE dbo.Licenses ADD {col};");
        }
        // Add-on fiyat kataloğu tablosu (BaseEntity kolonlarıyla birebir).
        db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID('dbo.AddOnPrices','U') IS NULL
CREATE TABLE dbo.AddOnPrices (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Kind INT NOT NULL, FeatureBit BIGINT NOT NULL CONSTRAINT DF_AOP_Bit DEFAULT(0),
    Name NVARCHAR(100) NOT NULL, UnitPrice DECIMAL(18,2) NOT NULL CONSTRAINT DF_AOP_Price DEFAULT(0),
    CurrencyCode NVARCHAR(8) NOT NULL CONSTRAINT DF_AOP_Cur DEFAULT('TRY'),
    IsActive BIT NOT NULL CONSTRAINT DF_AOP_Active DEFAULT(1),
    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_AOP_Cr DEFAULT(SYSUTCDATETIME()), UpdatedUtc DATETIME2 NULL,
    IsDeleted BIT NOT NULL CONSTRAINT DF_AOP_Del DEFAULT(0), DeletedUtc DATETIME2 NULL,
    CreatedByUserId NVARCHAR(450) NULL, UpdatedByUserId NVARCHAR(450) NULL,
    RowVersion ROWVERSION
);");
        // Seed: katalog boşsa varsayılan add-on fiyatları (admin düzenler).
        if (!await db.AddOnPrices.IgnoreQueryFilters().AnyAsync())
        {
            foreach (var f in MindAeonRest.Licensing.PackageMatrix.AddOnModules)
                db.AddOnPrices.Add(new AddOnPrice { Kind = AddOnKind.Module, FeatureBit = (long)f, Name = MindAeonRest.Licensing.PackageMatrix.FeatureName(f), UnitPrice = 250 });
            db.AddOnPrices.Add(new AddOnPrice { Kind = AddOnKind.User, Name = "Ek Kullanıcı (kişi başı)", UnitPrice = 50 });
            db.AddOnPrices.Add(new AddOnPrice { Kind = AddOnKind.Kiosk, Name = "Ek Terminal (KIOSK başı)", UnitPrice = 150 });
            db.AddOnPrices.Add(new AddOnPrice { Kind = AddOnKind.Branch, Name = "Ek Şube (şube başı)", UnitPrice = 300 });
            await db.SaveChangesAsync();
        }
    }
    catch (Exception ex)
    {
        // FAIL-CLOSED: idempotent şema-ensure sırasında hata olursa SESSİZCE GEÇME — uygulamayı durdur.
        throw new InvalidOperationException(
            "Lisans şeması (Dealers.Code / Licenses.MaxKiosks vb. / AddOnPrices) idempotent ensure sırasında başarısız oldu. " +
            "Şema doğrulanamadığı için uygulama başlatılmıyor (fail-closed). Ayrıntı: " + ex.Message, ex);
    }

    // FAIL-CLOSED doğrulama: ensure çalıştıktan sonra kritik kolon/tablolar GERÇEKTEN var mı?
    // (Örn. ensure beklenmedik bir şekilde no-op kaldıysa veya kısmen uygulandıysa.) Yoksa net mesajla DUR.
    {
        var missing = new List<string>();
        bool ColExists(string table, string col) =>
            db.Database.SqlQueryRaw<int>(
                "SELECT CASE WHEN COL_LENGTH({0}, {1}) IS NULL THEN 0 ELSE 1 END AS Value",
                "dbo." + table, col).AsEnumerable().FirstOrDefault() == 1;
        bool TableExists(string table) =>
            db.Database.SqlQueryRaw<int>(
                "SELECT CASE WHEN OBJECT_ID({0}, 'U') IS NULL THEN 0 ELSE 1 END AS Value",
                "dbo." + table).AsEnumerable().FirstOrDefault() == 1;

        if (!ColExists("Dealers", "Code")) missing.Add("Dealers.Code");
        foreach (var c in new[] { "MaxKiosks", "AddOnFeatures", "ExtraUsers", "ExtraKiosks", "ExtraBranches" })
            if (!ColExists("Licenses", c)) missing.Add("Licenses." + c);
        if (!TableExists("AddOnPrices")) missing.Add("AddOnPrices tablosu");

        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Lisans şeması eksik (fail-closed): " + string.Join(", ", missing) +
                ". İdempotent ensure beklenen kolon/tabloları oluşturamadı; uygulama başlatılmıyor.");
    }

    var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var r in new[] { "Admin", "Dealer", "Customer" })
        if (!await roleMgr.RoleExistsAsync(r)) await roleMgr.CreateAsync(new IdentityRole(r));

    var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();
    var adminEmail = app.Configuration["SeedAdmin:Email"]!;
    if (await userMgr.FindByEmailAsync(adminEmail) is null)
    {
        // GÜVENLİK — FAIL-OPEN KAPATILDI: appsettings.json'daki SeedAdmin:Password bir PLACEHOLDER'dır
        // (Identity min uzunluk 6'yı geçtiği için teknik olarak "geçerli" görünür). Üretimde ENV veya
        // appsettings.Production.json ile GÜÇLÜ bir parolayla override edilmezse, placeholder parolayla
        // BİLİNEN KİMLİKLİ bir admin doğardı. Bu nedenle ÜRETİMDE parola zayıf/placeholder ise admin
        // seed'i YAPILMAZ ve net bir hata loglanır. Geliştirmede mevcut davranış korunur
        // (admin@mobilteg.com / Admin!2026 çalışmaya devam eder).
        var adminPassword = app.Configuration["SeedAdmin:Password"];
        // GÜVENLİK: yalnız Development'ta zayıf/placeholder parolaya izin ver. Production DIŞINDAKİ her ortam
        // (Staging/QA/Test/özel) fail-closed olsun — aksi halde commit'li placeholder ile bilinen-kimlikli admin doğar.
        if (!app.Environment.IsDevelopment() && IsWeakSeedPassword(adminPassword))
        {
            app.Logger.LogError(
                "Seed-admin OLUŞTURULMADI (fail-closed): SeedAdmin:Password üretimde ENV veya " +
                "appsettings.Production.json ile GÜÇLÜ verilmelidir (>=12 karakter, placeholder değil). " +
                "Placeholder/zayıf parolayla bilinen kimlikli admin oluşturulması engellendi. " +
                "Güçlü bir parola ayarlayıp uygulamayı yeniden başlatın.");
        }
        else
        {
            var admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, FullName = "Sistem Yöneticisi", Kind = UserKind.Admin, EmailConfirmed = true };
            // IdentityResult SESSİZCE YUTULMAZ: parola Identity karmaşıklık kurallarını (büyük/küçük harf + rakam)
            // sağlamazsa CreateAsync başarısız olur; hata loglanır ve Development DIŞINDA fail-closed (throw).
            var createRes = await userMgr.CreateAsync(admin, adminPassword!);
            if (!createRes.Succeeded)
            {
                var errs = string.Join("; ", createRes.Errors.Select(e => e.Description));
                app.Logger.LogError("Seed-admin OLUŞTURULAMADI: {Errors}. SeedAdmin:Password Identity kurallarını (büyük/küçük harf + rakam) sağlamalı.", errs);
                if (!app.Environment.IsDevelopment())
                    throw new InvalidOperationException("Seed-admin oluşturulamadı (fail-closed): " + errs);
            }
            else
            {
                var roleRes = await userMgr.AddToRoleAsync(admin, "Admin");
                if (!roleRes.Succeeded)
                    app.Logger.LogError("Seed-admin 'Admin' rolüne atanamadı: {Errors}", string.Join("; ", roleRes.Errors.Select(e => e.Description)));
            }
        }
    }
}

// Seed-admin parolası üretim için "zayıf/placeholder" mı? (Yalnız üretimde uygulanır — bkz. admin seed.)
// Zayıf sayılır: boş; 12 karakterden kısa; ya da commit'lenen appsettings.json placeholder işaretlerini içeren.
static bool IsWeakSeedPassword(string? pwd)
{
    if (string.IsNullOrWhiteSpace(pwd)) return true;
    var p = pwd.Trim();
    if (p.Length < 12) return true;
    var lower = p.ToLowerInvariant();
    return lower.Contains("degistir") || lower.Contains("uretimde")
        || lower.Contains("placeholder") || lower.Contains("changeme");
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/ping", () => Results.Ok(new { ok = true, service = "MindAeonRest.Licence.Api", utc = DateTime.UtcNow }));
app.MapControllers();

app.Run();
