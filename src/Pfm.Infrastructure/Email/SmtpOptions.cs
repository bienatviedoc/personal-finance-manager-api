namespace Pfm.Infrastructure.Email;

/// <summary>Settings under "Smtp". Locally they point at the Mailpit catcher from compose.yaml.</summary>
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    /// <summary>Require STARTTLS. Off only for a local catcher, and never with credentials.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = "Personal Finance Manager <no-reply@localhost>";
}
