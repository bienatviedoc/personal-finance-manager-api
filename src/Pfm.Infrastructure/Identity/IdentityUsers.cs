using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pfm.Application.Identity;
using Pfm.Domain;

namespace Pfm.Infrastructure.Identity;

/// <summary>The users who sign in, over ASP.NET Core Identity's user manager.</summary>
internal sealed class IdentityUsers(UserManager<User> users) : IUsers
{
    public async Task<UserDetails?> FindByEmailAsync(string email) => Details(await users.FindByEmailAsync(email));

    // Identity's store finds by id through the DbContext, so a user loaded earlier in the request costs no query.
    public async Task<UserDetails?> FindByIdAsync(Guid userId) => Details(await users.FindByIdAsync(userId.ToString()));

    public async Task<NewUser> CreateAsync(string email, string password, Locale? locale)
    {
        var user = new User { UserName = email, Email = email, Locale = locale };
        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request registered this email a moment ago.
            created = IdentityResult.Failed(users.ErrorDescriber.DuplicateEmail(email));
        }

        return created.Succeeded
            ? new NewUser(Details(user), EmailTaken: false, Errors: [])
            : new NewUser(
                User: null,
                EmailTaken: created.Errors.Any(error => error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)),
                Errors: [.. created.Errors.Select(error => error.Description)]);
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password) =>
        await users.FindByIdAsync(userId.ToString()) is { } user && await users.CheckPasswordAsync(user, password);

    // The hasher does not depend on which user the password is for.
    public void HashAndDiscard(string password) => users.PasswordHasher.HashPassword(new User(), password);

    public async Task<string> CreateEmailConfirmationTokenAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} does not exist.");
        return await users.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<bool> ConfirmEmailAsync(Guid userId, string token) =>
        await users.FindByIdAsync(userId.ToString()) is { } user && (await users.ConfirmEmailAsync(user, token)).Succeeded;

    public async Task<string> CreatePasswordResetTokenAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} does not exist.");
        return await users.GeneratePasswordResetTokenAsync(user);
    }

    // Resetting changes the security stamp, which the token is bound to: that makes it work once, and ends every session.
    public async Task<bool> ResetPasswordAsync(Guid userId, string token, string newPassword)
    {
        if (await users.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return false;
        }

        var reset = await users.ResetPasswordAsync(user, token, newPassword);
        if (reset.Succeeded)
        {
            await ClearFailedSignInsAsync(user);
            return true;
        }

        // The use case checks the password first, so the token is the only thing left to refuse.
        return reset.Errors.All(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken))
            ? false
            : throw new InvalidOperationException($"Resetting the password failed: {string.Join(", ", reset.Errors.Select(error => error.Code))}");
    }

    // Changing the password changes the security stamp, which ends every session.
    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        if (await users.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return false;
        }

        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (changed.Succeeded)
        {
            return true;
        }

        // The use case checks the new password first, so the current one is the only thing left to refuse.
        return changed.Errors.All(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch))
            ? false
            : throw new InvalidOperationException($"Changing the password failed: {string.Join(", ", changed.Errors.Select(error => error.Code))}");
    }

    // Identity counts the failures and starts the lock when they reach the limit set in AddUsers.
    public async Task RecordFailedSignInAsync(Guid userId)
    {
        if (await users.FindByIdAsync(userId.ToString()) is { } user)
        {
            await users.AccessFailedAsync(user);
        }
    }

    public async Task<bool> IsLockedOutAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()) is { } user && await users.IsLockedOutAsync(user);

    public async Task ClearFailedSignInsAsync(Guid userId)
    {
        if (await users.FindByIdAsync(userId.ToString()) is { } user)
        {
            await ClearFailedSignInsAsync(user);
        }
    }

    private async Task ClearFailedSignInsAsync(User user)
    {
        if (user.AccessFailedCount > 0)
        {
            await users.ResetAccessFailedCountAsync(user);
        }

        if (user.LockoutEnd is not null)
        {
            await users.SetLockoutEndDateAsync(user, null);
        }
    }

    private static UserDetails? Details(User? user) =>
        user is null ? null : new UserDetails(user.Id, user.Email!, user.EmailConfirmed, user.Locale, user.TwoFactorEnabled);
}

/// <summary>Identity's password validators, configured in <see cref="InfrastructureServices.AddUsers"/>.</summary>
internal sealed class IdentityPasswordPolicy(UserManager<User> users) : IPasswordPolicy
{
    public async Task<IReadOnlyList<string>> CheckAsync(string email, string password)
    {
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new User { Email = email }, password);
            problems.AddRange(result.Errors.Select(error => error.Description));
        }

        return problems;
    }
}
