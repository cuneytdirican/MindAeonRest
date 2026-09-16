namespace MindAeonRest.Api;

// ===== Kimlik / giriş =====
public record CompanyDto(int Id, string Code, string Name);
public record BranchDto(int Id, string Name);
public record LoginUserDto(int Id, string Username, string FullName, string RoleName);

public record LoginRequest(string CompanyCode, string Username, string Password, int? BranchId);

// ===== Provisioning (GÜVENLİ tenant kurulum; X-Provision-Key ile korunur) =====
public record ProvisionTenantRequest(string Code, string Name, string AdminEmail, string AdminFullName);

// ===== Menü =====
public record CategoryDto(int Id, string Name, bool HasImage);
public record MenuProductDto(int Id, string Code, string Name, string? Description, decimal Price, decimal TaxRate,
    bool IsSalesStopped, bool IsMenu, bool HasImage, IReadOnlyList<int> CategoryIds);

// ===== Public (anonim / QR masadan sipariş) =====
// Fiyat/KDV DAİMA sunucudan (menü) çözülür; istemci fiyat göndermez → fiyat manipülasyonu imkânsız.
public record PublicOrderItem(int ProductId, decimal Qty, string? Note);
public record PublicOrderRequest(IReadOnlyList<PublicOrderItem> Items);
// QR "Garson Çağır" / "Hesap İste": Type = ServiceRequestType (0=CagriGarson, 2=Hesap); null/diğer → CagriGarson.
public sealed record PublicCallRequest(int? Type, string? Note);

// ===== Sipariş işlemleri =====
public record OpenOrderRequest(int TableId, int ZoneId);
public record AddLineRequest(int ProductId, decimal Qty, decimal UnitPrice, decimal TaxRate, string? Note, int SeatNo,
    IReadOnlyList<int>? OptionIds = null);
public record ChangeQtyRequest(decimal Qty);
public record PaymentLineDto(int Kind, decimal Amount, int? InstrumentId, int CurrencyId);
public record PayRequest(IReadOnlyList<PaymentLineDto> Payments, int? OverrideCustomerId = null);
public record InstrumentDto(int Id, string Name, string Scope);

// ===== PC'deki tüm adisyon işlemleri =====
public record TreatRequest(bool IsTreat);
public record DiscountRequest(decimal Amount);
public record NoteRequest(string? Note);
public record MoveRequest(int TargetTableId);
public record SplitRequest(int TargetTableId, List<int> LineIds);
public record MergeRequest(int TargetOrderId);
public record TransferLinesRequest(int TargetOrderId, List<int> LineIds);
public record CoverRequest(int Count);
public record OrderFlagsRequest(bool? Vip, bool? Priority);
public record CustomerDto(int Id, string Code, string Name);
public record SettleRequest(List<int>? TopLineIds, List<PaymentLineDto> Payments, int? OverrideCustomerId);
public record RefundRequest(decimal RefundQty, bool ReturnToStock, int RefundKind, int? InstrumentId);
public record CampaignDto(int Id, string Name, bool Applied, bool Eligible, bool Mandatory);

// ===== Paket / Gel-Al / Kurye / Teslimat =====
// Type: OrderType (Paket=1, TakeAway=2). Platform: DeliveryPlatform (Telefon=0, GelAl=1, Trendyol=2, Getir=3, YemekSepeti=4, Migros=5, Diger=6).
public record CreatePackageRequest(int Type, int Platform, string? Name, string? Phone, string? Address);
public record DeliveryStatusRequest(int Status);   // DeliveryStatus (Alindi=0, Hazirlaniyor=1, YolaCikti=2, TeslimEdildi=3, Iptal=4)
public record AssignCourierRequest(int CourierId);
public record CourierDto(int Id, string Code, string Name, string? Phone);

// FAZ 3b Online teslimat inbound: durum senkron (yerel → platforma geri bildir). Status = DeliveryStatus.
public record DeliverySyncStatusRequest(int Status, bool NotifyCustomer = false);
// Simülasyon sonucu (uçtan uca iç akış testi).
public record InboundIngestDto(int LocalOrderId, bool Duplicate, int MatchedLines, IEnumerable<string> UnmatchedItems, string Message);

// ===== Hediye çeki + Puan/Sadakat =====
public record GiftCardIssueRequest(decimal Amount, int? CurrencyId, int? CustomerId, DateTime? ExpiresUtc, string? Code, string? Note, int SaleKind = 0, int? SaleInstrumentId = null);
public record LoyaltySettingsDto(bool IsActive, decimal EarnRate, decimal RedeemRate);

public record LoginResponse(
    string Token,
    DateTime ExpiresUtc,
    int UserId,
    string FullName,
    string CompanyCode,
    string CompanyName,
    int? BranchId,
    string? BranchName,
    IReadOnlyList<string> Roles,
    string LandingScreen,     // "kitchen" | "waiter" | "patron"
    string LicensePackage,
    long FeatureBits);
