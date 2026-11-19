namespace Pfm.Application.Identity;

/// <summary>What makes a password acceptable. Implemented in Infrastructure by Identity's password validators.</summary>
public interface IPasswordPolicy
{
    /// <summary>Why the password is not acceptable for a user with this email address; empty when it is.</summary>
    Task<IReadOnlyList<string>> CheckAsync(string email, string password);
}
