using Pfm.Application.ExchangeRates;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Converts a transaction's amount into the user's base currency, once: at the exchange rate of its date, or
/// of the nearest earlier day that has one, and never again when rates move (spec section 3, rule 7; decision
/// record 0002). With no rate on or before its date the transaction is flagged not converted.
/// </summary>
public sealed class TransactionConversion(ICurrencyConversion conversion)
{
    /// <summary>
    /// Converts the amount unless it is already converted into <paramref name="baseCurrency"/>. The caller
    /// saves the transaction.
    /// </summary>
    public async Task ConvertAsync(Transaction transaction, Currency baseCurrency, CancellationToken cancellationToken)
    {
        if (transaction.IsConvertedInto(baseCurrency))
        {
            return;
        }

        var converted = await conversion.ConvertAsync(transaction.Amount, transaction.Currency, baseCurrency, transaction.Date, cancellationToken);

        // An amount too large for the base currency to hold has no converted amount either.
        if (converted is { Amount: { } amount, Rate: { } rate, RateDate: { } rateDate } && Transaction.CanHoldInBaseCurrency(amount, baseCurrency))
        {
            transaction.ConvertInto(baseCurrency, amount, rate, rateDate);
        }
        else
        {
            transaction.FlagNotConverted(baseCurrency);
        }
    }
}
