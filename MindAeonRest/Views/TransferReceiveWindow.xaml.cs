using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class TransferReceiveWindow : Window
{
    private readonly DocumentService _docs = new();
    private readonly DocumentPostingService _posting = new();
    private readonly int _docId;

    public TransferReceiveWindow(int docId)
    {
        InitializeComponent();
        _docId = docId;
        var d = _docs.GetById(docId);
        SubText.Text = d is null ? "" : $"{d.FisNo} · {d.DateText}";
        Grid.ItemsSource = _docs.GetLines(docId).Select(l => new ReceiveLineVM
        {
            LineId = l.Id, ProductName = l.ProductName ?? l.ProductCode, Qty = l.Qty,
            AlreadyReceived = l.ReceivedQty, ThisReceipt = Math.Max(0, l.Qty - l.ReceivedQty)
        }).ToList();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var dict = new Dictionary<int, decimal>();
        foreach (var item in Grid.Items)
            if (item is ReceiveLineVM v && v.ThisReceipt > 0)
            {
                if (v.ThisReceipt > v.Remaining) { Dialog.Warn($"{v.ProductName}: kalan {v.Remaining:N2}'den fazla teslim alınamaz."); return; }
                dict[v.LineId] = v.ThisReceipt;
            }
        if (dict.Count == 0) { Dialog.Info("Teslim alınacak miktar girin.", "Bilgi"); return; }
        try { _posting.ReceiveTransfer(_docId, dict, Session.CurrentUserId); Dialog.Success("Teslim alındı (hedef depoya giriş yapıldı)."); DialogResult = true; Close(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    private class ReceiveLineVM
    {
        public int LineId { get; set; }
        public string? ProductName { get; set; }
        public decimal Qty { get; set; }
        public decimal AlreadyReceived { get; set; }
        public decimal Remaining => Qty - AlreadyReceived;
        public decimal ThisReceipt { get; set; }
    }
}
