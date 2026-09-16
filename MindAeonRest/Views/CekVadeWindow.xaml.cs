using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CekVadeWindow : Window
{
    private readonly CheckNoteService _svc = new();
    private List<CheckNote> _all = new();
    private List<CheckNote> _view = new();

    public CekVadeWindow()
    {
        InitializeComponent();
        KindCombo.ItemsSource = new[] { new ComboItem(-1, "Tümü"), new ComboItem(0, "Çek"), new ComboItem(1, "Senet") };
        DirCombo.ItemsSource = new[] { new ComboItem(-1, "Tümü"), new ComboItem(0, "Alınan"), new ComboItem(1, "Verilen") };
        DueCombo.ItemsSource = new[]
        {
            new ComboItem(-1, "Tümü"), new ComboItem(0, "Vadesi geçmiş"), new ComboItem(7, "7 gün içinde"),
            new ComboItem(15, "15 gün içinde"), new ComboItem(30, "30 gün içinde"), new ComboItem(999, "Açık (kapanmamış)")
        };
        StatusCombo.ItemsSource = new[]
        {
            new ComboItem(-1, "Tümü"), new ComboItem(0, "Portföyde"), new ComboItem(1, "Tahsilde"),
            new ComboItem(2, "Tahsil Edildi"), new ComboItem(3, "Ciro Edildi"), new ComboItem(4, "Karşılıksız"), new ComboItem(5, "Ödendi")
        };
        KindCombo.SelectedIndex = 0; DirCombo.SelectedIndex = 0; DueCombo.SelectedValue = 999; StatusCombo.SelectedIndex = 0;
        Loaded += (_, _) => Load();
    }

    /// <summary>"Vadesi yaklaşan" kısayolu: 7 gün içinde + açık filtresine ayarlar.</summary>
    public void FocusUpcoming()
    {
        DueCombo.SelectedValue = 7;
        StatusCombo.SelectedValue = -1;
        Apply();
    }

    private void Load()
    {
        try
        {
            _all = _svc.GetAll();
            Apply();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Apply()
    {
        IEnumerable<CheckNote> q = _all;
        if (KindCombo.SelectedValue is int k && k >= 0) q = q.Where(x => (int)x.Kind == k);
        if (DirCombo.SelectedValue is int d && d >= 0) q = q.Where(x => (int)x.Direction == d);
        if (StatusCombo.SelectedValue is int s && s >= 0) q = q.Where(x => (int)x.Status == s);
        if (DueCombo.SelectedValue is int due)
        {
            if (due == 0) q = q.Where(x => !x.IsClosed && x.DaysToDue < 0);
            else if (due == 999) q = q.Where(x => !x.IsClosed);
            else if (due > 0 && due != -1) q = q.Where(x => !x.IsClosed && x.DaysToDue <= due);
        }
        _view = q.OrderBy(x => x.IsClosed).ThenBy(x => x.DueDate).ToList();
        VadeGrid.ItemsSource = _view;

        var open = _view.Where(x => !x.IsClosed).ToList();
        decimal toplam = open.Sum(x => x.Amount);
        decimal gecmis = open.Where(x => x.DaysToDue < 0).Sum(x => x.Amount);
        decimal yakin = open.Where(x => x.DaysToDue >= 0 && x.DaysToDue <= 7).Sum(x => x.Amount);
        SummaryText.Text = $"Kayıt: {_view.Count}  ·  Açık portföy: {toplam:N2} ₺  ·  ⚠ Vadesi geçmiş: {gecmis:N2} ₺  ·  🟠 7 gün içinde: {yakin:N2} ₺";
    }

    private CheckNote? Selected => VadeGrid.SelectedItem as CheckNote;

    private void ToBank_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } n) { Dialog.Info("Bir çek/senet seçin.", "Bilgi"); return; }
        if (n.IsClosed) { Dialog.Warn("Bu kayıt zaten kapanmış."); return; }
        var accounts = new BankAccountService().GetAll().Where(a => a.IsActive).Select(a => new ComboItem(a.Id, a.Display)).ToList();
        if (accounts.Count == 0) { Dialog.Warn("Önce bir banka hesabı tanımlayın."); return; }
        bool alinan = n.Direction == CheckDirection.Alinan;
        var w = new ComboChoiceWindow($"{n.KindText} Bankaya {(alinan ? "Tahsil" : "Ödeme")}",
            $"{n.AmountText} ₺ · {n.CustomerName}\nHedef banka hesabı:", accounts) { Owner = this };
        if (w.ShowDialog() != true || w.SelectedValue is not int accId) return;
        try
        {
            _svc.CollectToBank(n.Id, accId, Session.CurrentUserId);
            Dialog.Success($"{n.KindText} bankaya {(alinan ? "tahsil edildi" : "ödendi")}. Banka hesabına işlendi.");
            Load();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void ToCollection_Click(object sender, RoutedEventArgs e) => SetStatus(CheckStatus.Tahsilde, "tahsile verildi");
    private void Endorse_Click(object sender, RoutedEventArgs e) => SetStatus(CheckStatus.Ciro, "ciro edildi");
    private void Bounce_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } n && !Dialog.Confirm($"{n.KindText} KARŞILIKSIZ olarak işaretlensin mi? Cari hareketi ters çevrilir.", "Karşılıksız")) return;
        SetStatus(CheckStatus.Karsiliksiz, "karşılıksız işaretlendi");
    }

    private void SetStatus(CheckStatus st, string msg)
    {
        if (Selected is not { } n) { Dialog.Info("Bir çek/senet seçin.", "Bilgi"); return; }
        if (n.IsClosed && st != CheckStatus.Karsiliksiz) { Dialog.Warn("Bu kayıt zaten kapanmış."); return; }
        try { _svc.ChangeStatus(n.Id, st, Session.CurrentUserId); Dialog.Success($"{n.KindText} {msg}."); Load(); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Excel_Click(object sender, RoutedEventArgs e)
    {
        var t = new ReportTable
        {
            Title = "Çek / Senet Vade Takibi",
            Subtitle = $"{DateTime.Today:dd.MM.yyyy}",
            Columns = { new("Tip", 60), new("Yön", 70), new("Cari", 160), new("Seri", 100), new("Banka", 110),
                        new("Tutar", 100, true), new("Vade", 90), new("Kalan Gün", 90, true, "0"), new("Durum", 100) }
        };
        foreach (var n in _view)
            t.Rows.Add(new object?[] { n.KindText, n.DirectionText, n.CustomerName, n.Serial, n.BankName, n.Amount, n.DueText, n.IsClosed ? (object?)null : n.DaysToDue, n.StatusText });
        t.Footer = new object?[] { "TOPLAM (açık)", "", "", "", "", _view.Where(x => !x.IsClosed).Sum(x => x.Amount), "", null, "" };
        Exporter.SaveExcel(t);
    }

    private void Filter_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (IsLoaded) Apply(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
