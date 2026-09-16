using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>
/// FAZ 4c — Personel mesai (time-clock / puantaj): giriş (ClockIn) / çıkış (ClockOut), puantaj listesi,
/// labor-cost raporu (Personnel.Salary + çalışılan saat üzerinden işçilik + ciroya oran %).
/// DİKKAT: Kasa <see cref="ShiftService"/> (vardiya) ile KARIŞTIRILMAZ — bu, personelin işe geliş-gidiş kaydıdır.
/// İş mantığı saf hesabı <see cref="LaborMath"/>'te; burada yalnız DB + orkestrasyon.
/// </summary>
public class AttendanceService
{
    private const string SelectCols =
        "a.Id, a.PersonnelId, a.BranchId, a.ClockIn, a.ClockOut, a.Note, a.KioskNo, p.Code, p.FullName";

    private static readonly string SelectBase = $@"
SELECT {SelectCols}, b.Name AS BranchName
FROM dbo.Attendance a
JOIN dbo.Personnel p ON p.Id = a.PersonnelId
LEFT JOIN [{AppConfig.DatabaseName}].dbo.CompanyBranches b ON b.Id = a.BranchId";

    private static Attendance Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        PersonnelId = r.GetInt32(1),
        BranchId = r.GetInt32(2),
        ClockIn = r.GetDateTime(3),
        ClockOut = r.IsDBNull(4) ? null : r.GetDateTime(4),
        Note = r.IsDBNull(5) ? null : r.GetString(5),
        KioskNo = r.GetInt32(6),
        PersonnelCode = r.IsDBNull(7) ? null : r.GetString(7),
        PersonnelName = r.IsDBNull(8) ? null : r.GetString(8),
        BranchName = r.IsDBNull(9) ? null : r.GetString(9)
    };

    /// <summary>Personelin AÇIK (çıkış yapılmamış) mesai kaydını döndürür (yoksa null).</summary>
    public Attendance? GetOpen(int personnelId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE a.PersonnelId=@p AND a.ClockOut IS NULL AND a.IsDeleted=0 ORDER BY a.Id DESC;";
        cmd.Parameters.AddWithValue("@p", personnelId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    /// <summary>
    /// Mesai girişi: personel için açık mesai zaten varsa hata (önce çıkış yapılmalı).
    /// KioskNo terminal damgası; branchId bağlamdan gelir.
    /// </summary>
    public Attendance ClockIn(int personnelId, int branchId, int kioskNo, string? note, int? userId)
    {
        if (personnelId <= 0) throw new ArgumentException("Personel seçiniz.");
        using var c = Database.OpenCompany();
        using var tx = c.BeginTransaction();
        try
        {
            using (var chk = c.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = "SELECT COUNT(1) FROM dbo.Attendance WHERE PersonnelId=@p AND ClockOut IS NULL AND IsDeleted=0;";
                chk.Parameters.AddWithValue("@p", personnelId);
                if ((int)chk.ExecuteScalar()! > 0)
                    throw new InvalidOperationException("Bu personelin açık (çıkışsız) mesaisi var. Önce çıkış yapın.");
            }

            int id;
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.Attendance (PersonnelId, BranchId, ClockIn, Note, KioskNo, CreatedBy)
OUTPUT INSERTED.Id VALUES (@p,@b,SYSUTCDATETIME(),@n,@k,@by);";
                cmd.Parameters.AddWithValue("@p", personnelId);
                cmd.Parameters.AddWithValue("@b", branchId);
                cmd.Parameters.AddWithValue("@n", (object?)note ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@k", kioskNo);
                cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
                id = (int)cmd.ExecuteScalar()!;
            }
            tx.Commit();
            return GetById(id) ?? throw new InvalidOperationException("Mesai kaydı okunamadı.");
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>Mesai çıkışı: personelin açık mesaisini kapatır. Açık mesai yoksa hata.</summary>
    public Attendance ClockOut(int personnelId, string? note, int? userId)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
UPDATE dbo.Attendance
SET ClockOut = SYSUTCDATETIME(),
    Note = CASE WHEN @n IS NULL THEN Note ELSE @n END,
    UpdatedBy=@by, UpdatedAt=SYSUTCDATETIME()
OUTPUT INSERTED.Id
WHERE PersonnelId=@p AND ClockOut IS NULL AND IsDeleted=0;";
        cmd.Parameters.AddWithValue("@p", personnelId);
        cmd.Parameters.AddWithValue("@n", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@by", (object?)userId ?? DBNull.Value);
        var res = cmd.ExecuteScalar();
        if (res is null) throw new InvalidOperationException("Açık mesai bulunamadı.");
        return GetById((int)res) ?? throw new InvalidOperationException("Mesai kaydı okunamadı.");
    }

    public Attendance? GetById(int id)
    {
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + " WHERE a.Id=@id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    /// <summary>
    /// Puantaj (timesheet): şube + tarih aralığı (yerel gün) + (opsiyonel) personel filtreli mesai kayıtları.
    /// Giriş tarihine göre süzülür; en yeni önce.
    /// </summary>
    public List<Attendance> GetTimesheet(int branchId, DateTime from, DateTime to, int? personnelId = null)
    {
        var list = new List<Attendance>();
        using var c = Database.OpenCompany();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectBase + @"
WHERE a.IsDeleted=0 AND a.BranchId=@b
  AND a.ClockIn >= @from AND a.ClockIn < @to
  AND (@pid IS NULL OR a.PersonnelId=@pid)
ORDER BY a.ClockIn DESC;";
        cmd.Parameters.AddWithValue("@b", branchId);
        cmd.Parameters.AddWithValue("@from", from.Date);
        cmd.Parameters.AddWithValue("@to", to.Date.AddDays(1));
        cmd.Parameters.AddWithValue("@pid", (object?)personnelId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    /// <summary>
    /// Labor-cost raporu: dönemdeki KAPANMIŞ mesailerden personel bazında toplam saat + işçilik maliyeti
    /// (Personnel.Salary'den <see cref="LaborMath.HourlyRate"/> ile türetilir) + dönem cirosu (ClFiches.Total)
    /// üzerinden labor-cost %. Açık (çıkışsız) mesailer hesaba KATILMAZ (saat kesinleşmemiş).
    /// </summary>
    public LaborCostReport GetLaborCost(int branchId, DateTime from, DateTime to, decimal? standardMonthlyHours = null)
    {
        decimal stdHours = standardMonthlyHours is > 0 ? standardMonthlyHours.Value : LaborMath.DefaultMonthlyHours;
        var rep = new LaborCostReport
        {
            BranchId = branchId,
            From = from.Date,
            To = to.Date,
            StandardMonthlyHours = stdHours
        };

        using var c = Database.OpenCompany();

        // Personel bazında toplam saat (kapanmış mesai) + maaş. Saat hesabı DB'de DATEDIFF ile (SECOND → saat).
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT a.PersonnelId, p.Code, p.FullName, p.Salary,
       COUNT(1) AS Shifts,
       SUM(CAST(DATEDIFF(SECOND, a.ClockIn, a.ClockOut) AS BIGINT)) AS TotalSeconds
FROM dbo.Attendance a
JOIN dbo.Personnel p ON p.Id = a.PersonnelId
WHERE a.IsDeleted=0 AND a.BranchId=@b AND a.ClockOut IS NOT NULL
  AND a.ClockIn >= @from AND a.ClockIn < @to
GROUP BY a.PersonnelId, p.Code, p.FullName, p.Salary
ORDER BY p.FullName;";
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@from", from.Date);
            cmd.Parameters.AddWithValue("@to", to.Date.AddDays(1));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal salary = r.GetDecimal(3);
                long seconds = r.IsDBNull(5) ? 0 : r.GetInt64(5);
                decimal hours = decimal.Round((decimal)seconds / 3600m, 2);
                decimal rate = LaborMath.HourlyRate(salary, stdHours);
                rep.Rows.Add(new LaborCostRow
                {
                    PersonnelId = r.GetInt32(0),
                    PersonnelCode = r.IsDBNull(1) ? "" : r.GetString(1),
                    PersonnelName = r.IsDBNull(2) ? "" : r.GetString(2),
                    Salary = salary,
                    Shifts = r.GetInt32(4),
                    Hours = hours,
                    HourlyRate = rate,
                    LaborCost = LaborMath.LaborCost(rate, hours)
                });
            }
        }

        // Dönem cirosu (labor-cost % için): ClFiches.Total (şube + tarih aralığı).
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"
SELECT ISNULL(SUM(Total),0) FROM dbo.ClFiches
WHERE IsDeleted=0 AND BranchId=@b AND FicheDate >= @from AND FicheDate < @to;";
            cmd.Parameters.AddWithValue("@b", branchId);
            cmd.Parameters.AddWithValue("@from", from.Date);
            cmd.Parameters.AddWithValue("@to", to.Date.AddDays(1));
            rep.Revenue = (decimal)cmd.ExecuteScalar()!;
        }

        return rep;
    }
}
