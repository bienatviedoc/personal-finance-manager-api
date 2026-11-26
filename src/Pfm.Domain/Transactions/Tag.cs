namespace Pfm.Domain.Transactions;

/// <summary>
/// A free-form label a user puts on transactions, across categories: <c>tet-2027</c>, <c>japan-trip</c> (spec
/// TXN-5). A tag is the user's own and is created the first time they use it. Its name is kept trimmed and in
/// lower case, so <c>Japan-Trip</c> and <c>japan-trip</c> are one tag, and it never changes.
/// </summary>
public sealed class Tag : IUserOwned
{
    public const int MaxNameLength = 50;

    /// <summary>For persistence, which sets every property itself.</summary>
    private Tag()
    {
        Name = "";
    }

    private Tag(string name, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        Name = name;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Trimmed and in lower case; one of the user's tags has it.</summary>
    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A tag's name as it is kept: trimmed and in lower case; null when nothing is left.</summary>
    public static string? Normalised(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim().ToLowerInvariant();

    /// <summary>Whether the name makes a tag: 1 to <see cref="MaxNameLength"/> characters once trimmed.</summary>
    public static bool IsValidName(string? name) => Normalised(name) is { Length: <= MaxNameLength };

    /// <summary>A new tag of the user's, named as <see cref="Normalised"/> keeps it.</summary>
    public static Tag Create(string name, DateTimeOffset now)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException($"A tag's name is 1 to {MaxNameLength} characters.", nameof(name));
        }

        return new Tag(Normalised(name)!, now);
    }
}
