using Microsoft.Data.SqlClient;
using MindAeonRest.Data;
using MindAeonRest.Helpers;
using MindAeonRest.Models;

namespace MindAeonRest.Services;

/// <summary>Cariler ve adresleri (şirket DB).</summary>
public class CustomerService
{
    private const string SelectBase = @"
SELECT c.Id, c.Code, c.Name, c.CustomerGroupId, c.Address, c.CountryId, c.CityId, c.DistrictId,
       c.TaxOffice, c.TaxNumber, c.Phone, c.EInvoiceType, c.IsActive, g.Name AS GroupName, c.IsDeleted, c.CustomerType,
       c.PriceListId, pl.Name AS PriceListName
FROM dbo.Customers c
LEFT JOIN dbo.CustomerGroups g ON g.Id = c.CustomerGroupId
LEFT JOIN dbo.PriceLists pl ON pl.Id = c.PriceListId";

    public List<Customer> GetAll(bool includeDeleted = false)
    {
        var list = new List<Customer>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE (@inc = 1 OR c.IsDeleted = 0) ORDER BY c.Code;";
        command.Parameters.AddWithValue("@inc", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public Customer? GetById(int id)
    {
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = SelectBase + " WHERE c.Id=@Id AND c.IsDeleted=0;";
        command.Parameters.AddWithValue("@Id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public List<CustomerAddress> GetAddresses(int customerId)
    {
        var list = new List<CustomerAddress>();
        using var connection = Database.OpenCompany();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, CustomerId, Title, Address, CountryId, CityId, DistrictId, Phone, IsDefault
FROM dbo.CustomerAddresses WHERE CustomerId=@c AND IsDeleted=0 ORDER BY IsDefault DESC, Id;";
        command.Parameters.AddWithValue("@c", customerId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new CustomerAddress
            {
                Id = reader.GetInt32(0),
                CustomerId = reader.GetInt32(1),
                Title = reader.IsDBNull(2) ? null : reader.GetString(2),
                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                CountryId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                CityId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                DistrictId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Phone = reader.IsDBNull(7) ? null : reader.GetString(7),
                IsDefault = reader.GetBoolean(8)
            });
        return list;
    }

    public Customer Save(Customer c, IEnumerable<CustomerAddress> addresses, int? userId)
    {
        string code = CodeHelper.Normalize(c.Code);
        if (code.Length == 0) throw new ArgumentException("Geçerli bir cari kodu giriniz.");
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Cari adı boş olamaz.");

        using var connection = Database.OpenCompany();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM dbo.Customers WHERE Code=@c AND Id<>@id;";
            check.Parameters.AddWithValue("@c", code);
            check.Parameters.AddWithValue("@id", c.Id);
            if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"'{code}' kodlu cari zaten var.");
        }

        using (var command = connection.CreateCommand())
        {
            if (c.Id == 0)
            {
                command.CommandText = @"
INSERT INTO dbo.Customers (Code, Name, CustomerGroupId, Address, CountryId, CityId, DistrictId, TaxOffice, TaxNumber, Phone, EInvoiceType, CustomerType, PriceListId, IsActive, CreatedBy)
OUTPUT INSERTED.Id VALUES (@Code,@Name,@Grp,@Addr,@Co,@Ci,@Di,@TO,@TN,@Ph,@Ein,@CType,@PL,@Active,@By);";
            }
            else
            {
                command.CommandText = @"
UPDATE dbo.Customers SET Code=@Code, Name=@Name, CustomerGroupId=@Grp, Address=@Addr, CountryId=@Co, CityId=@Ci, DistrictId=@Di,
    TaxOffice=@TO, TaxNumber=@TN, Phone=@Ph, EInvoiceType=@Ein, CustomerType=@CType, PriceListId=@PL, IsActive=@Active, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id;";
                command.Parameters.AddWithValue("@Id", c.Id);
            }
            command.Parameters.AddWithValue("@Code", code);
            command.Parameters.AddWithValue("@Name", c.Name.Trim());
            command.Parameters.AddWithValue("@Grp", (object?)c.CustomerGroupId ?? DBNull.Value);
            CompanyService.AddNullable(command, "@Addr", c.Address);
            command.Parameters.AddWithValue("@Co", (object?)c.CountryId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Ci", (object?)c.CityId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Di", (object?)c.DistrictId ?? DBNull.Value);
            CompanyService.AddNullable(command, "@TO", c.TaxOffice);
            CompanyService.AddNullable(command, "@TN", c.TaxNumber);
            CompanyService.AddNullable(command, "@Ph", c.Phone);
            command.Parameters.AddWithValue("@Ein", (int)c.EInvoiceType);
            command.Parameters.AddWithValue("@CType", (int)c.CustomerType);
            command.Parameters.AddWithValue("@PL", (object?)c.PriceListId ?? DBNull.Value);
            command.Parameters.AddWithValue("@Active", c.IsActive);
            command.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);

            if (c.Id == 0) c.Id = (int)command.ExecuteScalar()!;
            else command.ExecuteNonQuery();
        }

        SaveAddresses(connection, c.Id, addresses.ToList(), userId);
        c.Code = code;
        return c;
    }

    private static void SaveAddresses(SqlConnection connection, int customerId, List<CustomerAddress> addresses, int? userId)
    {
        using (var del = connection.CreateCommand())
        {
            del.CommandText = "UPDATE dbo.CustomerAddresses SET IsDeleted=1, UpdatedBy=@By, UpdatedAt=SYSUTCDATETIME() WHERE CustomerId=@c AND IsDeleted=0;";
            del.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            del.Parameters.AddWithValue("@c", customerId);
            del.ExecuteNonQuery();
        }
        if (addresses.Count == 0) return;
        if (!addresses.Any(a => a.IsDefault)) addresses[0].IsDefault = true;
        bool defSeen = false;
        foreach (var a in addresses)
        {
            bool isDef = a.IsDefault && !defSeen;
            if (isDef) defSeen = true;
            using var ins = connection.CreateCommand();
            ins.CommandText = @"
INSERT INTO dbo.CustomerAddresses (CustomerId, Title, Address, CountryId, CityId, DistrictId, Phone, IsDefault, CreatedBy)
VALUES (@c,@t,@a,@co,@ci,@di,@ph,@def,@By);";
            ins.Parameters.AddWithValue("@c", customerId);
            CompanyService.AddNullable(ins, "@t", a.Title);
            CompanyService.AddNullable(ins, "@a", a.Address);
            ins.Parameters.AddWithValue("@co", (object?)a.CountryId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@ci", (object?)a.CityId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@di", (object?)a.DistrictId ?? DBNull.Value);
            CompanyService.AddNullable(ins, "@ph", a.Phone);
            ins.Parameters.AddWithValue("@def", isDef);
            ins.Parameters.AddWithValue("@By", (object?)userId ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
    }

    private static Customer Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Code = r.GetString(1),
        Name = r.GetString(2),
        CustomerGroupId = r.IsDBNull(3) ? null : r.GetInt32(3),
        Address = r.IsDBNull(4) ? null : r.GetString(4),
        CountryId = r.IsDBNull(5) ? null : r.GetInt32(5),
        CityId = r.IsDBNull(6) ? null : r.GetInt32(6),
        DistrictId = r.IsDBNull(7) ? null : r.GetInt32(7),
        TaxOffice = r.IsDBNull(8) ? null : r.GetString(8),
        TaxNumber = r.IsDBNull(9) ? null : r.GetString(9),
        Phone = r.IsDBNull(10) ? null : r.GetString(10),
        EInvoiceType = (EInvoiceType)r.GetInt32(11),
        IsActive = r.GetBoolean(12),
        CustomerGroupName = r.IsDBNull(13) ? null : r.GetString(13),
        IsDeleted = r.GetBoolean(14),
        CustomerType = (CustomerType)r.GetInt32(15),
        PriceListId = r.IsDBNull(16) ? null : r.GetInt32(16),
        PriceListName = r.IsDBNull(17) ? null : r.GetString(17)
    };
}
