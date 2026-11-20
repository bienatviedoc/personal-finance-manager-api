using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// Records income or an expense on one of the user's accounts, in that account's currency. Its amount is
/// converted into the user's base currency at the rate of its date and kept; with no rate on or before that
/// date it is recorded all the same, flagged not converted (spec section 3, rule 7).
/// </summary>
/// <param name="Type"><c>income</c> or <c>expense</c>.</param>
/// <param name="Amount">A decimal string greater than zero, such as <c>"125000.00"</c>.</param>
/// <param name="Date">A calendar date, <c>YYYY-MM-DD</c>, not after today in the user's time zone.</param>
/// <param name="CategoryId">A category of the kind the type takes.</param>
/// <param name="IdempotencyKey">
/// A key the caller made up for this transaction. Sending the command again with it answers the transaction
/// the first one recorded and records nothing; sending another transaction with it is refused.
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

/// <summary>What recording a transaction came to: the transaction, or nothing recorded because its idempotency key was already used.</summary>
/// <param name="Recorded">Null when the key was used before, for another transaction.</param>
public sealed record CreateTransactionOutcome(TransactionDetails? Recorded)
{
    /// <summary>The idempotency key already recorded a transaction that is not this one; nothing was recorded.</summary>
    public static readonly CreateTransactionOutcome KeyReused = new((TransactionDetails?)null);
}

/// <summary>
/// Answers the new transaction, or the one the same request already recorded under its idempotency key, as
/// it is now: edited since, or deleted. A deleted transaction's key stays used, so a late retry cannot bring
/// back what the user deleted. An account or category that is not one of the user's, or is archived, is an
/// error on its id.
/// </summary>
public sealed class CreateTransactionHandler(
    IValidator<CreateTransactionCommand> validator, ITransactionRepository transactions, TransactionContext context,
    TransactionConversion conversion, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateTransactionCommand, CreateTransactionOutcome>(validator)
{
    protected override async Task<Result<CreateTransactionOutcome>> ExecuteAsync(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        var request = IdempotentRequest.Of(command);
        if (request is not null && await transactions.FindByIdempotencyKeyAsync(request.Key, cancellationToken) is { } recorded)
        {
            return AlreadyRecorded(recorded, request);
        }

        var (found, errors) = await context.ReadAsync(command, currentAccountId: null, cancellationToken);
        if (found is null)
        {
            return Result<CreateTransactionOutcome>.Invalid(errors!);
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
            return Result<CreateTransactionOutcome>.Invalid(violation.AsErrors());
        }

        await conversion.ConvertAsync(transaction, found.BaseCurrency, cancellationToken);
        transactions.Add(transaction, request);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (AlreadyExistsException) when (request is not null)
        {
            // Two requests with the key, sent at once: the other one recorded its transaction first.
            return AlreadyRecorded((await transactions.FindByIdempotencyKeyAsync(request.Key, cancellationToken))!, request);
        }

        return new CreateTransactionOutcome(TransactionDetails.Of(transaction));
    }

    /// <summary>The same request again answers what it recorded; another one with its key records nothing.</summary>
    private static CreateTransactionOutcome AlreadyRecorded(RecordedRequest recorded, IdempotentRequest request) =>
        recorded.Fingerprint == request.Fingerprint
            ? new CreateTransactionOutcome(TransactionDetails.Of(recorded.Transaction))
            : CreateTransactionOutcome.KeyReused;
}
