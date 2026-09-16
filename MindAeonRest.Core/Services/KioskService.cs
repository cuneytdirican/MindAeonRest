using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>KIOSK tanımları (şirket DB, şube bazlı). Kasa/POS/Çek hesapları bir KIOSK'a atanabilir.</summary>
public class KioskService
{
    private static string SelectBase => $@"
SELECT k.Id, k.BranchId, b.Name AS BranchName, k.Code, k.Name, k.IsActive, k.IsDeleted, k.No, k.ReceiptPrinterName
FROM dbo.Kiosks k
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = k.BranchId";

    public List<Kiosk> GetAll(bool includeDeleted = false)
    {
        var list = new List<Kiosk>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE (@inc=1 OR k.IsDeleted=0) ORDER BY k.Code;";
        cmd.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Kiosk
            {
                Id = r.GetInt32(0), BranchId = r.GetInt32(1), BranchName = r.IsDBNull(2) ? null : r.GetString(2),
                Code = r.GetString(3), Name = r.GetString(4), IsActive = r.GetBoolean(5), IsDeleted = r.GetBoolean(6),
                No = r.IsDBNull(7) ? 1 : r.GetInt32(7),
                ReceiptPrinterName = r.IsDBNull(8) ? null : r.GetString(8)
            });
        return list;
    }

    /// <summary>Tek KIOSK (silinmiş dahil) — back-office düzenleme için.</summary>
    public Kiosk? GetById(int id) => GetAll(includeDeleted: true).FirstOrDefault(k => k.Id == id);

    /// <summary>Belirli şubenin aktif KIOSK'ları.</summary>
    public List<Kiosk> GetByBranch(int branchId) => GetAll(false).Where(k => k.BranchId == branchId && k.IsActive).ToList();

    /// <summary>Şube + KIOSK numarasına göre KIOSK (login'de terminal kimliği çözümleme).</summary>
    public Kiosk? GetByNo(int branchId, int no) => GetAll(false).FirstOrDefault(k => k.BranchId == branchId && k.No == no);

    /// <summary>Şubede verilen No'lu KIOSK yoksa otomatik oluşturur (login'de terminal kimliğini garanti eder).</summary>
    public Kiosk EnsureForBranch(int branchId, int no, int? userId)
    {
        var existing = GetByNo(branchId, no);
        if (existing is not null) return existing;
        // Kod şube içinde tekil olmalı; çakışmayı önlemek için şube id'sini de kata
        string code = $"KIOSK{no}-{branchId}";
        return Save(new Kiosk { BranchId = branchId, No = no, Code = code, Name = $"KIOSK {no}", IsActive = true }, userId);
    }

    /// <summary>LİSANS: aktif+silinmemiş terminal (KIOSK) sayısı paket limitine (MaxKiosks) ulaştıysa
    /// YENİ kayıt engellenir. Enterprise (MaxKiosks=int.MaxValue) sınırsız; limit 0/tanımsız (eski token)
    /// gelirse güvenli varsayılan olarak kısıtlama uygulanmaz (regresyon yok). Sayım şirket DB kapsamlıdır.</summary>
    private static void EnforceKioskLimit(SqlConnection c)
    {
        var lic = Helpers.Session.CurrentLicense;
        if (lic is null || lic.MaxKiosks <= 0 || lic.MaxKiosks == int.MaxValue) return;
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.Kiosks WHERE IsActive=1 AND IsDeleted=0;";
        int active = Convert.ToInt32(cmd.ExecuteScalar());
        if (active >= lic.MaxKiosks)
            throw new InvalidOperationException($"Terminal (KIOSK) limitine ulaşıldı ({lic.MaxKiosks}). Ek terminal için lisansınızı yükseltin.");
    }

    public Kiosk Save(Kiosk k, int? userId)
    {
        var code = CodeHelper.Normalize(k.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir KIOSK kodu giriniz.");
        if (string.IsNullOrWhiteSpace(k.Name)) throw new ArgumentException("KIOSK adı boş olamaz.");
        if (k.BranchId == 0) throw new ArgumentException("Şube seçiniz.");

        using var c = Database.OpenCompany();
        using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.Kiosks WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", k.Id);
            if ((int)check.ExecuteScalar()! > 0) throw new InvalidOperationException($"'{code}' kodlu KIOSK zaten var.");
        }
        int no = k.No <= 0 ? 1 : k.No;
        using (var nchk = c.CreateCommand())
        {
            nchk.CommandText = "SELECT COUNT(1) FROM dbo.Kiosks WHERE BranchId=@b AND No=@no AND IsDeleted=0 AND Id<>@id;";
            nchk.Parameters.AddWithValue("@b", k.BranchId);
            nchk.Parameters.AddWithValue("@no", no);
            nchk.Parameters.AddWithValue("@id", k.Id);
            if ((int)nchk.ExecuteScalar()! > 0) throw new InvalidOperationException($"Bu şubede {no} numaralı KIOSK zaten var.");
        }
        // Yalnız YENİ kayıt (INSERT) lisans tavanına tabi; mevcut kayıt güncellemesi (UPDATE) etkilenmez.
        if (k.Id == 0) EnforceKioskLimit(c);
        using var cmd = c.CreateCommand();
        if (k.Id == 0)
            cmd.CommandText = "INSERT INTO dbo.Kiosks (BranchId, No, Code, Name, IsActive, ReceiptPrinterName, CreatedBy) OUTPUT INSERTED.Id VALUES (@b,@no,@c,@n,@a,@printer,@by);";
        else
        {
            cmd.CommandText = "UPDATE dbo.Kiosks SET BranchId=@b, No=@no, Code=@c, Name=@n, IsActive=@a, ReceiptPrinterName=@printer, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", k.Id);
        }
        cmd.Parameters.AddWithValue("@b", k.BranchId);
        cmd.Parameters.AddWithValue("@no", no);
        cmd.Parameters.AddWithValue("@c", code);
        cmd.Parameters.AddWithValue("@n", k.Name.Trim());
        cmd.Parameters.AddWithValue("@a", k.IsActive);
        cmd.Parameters.AddWithValue("@printer", (object?)k.ReceiptPrinterName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        if (k.Id == 0) k.Id = (int)cmd.ExecuteScalar()!;
        else cmd.ExecuteNonQuery();
        k.Code = code;
        return k;
    }
}
