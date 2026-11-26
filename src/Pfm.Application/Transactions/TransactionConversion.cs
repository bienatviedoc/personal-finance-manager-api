using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.ExchangeRates;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Converts a transaction's amount into the user's base currency, once: at the exchange rate of its date, or
/// of the nearest earlier day that has one, and never again when rates move (spec section 3, rule 7; decision
/// record 0002). With no rate on or before its date the transaction is flagged not converted.
/// </summary>
public sealed class TransactionConversion(
    ICurrencyConversion conversion, ITransactionRepository transactions, IBudgetNotifications budgetNotifications, IActForUser actFor, IUnitOfWork unitOfWork)
    : ITransactionConversion
{
    /// <summary>Runs for no user, in the worker, so it acts for each user in turn and saves what it converted of theirs.</summary>
    public async Task<int> ConvertNotConvertedAsync(CancellationToken cancellationToken)
    {
        var usersNotSaved = 0;
        foreach (var userId in await transactions.UsersWithNotConvertedAsync(cancellationToken))
        {
            using var _ = actFor.ActFor(userId);
            if (!await ConvertNotConvertedOfUserAsync(cancellationToken))
            {
                usersNotSaved++;
            }
        }

        return usersNotSaved;
    }

    /// <summary>False when the user's transactions kept changing: nothing of theirs is saved, and nothing is left half done for the next user's save.</summary>
    private async Task<bool> ConvertNotConvertedOfUserAsync(CancellationToken cancellationToken)
    {
        try
        {
            // A save that fails found one edited or deleted by its user meanwhile, perhaps converted by that edit: start again from what was saved.
            var converted = await unitOfWork.RetryOnConcurrentChangeAsync(async () =>
            {
                var dates = new List<DateOnly>();
                foreach (var transaction in await transactions.ListNotConvertedAsync(cancellationToken))
                {
                    await ConvertAsync(transaction, transaction.Conversion!.BaseCurrency, cancellationToken);
                    if (transaction.Conversion!.Amount is not null)
                    {
                        dates.Add(transaction.Date);
                    }
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                return dates;
            });

            // An amount converted now counts in spent from now on.
            await budgetNotifications.CheckAsync(converted, cancellationToken);
            return true;
        }
        catch (ConcurrentChangeException)
        {
            unitOfWork.DiscardChanges();
            return false;
        }
    }

    /// <summary>The rate is looked up once for the transactions of one currency on one date, not once for each of them.</summary>
    public async Task ConvertEveryAmountIntoAsync(Currency baseCurrency, CancellationToken cancellationToken)
    {
        var toConvert = (await transactions.ListEveryAsync(cancellationToken)).Where(transaction => !transaction.IsConvertedInto(baseCurrency));
        foreach (var sameRate in toConvert.GroupBy(transaction => (transaction.Currency, transaction.Date)))
        {
            var (currency, date) = sameRate.Key;
            var converted = await conversion.ConvertAsync([.. sameRate.Select(transaction => transaction.Amount)], currency, baseCurrency, date, cancellationToken);
            foreach (var (transaction, amount) in sameRate.Zip(converted))
            {
                Keep(transaction, baseCurrency, amount);
            }
        }
    }

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

        Keep(
            transaction, baseCurrency,
            await conversion.ConvertAsync(transaction.Amount, transaction.Currency, baseCurrency, transaction.Date, cancellationToken));
    }

    private static void Keep(Transaction transaction, Currency baseCurrency, Conversion converted)
    {
        // An amount too large for the base currency to hold has no converted amount either.
        if (converted is { Amount: { } amount, Rate: { } rate, RateDate: { } rateDate } && BaseCurrencyConversion.CanHold(amount, baseCurrency))
        {
            transaction.ConvertInto(baseCurrency, amount, rate, rateDate);
        }
        else
        {
            transaction.FlagNotConverted(baseCurrency);
        }
    }
}
