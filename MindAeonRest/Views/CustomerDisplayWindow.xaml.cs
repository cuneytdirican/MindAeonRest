using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;
using WinForms = System.Windows.Forms;

namespace MindAeonRest.Views;

public partial class CustomerDisplayWindow : Window
{
    public CustomerDisplayWindow()
    {
        InitializeComponent();

        // Logo (renkli)
        try
        {
            var (color, _) = Session.CurrentCompany is null ? (null, null) : new CompanyService().GetLogos(Session.CurrentCompany.Id);
            if (color is { Length: > 0 })
            {
                var img = new BitmapImage(); using var ms = new MemoryStream(color);
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
                LogoImg.Source = img;
            }
            WelcomeText.Text = Session.CurrentCompany?.Name ?? "Hoş geldiniz";
        }
        catch { /* logo opsiyonel */ }

        Loaded += (_, _) => PlaceOnSecondScreen();
    }

    private void PlaceOnSecondScreen()
    {
        try
        {
            var screens = WinForms.Screen.AllScreens;
            var target = screens.Length > 1
                ? screens.FirstOrDefault(s => !s.Primary) ?? screens[1]
                : WinForms.Screen.PrimaryScreen;
            if (target is null) return;
            var b = target.Bounds;
            Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
            WindowState = WindowState.Maximized;
        }
        catch { }
    }

    public void Update(IEnumerable<OrderLine> lines, decimal total)
    {
        LinesHost.ItemsSource = lines.Where(l => l.ParentLineId == null && l.LineStatus != OrderLineStatus.Iptal).ToList();
        TotalText.Text = $"₺{total:N2}";
    }
}
