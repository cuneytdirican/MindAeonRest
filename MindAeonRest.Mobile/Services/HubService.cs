using Microsoft.AspNetCore.SignalR.Client;

namespace MindAeonRest.Mobile.Services;

/// <summary>SignalR gerçek zamanlı bağlantı. Mutfak "Hazır" deyince / yeni sipariş gelince
/// sunucu push yapar; sayfalar bu olaylara abone olur. Tek bağlantı tüm uygulamada paylaşılır.</summary>
public class HubService(AppState state)
{
    private HubConnection? _conn;
    // Birden çok sayfa aynı anda OnAppearing'te EnsureConnectedAsync/StopAsync çağırabilir →
    // eşzamanlı çift-bağlantı / bağlan-dur yarışını tek kilitle önle.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action? NewTicket;       // yeni sipariş mutfağa düştü
    public event Action<int>? TicketReady; // (lineId) hazır oldu
    public event Action? TableChanged;     // masa/sipariş değişti
    public event Action? ServiceChanged;   // masa servis isteği (çağrı/temizlik/hesap) değişti

    public bool IsConnected => _conn?.State == HubConnectionState.Connected;

    public async Task EnsureConnectedAsync()
    {
        if (_conn is not null && _conn.State != HubConnectionState.Disconnected) return;
        if (string.IsNullOrEmpty(state.ApiBaseUrl) || string.IsNullOrEmpty(state.Token)) return;

        await _gate.WaitAsync();
        try
        {
            // Beklerken başka bir çağrı bağlamış / oturum düşmüş olabilir → yeniden kontrol.
            if (_conn is not null && _conn.State != HubConnectionState.Disconnected) return;
            if (string.IsNullOrEmpty(state.ApiBaseUrl) || string.IsNullOrEmpty(state.Token)) return;

            var url = $"{state.ApiBaseUrl}/hubs/pos?access_token={state.Token}";
            var conn = new HubConnectionBuilder()
                .WithUrl(url)
                .WithAutomaticReconnect()
                .Build();

            conn.On("NewTicket", () => OnMain(() => NewTicket?.Invoke()));
            conn.On<System.Text.Json.JsonElement>("TicketReady", payload => OnMain(() =>
            {
                int lineId = payload.TryGetProperty("lineId", out var v) && v.TryGetInt32(out var id) ? id : 0;
                TicketReady?.Invoke(lineId);
            }));
            conn.On("TableChanged", () => OnMain(() => TableChanged?.Invoke()));
            conn.On("ServiceChanged", () => OnMain(() => ServiceChanged?.Invoke()));

            try { await conn.StartAsync(); } catch { /* bağlanamazsa sessiz; sayfalar yine de polling yapar */ }
            _conn = conn;
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_conn is not null) { try { await _conn.StopAsync(); } catch { } await _conn.DisposeAsync(); _conn = null; }
        }
        finally { _gate.Release(); }
    }

    private static void OnMain(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null) d.Dispatch(a); else a();
    }
}
