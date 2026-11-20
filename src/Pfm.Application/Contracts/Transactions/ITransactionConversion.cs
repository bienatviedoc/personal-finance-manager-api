using Pfm.Domain;

namespace Pfm.Application.Contracts.Transactions;

/// <summary>
/// What may be asked about converting transactions' amounts into their users' base currency, which
/// Transactions does once per amount and keeps (spec section 3, rule 7; decision record 0002).
/// </summary>
public interface ITransactionConversion
{
    /// <summary>
    /// Converts every user's transactions that are flagged not converted and now have an exchange rate on or
    /// before their date, and saves them. The fetch of exchange rates calls it when new rates have arrived.
    /// Amounts already converted are left as they are.
    /// </summary>
    Task ConvertNotConvertedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Converts every transaction of the signed-in user into a new base currency, each at the exchange rate
    /// of its own date, flagging as not converted the ones with no rate on or before it. Identity calls it
    /// when the user changes their base currency, and saves the transactions with the profile.
    /// </summary>
    Task ConvertEveryAmountIntoAsync(Currency baseCurrency, CancellationToken cancellationToken);
}
