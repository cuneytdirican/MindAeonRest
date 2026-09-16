using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ReyonRouteMatrixWindow : Window
{
    private readonly ReyonRouteService _routes = new();
    private List<RouteRowVM> _rows = new();

    public ReyonRouteMatrixWindow()
    {
        InitializeComponent();
        int companyId = Session.CurrentCompany?.Id ?? 0;
        var branches = new BranchService().GetByCompany(companyId).Select(b => new ComboItem(b.Id, b.Name)).ToList();
        BranchCombo.ItemsSource = branches;
        BranchCombo.SelectedValue = Session.CurrentBranch?.Id ?? branches.FirstOrDefault()?.Value;
    }

    private void Branch_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (BranchCombo.SelectedValue is not int branchId) return;
        var zones = new ZoneService().GetAll().Where(z => z.BranchId == branchId && z.IsActive)
            .Select(z => new ComboItem(z.Id, z.Name)).ToList();
        ZoneCombo.ItemsSource = zones;
        if (zones.Count > 0) ZoneCombo.SelectedIndex = 0;
        else { ZoneCombo.SelectedIndex = -1; LoadZone(); }
    }

    private void Zone_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => LoadZone();

    private void LoadZone()
    {
        if (ZoneCombo.SelectedValue is not int zoneId)
        {
            MatrixGrid.ItemsSource = null; DefaultReyonCombo.ItemsSource = null;
            HintText.Text = "Bu lokasyonda reyon yok — önce 'Reyonlar (Mutfak)' ekranından bu lokasyona reyon ekleyin.";
            return;
        }

        // Yalnız BU lokasyonun reyonları (zone-kısıtlı → yanlış bara yönlendirme imkânsız)
        var reyons = new ReyonService().GetAll().Where(r => r.ZoneId == zoneId && r.IsActive).ToList();
        var reyonItems = new List<ComboItem> { new(0, "↳ (varsayılan reyona düşsün)") };
        reyonItems.AddRange(reyons.Select(r => new ComboItem(r.Id, r.Name + (string.IsNullOrWhiteSpace(r.PrinterName) ? " (yalnız ekran)" : $" 🖨 {r.PrinterName}"))));
        ReyonColumn.ItemsSource = reyonItems;

        var defItems = new List<ComboItem> { new(0, "(yok — yazıcısız/ekran)") };
        defItems.AddRange(reyons.Select(r => new ComboItem(r.Id, r.Name + (string.IsNullOrWhiteSpace(r.PrinterName) ? " (yalnız ekran)" : $" 🖨 {r.PrinterName}"))));
        DefaultReyonCombo.ItemsSource = defItems;

        var existing = _routes.GetAll().Where(r => r.SourceZoneId == zoneId).ToList();

        // Varsayılan reyon = (zone, kategori yok, ürün yok) kuralı
        var def = existing.FirstOrDefault(r => r.CategoryId == null && r.ProductId == null);
        DefaultReyonCombo.SelectedValue = def?.ReyonId ?? 0;

        // Kategori satırları
        var cats = new ProductCategoryService().GetAll().Where(c => c.IsActive).OrderBy(c => c.Name).ToList();
        _rows = cats.Select(c =>
        {
            var rule = existing.FirstOrDefault(r => r.CategoryId == c.Id && r.ProductId == null);
            return new RouteRowVM { CategoryId = c.Id, CategoryName = c.Name, ReyonId = rule?.ReyonId ?? 0, ExistingRouteId = rule?.Id ?? 0 };
        }).ToList();
        MatrixGrid.ItemsSource = _rows;

        HintText.Text = $"{reyons.Count} reyon · {cats.Count} kategori. Boş bırakılan kategori, lokasyon varsayılan reyonuna düşer.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ZoneCombo.SelectedValue is not int zoneId) { Dialog.Warn("Lokasyon seçin."); return; }
        MatrixGrid.CommitEdit(); MatrixGrid.CommitEdit();
        try
        {
            int? uid = Session.CurrentUserId;
            var existing = _routes.GetAll().Where(r => r.SourceZoneId == zoneId).ToList();

            // Kategori → reyon kuralları
            foreach (var row in _rows)
            {
                var rule = existing.FirstOrDefault(r => r.CategoryId == row.CategoryId && r.ProductId == null);
                if (row.ReyonId > 0)
                    _routes.Save(new ReyonRoute { Id = rule?.Id ?? 0, SourceZoneId = zoneId, CategoryId = row.CategoryId, ProductId = null, ReyonId = row.ReyonId }, uid);
                else if (rule is not null)
                    _routes.Delete(rule.Id, uid);
            }

            // Lokasyon varsayılan reyonu (kategori yok)
            var def = existing.FirstOrDefault(r => r.CategoryId == null && r.ProductId == null);
            int defReyon = DefaultReyonCombo.SelectedValue as int? ?? 0;
            if (defReyon > 0)
                _routes.Save(new ReyonRoute { Id = def?.Id ?? 0, SourceZoneId = zoneId, CategoryId = null, ProductId = null, ReyonId = defReyon }, uid);
            else if (def is not null)
                _routes.Delete(def.Id, uid);

            Dialog.Success("Yönlendirme kuralları kaydedildi. Adisyonda bu lokasyondan verilen siparişler ilgili reyona/yazıcıya düşer.");
            LoadZone();
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Hata"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    public class RouteRowVM
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public int ReyonId { get; set; }
        public int ExistingRouteId { get; set; }
    }
}
