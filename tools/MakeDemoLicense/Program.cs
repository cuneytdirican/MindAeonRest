using Microsoft.Data.SqlClient;
using MindAeonRest.Licensing;

// DEMO şirketine imzalı ENTERPRISE lisansı üret + sistem DB dbo.Licenses'a yaz.
// GetStatusByCompany(DEMO) bu satırı okuyup Enterprise paket/özellikleri döndürür.
string companyCode = args.Length > 0 ? args[0] : "DEMO";
var pkg = LicensePackage.Enterprise;

var payload = new LicensePayload
{
    LicenseId = Guid.NewGuid().ToString("N"),
    CompanyCode = companyCode,
    HardwareId = "WEB-ENTERPRISE",
    Package = pkg,
    MaxBranches = PackageMatrix.MaxBranches(pkg),
    MaxUsers = PackageMatrix.MaxUsers(pkg),
    MaxKiosks = PackageMatrix.MaxKiosks(pkg),
    FeatureBits = (long)PackageMatrix.Features(pkg),
    IssuedUtc = DateTime.UtcNow,
    ExpiresUtc = null   // süresiz
};

string priv = File.ReadAllText(@"c:\Projeler\AdisyonProjeleri\V7ClaudeShelf\_licensekeys\private.xml");
string token = LicenseCrypto.SignLicense(payload, priv);

var check = LicenseCrypto.VerifyLicense(token);
Console.WriteLine($"İmza doğrulandı mı: {(check is null ? "HAYIR" : "EVET")} | Paket={check?.Package} | Features={check?.Features}");
if (check is null) { Console.WriteLine("İmza doğrulanamadı — çıkılıyor."); return; }

var cs = new SqlConnectionStringBuilder
{
    DataSource = "DB_SUNUCU,1434", UserID = "DB_KULLANICI", Password = "CHANGE_ME_DB_PASSWORD",
    InitialCatalog = "MindAeonRest", Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 30
}.ConnectionString;

using var c = new SqlConnection(cs);
c.Open();
using var cmd = c.CreateCommand();
cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.Licenses WHERE CompanyCode=@c AND HardwareId='WEB-ENTERPRISE')
    UPDATE dbo.Licenses SET LicenseKey=@k, UpdatedUtc=SYSUTCDATETIME() WHERE CompanyCode=@c AND HardwareId='WEB-ENTERPRISE';
ELSE
    INSERT INTO dbo.Licenses (CompanyCode, HardwareId, LicenseKey) VALUES (@c,'WEB-ENTERPRISE',@k);";
cmd.Parameters.AddWithValue("@c", companyCode);
cmd.Parameters.AddWithValue("@k", token);
int n = cmd.ExecuteNonQuery();
Console.WriteLine($"{companyCode} için ENTERPRISE lisansı yazıldı (etkilenen satır: {n}).");
