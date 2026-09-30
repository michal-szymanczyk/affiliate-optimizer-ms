using System.Security.Claims;
using AffiliateOptimizer.Domain.Common;

namespace AffiliateOptimizer.Web.Infrastructure.Identity;

/// <summary>The signed-in operator, read from the cookie principal (ArchitectureSimple.md §8.2).</summary>
public sealed class UserContext(IHttpContextAccessor accessor) : IUserContext
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public int UserId => int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string Email => User?.FindFirstValue(ClaimTypes.Email) ?? User?.Identity?.Name ?? string.Empty;
}
