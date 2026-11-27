using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>
/// Commits an uploaded statement (spec IMP-1): the rows chosen in the preview are imported in the background, each
/// as income or an expense on the batch's account, belonging to the batch.
/// </summary>
/// <param name="Mapping">The mapping the user previewed the statement with.</param>
/// <param name="Rows">The numbers of the rows to import: rows the preview reads as transactions, likely duplicates only when ticked.</param>
/// <param name="ExpenseCategoryId">The expense category money going out is recorded on; needed when a row chosen takes money out.</param>
/// <param name="IncomeCategoryId">The income category money coming in is recorded on; needed when a row chosen brings money in.</param>
public sealed record CommitImportCommand(Guid Id, MappingInput? Mapping, IReadOnlyList<int>? Rows, Guid? ExpenseCategoryId, Guid? IncomeCategoryId);

public sealed class CommitImportValidator : AbstractValidator<CommitImportCommand>
{
    public CommitImportValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.Mapping).NotNull().WithMessage("Send the mapping the statement was previewed with.");
        RuleFor(command => command.Mapping).MappingInRange(under: $"{nameof(CommitImportCommand.Mapping)}.");
        RuleFor(command => command.Rows).NotNull().WithMessage("Choose the rows to import.")
            .Must(rows => rows!.Count <= ImportBatch.MaxRows).WithMessage($"Choose at most {ImportBatch.MaxRows} rows.");
    }
}

/// <summary>What committing a batch came to.</summary>
public abstract record CommitImportOutcome
{
    private CommitImportOutcome()
    {
    }

    /// <summary>Queued for the worker, which imports the chosen rows a few at a time.</summary>
    public sealed record Committing(ImportBatchDetails Batch) : CommitImportOutcome;

    /// <summary>The batch had been committed already; nothing changed.</summary>
    public sealed record AlreadyCommitted : CommitImportOutcome;

    /// <summary>
    /// Another statement is being imported into the account; nothing changed. Once it is, the preview shows which of
    /// this one's rows it already brought in, as likely duplicates (spec IMP-3).
    /// </summary>
    public sealed record ImportInProgress : CommitImportOutcome;
}

/// <summary>
/// Answers null when the user has no such batch. The statement is read again with the mapping, as the preview read
/// it, so the rows chosen are the rows previewed; the likely duplicates left out are counted. The rows are imported by
/// <see cref="ImportCommits"/>, which the worker runs. One batch at a time is committed into an account: while
/// another is, committing answers <see cref="CommitImportOutcome.ImportInProgress"/>, so the second statement's
/// rows are checked against the first's once it is imported, and a statement committed twice is not imported twice.
/// The commit is recorded in the user's audit log in the same save.
/// </summary>
public sealed class CommitImportHandler(
    IValidator<CommitImportCommand> validator, IImportBatchRepository batches, StatementReader reader, ICategoryLookup categories, IAuditLog auditLog,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CommitImportCommand, CommitImportOutcome?>(validator)
{
    protected override Task<Result<CommitImportOutcome?>> ExecuteAsync(CommitImportCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => CommitAsync(command, cancellationToken));

    private async Task<Result<CommitImportOutcome?>> CommitAsync(CommitImportCommand command, CancellationToken cancellationToken)
    {
        if (await batches.FindAsync(command.Id, cancellationToken) is not { } batch)
        {
            return (CommitImportOutcome?)null;
        }

        if (batch.Status != ImportBatchStatus.Uploaded)
        {
            return new CommitImportOutcome.AlreadyCommitted();
        }

        var userCategories = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id);
        foreach (var (property, id, expense) in new[]
                 {
                     (nameof(command.ExpenseCategoryId), command.ExpenseCategoryId, true), (nameof(command.IncomeCategoryId), command.IncomeCategoryId, false),
                 })
        {
            if (id is { } categoryId && (!userCategories.TryGetValue(categoryId, out var category) || category.IsArchived || category.IsExpense != expense))
            {
                return Result<CommitImportOutcome?>.Invalid(FieldError.On(
                    property, $"Choose one of your {(expense ? "expense" : "income")} categories that is not archived."));
            }
        }

        var mapping = command.Mapping!.Read()!;
        var read = await reader.ReadAsync(batch, mapping, cancellationToken);
        if (read.Account.IsArchived)
        {
            return Result<CommitImportOutcome?>.Invalid(FieldError.On(
                nameof(command.Id), "The statement's account has been archived: upload it again for one of your accounts that is not."));
        }

        try
        {
            batch.Commit(mapping, read.Statement, command.Rows!, read.LikelyDuplicates, command.ExpenseCategoryId, command.IncomeCategoryId, read.Today, clock.GetUtcNow());
        }
        catch (ImportRuleViolation violation)
        {
            var property = violation.Property == nameof(ImportBatch.SelectedRows) ? nameof(command.Rows) : violation.Property;
            return Result<CommitImportOutcome?>.Invalid(FieldError.On(property, violation.Message));
        }

        if (await batches.IsCommittingIntoAsync(batch.AccountId, cancellationToken))
        {
            unitOfWork.DiscardChanges();
            return new CommitImportOutcome.ImportInProgress();
        }

        // Saved with the commit; dropped with it when another statement got in first.
        await auditLog.ImportCommittedAsync(batch.Id, batch.FileName, batch.SelectedCount, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (AlreadyExistsException)
        {
            // Another statement was committed into the account meanwhile: one batch at a time is.
            unitOfWork.DiscardChanges();
            return new CommitImportOutcome.ImportInProgress();
        }

        return new CommitImportOutcome.Committing(ImportBatchDetails.Of(batch));
    }
}
