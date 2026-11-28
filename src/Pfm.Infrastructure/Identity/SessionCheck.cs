using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Pfm.Infrastructure.Identity;

/// <summary>
/// Ends a session on its next request unless its user is still there, active, with the security stamp the cookie
/// was issued with: so it ends once the user is deactivated or gone, or their security
/// stamp has changed since the cookie was issued (deleting the user, a new password, two-factor turned on or off).
/// The host that signs people in runs it on every request that brings a session cookie (the API's
/// <c>AuthenticationSetup</c>).
/// </summary>
/// <remarks>
/// It costs one read of the user by primary key. Identity's user store finds by id through the request's
/// <c>DbContext</c>, so a use case that then loads the signed-in user (most of the Identity ones) gets it without a
/// second query. There is no cache in front of it on purpose: every API instance shares the database, so a user
/// deleted through one instance is refused by all of them at once, which a cache local to each could not promise.
/// </remarks>
public static class SessionCheck
{
    public static async Task EndUnlessUserActiveWithSameStampAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { } principal)
        {
            return;
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<User>>();
        var stamp = principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType);
        if (await users.GetUserAsync(principal) is { DeactivatedAt: null } user && stamp is not null && stamp == user.SecurityStamp)
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
