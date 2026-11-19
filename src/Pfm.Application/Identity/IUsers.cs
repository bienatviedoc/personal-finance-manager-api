using Pfm.Domain;

namespace Pfm.Application.Identity;

/// <summary>
/// The users who sign in, and their credentials. Implemented in Infrastructure over ASP.NET Core
/// Identity, which owns password hashing, tokens and the user store.
/// </summary>
public interface IUsers
{
    Task<UserDetails?> FindByEmailAsync(string email);

    Task<UserDetails?> FindByIdAsync(Guid userId);

    /// <summary>Creates a user with an unconfirmed email address and the default profile settings, but this locale.</summary>
    Task<NewUser> CreateAsync(string email, string password, Locale? locale);

    Task<bool> CheckPasswordAsync(Guid userId, string password);

    /// <summary>
    /// Hashes a password and throws the hash away. Costs what checking or setting a password costs,
    /// so an answer that does neither takes as long as one that does, and timing reveals nothing.
    /// </summary>
    void HashAndDiscard(string password);

    Task<string> CreateEmailConfirmationTokenAsync(Guid userId);

    /// <summary>False when there is no such user or the token is invalid or expired.</summary>
    Task<bool> ConfirmEmailAsync(Guid userId, string token);

    /// <summary>A token that lets whoever holds it set the user's password once, until it expires.</summary>
    Task<string> CreatePasswordResetTokenAsync(Guid userId);

    /// <summary>
    /// Sets a password the policy accepts, and ends every session the user has. False when there is no
    /// such user or the token is invalid, already used or expired.
    /// </summary>
    Task<bool> ResetPasswordAsync(Guid userId, string token, string newPassword);
}

/// <summary>What the sign-in use cases need to know about a user.</summary>
public sealed record UserDetails(Guid Id, string Email, bool EmailConfirmed);

/// <summary>
/// The result of creating a user: the user, or why not. <see cref="EmailTaken"/> is set when another
/// user has the email address, even one registered a moment ago by a concurrent request.
/// </summary>
public sealed record NewUser(UserDetails? User, bool EmailTaken, IReadOnlyList<string> Errors);
