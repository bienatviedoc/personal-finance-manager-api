namespace Pfm.Domain.Transactions;

/// <summary>
/// What a transaction moves: an amount on an account, in that account's currency. One leg of a transfer moves one.
/// </summary>
/// <param name="Currency">The currency of the account, which the transaction takes.</param>
/// <param name="Amount">Greater than zero; the transaction's type gives the sign.</param>
public sealed record AccountAmount(Guid AccountId, Currency Currency, decimal Amount);
