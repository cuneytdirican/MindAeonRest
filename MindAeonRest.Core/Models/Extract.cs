namespace MindAeonRest.Models;

/// <summary>Ekstre (hesap hareket dökümü) satırı — yürüyen bakiyeli.</summary>
public class ExtractRow
{
    public System.DateTime Date { get; set; }
    public string TypeText { get; set; } = "";
    public string Note { get; set; } = "";
    public decimal In { get; set; }       // borç/giriş (+)
    public decimal Out { get; set; }      // alacak/çıkış (+ olarak)
    public decimal Running { get; set; }  // yürüyen bakiye

    public string DateText => Date.ToString("dd.MM.yyyy HH:mm");
    public string InText => In != 0 ? $"{In:N2}" : "";
    public string OutText => Out != 0 ? $"{Out:N2}" : "";
    public string RunningText => $"{Running:N2}";
}

/// <summary>Ekstre sonucu: açılış + hareketler + toplamlar + kapanış.</summary>
public class ExtractResult
{
    public decimal Opening { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal Closing { get; set; }
    public System.Collections.Generic.List<ExtractRow> Rows { get; set; } = new();
}
