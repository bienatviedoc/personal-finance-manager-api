using Microsoft.AspNetCore.Identity;
using Pfm.Application.Identity;

namespace Pfm.Infrastructure.Identity;

/// <summary>
/// Two-factor sign-in over ASP.NET Core Identity: its authenticator (TOTP) token provider checks codes, and its
/// user store keeps the key and the recovery codes. Turning it on or off, or making a new key, changes the user's
/// security stamp, which ends their sessions.
/// </summary>
internal sealed class IdentityTwoFactor(UserManager<User> users, IUserStore<User> store) : ITwoFactor
{
    public const int RecoveryCodeCount = 10;

    public async Task<TwoFactorDetails?> FindAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) is { } user
            ? new TwoFactorDetails(user.TwoFactorEnabled, await users.CountRecoveryCodesAsync(user))
            : null;

    public async Task<string> AuthenticatorKeyAsync(Guid userId)
    {
        var user = await RequireAsync(userId);
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrEmpty(key))
        {
            return key;
        }

        // Straight to the store: the user manager's own reset also changes the security stamp, and a key nobody
        // has confirmed yet is no reason to end the user's sessions.
        key = users.GenerateNewAuthenticatorKey();
        await ((IUserAuthenticatorKeyStore<User>)store).SetAuthenticatorKeyAsync(user, key, CancellationToken.None);
        Succeeded(await users.UpdateAsync(user));
        return key;
    }

    public async Task<bool> CheckCodeAsync(Guid userId, string code) =>
        await users.FindByIdAsync(userId.ToString()) is { } user
        && await users.GetAuthenticatorKeyAsync(user) is { Length: > 0 }
        && await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code);

    public async Task<IReadOnlyList<string>> TurnOnAsync(Guid userId)
    {
        var user = await RequireAsync(userId);
        Succeeded(await users.SetTwoFactorEnabledAsync(user, true));
        return await NewRecoveryCodesAsync(user, RecoveryCodeCount);
    }

    public async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(Guid userId) =>
        await NewRecoveryCodesAsync(await RequireAsync(userId), RecoveryCodeCount);

    public async Task TurnOffAsync(Guid userId)
    {
        var user = await RequireAsync(userId);
        Succeeded(await users.SetTwoFactorEnabledAsync(user, false));
        Succeeded(await users.ResetAuthenticatorKeyAsync(user));
        await NewRecoveryCodesAsync(user, 0);
    }

    public async Task<bool> RedeemRecoveryCodeAsync(Guid userId, string recoveryCode) =>
        await users.FindByIdAsync(userId.ToString()) is { } user
        && (await users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode)).Succeeded;

    private async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(User user, int count) =>
        [.. await users.GenerateNewTwoFactorRecoveryCodesAsync(user, count) ?? []];

    private async Task<User> RequireAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException($"User {userId} does not exist.");

    private static void Succeeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Changing two-factor sign-in failed: {string.Join(", ", result.Errors.Select(error => error.Code))}");
        }
    }
}
