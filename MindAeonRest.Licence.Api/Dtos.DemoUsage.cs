using MindAeonRest.Licence.Data;

namespace MindAeonRest.Licence.Api;

// ---- DemoInstall ----
// Bayi/masaüstü demo kaydı oluşturma. DealerId: Admin verir; Bayi kendi tenant'ına yazılır.
public record CreateDemoReq(
    int? DealerId,
    string CompanyCode,
    string? BusinessTitle,
    string? HardwareId,
    string? ContactName,
    string? Phone,
    string? Email);

public record DemoResp(
    int Id,
    int? DealerId,
    string CompanyCode,
    string? BusinessTitle,
    string? HardwareId,
    string? ContactName,
    string? Phone,
    string? Email,
    DateTime StartedUtc,
    bool Converted,
    int? LicenseId);

// Demo'yu lisansa dönüştürüldü olarak işaretle.
public record ConvertDemoReq(int? LicenseId);

// ---- UsageReport ----
// Masaüstünden periyodik kullanım/ciro raporu.
public record UsageReportReq(
    string CompanyCode,
    string? HardwareId,
    int BranchCount,
    int UserCount,
    int KioskCount,
    decimal Revenue,
    string Period);

public record UsageReportResp(
    int Id,
    int? BusinessId,
    string CompanyCode,
    DateTime ReportedUtc,
    int BranchCount,
    int UserCount,
    int KioskCount,
    decimal Revenue,
    string? Period);
