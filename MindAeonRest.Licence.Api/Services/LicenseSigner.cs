using System.IO;
using MindAeonRest.Licensing;

namespace MindAeonRest.Licence.Api.Services;

/// <summary>Lisans imzalama — YALNIZ sunucuda. Özel anahtar tek noktadan yüklenir; UI/araç kripto bilmez.</summary>
public interface ILicenseSigner
{
    string SignLicense(LicensePayload payload);
    LicensePayload? VerifyLicense(string token);
    RequestPayload? DecodeRequest(string token);
}

public class LicenseSigner : ILicenseSigner
{
    private readonly string _privateKeyXml;

    public LicenseSigner(IConfiguration config, IWebHostEnvironment env)
    {
        var path = config["Licensing:PrivateKeyPath"]
            ?? throw new InvalidOperationException("Licensing:PrivateKeyPath tanımlı değil.");
        var resolved = ResolvePath(path, env.ContentRootPath);
        if (resolved is null)
            throw new InvalidOperationException(
                $"Lisans özel anahtarı bulunamadı: '{path}'. Anahtarı güvenli bir yola koyup config'i güncelleyin (üretimde C:\\ProgramData\\MindAeon\\keys + ACL/DPAPI).");
        _privateKeyXml = File.ReadAllText(resolved);
    }

    private static string? ResolvePath(string path, string contentRoot)
    {
        // Öncelik: ENV → güvenli sistem yolu (ProgramData) → config yolu → repo (yalnız dev fallback).
        var envPath = Environment.GetEnvironmentVariable("MINDAEON_LICENCE_PRIVATEKEY");
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var c in new[]
        {
            envPath,
            Path.Combine(programData, "MindAeon", "keys", "private.xml"),
            path,
            Path.Combine(contentRoot, path),
            Path.Combine(AppContext.BaseDirectory, path),
            Path.Combine(contentRoot, "_licensekeys", "private.xml"),
        })
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            try { var full = Path.GetFullPath(c); if (File.Exists(full)) return full; } catch { }
        }
        return null;
    }

    public string SignLicense(LicensePayload payload) => LicenseCrypto.SignLicense(payload, _privateKeyXml);
    public LicensePayload? VerifyLicense(string token) => LicenseCrypto.VerifyLicense(token);
    public RequestPayload? DecodeRequest(string token) => LicenseCrypto.DecodeRequest(token);
}
