namespace Pfm.Domain.Transactions;

/// <summary>
/// A tag on a transaction: the transaction's own, read and saved with it. It carries the tag's name, which never
/// changes, so a transaction is read with its tags and nothing else.
/// </summary>
public sealed class TransactionTag
{
    /// <summary>For persistence, which sets every property itself.</summary>
    private TransactionTag()
    {
        Name = "";
    }

    internal TransactionTag(Tag tag)
    {
        TagId = tag.Id;
        Name = tag.Name;
    }

    public Guid TagId { get; private set; }

    public string Name { get; private set; }
}
