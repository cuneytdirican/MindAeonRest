using System.Text;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// e-Fatura/e-Arşiv sağlayıcı arayüzü. Gerçek entegrasyon (SOAP/REST) bu arayüzü implemente eder.
/// Eklenti modül olarak satılacak; çekirdek yalnızca arayüzü ve seçimi bilir.
/// </summary>
public interface IEInvoiceProvider
{
    string Name { get; }
    EInvoiceResult Send(EInvoiceRequest request, EInvoiceSettings settings);
    /// <summary>Gönderilmiş faturanın durumunu sağlayıcıdan sorgular (ETTN ile). İskele — gerçek sorgu eklenti modülde.</summary>
    EInvoiceResult GetStatus(string ettn, EInvoiceSettings settings);
    /// <summary>Gönderilmiş faturayı iptal eder (ETTN ile). İskele — gerçek iptal eklenti modülde.</summary>
    EInvoiceResult Cancel(string ettn, EInvoiceSettings settings);
}

/// <summary>
/// SOAP tabanlı e-Fatura sağlayıcıları için ortak taban. Endpoint + istek gövdesi (UBL-TR benzeri)
/// YAPISI somut olarak kurulur; ancak kimlik yoksa ya da ÜRETİM modundaysa fail-closed davranır
/// (gerçek GİB gönderimi eklenti modülde bağlanacak). TEST modunda açıkça SİMÜLASYON döner.
/// </summary>
public abstract class SoapEInvoiceProviderBase : IEInvoiceProvider
{
    public abstract string Name { get; }
    /// <summary>Fatura no/ETTN önekindeki kısa sağlayıcı kodu.</summary>
    protected abstract string Prefix { get; }
    /// <summary>Test / canlı SOAP uç adresleri (gerçek gönderim eklenti modülde çağrılacak).</summary>
    protected abstract string TestEndpoint { get; }
    protected abstract string LiveEndpoint { get; }

    protected string Endpoint(EInvoiceSettings s) => s.IsTest ? TestEndpoint : LiveEndpoint;

    /// <summary>UBL-TR benzeri fatura zarfı (özet). Gerçek şema eklenti modülde tamamlanacak;
    /// burada istek gövdesinin İSKELETİ oluşturulur (matrah/KDV/toplam + müşteri + satırlar).</summary>
    public virtual string BuildEnvelope(EInvoiceRequest req)
    {
        var sb = new StringBuilder();
        sb.Append("<Invoice>");
        sb.Append("<ProfileID>").Append(req.IsEArsiv ? "EARSIVFATURA" : "TICARIFATURA").Append("</ProfileID>");
        sb.Append("<IssueDate>").Append(req.Date.ToString("yyyy-MM-dd")).Append("</IssueDate>");
        sb.Append("<Customer>");
        sb.Append("<Name>").Append(Xml(req.CustomerName)).Append("</Name>");
        if (!string.IsNullOrWhiteSpace(req.TaxNumber)) sb.Append("<TaxNumber>").Append(Xml(req.TaxNumber)).Append("</TaxNumber>");
        if (!string.IsNullOrWhiteSpace(req.TaxOffice)) sb.Append("<TaxOffice>").Append(Xml(req.TaxOffice)).Append("</TaxOffice>");
        if (!string.IsNullOrWhiteSpace(req.Address)) sb.Append("<Address>").Append(Xml(req.Address)).Append("</Address>");
        sb.Append("</Customer>");
        sb.Append("<Lines>");
        foreach (var l in req.Lines)
            sb.Append("<Line>")
              .Append("<Name>").Append(Xml(l.Name)).Append("</Name>")
              .Append("<Qty>").Append(Inv(l.Qty)).Append("</Qty>")
              .Append("<UnitPrice>").Append(Inv(l.UnitPrice)).Append("</UnitPrice>")
              .Append("<TaxRate>").Append(Inv(l.TaxRate)).Append("</TaxRate>")
              .Append("<LineTotal>").Append(Inv(l.LineTotal)).Append("</LineTotal>")
              .Append("</Line>");
        sb.Append("</Lines>");
        sb.Append("<Totals>")
          .Append("<Matrah>").Append(Inv(req.MatrahTotal)).Append("</Matrah>")
          .Append("<Tax>").Append(Inv(req.TaxTotal)).Append("</Tax>")
          .Append("<Grand>").Append(Inv(req.GrandTotal)).Append("</Grand>")
          .Append("</Totals>");
        sb.Append("</Invoice>");
        return sb.ToString();
    }

    public EInvoiceResult Send(EInvoiceRequest request, EInvoiceSettings settings)
    {
        // İstek gövdesi burada gerçekten kurulur (yapı hazır); gönderim fail-closed/simülasyon.
        var envelope = BuildEnvelope(request);
        var res = EInvoiceStub.Send(Name, Prefix, settings, Endpoint(settings), envelope);
        res.Status = res.Success ? EInvoiceStatus.Gonderildi : EInvoiceStatus.Hata;
        return res;
    }

    public EInvoiceResult GetStatus(string ettn, EInvoiceSettings settings)
        => EInvoiceStub.Query(Name, "durum sorgulama", ettn, settings, Endpoint(settings));

    public EInvoiceResult Cancel(string ettn, EInvoiceSettings settings)
    {
        var res = EInvoiceStub.Query(Name, "iptal", ettn, settings, Endpoint(settings));
        if (res.Success) res.Status = EInvoiceStatus.Iptal;
        return res;
    }

    protected static string Inv(decimal d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture);
    protected static string Xml(string? s) => System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;
}

/// <summary>EDM e-Fatura sağlayıcısı (SOAP iskelesi — gerçek API eklenti modülde bağlanacak).</summary>
public class EdmProvider : SoapEInvoiceProviderBase
{
    public override string Name => "EDM";
    protected override string Prefix => "EDM";
    protected override string TestEndpoint => "https://efaturatest.edmbilisim.com.tr/EFaturaEDM/EFaturaEDM.svc";
    protected override string LiveEndpoint => "https://efatura.edmbilisim.com.tr/EFaturaEDM/EFaturaEDM.svc";
}

/// <summary>Uyumsoft e-Fatura sağlayıcısı (SOAP iskelesi — gerçek API eklenti modülde bağlanacak).</summary>
public class UyumsoftProvider : SoapEInvoiceProviderBase
{
    public override string Name => "Uyumsoft";
    protected override string Prefix => "UYM";
    protected override string TestEndpoint => "https://efatura-test.uyumsoft.com.tr/Services/Integration";
    protected override string LiveEndpoint => "https://efatura.uyumsoft.com.tr/Services/Integration";
}

/// <summary>e-Fatura entegrasyonu henüz GERÇEK değil (eklenti modül, gap testinden sonra).
/// ÜRETİMDE asla sahte başarı/ETTN dönmez (kullanıcı yanılmasın); yalnız TEST modunda açıkça SİMÜLASYON döner.</summary>
internal static class EInvoiceStub
{
    public static EInvoiceResult Send(string name, string prefix, EInvoiceSettings settings, string endpoint, string envelope)
    {
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
            return new EInvoiceResult { Success = false, Message = $"{name} kullanıcı/parola tanımlı değil." };

        if (!settings.IsTest)
            return new EInvoiceResult
            {
                Success = false,
                Message = $"{name} e-Fatura entegrasyonu henüz aktif değil (eklenti modül). GERÇEK GÖNDERİM YAPILMADI.",
                ProviderResponse = $"POST {endpoint} (fail-closed; canlı gönderim eklenti modülde)."
            };

        // Yalnız TEST: açıkça simülasyon — ETTN gerçek değil. İstek gövdesi kuruldu (uzunluk log'a yazılır).
        return new EInvoiceResult
        {
            Success = true,
            Simulated = true,
            Ettn = "SIMULASYON-" + Guid.NewGuid().ToString("N")[..12],
            InvoiceNo = $"{prefix}TEST{DateTime.Now:yyMMddHHmmss}",
            Message = $"{name} TEST/SİMÜLASYON — gerçek gönderim YOK (entegrasyon eklenti modülde gelecek).",
            ProviderResponse = $"SIMULATED POST {endpoint} · gövde {envelope.Length} bayt."
        };
    }

    public static EInvoiceResult Query(string name, string op, string ettn, EInvoiceSettings settings, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
            return new EInvoiceResult { Success = false, Message = $"{name} kullanıcı/parola tanımlı değil." };
        if (string.IsNullOrWhiteSpace(ettn))
            return new EInvoiceResult { Success = false, Message = "ETTN yok — önce fatura gönderilmeli." };

        if (!settings.IsTest)
            return new EInvoiceResult
            {
                Success = false,
                Message = $"{name} {op} entegrasyonu henüz aktif değil (eklenti modül). İŞLEM YAPILMADI.",
                ProviderResponse = $"POST {endpoint} (fail-closed)."
            };

        return new EInvoiceResult
        {
            Success = true,
            Simulated = true,
            Ettn = ettn,
            Message = $"{name} TEST/SİMÜLASYON — {op} (gerçek {op} YOK).",
            ProviderResponse = $"SIMULATED {op} · ETTN {ettn}"
        };
    }
}

public static class EInvoiceProviderFactory
{
    public static IEInvoiceProvider? Get(EInvoiceProvider p) => p switch
    {
        EInvoiceProvider.EDM => new EdmProvider(),
        EInvoiceProvider.Uyumsoft => new UyumsoftProvider(),
        _ => null
    };
}
