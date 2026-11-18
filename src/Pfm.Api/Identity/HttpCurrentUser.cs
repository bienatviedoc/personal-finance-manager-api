using System.Security.Claims;
using Pfm.Application.Abstractions;

namespace Pfm.Api.Identity;

/// <summary>The user signed in on the current request.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
