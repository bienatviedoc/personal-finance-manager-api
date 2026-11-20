using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Changes a transaction, from the row version the caller last read (<paramref name="RowVersion"/>). Moved to
/// another account, it takes that account's currency.
/// </summary>
public sealed record EditTransactionCommand(
    Guid Id, uint RowVersion, string? Type, Guid AccountId, string? Amount, string? Date, Guid CategoryId, string? Payee, string? Note)
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
    IValidator<EditTransactionCommand> validator, ITransactionRepository transactions, TransactionContext context, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<EditTransactionCommand, EditTransactionOutcome?>(validator)
{
    protected override async Task<Result<EditTransactionOutcome?>> ExecuteAsync(EditTransactionCommand command, CancellationToken cancellationToken)
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

        var (found, errors) = await context.ReadAsync(command, currentAccountId: transaction.AccountId, cancellationToken);
        if (found is null)
        {
            return Result<EditTransactionOutcome?>.Invalid(errors!);
        }

        var fields = command.Parsed;
        try
        {
            transaction.Edit(
                fields.Type!.Value, found.Account.Id, found.Account.Currency, fields.Amount!.Value, fields.Date!.Value, found.Category, command.Payee,
                command.Note, found.Today, clock.GetUtcNow());
        }
        catch (TransactionRuleViolation violation)
        {
            return Result<EditTransactionOutcome?>.Invalid(violation.AsErrors());
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            // Someone else saved the transaction between reading it here and saving it.
            return EditTransactionOutcome.Stale;
        }

        return new EditTransactionOutcome(TransactionDetails.Of(transaction));
    }
}
