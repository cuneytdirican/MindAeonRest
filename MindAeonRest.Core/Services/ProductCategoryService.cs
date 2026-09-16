using System.Data;
using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Ürün kategorileri (aktif şirket DB'sinde). Resim DB'de binary.</summary>
public class ProductCategoryService
{
    public List<ProductCategory> GetAll(bool includeDeleted = false)
    {
        var list = new List<ProductCategory>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        // Resim bytes'ı listede yüklenmez; yalnızca var/yok bilgisi.
        command.CommandText = @"
SELECT Id, Code, Name, IsActive, CASE WHEN DATALENGTH(Image) > 0 THEN 1 ELSE 0 END AS HasImg, IsDeleted
FROM dbo.ProductCategories WHERE (@inc = 1 OR IsDeleted = 0) ORDER BY Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new ProductCategory
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                IsActive = reader.GetBoolean(3),
                HasImage = reader.GetInt32(4) == 1,
                IsDeleted = reader.GetBoolean(5)
            });
        return list;
    }

    public ProductCategory? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, Code, Name, IsActive, CASE WHEN DATALENGTH(Image) > 0 THEN 1 ELSE 0 END AS HasImg, IsDeleted
FROM dbo.ProductCategories WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new ProductCategory
        {
            Id = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2),
            IsActive = reader.GetBoolean(3), HasImage = reader.GetInt32(4) == 1, IsDeleted = reader.GetBoolean(5)
        };
    }

    public byte[]? GetImage(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Image FROM dbo.ProductCategories WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id);
        var result = command.ExecuteScalar();
        return result is byte[] bytes ? bytes : null;
    }

    public ProductCategory Save(ProductCategory category, int? userId)
    {
        string code = CodeHelper.Normalize(category.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir kategori kodu giriniz.");
        if (string.IsNullOrWhiteSpace(category.Name)) throw new ArgumentException("Kategori adı boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.ProductCategories WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", category.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu kategori zaten var.");
        }

        if (category.Id == 0)
        {
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.ProductCategories (Code, Name, Image, IsActive, CreatedBy)
OUTPUT INSERTED.Id VALUES (@Code, @Name, @Img, @Active, @By);";
            ins.Parameters.AddWithValue("@Code", code);
            ins.Parameters.AddWithValue("@Name", category.Name.Trim());
            AddImageParam(ins, category.Image);
            ins.Parameters.AddWithValue("@Active", category.IsActive);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            category.Id = (int)ins.ExecuteScalar()!;
        }
        else
        {
            using var upd = connection.CreateCommand();
            upd.CommandText = @"
UPDATE dbo.ProductCategories SET Code=@Code, Name=@Name, Image=@Img, IsActive=@Active,
    UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id;";
            upd.Parameters.AddWithValue("@Code", code);
            upd.Parameters.AddWithValue("@Name", category.Name.Trim());
            AddImageParam(upd, category.Image);
            upd.Parameters.AddWithValue("@Active", category.IsActive);
            upd.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            upd.Parameters.AddWithValue("@Id", category.Id);
            upd.ExecuteNonQuery();
        }

        category.Code = code;
        return category;
    }

    /// <summary>Resmi VARBINARY(MAX) olarak ekler (null güvenli — AddWithValue nvarchar gönderir, hata verir).</summary>
    private static void AddImageParam(SqlCommand cmd, byte[]? image)
    {
        var p = new SqlParameter("@Img", SqlDbType.VarBinary, -1)
        {
            Value = image is { Length: > 0 } ? image : DBNull.Value
        };
        cmd.Parameters.Add(p);
    }
}

