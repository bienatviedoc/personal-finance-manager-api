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
    /// before their date, and saves them, one user at a time. The fetch of exchange rates calls it every time.
    /// Amounts already converted are left as they are. Answers how many users' transactions could not be
    /// saved because they kept changing meanwhile: those wait for the next call, and every other user's are
    /// converted all the same.
    /// </summary>
    Task<int> ConvertNotConvertedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Converts every transaction of the signed-in user into a new base currency, each at the exchange rate
    /// of its own date, flagging as not converted the ones with no rate on or before it. Identity calls it
    /// when the user changes their base currency, and saves the transactions with the profile.
    /// </summary>
    Task ConvertEveryAmountIntoAsync(Currency baseCurrency, CancellationToken cancellationToken);
}
