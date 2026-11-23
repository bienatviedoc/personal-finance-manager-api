using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Changes a transaction, from the row version the caller last read (<paramref name="RowVersion"/>). Moved to
/// another account, it takes that account's currency. A changed amount, date or account is converted into
/// the base currency again; any other edit leaves the converted amount as it is (decision record 0002).
/// </summary>
/// <param name="Lines">
/// The lines of a split, which replace the ones the transaction had; <paramref name="CategoryId"/> is then
/// left empty. Null makes a split transaction one category again.
/// </param>
public sealed record EditTransactionCommand(
    Guid Id, uint RowVersion, string? Type, Guid AccountId, string? Amount, string? Date, Guid CategoryId, string? Payee, string? Note,
    IReadOnlyList<TransactionLineInput>? Lines = null)
    : ITransactionCommand
{
    public TransactionFields Parsed { get; } = TransactionFields.Read(Type, Amount, Date);
}

public sealed class EditTransactionValidator : AbstractValidator<EditTransactionCommand>
{
    public EditTransactionValidator()
    {
        RuleFor(command => command.Id).Required();
        this.TransactionFieldRules();
    }
}

/// <summary>What editing a transaction came to: the edited transaction, or nothing saved because it had changed.</summary>
/// <param name="Edited">Null when the edit was made from a stale row version.</param>
public sealed record EditTransactionOutcome(TransactionDetails? Edited)
{
    /// <summary>The transaction changed since the row version the edit was made from; nothing was saved.</summary>
    public static readonly EditTransactionOutcome Stale = new((TransactionDetails?)null);
}

/// <summary>
/// Answers the edited transaction; null when the user has no transaction with that id, or deleted it. It
/// may stay on an account or category that has since been archived, but not move to one.
/// </summary>
public sealed class EditTransactionHandler(
    IValidator<EditTransactionCommand> validator, ITransactionRepository transactions, TransactionContext context,
    TransactionConversion conversion, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<EditTransactionCommand, EditTransactionOutcome?>(validator)
{
    protected override async Task<Result<EditTransactionOutcome?>> ExecuteAsync(EditTransactionCommand command, CancellationToken cancellationToken)
    {
        try
        {
            // Made again when the account's currency or the user's base currency changed before it was saved.
            // When it was the transaction that changed, the row version is no longer its own and the edit is stale.
            return await unitOfWork.RetryOnConcurrentChangeAsync(() => EditAsync(command, cancellationToken));
        }
        catch (ConcurrentChangeException)
        {
            return EditTransactionOutcome.Stale;
        }
    }

    private async Task<Result<EditTransactionOutcome?>> EditAsync(EditTransactionCommand command, CancellationToken cancellationToken)
    {
        var transaction = await transactions.FindAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            return (EditTransactionOutcome?)null;
        }

        if (transaction.RowVersion != command.RowVersion)
        {
            return EditTransactionOutcome.Stale;
        }

        var read = await context.ReadAsync(command, currentAccountId: transaction.AccountId, cancellationToken);
        if (!read.IsValid)
        {
            return Result<EditTransactionOutcome?>.Invalid(read.Errors);
        }

        var against = read.Outcome;
        var fields = command.Parsed;
        try
        {
            if (against.Split is { } lines)
            {
                transaction.EditSplit(
                    fields.Type!.Value, against.Account.Id, against.Account.Currency, fields.Amount!.Value, fields.Date!.Value, lines, command.Payee,
                    command.Note, against.Today, clock.GetUtcNow());
            }
            else
            {
                transaction.Edit(
                    fields.Type!.Value, against.Account.Id, against.Account.Currency, fields.Amount!.Value, fields.Date!.Value, against.Category, command.Payee,
                    command.Note, against.Today, clock.GetUtcNow());
            }
        }
        catch (TransactionRuleViolation violation)
        {
            return Result<EditTransactionOutcome?>.Invalid(violation.AsErrors());
        }

        // Converts again only what an edit of the amount, the date or the account left without a converted amount.
        await conversion.ConvertAsync(transaction, against.BaseCurrency, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new EditTransactionOutcome(TransactionDetails.Of(transaction));
    }
}
