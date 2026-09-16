using System.Windows;
using System.Windows.Input;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class DocumentListWindow : Window
{
    private readonly DocType _type;
    private readonly DocumentService _docs = new();
    private readonly DocumentPostingService _posting = new();

    public DocumentListWindow(DocType type)
    {
        InitializeComponent();
        _type = type;
        TitleText.Text = type.ToTr();
        DateFrom.SelectedDate = DateTime.Today.AddMonths(-1);
        DateTo.SelectedDate = DateTime.Today;
        if (type.SourceType() is { } src) { ConvertButton.Visibility = Visibility.Visible; ConvertButton.Content = $"⇪ {src.ToTr()}'den"; }
        if (type == DocType.DepoTransfer) ReceiveButton.Visibility = Visibility.Visible;
        Loaded += (_, _) => Load();
    }

    private Document? Selected => Grid.SelectedItem as Document;

    private void Load()
    {
        var list = _docs.GetList(_type, DateFrom.SelectedDate, DateTo.SelectedDate, null, null, SearchBox.Text);
        Grid.ItemsSource = list;
        CountText.Text = $"{list.Count} evrak";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var w = new DocumentEditWindow(_type) { Owner = this };
        if (w.ShowDialog() == true) Load();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d) { Dialog.Info("Bir evrak seçin.", "Bilgi"); return; }
        if (d.Status != DocStatus.Taslak)
        {
            Dialog.Info("Onaylı/iptal evrak düzenlenemez. İncelemek için açılıyor; değişiklik için önce iptal edin.", "Bilgi");
        }
        var w = new DocumentEditWindow(_type, d.Id) { Owner = this };
        if (w.ShowDialog() == true) Load();
    }

    private string PostEffect => _type switch
    {
        DocType.DepoTransfer => "kaynak depodan çıkış yapılacak",
        DocType.Sayim => "sayım kesinleşip stok farkları işlenecek",
        _ when _type.RunsCari() => "cari + stok hareketleri oluşturulacak",
        _ when _type.RunsStock() => "stok hareketleri oluşturulacak",
        _ => "onaylanacak (defter çalışmaz)"
    };

    private void Post_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d) { Dialog.Info("İşlenecek evrağı seçin.", "Bilgi"); return; }
        string label = _type == DocType.Sayim ? "Kesinleştir" : "İşle";
        if (!Dialog.Confirm($"{d.FisNo} {label.ToLower()}? ({PostEffect})", label)) return;
        try
        {
            if (_type == DocType.DepoTransfer) _posting.PostTransfer(d.Id, Session.CurrentUserId);
            else if (_type == DocType.Sayim) _posting.PostCount(d.Id, Session.CurrentUserId);
            else _posting.Post(d.Id, Session.CurrentUserId);
            Dialog.Success("Evrak işlendi."); Load();
        }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Receive_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d) { Dialog.Info("Teslim alınacak transferi seçin.", "Bilgi"); return; }
        var w = new TransferReceiveWindow(d.Id) { Owner = this };
        if (w.ShowDialog() == true) Load();
    }

    private void CancelDoc_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d) { Dialog.Info("İptal edilecek evrağı seçin.", "Bilgi"); return; }
        if (!Dialog.Confirm($"{d.FisNo} iptal edilsin mi? (defter kayıtları geri alınacak)", "İptal")) return;
        try { _posting.Cancel(d.Id, Session.CurrentUserId); Dialog.Success("Evrak iptal edildi."); Load(); }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } d) { Dialog.Info("Yazdırılacak evrağı seçin.", "Bilgi"); return; }
        var full = _docs.GetById(d.Id);
        if (full is null) return;
        try { DocumentReceipt.Print(full, _docs.GetLines(d.Id), Session.CurrentCompany?.Name ?? "", Session.CurrentBranch?.Name); }
        catch (Exception ex) { Dialog.Error(ex.Message, "Yazdırma"); }
    }

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (_type.SourceType() is not { } src) return;
        var w = new DocumentConvertWindow(_type, src) { Owner = this };
        if (w.ShowDialog() != true) return;
        try
        {
            int id = _docs.CreateDraftFromSources(_type, w.SelectedIds, Session.CurrentUserId);
            var ew = new DocumentEditWindow(_type, id) { Owner = this };
            ew.ShowDialog();
            Load();
        }
        catch (Exception ex) { Dialog.Error(ex.Message); }
    }

    private void Show_Click(object sender, RoutedEventArgs e) => Load();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
