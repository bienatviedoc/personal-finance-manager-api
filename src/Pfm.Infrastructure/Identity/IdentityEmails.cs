using Microsoft.Extensions.Options;
using Pfm.Application.Abstractions;

namespace Pfm.Infrastructure.Identity;

/// <summary>The emails sent about a user's sign-in. English until users can choose a language.</summary>
public sealed class IdentityEmails(IEmailSender sender, IOptions<AppOptions> app)
{
    public Task SendVerificationAsync(User user, string token, CancellationToken cancellationToken)
    {
        var link = $"{app.Value.PublicUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        return sender.SendAsync(
            new EmailMessage(
                user.Email!,
                "Confirm your email address",
                $"""
                Welcome to Personal Finance Manager.

                Confirm your email address to finish registering:

                {link}

                If you did not register, you can ignore this email.
                """),
            cancellationToken);
    }

    /// <summary>Sent when someone registers with an email that already has a user.</summary>
    public Task SendAlreadyRegisteredAsync(User user, CancellationToken cancellationToken) =>
        sender.SendAsync(
            new EmailMessage(
                user.Email!,
                "Someone tried to register with your email address",
                $"""
                Someone tried to register with this email address, but it already has a user.

                If it was you, sign in instead:

                {app.Value.PublicUrl}/login

                If you have forgotten your password, you can reset it:

                {app.Value.PublicUrl}/reset

                If it was not you, you can ignore this email. Nothing has changed.
                """),
            cancellationToken);
}
