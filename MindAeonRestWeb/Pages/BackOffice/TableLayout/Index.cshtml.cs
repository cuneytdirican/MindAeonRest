using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MindAeonRest.Helpers;
using MindAeonRest.Licensing;
using MindAeonRest.Models;
using MindAeonRest.Services;

namespace MindAeonRestWeb.Pages.BackOffice.TableLayout;

/// <summary>Masa Yerleşim Planı — WPF ZoneLayoutWindow'un web karşılığı.
/// Canvas tabanlı sürükle-bırak tasarımcı YERİNE pratik tablo-tabanlı yönetim:
/// Lokasyon (Zone) seç (?zoneId=) → o lokasyonun masaları satır satır (Kod / Ad / Koltuk / Şekil / X / Y),
/// "+ Masa Ekle" (NextTableNumberForBranch ile oto kod) + satır sil, tek "Kaydet" → TableService.SaveLayout.
/// BranchId Zone'un şubesinden alınır. Width/Height sabit (80) kalır — koordinat sayısal input.</summary>
public class IndexModel : PageModel
{
    private readonly ZoneService _zones = new();
    private readonly TableService _tables = new();

    [BindProperty(SupportsGet = true)] public int ZoneId { get; set; }
    [BindProperty] public List<TableInput> Tables { get; set; } = new();
    [BindProperty] public string DeletedIds { get; set; } = "";   // virgülle ayrık silinen Table.Id listesi

    public List<SelectListItem> ZoneOptions { get; private set; } = new();
    public int BranchId { get; private set; }
    public int NextNumber { get; private set; }
    public string? Error { get; private set; }
    public string? Info { get; private set; }
    public bool HasZone => ZoneId > 0 && BranchId > 0;

    private static bool CanAccess => Session.IsTenantAdmin && Session.LicenseHas(Feature.LocationTables);

    public IActionResult OnGet()
    {
        if (!CanAccess) return Forbid();
        LoadZones();
        if (ZoneId > 0)
        {
            ResolveBranch();
            if (BranchId > 0)
            {
                Tables = _tables.GetByZone(ZoneId).Select(TableInput.From).ToList();
                NextNumber = _tables.NextTableNumberForBranch(BranchId);
            }
        }
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!CanAccess) return Forbid();
        LoadZones();
        ResolveBranch();
        if (BranchId <= 0) { Error = "Önce geçerli bir lokasyon seçin."; return Page(); }

        var deleted = (DeletedIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();

        var models = (Tables ?? new())
            .Where(t => !string.IsNullOrWhiteSpace(t.Code))
            .Select(t => t.ToModel(ZoneId, BranchId))
            .ToList();

        try
        {
            _tables.SaveLayout(ZoneId, BranchId, models, deleted, Session.CurrentUserId);
            Info = "Yerleşim planı kaydedildi.";
            Tables = _tables.GetByZone(ZoneId).Select(TableInput.From).ToList();
            NextNumber = _tables.NextTableNumberForBranch(BranchId);
            DeletedIds = "";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            NextNumber = _tables.NextTableNumberForBranch(BranchId);
        }
        return Page();
    }

    private void LoadZones()
    {
        var zones = _zones.GetAll();
        ZoneOptions = new() { new SelectListItem("(Lokasyon seçiniz)", "0") };
        ZoneOptions.AddRange(zones.Select(z => new SelectListItem(z.Display, z.Id.ToString())));
    }

    /// <summary>BranchId = seçili Zone'un şubesi (ZoneService). Yoksa oturumun şubesine düş.</summary>
    private void ResolveBranch()
    {
        if (ZoneId <= 0) { BranchId = 0; return; }
        var z = _zones.GetById(ZoneId);
        BranchId = z?.BranchId ?? Session.CurrentBranch?.Id ?? 0;
    }

    /// <summary>Formdan gelen tek masa satırı (indexed bind: Tables[i].X). Width/Height sabit 80.</summary>
    public class TableInput
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string? Name { get; set; }
        public int Seats { get; set; } = 4;
        public int Shape { get; set; }   // 0=Kare, 1=Yuvarlak
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 80;
        public double Height { get; set; } = 80;

        public static TableInput From(Table t) => new()
        {
            Id = t.Id, Code = t.Code, Name = t.Name, Seats = t.Seats, Shape = (int)t.Shape,
            X = t.X, Y = t.Y,
            Width = t.Width <= 0 ? 80 : t.Width,
            Height = t.Height <= 0 ? 80 : t.Height
        };

        public Table ToModel(int zoneId, int branchId) => new()
        {
            Id = Id,
            ZoneId = zoneId,
            BranchId = branchId,
            Code = (Code ?? "").Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(),
            Seats = Seats,
            Shape = (TableShape)Shape,
            X = X,
            Y = Y,
            Width = Width <= 0 ? 80 : Width,
            Height = Height <= 0 ? 80 : Height,
            IsActive = true
        };
    }
}
