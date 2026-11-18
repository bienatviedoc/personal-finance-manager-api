namespace Pfm.Infrastructure.Email;

/// <summary>Settings under "Smtp". Locally they point at the Mailpit catcher from compose.yaml.</summary>
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    /// <summary>Upgrade to TLS when the server offers it. Off only for a local catcher.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = "Personal Finance Manager <no-reply@localhost>";
}
