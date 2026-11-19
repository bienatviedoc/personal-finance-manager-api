using Microsoft.AspNetCore.Identity;
using Pfm.Application.Identity;
using Pfm.Infrastructure.Identity;

namespace Pfm.Api.Identity;

/// <summary>Sessions as the HttpOnly cookie Identity's sign-in manager issues (see <see cref="AuthenticationSetup"/>).</summary>
internal sealed class CookieSessions(UserManager<User> users, SignInManager<User> signIn) : ISessions
{
    public async Task SignInAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} does not exist.");
        await signIn.SignInAsync(user, isPersistent: true);
    }

    public Task SignOutAsync() => signIn.SignOutAsync();
}
