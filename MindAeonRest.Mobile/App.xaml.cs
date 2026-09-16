using MindAeonRest.Mobile.Pages;
using MindAeonRest.Mobile.Services;

namespace MindAeonRest.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _sp;
	private readonly AppState _state;

	public App(IServiceProvider sp, AppState state)
	{
		InitializeComponent();
		MauiTheme.LoadSaved();   // kayıtlı temayı (Açık/Koyu/Okyanus) uygula
		_sp = sp;
		_state = state;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var loading = new ContentPage
		{
			BackgroundColor = Color.FromArgb("#EEF0F4"),
			Content = new ActivityIndicator
			{
				IsRunning = true,
				Color = Color.FromArgb("#E91E63"),
				VerticalOptions = LayoutOptions.Center,
				HorizontalOptions = LayoutOptions.Center
			}
		};
		var window = new Window(new NavigationPage(loading));
		_ = InitAsync();
		return window;
	}

	private async Task InitAsync()
	{
		await _state.LoadAsync();
		if (!_state.IsConfigured) GoTo<ConfigPage>();
		else GoTo<LoginPage>();
	}

	/// <summary>Yığını sıfırlayıp verilen sayfaya geçer (DI ile çözülür).</summary>
	public void GoTo<TPage>() where TPage : Page
	{
		var page = _sp.GetService<TPage>() ?? Activator.CreateInstance<TPage>();
		Dispatcher.Dispatch(() =>
		{
			if (Windows.Count > 0) Windows[0].Page = new NavigationPage(page);
		});
	}

	/// <summary>Merkezi oturum kapatma: SignalR'ı durdur (eski JWT ile bağlı kalmasın),
	/// token + AppState'i temizle, LoginPage'e dön. Elle "Çıkış" ve 401 (oturum düştü) buraya akar.</summary>
	public async Task LogoutAsync()
	{
		try { var hub = _sp.GetService<HubService>(); if (hub is not null) await hub.StopAsync(); }
		catch { /* hub durdurulamazsa yine de oturumu düşür */ }
		await _state.LogoutAsync();
		GoTo<LoginPage>();
	}

	/// <summary>Giriş sonrası role/pakete göre açılış ekranı.</summary>
	public void GoToLanding(string landing)
	{
		switch (landing)
		{
			case "kitchen": GoTo<KitchenPage>(); break;
			case "patron": GoTo<PatronPage>(); break;
			default:
				if (_state.AdisyonV2) GoTo<WaiterV2Page>();
				else GoTo<WaiterPage>();
				break;
		}
	}
}
