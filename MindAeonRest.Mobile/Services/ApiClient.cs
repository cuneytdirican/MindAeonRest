using System.Net.Http.Json;
using System.Text.Json;
using MindAeonRest.Mobile.Models;
using MindAeonRest.Mobile;

namespace MindAeonRest.Mobile.Services;

/// <summary>Kayıtlı API ile tüm HTTP haberleşmesi. Token varsa Bearer ekler.</summary>
public class ApiClient(AppState state)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    // Aynı anda birden çok istek 401 dönebilir → oturumu yalnız bir kez düşür (çift navigasyonu önle).
    private bool _sessionDropInProgress;

    private string Base => state.ApiBaseUrl ?? throw new InvalidOperationException("API adresi tanımlı değil.");

    private HttpRequestMessage Req(HttpMethod m, string path, object? body = null)
    {
        var req = new HttpRequestMessage(m, Base + path);
        if (!string.IsNullOrEmpty(state.Token))
            req.Headers.Authorization = new("Bearer", state.Token);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: Json);
        return req;
    }

    /// <summary>Bağlantı testi: /api/ping. Verilen url ile (kaydetmeden) dener.</summary>
    public async Task<bool> TestConnectionAsync(string url)
    {
        try
        {
            var u = AppState.NormalizeUrl(url);
            using var resp = await _http.GetAsync(u + "/api/ping");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Kod-login. Tek uç: 401→hata, 200+needBranch→şube seçimi gerek (token yok),
    /// 200+token→başarı (token saklanır). branchId? 2. aşamada (şube seçimi sonrası) gönderilir.</summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest req)
    {
        using var resp = await _http.SendAsync(Req(HttpMethod.Post, "/api/auth/login", req));
        if (!resp.IsSuccessStatusCode)
        {
            var msg = await SafeMessage(resp);
            throw new ApiException(msg);
        }
        var login = (await resp.Content.ReadFromJsonAsync<LoginResponse>(Json))!;

        // Şube seçimi gerekiyor: henüz token yok, oturum kurma — çağıran şube Picker'ı gösterir.
        if (login.NeedBranch || string.IsNullOrEmpty(login.Token))
            return login;

        await state.SaveTokenAsync(login.Token);
        state.Login = login;
        _sessionDropInProgress = false;   // yeni oturum → 401 düşürme kilidini aç
        // V2 garson terminali açık mı? Hata durumunda varsayılan (false) kalır.
        try { state.AdisyonV2 = (await UiSettingsAsync()).AdisyonV2; }
        catch { state.AdisyonV2 = false; }
        return login;
    }

    // ---- V2 garson terminali uçları ----
    public Task<UiSettingsDto> UiSettingsAsync() => GetAsync<UiSettingsDto>("/api/settings/ui");
    public Task<NotePresetsDto> NotePresetsAsync() => GetAsync<NotePresetsDto>("/api/settings/note-presets");
    public Task<List<UnpaidTableDto>> UnpaidTablesAsync() => GetAsync<List<UnpaidTableDto>>("/api/orders/unpaid");
    public Task<List<KdsLineDto>> KitchenTicketsAsync(bool includeDone = false) =>
        GetAsync<List<KdsLineDto>>($"/api/kitchen/tickets?includeDone={(includeDone ? "true" : "false")}");
    public Task KitchenStartAsync(int lineId) => PostAsync($"/api/kitchen/lines/{lineId}/start");
    public Task KitchenReadyAsync(int lineId) => PostAsync($"/api/kitchen/lines/{lineId}/ready");
    public Task KitchenDeliveredAsync(int lineId) => PostAsync($"/api/kitchen/lines/{lineId}/delivered");
    public Task<List<ServiceRequestDto>> ServiceRequestsAsync(int? type = null) =>
        GetAsync<List<ServiceRequestDto>>("/api/service-requests" + (type is { } t ? $"?type={t}" : ""));
    public Task CreateServiceRequestAsync(CreateServiceRequestDto dto) => PostAsync("/api/service-requests", dto);
    public Task CompleteServiceRequestAsync(int id) => PostAsync($"/api/service-requests/{id}/complete");
    public Task SetAllergyAsync(int orderId, string? note) => PostAsync($"/api/orders/{orderId}/allergy", new NoteRequest(note));
    public Task SetOrderNoteAsync(int orderId, string? note) => PostAsync($"/api/orders/{orderId}/order-note", new NoteRequest(note));
    public Task UpdateLineNoteAsync(int lineId, string? note) => PostAsync($"/api/orders/lines/{lineId}/note", new NoteRequest(note));
    // İkram / iskonto (yalnız ödeme yetkili rol; backend de 403 ile korur)
    public Task TreatAsync(int lineId, bool isTreat) => PostAsync($"/api/orders/lines/{lineId}/treat", new TreatRequest(isTreat));
    public Task DiscountAsync(int lineId, decimal amount) => PostAsync($"/api/orders/lines/{lineId}/discount", new DiscountRequest(amount));
    public Task SetOrderFlagsAsync(int orderId, bool? vip, bool? priority) => PostAsync($"/api/orders/{orderId}/flags", new { vip, priority });

    // ---- Mobil sayım (Stok Zekâsı) ----
    public Task<List<StockWarehouseDto>> StockWarehousesAsync(int branchId) =>
        GetAsync<List<StockWarehouseDto>>($"/api/stock/warehouses?branchId={branchId}");
    public Task<ProductResolveDto> ResolveBarcodeAsync(string code) =>
        GetAsync<ProductResolveDto>($"/api/stock/product-by-barcode/{Uri.EscapeDataString(code)}");
    public Task<StockCountResultDto> SubmitStockCountAsync(StockCountRequestDto req) =>
        PostAsync<StockCountResultDto>("/api/stock/count", req);

    // ---- Ödeme uçları (hediye çeki / puan-sadakat / cari) ----
    public Task<List<InstrumentDto>> InstrumentsAsync(int kind) =>
        GetAsync<List<InstrumentDto>>($"/api/pos/instruments?kind={kind}");
    public Task<List<CustomerDto>> CustomersAsync() => GetAsync<List<CustomerDto>>("/api/customers");
    public Task<GiftCardDto> GiftCardByCodeAsync(string code) =>
        GetAsync<GiftCardDto>($"/api/giftcards/by-code/{Uri.EscapeDataString(code)}");
    public Task<LoyaltyBalanceDto> LoyaltyBalanceAsync(int customerId) =>
        GetAsync<LoyaltyBalanceDto>($"/api/loyalty/balance/{customerId}");

    // ---- Vardiya (kasiyer/admin) ----
    /// <summary>Bu terminaldeki (branch+kiosk) AÇIK vardiya; yoksa null.</summary>
    public Task<ShiftDto?> ShiftCurrentAsync() => GetNullableAsync<ShiftDto>("/api/shifts/current");
    /// <summary>Terminalde yeni vardiya aç (açılış kasası ile).</summary>
    public Task<ShiftDto> ShiftOpenAsync(decimal openingFloat) =>
        PostAsync<ShiftDto>("/api/shifts/open", new ShiftOpenRequest(openingFloat));
    /// <summary>Vardiyayı kapat (sayılan nakit + not).</summary>
    public Task ShiftCloseAsync(int id, decimal countedCash, string? note) =>
        PostAsync($"/api/shifts/{id}/close", new ShiftCloseRequest(countedCash, note));
    /// <summary>Vardiya raporu (ödeme yöntemi + tahsil eden kullanıcı kırılımı, beklenen kasa).</summary>
    public Task<ShiftReportDto> ShiftReportAsync(int id) => GetAsync<ShiftReportDto>($"/api/shifts/{id}/report");

    // ---- Personel mesai (time-clock) ----
    /// <summary>Bu şubenin aktif personelleri (mesai giriş/çıkış picker'ı için).</summary>
    public Task<List<PersonnelPickDto>> AttendancePersonnelAsync() =>
        GetAsync<List<PersonnelPickDto>>("/api/attendance/personnel");
    /// <summary>Personelin AÇIK (çıkışsız) mesaisi; yoksa null.</summary>
    public Task<AttendanceDto?> AttendanceOpenAsync(int personnelId) =>
        GetNullableAsync<AttendanceDto>($"/api/attendance/open?personnelId={personnelId}");
    /// <summary>Mesai girişi (açık mesai zaten varsa hata).</summary>
    public Task<AttendanceDto> ClockInAsync(int personnelId, string? note) =>
        PostAsync<AttendanceDto>("/api/attendance/clock-in", new ClockRequest(personnelId, note));
    /// <summary>Mesai çıkışı (açık mesaiyi kapatır).</summary>
    public Task<AttendanceDto> ClockOutAsync(int personnelId, string? note) =>
        PostAsync<AttendanceDto>("/api/attendance/clock-out", new ClockRequest(personnelId, note));

    // ---- Masa işlemleri (taşı / böl / birleştir / kişi-adisyon aktar) ----
    public Task MoveOrderAsync(int orderId, int targetTableId) =>
        PostAsync($"/api/orders/{orderId}/move", new MoveRequest(targetTableId));
    public Task<OrderDto> SplitOrderAsync(int orderId, int targetTableId, List<int> lineIds) =>
        PostAsync<OrderDto>($"/api/orders/{orderId}/split", new SplitRequest(targetTableId, lineIds));
    public Task MergeOrderAsync(int orderId, int targetOrderId) =>
        PostAsync($"/api/orders/{orderId}/merge", new MergeRequest(targetOrderId));
    public Task TransferLinesAsync(int orderId, int targetOrderId, List<int> lineIds) =>
        PostAsync($"/api/orders/{orderId}/transfer-lines", new TransferLinesRequest(targetOrderId, lineIds));

    public async Task<T> GetAsync<T>(string path)
    {
        using var resp = await SendAuthedAsync(Req(HttpMethod.Get, path));
        if (!resp.IsSuccessStatusCode) throw new ApiException(await SafeMessage(resp));
        return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>200 + boş/null gövdeye izin veren GET (ör. /api/shifts/current açık vardiya yoksa null döner).</summary>
    public async Task<T?> GetNullableAsync<T>(string path) where T : class
    {
        using var resp = await SendAuthedAsync(Req(HttpMethod.Get, path));
        if (!resp.IsSuccessStatusCode) throw new ApiException(await SafeMessage(resp));
        if (resp.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        var raw = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "null") return null;
        return JsonSerializer.Deserialize<T>(raw, Json);
    }

    public async Task<T> PostAsync<T>(string path, object? body = null)
    {
        using var resp = await SendAuthedAsync(Req(HttpMethod.Post, path, body));
        if (!resp.IsSuccessStatusCode) throw new ApiException(await SafeMessage(resp));
        return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task PostAsync(string path, object? body = null)
    {
        using var resp = await SendAuthedAsync(Req(HttpMethod.Post, path, body));
        if (!resp.IsSuccessStatusCode) throw new ApiException(await SafeMessage(resp));
    }

    public async Task PatchAsync(string path, object? body = null)
    {
        using var resp = await SendAuthedAsync(Req(HttpMethod.Patch, path, body));
        if (!resp.IsSuccessStatusCode) throw new ApiException(await SafeMessage(resp));
    }

    public async Task DeleteAsync(string path)
    {
        using var resp = await SendAuthedAsync(Req(HttpMethod.Delete, path));
        if (!resp.IsSuccessStatusCode) throw new ApiException(await SafeMessage(resp));
    }

    /// <summary>Yetkili (token'lı) tüm isteklerin tek çıkış noktası. 401 gelirse oturumu merkezi olarak
    /// düşürür (token temizle + AppState sıfırla + SignalR durdur + LoginPage'e dön); sonra yanıtı çağırana
    /// döndürür ki mevcut "Yetkisiz" hata mesajı davranışı korunsun. Login/ping bu yoldan GEÇMEZ.</summary>
    private async Task<HttpResponseMessage> SendAuthedAsync(HttpRequestMessage req)
    {
        var resp = await _http.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            await HandleUnauthorizedAsync();
        return resp;
    }

    /// <summary>Oturum süresi doldu / token geçersiz → uygulamayı login'e düşür. Tekrar-girişte sıfırlanır.</summary>
    private async Task HandleUnauthorizedAsync()
    {
        if (_sessionDropInProgress) return;
        _sessionDropInProgress = true;
        try
        {
            if (Application.Current is App app)
                await MainThread.InvokeOnMainThreadAsync(app.LogoutAsync);
            else
                await state.LogoutAsync();
        }
        catch { /* düşürme sırasında hata olsa bile isteği 401 olarak sonuçlandır */ }
    }

    private static async Task<string> SafeMessage(HttpResponseMessage resp)
    {
        try
        {
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("message", out var m))
                return m.GetString() ?? resp.ReasonPhrase ?? "Hata";
        }
        catch { }
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return "Yetkisiz / oturum geçersiz.";
        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return "Bu işlem için yetkiniz yok.";
        return resp.ReasonPhrase ?? "Sunucu hatası";
    }
}

public class ApiException(string message) : Exception(message);
