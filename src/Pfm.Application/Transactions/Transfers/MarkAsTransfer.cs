using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// Marks an expense on one account and income on another as one transfer (spec IMP-7; decision record 0001),
/// such as the two rows imported statements show for paying a card: the expense becomes the Transfer out leg
/// and the income the Transfer in leg, so they drop out of income, expense and budget totals. Nothing is
/// matched automatically: the user picks both.
/// </summary>
/// <param name="ExpenseId">The expense, which is not a refund: what left one account.</param>
/// <param name="IncomeId">The income on another account, of the same amount between accounts in one currency, and on the same date.</param>
public sealed record MarkAsTransferCommand(Guid ExpenseId, Guid IncomeId);

public sealed class MarkAsTransferValidator : AbstractValidator<MarkAsTransferCommand>
{
    public MarkAsTransferValidator()
    {
        RuleFor(command => command.ExpenseId).Required();
        RuleFor(command => command.IncomeId).Required();
    }
}

/// <summary>
/// Answers the transfer the two became. A transaction that is not one of the user's, or is deleted, is an error
/// on its id, as is one that breaks a rule of a transfer; neither changes then. Both legs are saved together.
/// </summary>
public sealed class MarkAsTransferHandler(
    IValidator<MarkAsTransferCommand> validator, ITransactionRepository transactions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<MarkAsTransferCommand, TransferDetails>(validator)
{
    /// <summary>Made again on what was saved when either transaction changes at the same moment, so the rules are checked against it.</summary>
    protected override Task<Result<TransferDetails>> ExecuteAsync(MarkAsTransferCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => MarkAsync(command, cancellationToken));

    private async Task<Result<TransferDetails>> MarkAsync(MarkAsTransferCommand command, CancellationToken cancellationToken)
    {
        if (await transactions.FindAsync(command.ExpenseId, cancellationToken) is not { } expense)
        {
            return Result<TransferDetails>.Invalid(FieldError.On(nameof(MarkAsTransferCommand.ExpenseId), Choices.NotATransaction));
        }

        if (await transactions.FindAsync(command.IncomeId, cancellationToken) is not { } income)
        {
            return Result<TransferDetails>.Invalid(FieldError.On(nameof(MarkAsTransferCommand.IncomeId), Choices.NotATransaction));
        }

        Transfer transfer;
        try
        {
            transfer = Transfer.Mark(expense, income, clock.GetUtcNow());
        }
        catch (TransferRuleViolation violation)
        {
            return Result<TransferDetails>.Invalid(violation.AsErrors());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TransferDetails.Of(transfer);
    }
}
