using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Authorization;
using MindAeonRest.Licence.Portal.Services;

var builder = WebApplication.CreateBuilder(args);

// IIS in-process barındırma
builder.WebHost.UseIIS();

// Cookie tabanlı kimlik doğrulama: HttpOnly çerez, JWT claim içinde taşınır.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name = "MindAeon.Portal";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

// Data Protection: anahtarları KALICI dosya sistemine yaz. IIS/Plesk'te app-pool'un kullanıcı profili/
// HKLM registry'si olmadığında ASP.NET Core EFEMERAL anahtar üretir → her geri dönüşümde antiforgery
// token'ı + auth cookie'si çözülemez ("key not found") → tüm POST'lar reddedilir. Kalıcı klasör bunu çözer.
var dpKeys = System.IO.Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
System.IO.Directory.CreateDirectory(dpKeys);
builder.Services.AddDataProtection()
    .SetApplicationName("MindAeonRest.Licence.Portal")
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(dpKeys));

// HttpContext erişimi (ApiClient cookie'den JWT okur)
builder.Services.AddHttpContextAccessor();

// API'yi tüketen typed HttpClient
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5100";
builder.Services.AddHttpClient<ApiClient>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
});

// Admin sayfalarının kullandığı isimli HttpClient (Models\AdminApi → factory.CreateClient("AdminApi"))
builder.Services.AddHttpClient("AdminApi", c => c.BaseAddress = new Uri(apiBaseUrl));

// POS provisioning (MindAeonRestWeb) typed istemcisi — SUNUCU-TARAFI çağrılır; X-Provision-Key
// header'ı istemci içinde eklenir (config Pos:ProvisionKey). Base adres Pos:ProvisionUrl.
var posProvisionUrl = builder.Configuration["Pos:ProvisionUrl"] ?? "https://mindaeonrestweb.mobilteg.com";
builder.Services.AddHttpClient<PosProvisionClient>(c =>
{
    c.BaseAddress = new Uri(posProvisionUrl);
});

// Varsayılan: tüm sayfalar [Authorize]; sayfalarda [AllowAnonymous] ile gevşetilir.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Logout");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
