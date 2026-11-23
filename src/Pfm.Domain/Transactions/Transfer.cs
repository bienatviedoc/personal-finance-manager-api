namespace Pfm.Domain.Transactions;

/// <summary>
/// A movement of a user's own money between two of their accounts: two linked transactions sharing a transfer
/// id, a Transfer out leg on the source account and a Transfer in leg on the destination (spec section 3,
/// rule 3; decision record 0001). It is neither income nor spending, so its legs have no category. The legs
/// are recorded, edited and deleted together, here and nowhere else.
/// </summary>
/// <remarks>
/// Each leg holds its own amount in its own account's currency. Between accounts in one currency the two
/// amounts are the same; between currencies the user enters both, and the exchange rate is what the two
/// amounts imply, never looked up (rule 4).
/// </remarks>
public sealed class Transfer
{
    private Transfer(Transaction outLeg, Transaction inLeg)
    {
        OutLeg = outLeg;
        InLeg = inLeg;
    }

    /// <summary>The transfer id the two legs share.</summary>
    public Guid Id => OutLeg.TransferId!.Value;

    /// <summary>The Transfer out leg, on the source account.</summary>
    public Transaction OutLeg { get; }

    /// <summary>The Transfer in leg, on the destination account.</summary>
    public Transaction InLeg { get; }

    /// <summary>The source account, which the money leaves.</summary>
    public Guid FromAccountId => OutLeg.AccountId;

    /// <summary>The destination account, which the money arrives on.</summary>
    public Guid ToAccountId => InLeg.AccountId;

    /// <summary>What leaves the source account, in its currency.</summary>
    public decimal Amount => OutLeg.Amount;

    /// <summary>What arrives on the destination account, in its currency.</summary>
    public decimal ToAmount => InLeg.Amount;

    public DateOnly Date => OutLeg.Date;

    public string? Note => OutLeg.Note;

    public bool IsDeleted => OutLeg.IsDeleted;

    /// <summary>
    /// The version an edit of the transfer names: the Transfer out leg's. It stands for both legs because the user
    /// changes a leg only through this type, which changes both together (a leg alone is refused), so an edit
    /// moves both versions at once. What changes one leg alone, converting it into the base currency once a rate
    /// arrives, is no edit to call stale. The in leg's own version still guards the save: one changed in between
    /// fails it as a concurrent change.
    /// </summary>
    public uint RowVersion => OutLeg.RowVersion;

    /// <param name="from">What leaves the source account.</param>
    /// <param name="to">What arrives on the destination account.</param>
    /// <param name="today">Today's date in the user's time zone.</param>
    public static Transfer Record(LegAmount from, LegAmount to, DateOnly date, string? note, DateOnly today, DateTimeOffset now)
    {
        GuardCanMove(from, to);
        var id = Guid.CreateVersion7(now);
        var outLeg = OfLeg(nameof(Amount), () => Transaction.RecordLeg(id, TransactionType.TransferOut, from, date, note, today, now));
        var inLeg = OfLeg(nameof(ToAmount), () => Transaction.RecordLeg(id, TransactionType.TransferIn, to, date, note, today, now));
        return new Transfer(outLeg, inLeg);
    }

    /// <summary>Pairs the two legs of one transfer, as read back, in either order.</summary>
    /// <exception cref="ArgumentException">The transactions are not one Transfer out leg and one Transfer in leg sharing a transfer id.</exception>
    public static Transfer Of(IReadOnlyCollection<Transaction> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);
        var outLeg = legs.FirstOrDefault(leg => leg.Type == TransactionType.TransferOut);
        var inLeg = legs.FirstOrDefault(leg => leg.Type == TransactionType.TransferIn);
        if (legs.Count != 2 || outLeg?.TransferId is null || inLeg is null || inLeg.TransferId != outLeg.TransferId)
        {
            throw new ArgumentException("A transfer is one Transfer out leg and one Transfer in leg sharing a transfer id.", nameof(legs));
        }

        return new Transfer(outLeg, inLeg);
    }

    /// <summary>Changes both legs together, to other accounts if need be; neither changes when a rule is broken.</summary>
    /// <exception cref="InvalidOperationException">The transfer is deleted.</exception>
    public void Edit(LegAmount from, LegAmount to, DateOnly date, string? note, DateOnly today, DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted transfer cannot be edited.");
        }

        GuardCanMove(from, to);

        // Checked on a leg nobody keeps first, so the out leg is not left changed when the in leg is refused.
        OfLeg(nameof(ToAmount), () => Transaction.RecordLeg(Id, TransactionType.TransferIn, to, date, note, today, now));
        OfLeg(nameof(Amount), () => OutLeg.EditLeg(from, date, note, today, now));
        OfLeg(nameof(ToAmount), () => InLeg.EditLeg(to, date, note, today, now));
    }

    /// <summary>Deletes both legs together, so both accounts' balances are as before the transfer. Deleting it again changes nothing.</summary>
    public void Delete(DateTimeOffset now)
    {
        OutLeg.DeleteLeg(now);
        InLeg.DeleteLeg(now);
    }

    private static void GuardCanMove(LegAmount from, LegAmount to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.AccountId == to.AccountId)
        {
            throw new TransferRuleViolation(nameof(ToAccountId), "A transfer is between two different accounts.");
        }

        if (from.Currency == to.Currency && from.Amount != to.Amount)
        {
            throw new TransferRuleViolation(nameof(ToAmount), "Accounts in the same currency move the same amount.");
        }
    }

    /// <summary>A leg is held to the rules of any transaction; one it breaks is named as the transfer names it.</summary>
    /// <param name="amountProperty">What the transfer calls this leg's amount.</param>
    private static Transaction OfLeg(string amountProperty, Func<Transaction> record)
    {
        try
        {
            return record();
        }
        catch (TransactionRuleViolation violation)
        {
            throw new TransferRuleViolation(
                violation.Property == nameof(Transaction.Amount) ? amountProperty : violation.Property,
                violation.Message.Replace("A transaction's date", "A transfer's date", StringComparison.Ordinal));
        }
    }

    private static void OfLeg(string amountProperty, Action edit) =>
        OfLeg(amountProperty, () =>
        {
            edit();
            return (Transaction)null!;
        });
}

/// <summary>What one leg of a transfer moves: an amount on an account, in that account's currency.</summary>
/// <param name="Currency">The currency of the account, which the leg takes.</param>
/// <param name="Amount">Greater than zero; the leg's type gives the sign.</param>
public sealed record LegAmount(Guid AccountId, Currency Currency, decimal Amount);
