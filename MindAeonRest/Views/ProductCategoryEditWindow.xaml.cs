using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MindAeonRest.Helpers;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRest.Views;

public partial class ProductCategoryEditWindow : Window
{
    private readonly ProductCategoryService _service = new();
    private readonly ProductCategory _category;
    private byte[]? _imageBytes;

    public ProductCategoryEditWindow(ProductCategory? existing = null)
    {
        InitializeComponent();
        _category = existing ?? new ProductCategory();

        if (existing is not null)
        {
            TitleText.Text = $"Kategori · {existing.Code}";
            CodeBox.Text = existing.Code;
            NameBox.Text = existing.Name;
            ActiveCheck.IsChecked = existing.IsActive;
            _imageBytes = _service.GetImage(existing.Id);
            ShowPreview(_imageBytes);
        }

        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Kategori Resmi Seç",
            Filter = "Resim Dosyaları|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm Dosyalar|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                _imageBytes = File.ReadAllBytes(dlg.FileName);
                ShowPreview(_imageBytes);
            }
            catch (Exception ex)
            {
                ShowError("Resim yüklenemedi: " + ex.Message);
            }
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        _imageBytes = null;
        ShowPreview(null);
    }

    private void ShowPreview(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
        {
            var img = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            PreviewImage.Source = img;
            PreviewImage.Visibility = Visibility.Visible;
            NoImageText.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            NoImageText.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        if (CodeHelper.Normalize(CodeBox.Text).Length == 0) { ShowError("Geçerli bir kod giriniz."); return; }
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { ShowError("Kategori adını giriniz."); return; }

        _category.Code = CodeBox.Text;
        _category.Name = NameBox.Text.Trim();
        _category.IsActive = ActiveCheck.IsChecked == true;
        _category.Image = _imageBytes;

        try
        {
            SaveButton.IsEnabled = false;
            _service.Save(_category, Session.CurrentUserId);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void ShowError(string m) { ErrorText.Text = m; ErrorText.Visibility = Visibility.Visible; }
    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
