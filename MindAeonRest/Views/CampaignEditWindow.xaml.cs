using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class CampaignEditWindow : Window
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly CampaignService _service = new();
    private readonly Campaign _c;
    private readonly List<BranchSel> _branches;
    private readonly System.Collections.ObjectModel.ObservableCollection<CampaignCondition> _conds = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<CampaignGift> _gifts = new();

    public int? SavedId => _c.Id == 0 ? null : _c.Id;

    public CampaignEditWindow(Campaign? existing = null)
    {
        InitializeComponent();
        _c = existing ?? new Campaign { StartDateTime = DateTime.Today, EndDateTime = DateTime.Today.AddMonths(1) };

        TypeCombo.ItemsSource = Enum.GetValues<CampaignType>().Select(t => new ComboItem((int)t, t.ToTr())).ToList();
        CatPicker.Configure(() => new ProductCategoryService().GetAll(), null, existing?.TargetCategoryId);
        ProdPicker.Configure(() => new ProductService().GetAll(includeDeleted: false), null, existing?.TargetProductId);
        CondProdPicker.Configure(() => new ProductService().GetAll(includeDeleted: false), null, null);
        GiftProdPicker.Configure(() => new ProductService().GetAll(includeDeleted: false), null, null);
        CondList.ItemsSource = _conds;
        GiftList.ItemsSource = _gifts;
        if (existing is not null)
        {
            foreach (var c in existing.Conditions) _conds.Add(c);
            foreach (var g in existing.Gifts) _gifts.Add(g);
            GiftMinBox.Text = existing.MinAmount.ToString("0.##", Tr);
        }

        _branches = new BranchService().GetByCompany(Session.CurrentCompany?.Id ?? 0)
            .Select(b => new BranchSel { Id = b.Id, Name = b.Name, IsSelected = existing?.BranchIds.Contains(b.Id) ?? false }).ToList();
        BranchList.ItemsSource = _branches;

        if (existing is not null)
        {
            TitleText.Text = $"Kampanya · {existing.Code}";
            CodeBox.Text = existing.Code; NameBox.Text = existing.Name; DescBox.Text = existing.Description;
            TypeCombo.SelectedValue = (int)existing.Type;
            ValueBox.Text = existing.Value.ToString("0.##", Tr);
            MinBox.Text = existing.MinAmount.ToString("0.##", Tr);
            BuyBox.Text = existing.BuyQty.ToString("0.##", Tr);
            PayBox.Text = existing.PayQty.ToString("0.##", Tr);
            StartBox.Text = DateHelper.ToDateTimeText(existing.StartDateTime);
            EndBox.Text = DateHelper.ToDateTimeText(existing.EndDateTime);
            D1.IsChecked = (existing.DaysOfWeek & 1) != 0; D2.IsChecked = (existing.DaysOfWeek & 2) != 0;
            D3.IsChecked = (existing.DaysOfWeek & 4) != 0; D4.IsChecked = (existing.DaysOfWeek & 8) != 0;
            D5.IsChecked = (existing.DaysOfWeek & 16) != 0; D6.IsChecked = (existing.DaysOfWeek & 32) != 0; D7.IsChecked = (existing.DaysOfWeek & 64) != 0;
            DailyStartBox.Text = MinToText(existing.DailyStartMin); DailyEndBox.Text = MinToText(existing.DailyEndMin);
            AllBranchesCheck.IsChecked = existing.AllBranches;
            ActiveCheck.IsChecked = existing.IsActive;
            StackableCheck.IsChecked = existing.Stackable;
            MandatoryCheck.IsChecked = existing.IsMandatory;
        }
        else { TypeCombo.SelectedValue = (int)CampaignType.TumSiparisYuzde; StartBox.Text = DateHelper.ToDateTimeText(DateTime.Now); EndBox.Text = DateHelper.ToDateTimeText(DateTime.Now.AddMonths(1)); }

        UpdateTypePanels();
        UpdateBranchPanel();
        Loaded += (_, _) => NameBox.Focus();
    }

    private static string MinToText(int? min) => min is int m ? $"{m / 60:00}:{m % 60:00}" : "";

    private void Type_Changed(object sender, SelectionChangedEventArgs e) => UpdateTypePanels();

    private void UpdateTypePanels()
    {
        if (CatPanel == null) return;   // XAML init koruması
        var t = (CampaignType)(TypeCombo.SelectedValue as int? ?? 0);
        CatPanel.Visibility = t == CampaignType.KategoriYuzde ? Visibility.Visible : Visibility.Collapsed;
        ProdPanel.Visibility = (t == CampaignType.UrunYuzde || t == CampaignType.XAlYOde) ? Visibility.Visible : Visibility.Collapsed;
        MinPanel.Visibility = (t == CampaignType.MinTutarYuzde || t == CampaignType.MinTutarTutar) ? Visibility.Visible : Visibility.Collapsed;
        XYPanel.Visibility = t == CampaignType.XAlYOde ? Visibility.Visible : Visibility.Collapsed;
        HediyePanel.Visibility = t == CampaignType.Hediye ? Visibility.Visible : Visibility.Collapsed;
        bool amount = t is CampaignType.TumSiparisTutar or CampaignType.MinTutarTutar;
        bool noValue = t is CampaignType.XAlYOde or CampaignType.Hediye;
        ValueLabel.Text = amount ? "İNDİRİM TUTARI (₺)" : "İNDİRİM (%)";
        ValueBox.IsEnabled = !noValue;
        TypeDescText.Text = Campaign.TypeDescription(t);
        RefreshSummary();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => RefreshSummary();

    private void RefreshSummary()
    {
        if (SummaryText == null) return;
        try { SummaryText.Text = BuildPreview().Summary; }
        catch { SummaryText.Text = "(özet oluşturulamadı)"; }
    }

    /// <summary>Mevcut form alanlarından (lenient) bir Campaign kurar — yalnızca ÖZET göstermek için.</summary>
    private Campaign BuildPreview()
    {
        var t = (CampaignType)(TypeCombo.SelectedValue as int? ?? 0);
        TryDec(ValueBox.Text, out var val);
        decimal min = 0;
        if (t is CampaignType.MinTutarYuzde or CampaignType.MinTutarTutar) TryDec(MinBox.Text, out min);
        else if (t == CampaignType.Hediye) TryDec(GiftMinBox.Text, out min);
        TryDec(BuyBox.Text, out var buy); TryDec(PayBox.Text, out var pay);
        DateHelper.TryParseDateTime(StartBox.Text, out var sd);
        DateHelper.TryParseDateTime(EndBox.Text, out var ed);
        int dow = (D1.IsChecked == true ? 1 : 0) | (D2.IsChecked == true ? 2 : 0) | (D3.IsChecked == true ? 4 : 0)
                | (D4.IsChecked == true ? 8 : 0) | (D5.IsChecked == true ? 16 : 0) | (D6.IsChecked == true ? 32 : 0) | (D7.IsChecked == true ? 64 : 0);
        string? catName = CatPicker.SelectedValue is int cid ? new ProductCategoryService().GetAll().FirstOrDefault(x => x.Id == cid)?.Name : null;
        string? prodName = ProdPicker.SelectedValue is int pid ? new ProductService().GetById(pid)?.Name : null;
        return new Campaign
        {
            Type = t, Value = val, MinAmount = min, BuyQty = buy, PayQty = pay,
            TargetCategoryName = catName, TargetProductName = prodName,
            StartDateTime = sd == default ? DateTime.Now : sd, EndDateTime = ed == default ? DateTime.Now : ed,
            DaysOfWeek = dow, DailyStartMin = ParseMin(DailyStartBox.Text), DailyEndMin = ParseMin(DailyEndBox.Text),
            AllBranches = AllBranchesCheck.IsChecked == true, BranchIds = _branches.Where(b => b.IsSelected).Select(b => b.Id).ToList(),
            Stackable = StackableCheck.IsChecked == true,
            IsMandatory = MandatoryCheck.IsChecked == true,
            Conditions = _conds.ToList(), Gifts = _gifts.ToList()
        };
    }

    private void AllBranches_Changed(object sender, RoutedEventArgs e) => UpdateBranchPanel();
    private void UpdateBranchPanel()
    {
        if (BranchPanel == null) return;   // XAML init sırasında Checked tetiklenebilir
        bool all = AllBranchesCheck.IsChecked == true;
        // Her zaman GÖRÜNÜR (keşfedilebilir olsun); "Tüm şubeler" seçiliyken pasif + soluk
        BranchList.IsEnabled = !all;
        BranchPanel.Opacity = all ? 0.5 : 1.0;
        if (NoBranchText != null) NoBranchText.Visibility = (_branches != null && _branches.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool TryDec(string t, out decimal v) => decimal.TryParse((t ?? "").Trim().Replace('.', ','), NumberStyles.Any, Tr, out v);
    private int? ParseMin(string t)
    {
        t = (t ?? "").Trim();
        if (t.Length == 0) return null;
        var parts = t.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m)) return Math.Clamp(h, 0, 23) * 60 + Math.Clamp(m, 0, 59);
        return null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var t = (CampaignType)(TypeCombo.SelectedValue as int? ?? 0);
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { Err("Kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { Err("Ad giriniz."); return; }
        if (!DateHelper.TryParseDateTime(StartBox.Text, out var sd)) { Err("Başlangıç gg.aa.yyyy SS:dd biçiminde olmalı."); return; }
        if (!DateHelper.TryParseDateTime(EndBox.Text, out var ed)) { Err("Bitiş gg.aa.yyyy SS:dd biçiminde olmalı."); return; }
        if (ed < sd) { Err("Bitiş başlangıçtan önce olamaz."); return; }

        decimal val = 0, min = 0, buy = 0, pay = 0;
        if (t is not (CampaignType.XAlYOde or CampaignType.Hediye)) { if (!TryDec(ValueBox.Text, out val) || val < 0) { Err("Geçerli değer giriniz."); return; } }
        if (t is CampaignType.MinTutarYuzde or CampaignType.MinTutarTutar) TryDec(MinBox.Text, out min);
        if (t == CampaignType.KategoriYuzde && CatPicker.SelectedValue is not int) { Err("Hedef kategori seçiniz."); return; }
        if ((t == CampaignType.UrunYuzde || t == CampaignType.XAlYOde) && ProdPicker.SelectedValue is not int) { Err("Hedef ürün seçiniz."); return; }
        if (t == CampaignType.XAlYOde)
        {
            TryDec(BuyBox.Text, out buy); TryDec(PayBox.Text, out pay);
            if (buy <= 0 || pay < 0 || pay >= buy) { Err("X Al Y Öde: 'kaç al' > 'kaç öde' olmalı."); return; }
        }
        if (t == CampaignType.Hediye)
        {
            TryDec(GiftMinBox.Text, out min);
            if (_gifts.Count == 0) { Err("En az bir ikram (hediye) ürün ekleyin."); return; }
        }
        bool allBranches = AllBranchesCheck.IsChecked == true;
        var branchIds = _branches.Where(b => b.IsSelected).Select(b => b.Id).ToList();
        if (!allBranches && branchIds.Count == 0) { Err("En az bir şube seçin ya da 'Tüm şubeler'i işaretleyin."); return; }

        int dow = (D1.IsChecked == true ? 1 : 0) | (D2.IsChecked == true ? 2 : 0) | (D3.IsChecked == true ? 4 : 0)
                | (D4.IsChecked == true ? 8 : 0) | (D5.IsChecked == true ? 16 : 0) | (D6.IsChecked == true ? 32 : 0) | (D7.IsChecked == true ? 64 : 0);

        _c.Code = CodeBox.Text; _c.Name = NameBox.Text.Trim(); _c.Description = string.IsNullOrWhiteSpace(DescBox.Text) ? null : DescBox.Text.Trim();
        _c.Type = t; _c.Value = val; _c.MinAmount = min; _c.BuyQty = buy; _c.PayQty = pay;
        _c.TargetCategoryId = t == CampaignType.KategoriYuzde ? CatPicker.SelectedValue : null;
        _c.TargetProductId = (t == CampaignType.UrunYuzde || t == CampaignType.XAlYOde) ? ProdPicker.SelectedValue : null;
        _c.StartDateTime = sd; _c.EndDateTime = ed; _c.DaysOfWeek = dow;
        _c.DailyStartMin = ParseMin(DailyStartBox.Text); _c.DailyEndMin = ParseMin(DailyEndBox.Text);
        _c.AllBranches = allBranches; _c.BranchIds = branchIds; _c.IsActive = ActiveCheck.IsChecked == true;
        _c.Stackable = StackableCheck.IsChecked == true;
        _c.IsMandatory = MandatoryCheck.IsChecked == true;
        _c.Conditions = t == CampaignType.Hediye ? _conds.ToList() : new();
        _c.Gifts = t == CampaignType.Hediye ? _gifts.ToList() : new();

        try { _service.Save(_c, Session.CurrentUserId); DialogResult = true; Close(); }
        catch (Exception ex) { Err(ex.Message); }
    }

    private void AddCond_Click(object sender, RoutedEventArgs e)
    {
        if (CondProdPicker.SelectedValue is not int pid) { Dialog.Warn("Koşul ürünü seçin.", "Kampanya"); return; }
        TryDec(CondQtyBox.Text, out var q); if (q <= 0) q = 1;
        var name = new ProductService().GetById(pid)?.Name;
        _conds.Add(new CampaignCondition { ProductId = pid, MinQty = q, ProductName = name });
        CondProdPicker.SelectedValue = null; CondQtyBox.Text = "1"; RefreshSummary();
    }
    private void RemoveCond_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is CampaignCondition c) { _conds.Remove(c); RefreshSummary(); } }

    private void AddGift_Click(object sender, RoutedEventArgs e)
    {
        if (GiftProdPicker.SelectedValue is not int pid) { Dialog.Warn("İkram ürünü seçin.", "Kampanya"); return; }
        TryDec(GiftQtyBox.Text, out var q); if (q <= 0) q = 1;
        var name = new ProductService().GetById(pid)?.Name;
        _gifts.Add(new CampaignGift { ProductId = pid, Qty = q, ProductName = name });
        GiftProdPicker.SelectedValue = null; GiftQtyBox.Text = "1"; RefreshSummary();
    }
    private void RemoveGift_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is CampaignGift g) { _gifts.Remove(g); RefreshSummary(); } }

    private void Err(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; Dialog.Warn(m, "Kampanya"); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    public class BranchSel { public int Id { get; set; } public string Name { get; set; } = ""; public bool IsSelected { get; set; } }
}
