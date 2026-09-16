namespace MindAeonRest.Models;

/// <summary>Bir reyona (mutfak/bar) basılacak mutfak fişi — fiyatsız, ürün+miktar+not odaklı.</summary>
public class KitchenTicket
{
    public int ReyonId { get; set; }
    public string ReyonName { get; set; } = "";
    public string? PrinterName { get; set; }
    public string OrderCode { get; set; } = "";
    public string? TableCodes { get; set; }
    public string? WaiterName { get; set; }
    public System.Collections.Generic.List<KitchenTicketLine> Lines { get; set; } = new();

    public bool HasPrinter => !string.IsNullOrWhiteSpace(PrinterName);
}

public class KitchenTicketLine
{
    public string Name { get; set; } = "";
    public decimal Qty { get; set; }
    public string? Note { get; set; }
    public bool IsComponent { get; set; }
    public int SeatNo { get; set; }

    public string QtyText => $"{Qty:0.##} x";
}
