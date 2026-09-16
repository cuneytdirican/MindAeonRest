using System;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class RaporWindow : Window
{
    private readonly ReportService _reports = new();
    private readonly int _branchId;
    private XZReport? _current;

    public RaporWindow()
    {
        InitializeComponent();
        _branchId = Session.CurrentBranch?.Id ?? 0;
        // İŞ GÜNÜ: "bugün" yerine içinde bulunulan iş gününü kullan (şubenin kesim/DayStartMinutes değerine göre).
        Loaded += (_, _) =>
        {
            int cut = Session.CurrentBranch?.DayStartMinutes ?? 0;
            DateBox.SelectedDate = BusinessDay.CurrentDate(cut);
            Load();
        };
    }

    private void Load()
    {
        try
        {
            int cut = Session.CurrentBranch?.DayStartMinutes ?? 0;
            var date = DateBox.SelectedDate ?? BusinessDay.CurrentDate(cut);
            var rep = _reports.GetDailyReport(_branchId, date);
            _current = rep;
            RepTitle.Text = "GÜN RAPORU (X)";
            // Kesim 0'dan büyükse iş günü aralığını göster (örn. 06:00 – ertesi 06:00). cut=0 ise mevcut başlık aynı kalır.
            if (cut > 0)
            {
                string startHm = TimeSpan.FromMinutes(cut).ToString(@"hh\:mm");
                RepSub.Text = $"{rep.BranchName} · {rep.DateText}  (İş günü: {date:dd.MM.yyyy} {startHm} – ertesi {startHm})";
            }
            else
            {
                RepSub.Text = $"{rep.BranchName} · {rep.DateText}";
            }
            SalesText.Text = rep.SalesText;
            CountText.Text = $"{rep.OrderCount} adisyon · {rep.FisCount} fiş";
            PayHost.ItemsSource = rep.Payments;
            KdvHost.ItemsSource = rep.Kdv;
            FireText.Text = $"İkram {rep.TreatText} · Fire {rep.FireQty:0.##} adet";
            CashHost.ItemsSource = rep.CashBalances;
            BankHost.ItemsSource = rep.BankBalances;
            PosHost.ItemsSource = rep.PosBalances;
            MealHost.ItemsSource = rep.MealVoucherBalances;

            // Gün sonu devri: hedef ŞUBE kasası/POS (şube kapsamlı, aktif) + nakit devir varsayılan
            var subeCash = new CashRegisterService().GetAll().Where(k => k.BranchId == _branchId && k.IsActive && k.Scope == FinanceScope.Sube).Select(k => new ComboItem(k.Id, k.Name)).ToList();
            TargetCashCombo.ItemsSource = subeCash;
            if (subeCash.Count > 0 && TargetCashCombo.SelectedValue is null) TargetCashCombo.SelectedIndex = 0;
            var subePos = new PosService().GetAll().Where(p => p.BranchId == _branchId && p.IsActive && p.Scope == FinanceScope.Sube).Select(p => new ComboItem(p.Id, p.Name)).ToList();
            TargetPosCombo.ItemsSource = subePos;
            if (subePos.Count > 0 && TargetPosCombo.SelectedValue is null) TargetPosCombo.SelectedIndex = 0;
            // Varsayılan nakit devir = KIOSK kasalarındaki devredilebilir nakit (şube kasaları hariç)
            var kioskCashIds = new CashRegisterService().GetAll().Where(k => k.BranchId == _branchId && k.Scope == FinanceScope.Kiosk).Select(k => k.Id).ToHashSet();
            decimal kioskCash = new CashMovementService().GetBalances(_branchId).Where(b => kioskCashIds.Contains(b.CashRegisterId)).Sum(b => b.Balance);
            CashTransferBox.Text = kioskCash.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Date_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Load(); }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new System.Windows.Controls.PrintDialog();
            if (dlg.ShowDialog() != true) return;
            ReportPanel.Measure(new Size(dlg.PrintableAreaWidth, double.PositiveInfinity));
            ReportPanel.Arrange(new Rect(new Point(0, 0), ReportPanel.DesiredSize));
            dlg.PrintVisual(ReportPanel, "X/Z Raporu");
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void TakeZ_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (!Permissions.ZReport) { Dialog.Warn("Z (gün sonu) raporu almak için yetkiniz yok. Yalnız yönetici veya 'Z_Rapor' rolü alabilir."); return; }
        // Z yalnız İÇİNDE BULUNULAN İŞ GÜNÜ için alınır: geçmiş/gelecek iş-günü tarihli Z, kapanmış siparişleri beklenmedik şekilde kilitler.
        int zCut = Session.CurrentBranch?.DayStartMinutes ?? 0;
        var bizToday = BusinessDay.CurrentDate(zCut);
        var selected = (DateBox.SelectedDate ?? bizToday).Date;
        if (selected != bizToday)
        { Dialog.Warn("Z raporu yalnız içinde bulunulan iş günü için alınabilir. Lütfen tarihi geçerli iş gününe alın."); return; }
        decimal cashTransfer = 0;
        decimal.TryParse((CashTransferBox.Text ?? "").Trim().Replace('.', ','), NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out cashTransfer);
        int? tc = TargetCashCombo.SelectedValue as int?;
        int? tp = TargetPosCombo.SelectedValue as int?;
        if (cashTransfer > 0 && tc is null) { Dialog.Warn("Nakit devri için hedef şube nakit kasası seçin (ya da devir tutarını 0 yapın)."); return; }

        // AÇIK MASA UYARISI: Z almadan önce şubede açık (Status=Açık) adisyon var mı kontrol et.
        // Varsa kullanıcıya listele ve seçim sun: Önce Kapat (Z iptal) / Yine de devam et / Vazgeç.
        try
        {
            var openTables = new OrderService().GetUnpaidTables(_branchId);
            if (openTables.Count > 0)
            {
                // Listeyi rapor üzerindeki expander'da göster
                OpenTablesHost.ItemsSource = openTables;
                OpenTablesPanel.Visibility = Visibility.Visible;
                OpenTablesExpander.IsExpanded = true;
                OpenTablesTitle.Text = $"⚠ AÇIK MASALAR ({openTables.Count}) — Z'den önce kontrol edin";

                decimal openRemaining = openTables.Sum(t => t.Remaining);
                var lines = openTables
                    .Take(15)
                    .Select(t => $"• {(string.IsNullOrWhiteSpace(t.TableCodes) ? t.OrderCode : t.TableCodes)} — Kalan {t.Remaining:N2} ₺ ({t.StatusText})");
                string list = string.Join("\n", lines);
                if (openTables.Count > 15) list += $"\n… ve {openTables.Count - 15} masa daha";

                string warnMsg =
                    $"Şubede {openTables.Count} AÇIK masa var (toplam kalan ₺{openRemaining:N2}).\n\n" +
                    list +
                    "\n\nBu masalar henüz kapatılmadı. Ne yapmak istersiniz?\n\n" +
                    "• Evet (Yine de devam et): Z alınır, açık masalar sonraki güne devreder.\n" +
                    "• Hayır (Önce Kapat): Z iptal edilir, masaları kapatmaya dönersiniz.\n" +
                    "• Vazgeç: Hiçbir şey yapılmaz.";

                var choice = Dialog.YesNoCancel(warnMsg, "Açık Masa Uyarısı");
                if (choice != DialogResultType.Yes) return; // Hayır = Önce Kapat, Vazgeç = iptal → Z alınmaz
            }
            else
            {
                OpenTablesPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); return; }

        string msg = cashTransfer > 0 || tp is not null
            ? $"Gün sonu (Z) raporu alınsın mı?\n\nDevir: Nakit ₺{cashTransfer:N2} → şube kasası" + (tp is not null ? ", Kart bakiyeleri → şube POS" : "") + ".\nKalan nakit kasada kalır. Açık masalar etkilenmez."
            : "Gün sonu (Z) raporu alınsın mı? Açık/ödenmemiş masalar etkilenmez; sonraki vardiyaya devreder.";
        if (!Dialog.Confirm(msg, "Z Raporu")) return;
        try
        {
            _reports.TakeZWithTransfer(_current, _branchId, cashTransfer, tc, tp, Session.CurrentUserId);
            RepTitle.Text = "GÜN SONU RAPORU (Z)";
            Dialog.Info("Z raporu alındı, devir yapıldı ve kaydedildi. Açık masalar değişmeden devam eder.", "Bilgi");
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
