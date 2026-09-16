namespace MindAeonRest.Mobile.Models;

public record BranchDto(int Id, string Name);

// Kod-login: şirket kodu + kullanıcı + parola tek istek. branchId? çok şubeli girişte 2. aşamada gönderilir.
public record LoginRequest(string CompanyCode, string Username, string Password, int? BranchId);

// Tek uç iki sonuç döndürebilir:
//  - needBranch=true → token/roller yok, yalnız Branches dolu (şube seçimi gerekiyor).
//  - başarı → Token + roller + landing dolu (needBranch=false, Branches null/boş).
public record LoginResponse(
    string? Token, DateTime ExpiresUtc, int UserId, string FullName,
    string CompanyCode, string CompanyName, int? BranchId, string? BranchName,
    List<string>? Roles, string? LandingScreen, string? LicensePackage, long FeatureBits,
    bool NeedBranch = false, List<BranchDto>? Branches = null);

// FAZ 4b mobil sayım (Stok Zekâsı)
public record StockWarehouseDto(int Id, string Name, int BranchId);
public record ProductResolveDto(int ProductId, string Code, string Name);
public record StockCountLineDto(int ProductId, decimal CountedQty);
public record StockCountRequestDto(int? BranchId, int WarehouseId, List<StockCountLineDto> Lines, bool Post);
public record StockCountResultDto(int DocumentId, string FisNo, bool Posted);

// Menü / masa / sipariş (M3+ için)
public record CategoryDto(int Id, string Name, bool HasImage);
public record MenuProductDto(int Id, string Code, string Name, string? Description, decimal Price, decimal TaxRate,
    bool IsSalesStopped, bool IsMenu, bool HasImage, List<int> CategoryIds);
public record ZoneDto(int Id, string Name, int BranchId);
public record TableCardDto(int TableId, string Code, string? Name, string Display, int Seats,
    int Status, int? OrderId, decimal Amount, string? WaiterName, DateTime? OpenedAt);

public record OrderDto(int Id, int ZoneId, int BranchId, int CustomerId, int CoverCount = 1);
public record OrderLineDto(int Id, int OrderId, int ProductId, string? ProductName, decimal Qty,
    decimal UnitPrice, decimal TaxRate, int LineStatus, int? ParentLineId, bool IsTreat,
    decimal DiscountAmount, decimal LineTotal, string? KitchenNote = null, int? SettledClFicheId = null);
public record ByTableResult(OrderDto? Order, List<OrderLineDto> Lines);

public record OpenOrderRequest(int TableId, int ZoneId);
public record AddLineRequest(int ProductId, decimal Qty, decimal UnitPrice, decimal TaxRate, string? Note, int SeatNo);
public record ChangeQtyRequest(decimal Qty);
public record PaymentLineDto(int Kind, decimal Amount, int? InstrumentId, int CurrencyId);
public record PayRequest(List<PaymentLineDto> Payments);
public record InstrumentDto(int Id, string Name, string Scope);
public record CustomerDto(int Id, string Code, string Name);
public record SettleRequest(List<int>? TopLineIds, List<PaymentLineDto> Payments, int? OverrideCustomerId);
public record TreatRequest(bool IsTreat);
public record DiscountRequest(decimal Amount);
public record NoteRequest(string? Note);
public record CoverRequest(int Count);
public record MoveRequest(int TargetTableId);
// Masa işlemleri (böl / birleştir / kişi-adisyon aktar)
public record SplitRequest(int TargetTableId, List<int> LineIds);
public record MergeRequest(int TargetOrderId);
public record TransferLinesRequest(int TargetOrderId, List<int> LineIds);

// Patron raporları
public record TopProductDto(string Name, decimal Amount, double Percent);
public record DashboardDto(decimal SalesTotal, decimal Nakit, decimal Kart, decimal Diger,
    int AdisyonCount, decimal KasaTotal, decimal Average, List<TopProductDto> TopProducts,
    decimal GiftCardLiability = 0m, decimal LoyaltyLiability = 0m);
public record TableLiveDto(string TableCode, string State, decimal Total, string? WaiterName, string ZoneName);

// Mutfak (KDS)
public record KdsChildDto(string? ProductName, decimal Qty, string? KitchenNote)
{
    public string QtyText => $"{Qty:0.##}x";
    public bool HasNote => !string.IsNullOrWhiteSpace(KitchenNote);
}

// API KdsLine'ı camelCase JSON serileştirir; enum'lar int olarak gelir (LineStatus/StationType/OrderType/Platform).
// Hesaplanmış alanlar (typeLabel/statusColor/isCooling/isCritical/isVip/priority/isSpicy/elapsedText...) de gövdede gelir.
public record KdsLineDto(int LineId, int OrderId, string OrderCode, string? ProductName, decimal Qty,
    int SeatNo, string? KitchenNote, int? ReyonId, string? ReyonName, int LineStatus, DateTime? SentAt,
    string? WaiterName, string? TableCodes, List<KdsChildDto> Children,
    int StationType, string? ZoneName, int CoverCount, string? OrderNote, string? AllergyNote,
    // V2 zengin kart alanları (sunucudan hesaplanmış gelir)
    int OrderType, int Platform, string? TypeLabel,
    bool IsOnline, bool IsPackage, bool IsNonTable, bool IsTakeaway, bool IsKiosk,
    bool IsVip, bool Priority, bool IsSpicy,
    bool IsReady, bool IsPreparing, bool IsBeverage,
    bool IsCooling, bool IsCritical, bool HasAllergy,
    string? StatusColor, int ElapsedMinutes, string? ElapsedText,
    int PrintCount, bool RePrinted)
{
    // null gelirse güvenli varsayılanlar (Children API'de daima dolu döner ama temkinli ol)
    public List<KdsChildDto> Children { get; init; } = Children ?? new();
    public bool HasChildren => Children.Count > 0;
}

// V2 — ekran sürümü, servis istekleri, ödeme bekleyen masalar
public record UiSettingsDto(string AdisyonVersion, string MutfakVersion, bool AdisyonV2, bool MutfakV2);
public record ServiceRequestDto(int Id, int? TableId, string? TableCode, int? ZoneId, string? ZoneName,
    int? OrderId, int Type, string TypeText, int Status, string? Note, string ElapsedText);
public record CreateServiceRequestDto(int? TableId, int? ZoneId, int? OrderId, int Type, string? Note);
// Masasız (paket/gel-al/self-servis) siparişler de bu listede gelir; sunucu ayrıca IsNonTable/ChannelText/DisplayLabel
// hesaplanmış alanlarını da döndürür. Yeni alanlar opsiyoneldir (eksik JSON tolere edilir); NonTable/Channel/Label
// yardımcıları sunucu değeri boşsa istemci tarafında yedek üretir.
public record UnpaidTableDto(int OrderId, string OrderCode, string? TableCodes, string? ZoneName,
    string? WaiterName, int CoverCount, DateTime OpenedAt, decimal Total, decimal Paid, bool HasBillRequest,
    int OrderType = 0, string? DeliveryName = null, bool IsNonTable = false,
    string? ChannelText = null, string? DisplayLabel = null)
{
    public decimal Remaining => Total - Paid;
    public bool IsPartial => Paid > 0 && Remaining > 0;
    // Masaya bağlı DEĞİL (paket/gel-al/self-servis/kiosk) → masa listesinde görünmez, kanal etiketiyle gösterilmeli.
    public bool NonTable => IsNonTable || OrderType != 0 || string.IsNullOrWhiteSpace(TableCodes);
    public string Channel => !string.IsNullOrWhiteSpace(ChannelText) ? ChannelText!
        : OrderType switch { 1 => "Paket", 2 => "Gel-Al / Self-Servis", 3 => "Kiosk", _ => "Masa" };
    // Ana etiket: masa kodu ?? teslim adı ?? adisyon kodu (sunucu DisplayLabel'ı öncelikli).
    public string Label => !string.IsNullOrWhiteSpace(DisplayLabel) ? DisplayLabel!
        : !string.IsNullOrWhiteSpace(TableCodes) ? TableCodes!
        : !string.IsNullOrWhiteSpace(DeliveryName) ? DeliveryName! : OrderCode;
}
public record NotePresetsDto(List<string> General, List<string> Allergy);
public record CampaignDto(int Id, string Name, bool Applied, bool Eligible);

// Ödeme — hediye çeki ve puan/sadakat
public record GiftCardDto(int Id, decimal Balance, int Status, string? Code = null);
public record LoyaltyBalanceDto(decimal Points, bool IsActive, decimal RedeemRate, decimal EarnRate);

// Vardiya (kasiyer/admin). Branch+kiosk JWT'den çözülür; istemci geçmez.
public record ShiftDto(
    int Id, int BranchId, int KioskNo, DateTime OpenedAt, DateTime? ClosedAt,
    decimal OpeningFloat, decimal? ClosingCounted, int Status, string? Note);
public record ShiftOpenRequest(decimal OpeningFloat);
public record ShiftCloseRequest(decimal CountedCash, string? Note);
public record ShiftReportPayDto(int Kind, decimal Amount);
public record ShiftUserLineDto(int UserId, string UserName, decimal Amount);
public record ShiftReportDto(
    int ShiftId, DateTime OpenedAt, DateTime? ClosedAt, decimal OpeningFloat,
    List<ShiftReportPayDto> Payments, List<ShiftUserLineDto> ByUser,
    decimal CashCollected, decimal ExpectedCash, decimal Total);

// FAZ 4c — Personel mesai (time-clock). Branch/kiosk JWT'den çözülür; istemci geçmez.
public record PersonnelPickDto(int Id, string Code, string FullName);
public record AttendanceDto(
    int Id, int PersonnelId, int BranchId, DateTime ClockIn, DateTime? ClockOut,
    string? Note, int KioskNo, string? PersonnelName, string? PersonnelCode);
public record ClockRequest(int PersonnelId, string? Note);
