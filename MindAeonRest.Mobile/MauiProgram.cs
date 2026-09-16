using Microsoft.Extensions.Logging;
using MindAeonRest.Mobile.Pages;
using MindAeonRest.Mobile.Services;
using ZXing.Net.Maui.Controls;

namespace MindAeonRest.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Çekirdek servisler
		builder.Services.AddSingleton<AppState>();
		builder.Services.AddSingleton<ApiClient>();
		builder.Services.AddSingleton<HubService>();

		// Sayfalar
		builder.Services.AddTransient<ConfigPage>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<WaiterPage>();
		builder.Services.AddTransient<KitchenPage>();
		builder.Services.AddTransient<PatronPage>();
		builder.Services.AddTransient<ShiftClosePage>();
		builder.Services.AddTransient<StockCountPage>();
		builder.Services.AddTransient<TimeClockPage>();

		// V2 garson terminali
		builder.Services.AddTransient<WaiterV2Page>();
		// MenuV2Page navigasyonda parametreli (new) oluşturulur, DI'a gerekmez.

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
