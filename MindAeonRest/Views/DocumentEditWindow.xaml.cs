using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class DocumentEditWindow : Window
{
    private readonly DocType _type;
    private readonly DocumentService _docs = new();
    private readonly ProductService _productService = new();
    private readonly TaxGroupService _taxService = new();
    private readonly ObservableCollection<DocumentLine> _lines = new();
    private List<Product> _products = new();
    private Document? _doc;

    public int? SavedId => _doc?.Id == 0 ? null : _doc?.Id;

    public DocumentEditWindow(DocType type, int? existingId = null)
    {
        InitializeComponent();
        _type = type;
        TitleText.Text = type.ToTr();
        LinesGrid.ItemsSource = _lines;

        // Kenarsız + Maximized pencere ekran kenarlarından taşar (içerik kırpılır); maximize'da iç boşluk ver
        StateChanged += (_, _) => RootBorder.Margin =
            WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
        Loaded += (_, _) => { if (WindowState == WindowState.Maximized) RootBorder.Margin = new Thickness(8); };

        LoadCombos();
        ConfigurePickers();
        ApplyTypeVisibility();

        DateBox.SelectedDate = DateTime.Today;
        if (existingId is { } id) LoadExisting(id);
        Recalc();
    }

    // ---------- yükleme ----------
    private void LoadCombos()
    {
        var co = Session.CurrentCompany;
        var branches = co is null ? new List<ComboItem>() : new BranchService().GetByCompany(co.Id).Select(b => new ComboItem(b.Id, b.Name)).ToList();
        BranchCombo.ItemsSource = branches;
        BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? branches.FirstOrDefault()?.Value;
        TargetBranchCombo.ItemsSource = branches.ToList();

        CurrencyCombo.ItemsSource = new CurrencyService().GetAll().Where(c => c.IsActive).Select(c => new ComboItem(c.Id, c.Code)).ToList();
        CurrencyCombo.SelectedValue = new CurrencyService().GetDefaultId();

        var prj = new ProjectService().GetAll().Where(p => p.IsActive).Select(p => new ComboItem(p.Id, $"{p.Code} · {p.Name}")).ToList();
        ProjectCombo.ItemsSource = prj;
        if (prj.Count > 0) ProjectCombo.SelectedIndex = 0;

        PersonnelPicker.Configure(() => new PersonnelService().GetAll().Where(p => p.IsActive), AddPersonnel, null);

        ExpenseCombo.ItemsSource = new ExpenseTypeService().GetAll().Where(x => x.IsActive).Select(x => new ComboItem(x.Id, x.Name)).ToList();

        var tax = new List<ComboItem> { new(0, "(KDV yok)") };
        tax.AddRange(_taxService.GetAll().Where(t => t.IsActive).Select(t => new ComboItem(t.Id, t.Display)));
        TaxCombo.ItemsSource = tax; TaxCombo.SelectedIndex = 0;

        ReloadWarehouses();
    }

    private void ReloadWarehouses()
    {
        int? branch = BranchCombo.SelectedValue as int?;
        WarehouseCombo.ItemsSource = new WarehouseService().GetAll().Where(w => w.IsActive && w.BranchId == branch)
            .Select(w => new ComboItem(w.Id, w.Name)).ToList();
        if (WarehouseCombo.Items.Count > 0) WarehouseCombo.SelectedIndex = 0;
    }

    private void ReloadTargetWarehouses()
    {
        int? branch = TargetBranchCombo.SelectedValue as int?;
        TargetWarehouseCombo.ItemsSource = new WarehouseService().GetAll().Where(w => w.IsActive && w.BranchId == branch)
            .Select(w => new ComboItem(w.Id, w.Name)).ToList();
        if (TargetWarehouseCombo.Items.Count > 0) TargetWarehouseCombo.SelectedIndex = 0;
    }

    private void ConfigurePickers()
    {
        _products = _productService.GetAll().Where(p => p.IsActive).ToList();
        ProductPicker.Configure(() => _products, AddProduct, null);
        ProductPicker.PlaceholderText = "Stok ara…";
        ProductPicker.SearchTitle = "Stok Ara (kod/ad/…)";
        ProductPicker.SelectionChanged += (_, _) => OnProductSelected();
        CustomerPicker.Configure(() => new CustomerService().GetAll(), AddCustomer, null);
        CustomerPicker.SearchTitle = "Cari Ara (kod/ad/telefon/vergi…)";
        CustomerPicker.SelectionChanged += (_, _) => LoadAddresses();
    }

    private int? AddProduct()
    {
        var w = new ProductEditWindow { Owner = this };
        if (w.ShowDialog() == true && w.SavedId is { } id) { _products = _productService.GetAll().Where(p => p.IsActive).ToList(); return id; }
        return null;
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private int? AddPersonnel()
    {
        var w = new PersonnelEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void LoadAddresses()
    {
        if (CustomerPicker.SelectedValue is not int cid) { AddressCombo.ItemsSource = null; return; }
        var list = new List<ComboItem> { new(0, "(Adres yok)") };
        list.AddRange(new CustomerService().GetAddresses(cid).Select(a => new ComboItem(a.Id, a.Title ?? a.Address ?? $"Adres {a.Id}")));
        AddressCombo.ItemsSource = list; AddressCombo.SelectedIndex = 0;
    }

    private void OnProductSelected()
    {
        if (ProductPicker.SelectedValue is not int pid) return;
        var p = _products.FirstOrDefault(x => x.Id == pid);
        if (p is null) return;
        int? tg = IsPurchase(_type) ? p.PurchaseTaxGroupId : p.SalesTaxGroupId;
        TaxCombo.SelectedValue = tg ?? 0;
    }

    private void ApplyTypeVisibility()
    {
        bool needCust = _type.NeedsCustomer();
        CustomerPanel.Visibility = AddressPanel.Visibility = needCust ? Visibility.Visible : Visibility.Collapsed;
        bool transfer = _type == DocType.DepoTransfer;
        TargetBranchPanel.Visibility = TargetWarehousePanel.Visibility = transfer ? Visibility.Visible : Visibility.Collapsed;
        ExpensePanel.Visibility = _type == DocType.Gider ? Visibility.Visible : Visibility.Collapsed;
        if (transfer) ReloadTargetWarehouses();
    }

    private void LoadExisting(int id)
    {
        _doc = _docs.GetById(id);
        if (_doc is null) return;
        FisNoText.Text = $"Fiş No: {_doc.FisNo}";
        BelgeNoBox.Text = _doc.BelgeNo;
        DateBox.SelectedDate = _doc.DocDate.Date;
        BelgeTarihiBox.SelectedDate = _doc.BelgeTarihi;
        BranchCombo.SelectedValue = _doc.BranchId;
        ReloadWarehouses();
        WarehouseCombo.SelectedValue = _doc.WarehouseId;
        ProjectCombo.SelectedValue = _doc.ProjectId;
        CurrencyCombo.SelectedValue = _doc.CurrencyId;
        PersonnelPicker.SelectedValue = _doc.PersonnelId;
        if (_doc.CustomerId is { } c) { CustomerPicker.SelectedValue = c; LoadAddresses(); AddressCombo.SelectedValue = _doc.AddressId ?? 0; }
        if (_doc.TargetBranchId is { } tb) { TargetBranchCombo.SelectedValue = tb; ReloadTargetWarehouses(); TargetWarehouseCombo.SelectedValue = _doc.TargetWarehouseId; }
        ExpenseCombo.SelectedValue = _doc.ExpenseTypeId ?? 0;
        foreach (var l in _docs.GetLines(id)) _lines.Add(l);
    }

    // ---------- satır ----------
    private static bool IsPurchase(DocType t) =>
        t is DocType.AlisFatura or DocType.AlisSiparis or DocType.AlisIrsaliye or DocType.AlisIadeFatura;

    private decimal EffectiveRate(int taxGroupId)
    {
        if (taxGroupId == 0) return 0;
        var asOf = (DateBox.SelectedDate ?? DateTime.Today).Date;   // KDV oranı EVRAK TARİHİNE göre (geri tarihli evrak doğru oran)
        var r = _taxService.GetRates(taxGroupId)
            .Where(x => x.StartDate.Date <= asOf && (x.EndDate is null || x.EndDate.Value.Date >= asOf))
            .OrderByDescending(x => x.StartDate).FirstOrDefault();
        return r?.Rate ?? 0;
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        if (ProductPicker.SelectedValue is not int pid) { Dialog.Info("Önce ürün seçin.", "Satır"); return; }
        var p = _products.FirstOrDefault(x => x.Id == pid);
        if (!Dec(QtyBox.Text, out var qty) || qty <= 0) { Dialog.Warn("Geçerli miktar girin."); return; }
        if (!Dec(PriceBox.Text, out var price) || price < 0) { Dialog.Warn("Geçerli fiyat girin."); return; }
        Dec(DiscBox.Text, out var disc);
        int tgId = TaxCombo.SelectedValue as int? ?? 0;

        var line = new DocumentLine
        {
            ProductId = pid, ProductName = p?.Name, ProductCode = p?.Code,
            Qty = qty, UnitId = p?.MainUnitId, UnitName = p?.MainUnitName, UnitPrice = price,
            Disc1 = disc, TaxGroupId = tgId == 0 ? null : tgId, TaxRate = EffectiveRate(tgId)
        };
        line.Recalc();
        _lines.Add(line);
        Renumber();
        Recalc();

        ProductPicker.Clear();
        QtyBox.Text = "1"; PriceBox.Text = "0"; DiscBox.Text = "0";
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not DocumentLine l) { Dialog.Info("Silinecek satırı seçin.", "Satır"); return; }
        _lines.Remove(l); Renumber(); Recalc();
    }

    private void EditDiscounts_Click(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not DocumentLine l) { Dialog.Info("İskonto için bir satır seçin (ya da satırdaki '% İskonto' düğmesini kullanın).", "Satır"); return; }
        EditDiscountsFor(l);
    }

    private void RowDiscounts_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentLine l }) EditDiscountsFor(l);
    }

    private void EditDiscountsFor(DocumentLine l)
    {
        var w = new DiscountsDialog(l.Discounts.ToArray()) { Owner = this };
        if (w.ShowDialog() != true) return;
        l.Disc1 = w.Values[0]; l.Disc2 = w.Values[1]; l.Disc3 = w.Values[2]; l.Disc4 = w.Values[3]; l.Disc5 = w.Values[4];
        l.Disc6 = w.Values[5]; l.Disc7 = w.Values[6]; l.Disc8 = w.Values[7]; l.Disc9 = w.Values[8]; l.Disc10 = w.Values[9];
        l.Recalc();
        LinesGrid.Items.Refresh();
        Recalc();
    }

    private void Renumber() { int n = 1; foreach (var l in _lines) l.LineNo = n++; LinesGrid.Items.Refresh(); }

    private void Recalc()
    {
        decimal sub = _lines.Sum(l => l.LineNet), disc = _lines.Sum(l => l.DiscountAmount),
                tax = _lines.Sum(l => l.LineTax), grand = _lines.Sum(l => l.LineTotal);
        decimal gross = sub + disc;   // ara toplam (iskonto öncesi)
        GrossText.Text = $"Ara Toplam: {gross:N2}";
        DiscText.Text = $"İskonto: {disc:N2}";
        SubText.Text = $"Matrah: {sub:N2}";
        TaxText.Text = $"KDV: {tax:N2}";
        GrandText.Text = $"GENEL: {grand:N2}";
        UpdateFx(grand);
    }

    private void UpdateFx(decimal grand)
    {
        try
        {
            string code = (CurrencyCombo.SelectedItem as ComboItem)?.Text ?? "TRY";
            var (usd, eur) = new CurrencyRateService().GetUsdEur();
            var parts = new List<string>();
            if (code.Equals("TRY", StringComparison.OrdinalIgnoreCase))
            {
                if (usd is > 0) parts.Add($"≈ {grand / usd.Value:N2} USD");
                if (eur is > 0) parts.Add($"≈ {grand / eur.Value:N2} EUR");
            }
            else if (code.Equals("USD", StringComparison.OrdinalIgnoreCase) && usd is > 0) parts.Add($"≈ {grand * usd.Value:N2} TRY");
            else if (code.Equals("EUR", StringComparison.OrdinalIgnoreCase) && eur is > 0) parts.Add($"≈ {grand * eur.Value:N2} TRY");
            FxText.Text = parts.Count > 0 ? "Diğer döviz: " + string.Join("   ", parts) : "";
        }
        catch { FxText.Text = ""; }
    }

    private void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var w = new ProjectEditWindow { Owner = this };
        if (w.ShowDialog() != true) return;
        var all = new ProjectService().GetAll().Where(p => p.IsActive).ToList();
        ProjectCombo.ItemsSource = all.Select(p => new ComboItem(p.Id, $"{p.Code} · {p.Name}")).ToList();
        var newest = all.OrderByDescending(p => p.Id).FirstOrDefault();
        if (newest != null) ProjectCombo.SelectedValue = newest.Id;
    }

    // ---------- kaydet ----------
    private Document? BuildAndSave(int? userId)
    {
        // Zorunlu alanlar: üst bilgilerde PERSONEL ve PROJE hariç her şey zorunlu
        if (string.IsNullOrWhiteSpace(BelgeNoBox.Text)) { Dialog.Warn("Belge No zorunlu (firmadan gelen ya da resmi belge numarası)."); return null; }
        if (DateBox.SelectedDate is null) { Dialog.Warn("Tarih zorunlu."); return null; }
        if (BelgeTarihiBox.SelectedDate is null) { Dialog.Warn("Belge Tarihi zorunlu."); return null; }
        if (CurrencyCombo.SelectedValue is not int) { Dialog.Warn("Döviz zorunlu."); return null; }
        if (BranchCombo.SelectedValue is not int br) { Dialog.Warn("Şube zorunlu."); return null; }
        if (WarehouseCombo.Visibility == Visibility.Visible && WarehouseCombo.SelectedValue is not int) { Dialog.Warn("Depo zorunlu."); return null; }
        if (_lines.Count == 0) { Dialog.Warn("En az bir satır ekleyin."); return null; }
        if (_type.NeedsCustomer() && CustomerPicker.SelectedValue is null) { Dialog.Warn("Cari zorunlu."); return null; }
        if (_type == DocType.DepoTransfer && TargetWarehouseCombo.SelectedValue is not int) { Dialog.Warn("Hedef depo zorunlu."); return null; }
        if (_type == DocType.Gider && (ExpenseCombo.SelectedValue as int? ?? 0) == 0) { Dialog.Warn("Gider tipi zorunlu."); return null; }
        int? prj = ProjectCombo.SelectedValue as int?;   // PROJE opsiyonel

        var doc = _doc ?? new Document { DocType = _type };
        doc.BelgeNo = string.IsNullOrWhiteSpace(BelgeNoBox.Text) ? null : BelgeNoBox.Text.Trim();
        var datePart = (DateBox.SelectedDate ?? DateTime.Today).Date;
        // Düzenlemede tarih değişmediyse orijinal saat korunur; yeni/değişen tarihte güncel saat
        doc.DocDate = (doc.Id != 0 && doc.DocDate.Date == datePart) ? doc.DocDate : datePart.Add(DateTime.Now.TimeOfDay);
        doc.BelgeTarihi = BelgeTarihiBox.SelectedDate;
        doc.BranchId = br;
        doc.WarehouseId = WarehouseCombo.SelectedValue as int?;
        doc.ProjectId = prj is > 0 ? prj : null;
        doc.CurrencyId = CurrencyCombo.SelectedValue as int? ?? 0;
        doc.PersonnelId = PersonnelPicker.SelectedValue is int pe && pe > 0 ? pe : null;
        doc.CustomerId = _type.NeedsCustomer() ? CustomerPicker.SelectedValue : null;
        doc.AddressId = AddressCombo.SelectedValue is int ad && ad > 0 ? ad : null;
        doc.TargetBranchId = _type == DocType.DepoTransfer ? TargetBranchCombo.SelectedValue as int? : null;
        doc.TargetWarehouseId = _type == DocType.DepoTransfer ? TargetWarehouseCombo.SelectedValue as int? : null;
        doc.ExpenseTypeId = _type == DocType.Gider && ExpenseCombo.SelectedValue is int exp && exp > 0 ? exp : null;

        try { _doc = _docs.Save(doc, _lines.ToList(), userId); return _doc; }
        catch (Exception ex) { Dialog.Error(ex.Message, "Kayıt Hatası"); return null; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var d = BuildAndSave(Session.CurrentUserId);
        if (d is null) return;
        Dialog.Success($"Evrak kaydedildi.\nFiş No: {d.FisNo}");
        DialogResult = true; Close();
    }

    private void SavePost_Click(object sender, RoutedEventArgs e)
    {
        var d = BuildAndSave(Session.CurrentUserId);
        if (d is null) return;
        try
        {
            var posting = new DocumentPostingService();
            if (d.DocType == DocType.DepoTransfer) posting.PostTransfer(d.Id, Session.CurrentUserId);
            else if (d.DocType == DocType.Sayim) posting.PostCount(d.Id, Session.CurrentUserId);
            else posting.Post(d.Id, Session.CurrentUserId);   // fatura/irsaliye/fire → cari + stok yazar
            Dialog.Success($"Evrak kaydedildi ve İŞLENDİ.\nCari/stok güncellendi, ekstrelerde görünür.\nFiş No: {d.FisNo}");
            DialogResult = true; Close();
        }
        catch (Exception ex)
        {
            Dialog.Error("Kaydedildi ancak işlenemedi: " + ex.Message + "\n\nEvrak TASLAK olarak duruyor; evrak listesinden 'İşle' ile tekrar deneyebilirsiniz.", "İşleme Hatası");
            DialogResult = true; Close();
        }
    }

    private void SavePrint_Click(object sender, RoutedEventArgs e)
    {
        var d = BuildAndSave(Session.CurrentUserId);
        if (d is null) return;
        d.CustomerName ??= (CustomerPicker.SelectedValue is int ? new CustomerService().GetById(CustomerPicker.SelectedValue!.Value)?.Name : null);
        try { DocumentReceipt.Print(d, _docs.GetLines(d.Id), Session.CurrentCompany?.Name ?? "", Session.CurrentBranch?.Name); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Yazdırma"); }
        DialogResult = true; Close();
    }

    private static bool Dec(string s, out decimal v) =>
        decimal.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v);

    private void Currency_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Recalc(); }
    private void Branch_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ReloadWarehouses(); }
    private void TargetBranch_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ReloadTargetWarehouses(); }
    private void Keyboard_Click(object sender, RoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is Control c and (TextBox or System.Windows.Controls.PasswordBox)) VirtualKeyboardWindow.ShowFor(c);
        else { QtyBox.Focus(); VirtualKeyboardWindow.ShowFor(QtyBox); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
