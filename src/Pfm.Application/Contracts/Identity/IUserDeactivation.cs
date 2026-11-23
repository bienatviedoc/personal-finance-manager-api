namespace Pfm.Application.Contracts.Identity;

/// <summary>
/// How other modules learn that a user is a deactivated user: one who asked to be deleted and is within the 30
/// days before erasure (see the glossary). A job leaves such a user's data as it is.
/// </summary>
public interface IUserDeactivation
{
    /// <summary>Whether the user is deactivated. A user there is no longer is not: there is nothing of theirs to leave alone.</summary>
    Task<bool> IsDeactivatedAsync(Guid userId, CancellationToken cancellationToken);
}
