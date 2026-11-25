using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Transfers;

/// <summary>
/// Unmarks a transfer: its two legs become ordinary transactions again (spec IMP-7), the Transfer out leg an
/// expense and the Transfer in leg income, each on the category the user chooses for it, as a leg has none.
/// Each keeps its id, account, amount, date and note. Any transfer can be unmarked, whether it was marked or
/// recorded as one.
/// </summary>
/// <param name="Id">The transfer id its two legs share.</param>
/// <param name="ExpenseCategoryId">An expense category of the user's that is not archived, for what left the source account.</param>
/// <param name="IncomeCategoryId">An income category of the user's that is not archived, for what arrived on the destination account.</param>
public sealed record UnmarkTransferCommand(Guid Id, Guid ExpenseCategoryId, Guid IncomeCategoryId);

public sealed class UnmarkTransferValidator : AbstractValidator<UnmarkTransferCommand>
{
    public UnmarkTransferValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.ExpenseCategoryId).Required();
        RuleFor(command => command.IncomeCategoryId).Required();
    }
}

/// <summary>The expense and the income a transfer became once unmarked.</summary>
public sealed record UnmarkedTransfer(TransactionDetails Expense, TransactionDetails Income);

/// <summary>Answers null when the user has no transfer with that id, or deleted it. Both legs are saved together.</summary>
public sealed class UnmarkTransferHandler(
    IValidator<UnmarkTransferCommand> validator, ITransactionRepository transactions, ICategoryRepository categories, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<UnmarkTransferCommand, UnmarkedTransfer?>(validator)
{
    /// <summary>An unmark names no row version, like a delete, so one that meets a change made at the same moment is made again on what was saved.</summary>
    protected override Task<Result<UnmarkedTransfer?>> ExecuteAsync(UnmarkTransferCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => UnmarkAsync(command, cancellationToken));

    private async Task<Result<UnmarkedTransfer?>> UnmarkAsync(UnmarkTransferCommand command, CancellationToken cancellationToken)
    {
        if (await transactions.FindTransferAsync(command.Id, deletedToo: false, cancellationToken) is not { } transfer)
        {
            return (UnmarkedTransfer?)null;
        }

        if (await categories.FindAsync(command.ExpenseCategoryId, cancellationToken) is not { } expenseCategory)
        {
            return Result<UnmarkedTransfer?>.Invalid(FieldError.On(nameof(UnmarkTransferCommand.ExpenseCategoryId), Choices.NotACategory));
        }

        if (await categories.FindAsync(command.IncomeCategoryId, cancellationToken) is not { } incomeCategory)
        {
            return Result<UnmarkedTransfer?>.Invalid(FieldError.On(nameof(UnmarkTransferCommand.IncomeCategoryId), Choices.NotACategory));
        }

        Transaction expense, income;
        try
        {
            (expense, income) = transfer.Unmark(expenseCategory, incomeCategory, clock.GetUtcNow());
        }
        catch (TransferRuleViolation violation)
        {
            return Result<UnmarkedTransfer?>.Invalid(FieldError.On(violation.Property + "Id", violation.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new UnmarkedTransfer(TransactionDetails.Of(expense), TransactionDetails.Of(income));
    }
}
