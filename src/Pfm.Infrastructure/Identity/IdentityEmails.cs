using Microsoft.Extensions.Options;
using Pfm.Application.Abstractions;
using Pfm.Application.Identity;

namespace Pfm.Infrastructure.Identity;

/// <summary>The emails sent about a user's sign-in. English until users can choose a language.</summary>
internal sealed class IdentityEmails(IEmailSender sender, IOptions<AppOptions> app) : IIdentityEmails
{
    public Task SendVerificationAsync(UserDetails user, string token, CancellationToken cancellationToken)
    {
        var link = $"{app.Value.PublicUrl}/verify-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        return sender.SendAsync(
            new EmailMessage(
                user.Email,
                "Confirm your email address",
                $"""
                Welcome to Personal Finance Manager.

                Confirm your email address to finish registering:

                {link}

                If you did not register, you can ignore this email.
                """),
            cancellationToken);
    }

    public Task SendAlreadyRegisteredAsync(UserDetails user, CancellationToken cancellationToken) =>
        sender.SendAsync(
            new EmailMessage(
                user.Email,
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
