namespace Pfm.Application.Identity;

/// <summary>The emails sent about a user's sign-in.</summary>
public interface IIdentityEmails
{
    /// <summary>The link that confirms the user's email address.</summary>
    Task SendVerificationAsync(UserDetails user, string token, CancellationToken cancellationToken);

    /// <summary>Sent when someone registers with an email that already has a user.</summary>
    Task SendAlreadyRegisteredAsync(UserDetails user, CancellationToken cancellationToken);
}
