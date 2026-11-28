using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// Reads what a transfer is made between: the two accounts the command names, with what each leg moves on
/// them, today's date for the user and their base currency. Shared by recording and editing.
/// </summary>
public sealed class TransferContext(IAccountLookup accounts, IUserCalendar calendar, IBaseCurrency baseCurrency)
{
    /// <summary>
    /// What the transfer is made between, or the error, keyed by command property, when an account is not one
    /// the transfer can be on or the amount that arrives is needed and was not sent.
    /// </summary>
    /// <param name="current">The transfer being edited, which may stay on an account after it is archived.</param>
    internal async Task<Result<TransferredBetween>> ReadAsync(ITransferCommand command, Transfer? current, CancellationToken cancellationToken)
    {
        if (await accounts.FindChoosableAsync(command.FromAccountId, current?.FromAccountId, cancellationToken) is not { } from)
        {
            return Result<TransferredBetween>.Invalid(FieldError.On(nameof(ITransferCommand.FromAccountId), Choices.NotAnAccount));
        }

        if (await accounts.FindChoosableAsync(command.ToAccountId, current?.ToAccountId, cancellationToken) is not { } to)
        {
            return Result<TransferredBetween>.Invalid(FieldError.On(nameof(ITransferCommand.ToAccountId), Choices.NotAnAccount));
        }

        // Accounts in one currency move one amount. Between currencies the user says what arrived: no exchange
        // rate is looked up (spec section 3, rule 4).
        var amount = command.Parsed.Amount!.Value;
        if (command.Parsed.ToAmount is not { } toAmount)
        {
            if (from.Currency != to.Currency)
            {
                return Result<TransferredBetween>.Invalid(FieldError.On(
                    nameof(ITransferCommand.ToAmount), $"Enter the amount that arrives, in {to.Currency}: the two accounts are in different currencies."));
            }

            toAmount = amount;
        }

        return new TransferredBetween(
            new AccountAmount(from.Id, from.Currency, amount), new AccountAmount(to.Id, to.Currency, toAmount),
            await calendar.TodayAsync(cancellationToken), await baseCurrency.OfCurrentUserAsync(cancellationToken));
    }
}

/// <summary>What a transfer is made between.</summary>
/// <param name="From">What leaves the source account, in its currency.</param>
/// <param name="To">What arrives on the destination account, in its currency.</param>
/// <param name="Today">Today's date in the user's time zone.</param>
/// <param name="BaseCurrency">The currency each leg's amount is converted into, as any transaction's is.</param>
internal sealed record TransferredBetween(AccountAmount From, AccountAmount To, DateOnly Today, Currency BaseCurrency);
