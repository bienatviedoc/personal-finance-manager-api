using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pfm.Application.Identity;

namespace Pfm.Infrastructure.Identity;

/// <summary>The users who sign in, over ASP.NET Core Identity's user manager.</summary>
internal sealed class IdentityUsers(UserManager<User> users) : IUsers
{
    public async Task<UserDetails?> FindByEmailAsync(string email) => Details(await users.FindByEmailAsync(email));

    // Identity's store finds by id through the DbContext, so a user loaded earlier in the request costs no query.
    public async Task<UserDetails?> FindByIdAsync(Guid userId) => Details(await users.FindByIdAsync(userId.ToString()));

    public async Task<NewUser> CreateAsync(string email, string password)
    {
        var user = new User { UserName = email, Email = email };
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

    private static UserDetails? Details(User? user) => user is null ? null : new UserDetails(user.Id, user.Email!, user.EmailConfirmed);
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
