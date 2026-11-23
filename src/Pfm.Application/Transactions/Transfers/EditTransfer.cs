using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// Changes a transfer, both legs in one save, from the row version the caller last read
/// (<paramref name="RowVersion"/>). Moved to another account, a leg takes that account's currency.
/// </summary>
/// <param name="Id">The transfer id its two legs share.</param>
public sealed record EditTransferCommand(
    Guid Id, uint RowVersion, Guid FromAccountId, Guid ToAccountId, string? Amount, string? ToAmount, string? Date, string? Note)
    : ITransferCommand
{
    public TransferFields Parsed { get; } = TransferFields.Read(Amount, ToAmount, Date);
}

public sealed class EditTransferValidator : AbstractValidator<EditTransferCommand>
{
    public EditTransferValidator()
    {
        RuleFor(command => command.Id).Required();
        this.TransferFieldRules();
    }
}

/// <summary>What editing a transfer came to: the edited transfer, or nothing saved because it had changed.</summary>
/// <param name="Edited">Null when the edit was made from a stale row version.</param>
public sealed record EditTransferOutcome(TransferDetails? Edited)
{
    /// <summary>The transfer changed since the row version the edit was made from; nothing was saved.</summary>
    public static readonly EditTransferOutcome Stale = new((TransferDetails?)null);
}

/// <summary>
/// Answers the edited transfer; null when the user has no transfer with that id, or deleted it. It may stay
/// on an account that has since been archived, but not move to one.
/// </summary>
public sealed class EditTransferHandler(
    IValidator<EditTransferCommand> validator, ITransactionRepository transactions, TransferContext context, TransactionConversion conversion,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<EditTransferCommand, EditTransferOutcome?>(validator)
{
    protected override async Task<Result<EditTransferOutcome?>> ExecuteAsync(EditTransferCommand command, CancellationToken cancellationToken)
    {
        try
        {
            // Made again when an account's currency or the user's base currency changed before it was saved.
            // When it was the transfer that changed, the row version is no longer its own and the edit is stale.
            return await unitOfWork.RetryOnConcurrentChangeAsync(() => EditAsync(command, cancellationToken));
        }
        catch (ConcurrentChangeException)
        {
            return EditTransferOutcome.Stale;
        }
    }

    private async Task<Result<EditTransferOutcome?>> EditAsync(EditTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await transactions.FindTransferAsync(command.Id, deletedToo: false, cancellationToken);
        if (transfer is null)
        {
            return (EditTransferOutcome?)null;
        }

        if (transfer.RowVersion != command.RowVersion)
        {
            return EditTransferOutcome.Stale;
        }

        var read = await context.ReadAsync(command, transfer, cancellationToken);
        if (!read.IsValid)
        {
            return Result<EditTransferOutcome?>.Invalid(read.Errors);
        }

        var between = read.Outcome;
        try
        {
            transfer.Edit(between.From, between.To, command.Parsed.Date!.Value, command.Note, between.Today, clock.GetUtcNow());
        }
        catch (TransferRuleViolation violation)
        {
            return Result<EditTransferOutcome?>.Invalid(violation.AsErrors());
        }

        // Converts again only a leg whose amount, date or account the edit changed.
        await conversion.ConvertAsync(transfer.OutLeg, between.BaseCurrency, cancellationToken);
        await conversion.ConvertAsync(transfer.InLeg, between.BaseCurrency, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new EditTransferOutcome(TransferDetails.Of(transfer));
    }
}
