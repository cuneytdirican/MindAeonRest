using System.Security.Claims;

namespace MindAeonRest.Licence.Portal.Models;

/// <summary>
/// Cookie içindeki JWT claim'lerinden bayi/işletme kimliği okumak için küçük yardımcı.
/// (Login sırasında "dealer"/"business" claim'leri cookie'ye taşınır.)
/// </summary>
public static class ClaimsHelper
{
    public static int? DealerId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("dealer")?.Value, out var d) ? d : null;

    public static int? BusinessId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("business")?.Value, out var b) ? b : null;
}
