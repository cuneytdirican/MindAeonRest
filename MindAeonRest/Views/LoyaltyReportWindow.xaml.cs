using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

/// <summary>Puan/sadakat raporu: toplam puan yükümlülüğü (₺), müşteri seç → puan bakiyesi + hareket ekstresi.</summary>
public partial class LoyaltyReportWindow : Window
{
    private readonly LoyaltyService _service = new();

    public LoyaltyReportWindow()
    {
        InitializeComponent();
        CustomerPicker.Configure(() => new CustomerService().GetAll().Where(c => c.Code != "PA"), AddCustomer, null);
        CustomerPicker.SelectionChanged += (_, _) => LoadCustomer();
        Loaded += (_, _) => RefreshLiability();
    }

    private int? AddCustomer()
    {
        var w = new CustomerEditWindow { Owner = this };
        return w.ShowDialog() == true ? w.SavedId : null;
    }

    private void RefreshLiability()
    {
        try { LiabilityText.Text = $"₺{_service.GetTotalLiability():N2}"; }
        catch (Exception ex) { Dialog.Error(ex.Message, "Sadakat"); }
    }

    private void LoadCustomer()
    {
        if (CustomerPicker.SelectedValue is not int customerId)
        {
            BalanceText.Text = "—";
            MovGrid.ItemsSource = null;
            return;
        }
        try
        {
            BalanceText.Text = $"{_service.GetBalance(customerId):N0} puan";
            MovGrid.ItemsSource = _service.GetMovements(customerId);
        }
        catch (Exception ex) { Dialog.Error(ex.Message, "Sadakat"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
