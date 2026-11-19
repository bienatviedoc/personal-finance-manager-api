using Microsoft.AspNetCore.Identity;
using Pfm.Application.Identity;

namespace Pfm.Infrastructure.Identity;

/// <summary>
/// Sessions as the cookie Identity's sign-in manager issues. The host that signs people in configures the cookie
/// itself (the API's <c>AuthenticationSetup</c>) and registers this through <see cref="InfrastructureServices.AddSignIn"/>.
/// </summary>
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
