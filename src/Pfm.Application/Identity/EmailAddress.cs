using System.Net.Mail;

namespace Pfm.Application.Identity;

/// <summary>The rules for the email address a user signs in with.</summary>
public static class EmailAddress
{
    /// <summary>An address with surrounding spaces is the same address.</summary>
    public static string Normalize(string? email) => email?.Trim() ?? "";

    /// <summary>A bare address (<c>someone@example.com</c>), not a named one (<c>Someone &lt;someone@example.com&gt;</c>).</summary>
    public static bool IsValid(string email) =>
        MailAddress.TryCreate(email, out var address) && address.Address == email && address.DisplayName.Length == 0;
}
