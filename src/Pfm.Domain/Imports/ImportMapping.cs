namespace Pfm.Domain.Imports;

/// <summary>
/// The mapping saved for an account (spec IMP-2): how its last statement was read when it was committed, offered for
/// the next statement uploaded for the account, so importing it is one click. The user can still change it in the
/// preview, and the mapping the next statement is committed with is saved in its place. One per account.
/// </summary>
public sealed class ImportMapping : IUserOwned
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private ImportMapping()
    {
        Mapping = null!;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The account it is saved for.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>How the account's statements are read.</summary>
    public StatementMapping Mapping { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Saves the mapping a statement was committed with as the account's.</summary>
    public static ImportMapping Save(Guid accountId, StatementMapping mapping, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return new ImportMapping { Id = Guid.CreateVersion7(now), AccountId = accountId, Mapping = mapping, CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>Saves another mapping in place of this one: the one the account's latest statement was committed with.</summary>
    public void Change(StatementMapping mapping, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping == Mapping)
        {
            return;
        }

        Mapping = mapping;
        UpdatedAt = now;
    }
}
