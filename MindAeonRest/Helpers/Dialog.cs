using System.Linq;
using System.Windows;
using MindAeonRest.Views;

namespace MindAeonRest.Helpers;

/// <summary>Tüm MessageBox.Show çağrılarının yerini alan tema uyumlu diyalog (CustomMessageBox).</summary>
public static class Dialog
{
    private static Window? ActiveOwner()
    {
        var app = Application.Current;
        if (app is null) return null;
        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            ?? app.Windows.OfType<Window>().LastOrDefault(w => w.IsVisible)
            ?? app.MainWindow;
    }

    public static void Info(string? message, string title = "Bilgi") => Show(message, title, DialogIcon.Info, DialogButtons.Ok);
    public static void Warn(string? message, string title = "Uyarı") => Show(message, title, DialogIcon.Warn, DialogButtons.Ok);
    public static void Error(string? message, string title = "Hata") => Show(message, title, DialogIcon.Error, DialogButtons.Ok);
    public static void Success(string? message, string title = "Başarılı") => Show(message, title, DialogIcon.Success, DialogButtons.Ok);

    /// <summary>Evet/Hayır — Evet ise true.</summary>
    public static bool Confirm(string? message, string title = "Onay") => Show(message, title, DialogIcon.Question, DialogButtons.YesNo) == DialogResultType.Yes;

    /// <summary>Evet/Hayır/Vazgeç.</summary>
    public static DialogResultType YesNoCancel(string? message, string title = "Onay") => Show(message, title, DialogIcon.Question, DialogButtons.YesNoCancel);

    private static DialogResultType Show(string? message, string title, DialogIcon icon, DialogButtons buttons)
    {
        var box = new CustomMessageBox(message ?? "", title, icon, buttons);
        var owner = ActiveOwner();
        try { if (owner != null && !ReferenceEquals(owner, box)) box.Owner = owner; }
        catch { /* owner gösterilmemiş olabilir */ }
        box.ShowDialog();
        return box.Result;
    }
}
