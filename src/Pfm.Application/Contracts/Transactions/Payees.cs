using Pfm.Domain.Transactions;

namespace Pfm.Application.Contracts.Transactions;

/// <summary>What another module needs to know of a payee.</summary>
public static class Payees
{
    /// <summary>The most characters a transaction's payee has, so a longer one matches none.</summary>
    public const int MaxLength = Transaction.MaxPayeeLength;
}
