namespace Pfm.Application.Abstractions;

/// <summary>Sends email. The one way the application reaches people outside the app.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>A plain-text email to one recipient.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);
