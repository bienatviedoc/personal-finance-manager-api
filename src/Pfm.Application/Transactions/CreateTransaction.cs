using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>Records income or an expense on one of the user's accounts, in that account's currency.</summary>
/// <param name="Type"><c>income</c> or <c>expense</c>.</param>
/// <param name="Amount">A decimal string greater than zero, such as <c>"125000.00"</c>.</param>
/// <param name="Date">A calendar date, <c>YYYY-MM-DD</c>, not after today in the user's time zone.</param>
/// <param name="CategoryId">A category of the kind the type takes.</param>
/// <param name="IdempotencyKey">
/// A key the caller made up for this transaction. Sending the command again with it answers the transaction
/// the first one recorded and records nothing.
/// </param>
public sealed record CreateTransactionCommand(
    string? Type, Guid AccountId, string? Amount, string? Date, Guid CategoryId, string? Payee, string? Note, string? IdempotencyKey = null)
    : ITransactionCommand
{
    public const int MaxIdempotencyKeyLength = 200;

    public TransactionFields Parsed { get; } = TransactionFields.Read(Type, Amount, Date);
}

public sealed class CreateTransactionValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionValidator()
    {
        this.TransactionFieldRules();
        RuleFor(command => command.IdempotencyKey)
            .Must(key => key is null || (!string.IsNullOrWhiteSpace(key) && key.Length <= CreateTransactionCommand.MaxIdempotencyKeyLength))
            .WithMessage($"Send a key of 1 to {CreateTransactionCommand.MaxIdempotencyKeyLength} characters.");
    }
}

/// <summary>
/// Answers the new transaction, or the one a request with the same idempotency key already recorded. An
/// account or category that is not one of the user's, or is archived, is an error on its id.
/// </summary>
public sealed class CreateTransactionHandler(
    IValidator<CreateTransactionCommand> validator, ITransactionRepository transactions, TransactionContext context, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<CreateTransactionCommand, TransactionDetails>(validator)
{
    protected override async Task<Result<TransactionDetails>> ExecuteAsync(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is { } key && await transactions.FindByIdempotencyKeyAsync(key, cancellationToken) is { } recorded)
        {
            return TransactionDetails.Of(recorded);
        }

        var (found, errors) = await context.ReadAsync(command, currentAccountId: null, cancellationToken);
        if (found is null)
        {
            return Result<TransactionDetails>.Invalid(errors!);
        }

        var fields = command.Parsed;
        Transaction transaction;
        try
        {
            transaction = Transaction.Record(
                fields.Type!.Value, found.Account.Id, found.Account.Currency, fields.Amount!.Value, fields.Date!.Value, found.Category, command.Payee,
                command.Note, found.Today, clock.GetUtcNow());
        }
        catch (TransactionRuleViolation violation)
        {
            return Result<TransactionDetails>.Invalid(violation.AsErrors());
        }

        transactions.Add(transaction, command.IdempotencyKey);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (AlreadyExistsException) when (command.IdempotencyKey is not null)
        {
            // The same request, sent twice at once: the other one recorded the transaction first.
            return TransactionDetails.Of((await transactions.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken))!);
        }

        return TransactionDetails.Of(transaction);
    }
}
