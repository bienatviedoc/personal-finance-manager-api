using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.ExchangeRates;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Converts a transaction's amount into the user's base currency, once: at the exchange rate of its date, or
/// of the nearest earlier day that has one, and never again when rates move (spec section 3, rule 7; decision
/// record 0002). With no rate on or before its date the transaction is flagged not converted.
/// </summary>
public sealed class TransactionConversion(
    ICurrencyConversion conversion, ITransactionRepository transactions, IActForUser actFor, IUnitOfWork unitOfWork)
    : ITransactionConversion
{
    /// <summary>A user's transactions that changed between being read and saved are read again and converted as saved, this many times at most.</summary>
    private const int Attempts = 3;

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
        for (var attempt = 1; ; attempt++)
        {
            var waiting = await transactions.ListNotConvertedAsync(cancellationToken);
            foreach (var transaction in waiting)
            {
                await ConvertAsync(transaction, transaction.BaseCurrency!.Value, cancellationToken);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (ConcurrentChangeException)
            {
                // Edited or deleted by its user meanwhile, perhaps converted by that edit. Start again from what was saved.
                foreach (var transaction in waiting)
                {
                    await transactions.RefreshAsync(transaction, cancellationToken);
                }

                if (attempt == Attempts)
                {
                    return false;
                }
            }
        }
    }

    public async Task ConvertEveryAmountIntoAsync(Currency baseCurrency, CancellationToken cancellationToken)
    {
        foreach (var transaction in await transactions.ListEveryAsync(cancellationToken))
        {
            await ConvertAsync(transaction, baseCurrency, cancellationToken);
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
