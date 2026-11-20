namespace Pfm.Application.Identity;

/// <summary>The emails sent about a user's sign-in, each in the user's locale.</summary>
public interface IIdentityEmails
{
    /// <summary>The link that confirms the user's email address.</summary>
    Task SendVerificationAsync(UserDetails user, string token, CancellationToken cancellationToken);

    /// <summary>Sent when someone registers with an email that already has a user.</summary>
    Task SendAlreadyRegisteredAsync(UserDetails user, CancellationToken cancellationToken);

    /// <summary>The link that lets a user who has forgotten their password set a new one.</summary>
    Task SendPasswordResetAsync(UserDetails user, string token, CancellationToken cancellationToken);

    /// <summary>Sent when someone asks to reset the password of an email address that has no user.</summary>
    Task SendNoUserToResetAsync(string email, CancellationToken cancellationToken);
}
