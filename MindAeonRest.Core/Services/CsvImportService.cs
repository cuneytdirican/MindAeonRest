using System.Globalization;
using System.IO;
using System.Text;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// Stok kartı + fiyat CSV içe aktarımı: ayrıştırma, satır-satır doğrulama ve aktarım.
/// Başlık satırı zorunlu; kolonlar başlık adına göre eşleşir (sıra serbest). Ayraç ; veya ,
/// </summary>
public class CsvImportService
{
    private static readonly CultureInfo Tr = new("tr-TR");

    // Beklenen başlık anahtarları (normalize edilmiş, küçük harf)
    public const string SampleHeader = "Kod;Ad;Aciklama;BirimKodu;SatisVergiKodu;AlisVergiKodu;IadeVergiKodu;Takip;KategoriKodlari;Fiyat;DovizKodu;SubeKodu;Barkod";

    public string BuildSample()
    {
        var sb = new StringBuilder();
        sb.AppendLine(SampleHeader);
        sb.AppendLine("HMB001;Hamburger;Klasik hamburger;ADET;KDV10;KDV10;KDV10;YOK;ANAYEMEK;120,00;TRY;;8690000000001");
        sb.AppendLine("CLA001;Kola 330ml;;ADET;KDV20;;;YOK;ICECEK|SOGUK;35,50;TRY;MERKEZ;8690000000002");
        return sb.ToString();
    }

    // ---- Lookups ----
    private Dictionary<string, (int Id, string Name)> _units = new();
    private Dictionary<string, int> _taxes = new();
    private Dictionary<string, int> _currencies = new();
    private Dictionary<string, int> _categories = new();
    private Dictionary<string, int> _branches = new();
    private readonly ProductService _products = new();

    private void LoadLookups()
    {
        _units = new MainUnitService().GetAll().ToDictionary(u => Key(u.Code), u => (u.Id, u.Name));
        _taxes = new TaxGroupService().GetAll().ToDictionary(t => Key(t.Code), t => t.Id);
        _currencies = new CurrencyService().GetAll().ToDictionary(c => Key(c.Code), c => c.Id);
        _categories = new ProductCategoryService().GetAll().ToDictionary(c => Key(c.Code), c => c.Id);
        _branches = new BranchService().GetByCompany(Session.CurrentCompany?.Id ?? 0).ToDictionary(b => Key(b.Code), b => b.Id);
    }

    private static string Key(string s) => CodeHelper.Normalize(s);

    /// <summary>CSV dosyasını ayrıştırır ve verilen varsayılanlara göre doğrular.</summary>
    public List<CsvProductRow> ParseAndValidate(string filePath, CsvImportOptions opts)
    {
        LoadLookups();
        string content = File.ReadAllText(filePath, Encoding.UTF8);
        char delim = DetectDelimiter(content);
        var grid = SplitCsv(content, delim);
        var result = new List<CsvProductRow>();
        if (grid.Count == 0) return result;

        // başlık -> kolon index
        var header = grid[0];
        var map = new Dictionary<string, int>();
        for (int i = 0; i < header.Count; i++)
        {
            string k = Key(header[i]).ToLowerInvariant();
            if (k.Length > 0 && !map.ContainsKey(k)) map[k] = i;
        }

        string? Cell(List<string> row, string key) =>
            map.TryGetValue(key, out int idx) && idx < row.Count ? row[idx].Trim() : null;

        var seenCodes = new HashSet<string>();
        for (int r = 1; r < grid.Count; r++)
        {
            var line = grid[r];
            if (line.All(string.IsNullOrWhiteSpace)) continue;

            var row = new CsvProductRow
            {
                RowNo = r,
                Code = CodeHelper.Normalize(Cell(line, "kod") ?? ""),
                Name = (Cell(line, "ad") ?? "").Trim(),
                Description = Cell(line, "aciklama"),
                UnitCode = Cell(line, "birimkodu"),
                SalesTaxCode = Cell(line, "satisvergikodu"),
                PurchaseTaxCode = Cell(line, "alisvergikodu"),
                ReturnTaxCode = Cell(line, "iadevergikodu"),
                TrackingRaw = Cell(line, "takip"),
                CategoryCodes = Cell(line, "kategorikodlari"),
                PriceRaw = Cell(line, "fiyat"),
                CurrencyCode = Cell(line, "dovizkodu"),
                BranchCode = Cell(line, "subekodu"),
                Barcode = Cell(line, "barkod")
            };
            Validate(row, opts, seenCodes);
            result.Add(row);
        }
        return result;
    }

    private void Validate(CsvProductRow row, CsvImportOptions opts, HashSet<string> seenCodes)
    {
        var errors = new List<string>();

        if (row.Code.Length == 0) errors.Add("Kod zorunlu");
        else if (!seenCodes.Add(row.Code)) errors.Add("Dosyada tekrar eden kod");
        if (string.IsNullOrWhiteSpace(row.Name)) errors.Add("Ad zorunlu");

        // Birim
        row.UnitId = ResolveOrDefault(row.UnitCode, _units.TryGetValue(Key(row.UnitCode ?? ""), out var u) ? u.Id : (int?)null,
            opts.DefaultUnitId, "Birim kodu bulunamadı", errors);
        row.UnitName = row.UnitId.HasValue ? _units.Values.FirstOrDefault(x => x.Id == row.UnitId).Name : null;

        // Vergi grupları
        row.SalesTaxId = ResolveOrDefault(row.SalesTaxCode, Tax(row.SalesTaxCode), opts.DefaultSalesTaxId, "Satış vergi kodu bulunamadı", errors);
        row.PurchaseTaxId = ResolveOrDefault(row.PurchaseTaxCode, Tax(row.PurchaseTaxCode), opts.DefaultPurchaseTaxId, "Alış vergi kodu bulunamadı", errors);
        row.ReturnTaxId = ResolveOrDefault(row.ReturnTaxCode, Tax(row.ReturnTaxCode), opts.DefaultReturnTaxId, "İade vergi kodu bulunamadı", errors);

        // Takip
        row.TrackingType = ParseTracking(row.TrackingRaw, errors);

        // Kategoriler (| ile ayrılır)
        if (!string.IsNullOrWhiteSpace(row.CategoryCodes))
            foreach (var c in row.CategoryCodes.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (_categories.TryGetValue(Key(c), out int cid)) row.ResolvedCategoryIds.Add(cid);
                else errors.Add($"Kategori kodu bulunamadı: {c}");
            }

        // Fiyat
        if (!string.IsNullOrWhiteSpace(row.PriceRaw))
        {
            if (decimal.TryParse(row.PriceRaw.Replace('.', ','), NumberStyles.Any, Tr, out var price) && price >= 0)
            {
                row.Price = price;
                row.CurrencyId = ResolveOrDefault(row.CurrencyCode, Cur(row.CurrencyCode), opts.DefaultCurrencyId, "Döviz kodu bulunamadı", errors);
                if (row.CurrencyId is null) errors.Add("Fiyat için döviz gerekli");
                if (row.UnitId is null) errors.Add("Fiyat için birim gerekli");
                // Şube (boşsa hedef kapsam)
                if (!string.IsNullOrWhiteSpace(row.BranchCode))
                {
                    if (_branches.TryGetValue(Key(row.BranchCode), out int bid)) row.BranchId = bid;
                    else errors.Add($"Şube kodu bulunamadı: {row.BranchCode}");
                }
                else row.BranchId = opts.TargetBranchId;
            }
            else errors.Add("Fiyat sayısal olmalı");
        }

        // Barkod birim gerektirir
        if (!string.IsNullOrWhiteSpace(row.Barcode) && row.UnitId is null)
            errors.Add("Barkod için birim gerekli");

        // Var olan kod kontrolü
        if (row.Code.Length > 0)
        {
            row.ExistsInDb = _products.CodeExists(row.Code);
            if (row.ExistsInDb && !opts.UpdateExisting) errors.Add("Kod zaten var (güncelleme kapalı)");
        }

        row.IsValid = errors.Count == 0;
        row.Error = errors.Count == 0 ? null : string.Join("; ", errors);
    }

    private int? Tax(string? code) => code != null && _taxes.TryGetValue(Key(code), out int id) ? id : null;
    private int? Cur(string? code) => code != null && _currencies.TryGetValue(Key(code), out int id) ? id : null;

    private static int? ResolveOrDefault(string? code, int? resolved, int? def, string notFoundMsg, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (resolved.HasValue) return resolved;
            errors.Add($"{notFoundMsg}: {code}");
            return null;
        }
        return def;
    }

    private static TrackingType ParseTracking(string? raw, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return TrackingType.Yok;
        return Key(raw) switch
        {
            "YOK" or "0" => TrackingType.Yok,
            "PARTI" or "1" => TrackingType.Parti,
            "PARTILOT" or "2" => TrackingType.PartiLot,
            "SERI" or "3" => TrackingType.Seri,
            "RENKBEDEN" or "4" => TrackingType.RenkBeden,
            _ => Add(errors, raw)
        };
        static TrackingType Add(List<string> e, string r) { e.Add($"Geçersiz takip: {r}"); return TrackingType.Yok; }
    }

    /// <summary>Geçerli satırları aktarır (oluştur/güncelle). Güncellemede ürünün koleksiyonları CSV ile değişir.</summary>
    public CsvImportResult Import(IEnumerable<CsvProductRow> rows, int? userId)
    {
        var res = new CsvImportResult();
        foreach (var row in rows.Where(r => r.IsValid))
        {
            try
            {
                var existing = row.ExistsInDb ? _products.GetById(GetIdByCode(row.Code)) : null;
                var p = existing ?? new Product();
                p.Code = row.Code;
                p.Name = row.Name;
                p.Description = row.Description;
                p.MainUnitId = row.UnitId;
                p.SalesTaxGroupId = row.SalesTaxId;
                p.PurchaseTaxGroupId = row.PurchaseTaxId;
                p.ReturnTaxGroupId = row.ReturnTaxId;
                p.TrackingType = row.TrackingType;
                p.IsActive = true;

                var units = new List<ProductUnit>();
                var barcodes = new List<ProductBarcode>();
                var prices = new List<ProductPrice>();
                if (row.UnitId is int unitId)
                {
                    units.Add(new ProductUnit { UnitId = unitId, IsMainUnit = true, ConversionFactor1 = 1, ConversionFactor2 = 1 });
                    if (!string.IsNullOrWhiteSpace(row.Barcode))
                        barcodes.Add(new ProductBarcode { UnitId = unitId, Barcode = row.Barcode! });
                    if (row.Price is decimal pr)
                        prices.Add(new ProductPrice { UnitId = unitId, PriceType = PriceType.Satis, Price = pr, CurrencyId = row.CurrencyId, BranchId = row.BranchId });
                }

                _products.Save(p, row.ResolvedCategoryIds, Array.Empty<ProductImage>(), Array.Empty<ProductDescription>(),
                    units, barcodes, prices, Array.Empty<ProductRecipe>(), Array.Empty<ProductBranchConsume>(), userId);

                if (existing is null) res.Created++; else res.Updated++;
            }
            catch (Exception ex)
            {
                res.Failed++;
                res.Errors.Add($"Satır {row.RowNo} ({row.Code}): {ex.Message}");
            }
        }
        return res;
    }

    private int GetIdByCode(string code)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP 1 Id FROM dbo.Products WHERE Code=@c AND IsDeleted=0;";
        command.Parameters.AddWithValue("@c", code);
        return command.ExecuteScalar() is int id ? id : 0;
    }

    // ---- CSV ayrıştırma ----
    private static char DetectDelimiter(string content)
    {
        int nl = content.IndexOf('\n');
        string first = nl >= 0 ? content[..nl] : content;
        return first.Count(c => c == ';') >= first.Count(c => c == ',') ? ';' : ',';
    }

    private static List<List<string>> SplitCsv(string text, char delim)
    {
        var rows = new List<List<string>>();
        var cur = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delim) { cur.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { cur.Add(sb.ToString()); rows.Add(cur); cur = new List<string>(); sb.Clear(); }
            else sb.Append(c);
        }
        if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); rows.Add(cur); }
        return rows;
    }
}
