using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Kampanya yönetimi (şirket DB): CRUD + şube junction + "şu an geçerli" sorgusu.</summary>
public class CampaignService
{
    private const string SelectBase = @"
SELECT c.Id, c.Code, c.Name, c.Description, c.Type, c.Value, c.TargetCategoryId, c.TargetProductId,
       c.MinAmount, c.BuyQty, c.PayQty, c.StartDateTime, c.EndDateTime, c.DaysOfWeek, c.DailyStartMin, c.DailyEndMin,
       c.AllBranches, c.IsActive, c.IsDeleted, cat.Name, pr.Name, c.Stackable, c.IsMandatory
FROM dbo.Campaigns c
LEFT JOIN dbo.ProductCategories cat ON cat.Id = c.TargetCategoryId
LEFT JOIN dbo.Products pr ON pr.Id = c.TargetProductId";

    public List<Campaign> GetAll(bool includeDeleted = false)
    {
        var list = new List<Campaign>();
        using var c = Database.OpenCompany();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = SelectBase + " WHERE (@inc=1 OR c.IsDeleted=0) ORDER BY c.IsActive DESC, c.Name;";
            cmd.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Map(r));
        }
        LoadDetails(c, list);
        return list;
    }

    public Campaign? GetById(int id)
    {
        using var c = Database.OpenCompany();
        Campaign? camp = null;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = SelectBase + " WHERE c.Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (r.Read()) camp = Map(r);
        }
        if (camp != null) LoadDetails(c, new List<Campaign> { camp });
        return camp;
    }

    /// <summary>Verilen şube için ŞU AN geçerli kampanyalar (aktif + tarih/gün/saat + şube).</summary>
    public List<Campaign> GetActive(int branchId, DateTime now)
        => GetAll(includeDeleted: false).Where(c => c.IsValidNow(branchId, now)).ToList();

    public Campaign Save(Campaign m, int? userId)
    {
        var code = CodeHelper.Normalize(m.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir kampanya kodu giriniz.");
        if (string.IsNullOrWhiteSpace(m.Name)) throw new ArgumentException("Kampanya adı boş olamaz.");
        if (m.EndDateTime < m.StartDateTime) throw new InvalidOperationException("Bitiş tarihi başlangıçtan önce olamaz.");

        // AUDIT: güncelleme öncesi durum (kampanya = para/indirim tanımı) — yazımdan önce snapshot.
        bool isNew = m.Id == 0;
        var beforeSnapshot = isNew ? null : GetById(m.Id);

        using var c = Database.OpenCompany();
        using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.Campaigns WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", m.Id);
            if ((int)check.ExecuteScalar()! > 0) throw new InvalidOperationException($"'{code}' kodlu kampanya zaten var.");
        }
        using (var cmd = c.CreateCommand())
        {
            if (m.Id == 0)
                cmd.CommandText = @"
INSERT INTO dbo.Campaigns (Code, Name, Description, Type, Value, TargetCategoryId, TargetProductId, MinAmount, BuyQty, PayQty, StartDateTime, EndDateTime, DaysOfWeek, DailyStartMin, DailyEndMin, AllBranches, IsActive, Stackable, IsMandatory, CreatedBy)
OUTPUT INSERTED.Id VALUES (@code,@name,@desc,@type,@val,@cat,@prod,@min,@buy,@pay,@s,@e,@dow,@ds,@de,@all,@act,@stack,@mand,@by);";
            else
            {
                cmd.CommandText = @"
UPDATE dbo.Campaigns SET Code=@code, Name=@name, Description=@desc, Type=@type, Value=@val, TargetCategoryId=@cat, TargetProductId=@prod,
    MinAmount=@min, BuyQty=@buy, PayQty=@pay, StartDateTime=@s, EndDateTime=@e, DaysOfWeek=@dow, DailyStartMin=@ds, DailyEndMin=@de,
    AllBranches=@all, IsActive=@act, Stackable=@stack, IsMandatory=@mand, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
                cmd.Parameters.AddWithValue("@id", m.Id);
            }
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@name", m.Name.Trim());
            cmd.Parameters.AddWithValue("@desc", (object?)m.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@type", (int)m.Type);
            cmd.Parameters.AddWithValue("@val", m.Value);
            cmd.Parameters.AddWithValue("@cat", (object?)m.TargetCategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@prod", (object?)m.TargetProductId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@min", m.MinAmount);
            cmd.Parameters.AddWithValue("@buy", m.BuyQty);
            cmd.Parameters.AddWithValue("@pay", m.PayQty);
            cmd.Parameters.AddWithValue("@s", m.StartDateTime);
            cmd.Parameters.AddWithValue("@e", m.EndDateTime);
            cmd.Parameters.AddWithValue("@dow", m.DaysOfWeek);
            cmd.Parameters.AddWithValue("@ds", (object?)m.DailyStartMin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@de", (object?)m.DailyEndMin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@all", m.AllBranches);
            cmd.Parameters.AddWithValue("@act", m.IsActive);
            cmd.Parameters.AddWithValue("@stack", m.Stackable);
            cmd.Parameters.AddWithValue("@mand", m.IsMandatory);
            cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
            if (m.Id == 0) m.Id = (int)cmd.ExecuteScalar()!;
            else cmd.ExecuteNonQuery();
        }
        // şube junction (varsa güncelle, yoksa oluştur)
        using (var del = c.CreateCommand()) { del.CommandText = "DELETE FROM dbo.CampaignBranches WHERE CampaignId=@c;"; del.Parameters.AddWithValue("@c", m.Id); del.ExecuteNonQuery(); }
        if (!m.AllBranches)
            foreach (var bid in m.BranchIds.Distinct())
            {
                using var ins = c.CreateCommand();
                ins.CommandText = "INSERT INTO dbo.CampaignBranches (CampaignId, BranchId, CreatedBy) VALUES (@c,@b,@by);";
                ins.Parameters.AddWithValue("@c", m.Id); ins.Parameters.AddWithValue("@b", bid); ins.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }

        // Hediye koşulları + ikram ürünleri (varsa güncelle, yoksa oluştur)
        using (var d = c.CreateCommand()) { d.CommandText = "DELETE FROM dbo.CampaignConditions WHERE CampaignId=@c; DELETE FROM dbo.CampaignGifts WHERE CampaignId=@c;"; d.Parameters.AddWithValue("@c", m.Id); d.ExecuteNonQuery(); }
        if (m.Type == CampaignType.Hediye)
        {
            foreach (var cond in m.Conditions.Where(x => x.ProductId > 0 && x.MinQty > 0))
            {
                using var ins = c.CreateCommand();
                ins.CommandText = "INSERT INTO dbo.CampaignConditions (CampaignId, ProductId, MinQty, CreatedBy) VALUES (@c,@p,@q,@by);";
                ins.Parameters.AddWithValue("@c", m.Id); ins.Parameters.AddWithValue("@p", cond.ProductId); ins.Parameters.AddWithValue("@q", cond.MinQty); ins.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
            foreach (var g in m.Gifts.Where(x => x.ProductId > 0 && x.Qty > 0))
            {
                using var ins = c.CreateCommand();
                ins.CommandText = "INSERT INTO dbo.CampaignGifts (CampaignId, ProductId, Qty, CreatedBy) VALUES (@c,@p,@q,@by);";
                ins.Parameters.AddWithValue("@c", m.Id); ins.Parameters.AddWithValue("@p", g.ProductId); ins.Parameters.AddWithValue("@q", g.Qty); ins.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
        }

        // AUDIT (best-effort): kampanya ekleme/güncelleme izi (tip/değer/tarih/aktiflik).
        AuditService.Write("Campaign", m.Id, isNew ? "Insert" : "Update",
            beforeSnapshot is null ? null : new { beforeSnapshot.Code, beforeSnapshot.Name, beforeSnapshot.Type, beforeSnapshot.Value, beforeSnapshot.MinAmount, beforeSnapshot.StartDateTime, beforeSnapshot.EndDateTime, beforeSnapshot.IsActive },
            new { m.Code, m.Name, m.Type, m.Value, m.MinAmount, m.StartDateTime, m.EndDateTime, m.IsActive },
            userId);
        return m;
    }

    public void SetActive(int id, bool active, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Campaigns SET IsActive=@a, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@a", active);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE dbo.Campaigns SET IsDeleted=1, UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;";
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void LoadDetails(SqlConnection c, List<Campaign> list)
    {
        if (list.Count == 0) return;
        var byId = list.ToDictionary(x => x.Id);
        var ids = string.Join(",", byId.Keys);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT CampaignId, BranchId FROM dbo.CampaignBranches WHERE CampaignId IN ({ids});";
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (byId.TryGetValue(r.GetInt32(0), out var camp)) camp.BranchIds.Add(r.GetInt32(1));
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT cc.Id, cc.CampaignId, cc.ProductId, cc.MinQty, p.Name FROM dbo.CampaignConditions cc LEFT JOIN dbo.Products p ON p.Id=cc.ProductId WHERE cc.CampaignId IN ({ids}) ORDER BY cc.Id;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (byId.TryGetValue(r.GetInt32(1), out var camp))
                camp.Conditions.Add(new CampaignCondition { Id = r.GetInt32(0), CampaignId = r.GetInt32(1), ProductId = r.GetInt32(2), MinQty = r.GetDecimal(3), ProductName = r.IsDBNull(4) ? null : r.GetString(4) });
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT g.Id, g.CampaignId, g.ProductId, g.Qty, p.Name FROM dbo.CampaignGifts g LEFT JOIN dbo.Products p ON p.Id=g.ProductId WHERE g.CampaignId IN ({ids}) ORDER BY g.Id;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (byId.TryGetValue(r.GetInt32(1), out var camp))
                camp.Gifts.Add(new CampaignGift { Id = r.GetInt32(0), CampaignId = r.GetInt32(1), ProductId = r.GetInt32(2), Qty = r.GetDecimal(3), ProductName = r.IsDBNull(4) ? null : r.GetString(4) });
        }
    }

    private static Campaign Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), Code = r.GetString(1), Name = r.GetString(2),
        Description = r.IsDBNull(3) ? null : r.GetString(3),
        Type = (CampaignType)r.GetInt32(4), Value = r.GetDecimal(5),
        TargetCategoryId = r.IsDBNull(6) ? null : r.GetInt32(6), TargetProductId = r.IsDBNull(7) ? null : r.GetInt32(7),
        MinAmount = r.GetDecimal(8), BuyQty = r.GetDecimal(9), PayQty = r.GetDecimal(10),
        StartDateTime = r.GetDateTime(11), EndDateTime = r.GetDateTime(12),
        DaysOfWeek = r.GetInt32(13), DailyStartMin = r.IsDBNull(14) ? null : r.GetInt32(14), DailyEndMin = r.IsDBNull(15) ? null : r.GetInt32(15),
        AllBranches = r.GetBoolean(16), IsActive = r.GetBoolean(17), IsDeleted = r.GetBoolean(18),
        TargetCategoryName = r.IsDBNull(19) ? null : r.GetString(19), TargetProductName = r.IsDBNull(20) ? null : r.GetString(20),
        Stackable = r.GetBoolean(21),
        IsMandatory = r.GetBoolean(22)
    };
}
