using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Budgets;
using Pfm.Application.Transactions.Tags;
using Pfm.Application.UseCases;
using Pfm.Domain;
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
/// <param name="IsRefund">Whether the expense is a refund: money coming back, which raises the balance. Only an expense can be one.</param>
/// <param name="IdempotencyKey">
/// A key the caller made up for this transaction. Sending the command again with it answers the transaction
/// the first one recorded and records nothing; sending another transaction with it is refused.
/// </param>
/// <param name="Lines">The lines of a split, which sum exactly to the amount; <paramref name="CategoryId"/> is then left empty.</param>
/// <param name="RecurringRuleId">The recurring rule posting the transaction as one of its occurrences; null when a user records it.</param>
/// <param name="Tags">The names of its tags, in any case; a name the user has no tag with yet creates one.</param>
public sealed record CreateTransactionCommand(
    string? Type, Guid AccountId, string? Amount, string? Date, Guid CategoryId, string? Payee, string? Note, string? IdempotencyKey = null,
    bool IsRefund = false, IReadOnlyList<TransactionLineInput>? Lines = null, Guid? RecurringRuleId = null,
    IReadOnlyList<string?>? Tags = null)
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
/// error on its id. When the account's currency or the user's base currency changes before it is saved, it is
/// recorded again in the new one; when the category's kind does, it is held to the new kind.
/// </summary>
public sealed class CreateTransactionHandler(
    IValidator<CreateTransactionCommand> validator, ITransactionRepository transactions, TransactionContext context,
    TransactionConversion conversion, TransactionTags tags, IBudgetNotifications budgetNotifications, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateTransactionCommand, CreateTransactionOutcome>(validator)
{
    protected override Task<Result<CreateTransactionOutcome>> ExecuteAsync(CreateTransactionCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => RecordAsync(command, cancellationToken));

    private async Task<Result<CreateTransactionOutcome>> RecordAsync(CreateTransactionCommand command, CancellationToken cancellationToken)
    {
        var request = IdempotentRequest.Of(command);
        if (request is not null && await transactions.FindByIdempotencyKeyAsync(request.Key, cancellationToken) is { } recorded)
        {
            return AlreadyRecorded(recorded, request);
        }

        var read = await context.ReadAsync(command, currentAccountId: null, cancellationToken);
        if (!read.IsValid)
        {
            return Result<CreateTransactionOutcome>.Invalid(read.Errors);
        }

        var against = read.Outcome;
        var fields = command.Parsed;
        Transaction transaction;
        try
        {
            var moves = new AccountAmount(against.Account.Id, against.Account.Currency, fields.Amount!.Value);
            var at = new Moment(against.Today, clock.GetUtcNow());
            transaction = against.Split is { } lines
                ? Transaction.RecordSplit(fields.Type!.Value, moves, fields.Date!.Value, lines, command.Payee, command.Note, at, command.IsRefund)
                : Transaction.Record(fields.Type!.Value, moves, fields.Date!.Value, against.Category, command.Payee, command.Note, at, command.IsRefund);
        }
        catch (TransactionRuleViolation violation)
        {
            return Result<CreateTransactionOutcome>.Invalid(violation.AsErrors());
        }

        transaction.TagWith(await tags.FindOrCreateAsync(TagNames.Of(command.Tags), cancellationToken), clock.GetUtcNow());
        if (command.RecurringRuleId is { } recurringRuleId)
        {
            transaction.PostedBy(recurringRuleId);
        }

        await conversion.ConvertAsync(transaction, against.BaseCurrency, cancellationToken);
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

        await budgetNotifications.CheckAsync([transaction.Date], cancellationToken);
        return new CreateTransactionOutcome(TransactionDetails.Of(transaction));
    }

    /// <summary>The same request again answers what it recorded; another one with its key records nothing.</summary>
    private static CreateTransactionOutcome AlreadyRecorded(RecordedRequest recorded, IdempotentRequest request) =>
        recorded.Fingerprint == request.Fingerprint
            ? new CreateTransactionOutcome(TransactionDetails.Of(recorded.Transaction))
            : CreateTransactionOutcome.KeyReused;
}
