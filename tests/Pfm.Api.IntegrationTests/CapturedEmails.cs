using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Pfm.Application.Abstractions;

namespace Pfm.Api.IntegrationTests;

/// <summary>Stands in for email delivery, the API's boundary with the outside world.</summary>
public sealed partial class CapturedEmails : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address) =>
        [.. _sent.Where(message => string.Equals(message.To, address, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The links in an email's body.</summary>
    public static IReadOnlyList<Uri> Links(EmailMessage message) =>
        [.. LinkPattern().Matches(message.Body).Select(match => new Uri(match.Value))];

    [GeneratedRegex(@"https://\S+")]
    private static partial Regex LinkPattern();
}
