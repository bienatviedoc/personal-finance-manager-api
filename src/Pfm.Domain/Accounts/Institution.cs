namespace Pfm.Domain.Accounts;

/// <summary>
/// A bank or wallet provider that a user holds accounts with. Never deleted: archiving hides it
/// from pickers while it stays on the accounts that already use it.
/// </summary>
public sealed class Institution : IUserOwned
{
    public const int MaxNameLength = 100;

    /// <summary>
    /// The logos a user can pick from: generic marks the SPA draws, not brand artwork or uploads.
    /// </summary>
    public static readonly IReadOnlyList<string> LogoKeys = ["bank", "building", "wallet", "card", "piggy-bank", "coins", "smartphone"];

    /// <summary>For persistence, which sets every property itself.</summary>
    private Institution()
    {
    }

    private Institution(string name, Color? color, string? logoKey, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CreatedAt = now;
        Set(name, color, logoKey, now);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>The colour the institution is shown in, or none.</summary>
    public Color? Color { get; private set; }

    /// <summary>One of <see cref="LogoKeys"/>, or none.</summary>
    public string? LogoKey { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Institution Create(string name, Color? color, string? logoKey, DateTimeOffset now) =>
        new(name, color, logoKey, now);

    public void Edit(string name, Color? color, string? logoKey, DateTimeOffset now) => Set(name, color, logoKey, now);

    /// <summary>Hides the institution from pickers. Archiving it again changes nothing.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (IsArchived)
        {
            return;
        }

        IsArchived = true;
        UpdatedAt = now;
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxNameLength;

    public static bool IsValidLogoKey(string? logoKey) => logoKey is not null && LogoKeys.Contains(logoKey);

    private void Set(string name, Color? color, string? logoKey, DateTimeOffset now)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException($"An institution's name is required and at most {MaxNameLength} characters.", nameof(name));
        }

        if (logoKey is not null && !IsValidLogoKey(logoKey))
        {
            throw new ArgumentException("The logo is not one of the logos on offer.", nameof(logoKey));
        }

        Name = name.Trim();
        Color = color;
        LogoKey = logoKey;
        UpdatedAt = now;
    }
}
