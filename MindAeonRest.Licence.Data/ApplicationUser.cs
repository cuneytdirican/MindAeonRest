using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MindAeonRest.Licence.Data;

/// <summary>Portal kullanıcısı (Admin/Bayi/Müşteri). Identity tabanlı.</summary>
public class ApplicationUser : IdentityUser
{
    [MaxLength(150)] public string? FullName { get; set; }
    public UserKind Kind { get; set; } = UserKind.Customer;
    public int? DealerId { get; set; }      // Kind=Dealer ise
    public int? BusinessId { get; set; }    // Kind=Customer ise
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
