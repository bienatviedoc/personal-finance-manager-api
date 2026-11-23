using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// Moves money between two of the user's accounts: records the two legs of a transfer together (spec
/// section 3, rule 3; decision record 0001). Between accounts in different currencies the user sends both
/// amounts and no exchange rate is looked up (rule 4).
/// </summary>
/// <param name="FromAccountId">The source account, which the money leaves.</param>
/// <param name="ToAccountId">The destination account, which is another one.</param>
/// <param name="Amount">What leaves the source account: a decimal string greater than zero, in that account's currency.</param>
/// <param name="ToAmount">
/// What arrives on the destination account, in that account's currency. Needed when the two accounts are in
/// different currencies; between accounts in one currency it is left out, or equal to <paramref name="Amount"/>.
/// </param>
/// <param name="Date">A calendar date, <c>YYYY-MM-DD</c>, not after today in the user's time zone.</param>
/// <param name="IdempotencyKey">
/// A key the caller made up for this transfer. Sending the command again with it answers the transfer the
/// first one recorded and records nothing; sending anything else with it is refused.
/// </param>
public sealed record CreateTransferCommand(
    Guid FromAccountId, Guid ToAccountId, string? Amount, string? ToAmount, string? Date, string? Note, string? IdempotencyKey = null)
    : ITransferCommand
{
    public TransferFields Parsed { get; } = TransferFields.Read(Amount, ToAmount, Date);
}

public sealed class CreateTransferValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferValidator()
    {
        this.TransferFieldRules();
        RuleFor(command => command.IdempotencyKey)
            .Must(key => key is null || (!string.IsNullOrWhiteSpace(key) && key.Length <= CreateTransactionCommand.MaxIdempotencyKeyLength))
            .WithMessage($"Send a key of 1 to {CreateTransactionCommand.MaxIdempotencyKeyLength} characters.");
    }
}

/// <summary>What recording a transfer came to: the transfer, or nothing recorded because its idempotency key was already used.</summary>
/// <param name="Recorded">Null when the key was used before, for something else.</param>
public sealed record CreateTransferOutcome(TransferDetails? Recorded)
{
    /// <summary>The idempotency key already recorded a transaction or transfer that is not this one; nothing was recorded.</summary>
    public static readonly CreateTransferOutcome KeyReused = new((TransferDetails?)null);
}

/// <summary>
/// Answers the new transfer, or the one the same request already recorded under its idempotency key, as it
/// is now: edited since, or deleted. An account that is not one of the user's, or is archived, is an error on
/// its id. Both legs are saved together or neither is. When an account's currency or the user's base currency
/// changes before they are saved, the transfer is recorded again against the new one.
/// </summary>
public sealed class CreateTransferHandler(
    IValidator<CreateTransferCommand> validator, ITransactionRepository transactions, TransferContext context, TransactionConversion conversion,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateTransferCommand, CreateTransferOutcome>(validator)
{
    protected override Task<Result<CreateTransferOutcome>> ExecuteAsync(CreateTransferCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => RecordAsync(command, cancellationToken));

    private async Task<Result<CreateTransferOutcome>> RecordAsync(CreateTransferCommand command, CancellationToken cancellationToken)
    {
        var request = IdempotentRequest.Of(command);
        if (request is not null && await transactions.FindByIdempotencyKeyAsync(request.Key, cancellationToken) is { } recorded)
        {
            return await AlreadyRecordedAsync(recorded, request, cancellationToken);
        }

        var read = await context.ReadAsync(command, current: null, cancellationToken);
        if (!read.IsValid)
        {
            return Result<CreateTransferOutcome>.Invalid(read.Errors);
        }

        var between = read.Outcome;
        Transfer transfer;
        try
        {
            transfer = Transfer.Record(between.From, between.To, command.Parsed.Date!.Value, command.Note, between.Today, clock.GetUtcNow());
        }
        catch (TransferRuleViolation violation)
        {
            return Result<CreateTransferOutcome>.Invalid(violation.AsErrors());
        }

        await conversion.ConvertAsync(transfer.OutLeg, between.BaseCurrency, cancellationToken);
        await conversion.ConvertAsync(transfer.InLeg, between.BaseCurrency, cancellationToken);
        transactions.Add(transfer, request);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (AlreadyExistsException) when (request is not null)
        {
            // Two requests with the key, sent at once: the other one recorded first.
            unitOfWork.DiscardChanges();
            return await AlreadyRecordedAsync((await transactions.FindByIdempotencyKeyAsync(request.Key, cancellationToken))!, request, cancellationToken);
        }

        return new CreateTransferOutcome(TransferDetails.Of(transfer));
    }

    /// <summary>The same request again answers what it recorded, deleted since or not; anything else with its key records nothing.</summary>
    private async Task<CreateTransferOutcome> AlreadyRecordedAsync(RecordedRequest recorded, IdempotentRequest request, CancellationToken cancellationToken) =>
        recorded.Fingerprint == request.Fingerprint
        && recorded.Transaction.TransferId is { } transferId
        && await transactions.FindTransferAsync(transferId, deletedToo: true, cancellationToken) is { } transfer
            ? new CreateTransferOutcome(TransferDetails.Of(transfer))
            : CreateTransferOutcome.KeyReused;
}
