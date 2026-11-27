namespace Pfm.Domain.Imports;

/// <summary>
/// The mapping saved for an account (spec IMP-2): how its last statement was read when it was committed, and the
/// categories its rows were recorded on, offered for the next statement uploaded for the account, so importing it is
/// one click. The user can still change them in the preview, and what the next statement is committed with is saved in
/// their place. One per account.
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

    /// <summary>
    /// The expense category the last statement that took money out was committed with; null when none has. It may have
    /// been archived since.
    /// </summary>
    public Guid? ExpenseCategoryId { get; private set; }

    /// <summary>The income category the last statement that brought money in was committed with; null when none has.</summary>
    public Guid? IncomeCategoryId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Saves the mapping and categories a statement was committed with as the account's.</summary>
    public static ImportMapping Save(Guid accountId, StatementMapping mapping, Guid? expenseCategoryId, Guid? incomeCategoryId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return new ImportMapping
        {
            Id = Guid.CreateVersion7(now), AccountId = accountId, Mapping = mapping, ExpenseCategoryId = expenseCategoryId,
            IncomeCategoryId = incomeCategoryId, CreatedAt = now, UpdatedAt = now,
        };
    }

    /// <summary>
    /// Saves what the account's latest statement was committed with in place of this. A category it was committed
    /// without, needing none of that kind, leaves the one saved.
    /// </summary>
    public void Change(StatementMapping mapping, Guid? expenseCategoryId, Guid? incomeCategoryId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var (expense, income) = (expenseCategoryId ?? ExpenseCategoryId, incomeCategoryId ?? IncomeCategoryId);
        if (mapping == Mapping && expense == ExpenseCategoryId && income == IncomeCategoryId)
        {
            return;
        }

        (Mapping, ExpenseCategoryId, IncomeCategoryId) = (mapping, expense, income);
        UpdatedAt = now;
    }
}
