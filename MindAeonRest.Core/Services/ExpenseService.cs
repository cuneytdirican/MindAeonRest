using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Giderler: gider fişi kaydı + Kasa/Banka'dan çıkış. Maaş tipinde personel zorunlu.</summary>
public class ExpenseService
{
    public int Save(Expense x, int? userId)
    {
        if (x.ExpenseTypeId == 0) throw new ArgumentException("Gider tipi seçiniz.");
        if (x.Amount <= 0) throw new ArgumentException("Tutar 0'dan büyük olmalı.");
        if (x.InstrumentId == 0) throw new ArgumentException("Ödeme aracı (kasa/banka) seçiniz.");
        if (x.CurrencyId == 0) x.CurrencyId = new CurrencyService().GetDefaultId() ?? 0;
        if (x.ExpenseDate == default) x.ExpenseDate = DateTime.Now;

        // gider tipi maaş ise personel zorunlu
        var et = new ExpenseTypeService().GetAll().FirstOrDefault(e => e.Id == x.ExpenseTypeId);
        if (et is { IsSalary: true } && (x.PersonnelId is null or 0)) throw new InvalidOperationException("Maaş gideri için personel seçilmeli.");

        string note = $"Gider {x.Code}" + (et is null ? "" : $" · {et.Name}") + (x.Description is null ? "" : $" · {x.Description}");
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            x.Code = CodeSeriesService.NextDocCode(c, tx, CodeSeriesType.Gider, x.BranchId, userId);
            note = $"Gider {x.Code}" + (et is null ? "" : $" · {et.Name}") + (x.Description is null ? "" : $" · {x.Description}");
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.Expenses (Code, BranchId, ExpenseTypeId, PersonnelId, ProjectId, Amount, CurrencyId, PayKind, InstrumentId, ExpenseDate, Description, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@code,@br,@et,@per,@prj,@amt,@cur,@pk,@inst,@date,@desc,@by);";
                cmd.Parameters.AddWithValue("@code", x.Code);
                cmd.Parameters.AddWithValue("@br", x.BranchId);
                cmd.Parameters.AddWithValue("@et", x.ExpenseTypeId);
                cmd.Parameters.AddWithValue("@per", (object?)x.PersonnelId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@prj", (object?)x.ProjectId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@amt", x.Amount);
                cmd.Parameters.AddWithValue("@cur", x.CurrencyId);
                cmd.Parameters.AddWithValue("@pk", (int)x.PayKind);
                cmd.Parameters.AddWithValue("@inst", x.InstrumentId);
                cmd.Parameters.AddWithValue("@date", x.ExpenseDate);
                cmd.Parameters.AddWithValue("@desc", (object?)x.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                x.Id = (int)cmd.ExecuteScalar()!;
            }

            // Kasa/Banka çıkışı — AYNI transaction (atomik)
            if (x.PayKind == ExpensePayKind.Kasa)
                CashMovementService.Post(c, tx, x.BranchId, x.InstrumentId, x.CurrencyId, CashMovementType.Cikis, x.Amount, null, null, note, userId);
            else
                BankMovementService.Post(c, tx, x.BranchId, x.InstrumentId, x.CurrencyId, BankMovementType.Cikis, x.Amount, null, null, note, userId);

            tx.Commit();
            return x.Id;
        }
        catch { tx.Rollback(); throw; }
    }

    public List<Expense> GetList(DateTime? from = null, DateTime? to = null)
    {
        var list = new List<Expense>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $@"
SELECT e.Id, e.Code, e.BranchId, e.ExpenseTypeId, e.PersonnelId, e.ProjectId, e.Amount, e.CurrencyId, e.PayKind, e.InstrumentId,
       e.ExpenseDate, e.Description, et.Name, pe.FullName
FROM dbo.Expenses e
LEFT JOIN dbo.ExpenseTypes et ON et.Id=e.ExpenseTypeId
LEFT JOIN dbo.Personnel pe ON pe.Id=e.PersonnelId
WHERE (@from IS NULL OR e.ExpenseDate >= @from) AND (@to IS NULL OR e.ExpenseDate < @to)
ORDER BY e.ExpenseDate DESC, e.Id DESC;";
        cmd.Parameters.AddWithValue("@from", (object?)from?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)to?.Date.AddDays(1) ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Expense
            {
                Id = r.GetInt32(0), Code = r.GetString(1), BranchId = r.GetInt32(2), ExpenseTypeId = r.GetInt32(3),
                PersonnelId = r.IsDBNull(4) ? null : r.GetInt32(4), ProjectId = r.IsDBNull(5) ? null : r.GetInt32(5),
                Amount = r.GetDecimal(6), CurrencyId = r.GetInt32(7), PayKind = (ExpensePayKind)r.GetInt32(8),
                InstrumentId = r.GetInt32(9), ExpenseDate = r.GetDateTime(10),
                Description = r.IsDBNull(11) ? null : r.GetString(11),
                ExpenseTypeName = r.IsDBNull(12) ? null : r.GetString(12),
                PersonnelName = r.IsDBNull(13) ? null : r.GetString(13)
            });
        return list;
    }
}
