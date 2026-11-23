namespace Pfm.Domain.Transactions;

/// <summary>
/// A part of a transaction's amount and the category it was for. A transaction has one line, with its whole
/// amount, or several when it is split; its lines always sum exactly to its amount (spec section 3, rule 5).
/// Lines are the transaction's own: they are written, changed and removed only through it.
/// </summary>
public sealed class TransactionLine
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private TransactionLine()
    {
    }

    internal TransactionLine(int position, SplitLine entered, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        Position = position;
        Become(entered);
    }

    public Guid Id { get; private set; }

    /// <summary>Where the line stands among its transaction's lines, from zero, in the order they were entered.</summary>
    public int Position { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>
    /// The kind of the line's category, which is the kind its transaction's type takes. Kept on the line so
    /// the database can hold the category to it (spec section 3, rule 13).
    /// </summary>
    public CategoryKind CategoryKind { get; private set; }

    /// <summary>Always greater than zero, in the transaction's currency; the transaction's type gives the sign.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What this part was for; null when not entered. The transaction has a note of its own.</summary>
    public string? Note { get; private set; }

    /// <summary>The line at this position now says what was entered there. The transaction has checked it.</summary>
    internal void Become(SplitLine entered)
    {
        CategoryId = entered.Category.Id;
        CategoryKind = entered.Category.Kind;
        Amount = entered.Amount;
        Note = Transaction.Normalised(entered.Note);
    }
}

/// <summary>A line as it is entered: one of the user's categories, the part of the amount it takes and an optional note.</summary>
public sealed record SplitLine(Category Category, decimal Amount, string? Note = null);
