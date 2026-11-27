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
/// <param name="RowCategories">
/// Categories of their own for some of the chosen rows (spec IMP-4), each of the kind its row takes; the batch's
/// categories are for the rest, so each is needed only for a chosen row without one of its own.
/// </param>
public sealed record CommitImportCommand(
    Guid Id, MappingInput? Mapping, IReadOnlyList<int>? Rows, Guid? ExpenseCategoryId, Guid? IncomeCategoryId,
    IReadOnlyList<RowCategoryInput>? RowCategories);

/// <summary>The category the user gave one chosen row of a statement.</summary>
/// <param name="Row">The row's number, as the preview numbers it.</param>
public sealed record RowCategoryInput(int Row, Guid CategoryId);

public sealed class CommitImportValidator : AbstractValidator<CommitImportCommand>
{
    public CommitImportValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.Mapping).NotNull().WithMessage("Send the mapping the statement was previewed with.");
        RuleFor(command => command.Mapping).MappingInRange(under: $"{nameof(CommitImportCommand.Mapping)}.");
        RuleFor(command => command.Rows).NotNull().WithMessage("Choose the rows to import.")
            .Must(rows => rows!.Count <= ImportBatch.MaxRows).WithMessage($"Choose at most {ImportBatch.MaxRows} rows.");
        RuleFor(command => command.RowCategories).Must(rows => rows is null || rows.Count <= ImportBatch.MaxRows)
            .WithMessage($"Give at most {ImportBatch.MaxRows} rows a category.");
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
    IValidator<CommitImportCommand> validator, IImportBatchRepository batches, IImportMappingRepository mappings, StatementReader reader, ICategoryLookup categories,
    IAuditLog auditLog, IUnitOfWork unitOfWork, TimeProvider clock)
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

        var (rowCategories, problem) = RowCategoriesOf(command, read.Statement, userCategories);
        if (problem is not null)
        {
            return Result<CommitImportOutcome?>.Invalid(FieldError.On(nameof(command.RowCategories), problem));
        }

        try
        {
            batch.Commit(
                mapping, read.Statement, command.Rows!, read.LikelyDuplicates, command.ExpenseCategoryId, command.IncomeCategoryId, read.Today, clock.GetUtcNow(),
                rowCategories);
        }
        catch (ImportRuleViolation violation)
        {
            var property = violation.Property switch
            {
                nameof(ImportBatch.SelectedRows) => nameof(command.Rows),
                nameof(ImportBatch.RowCategories) => nameof(command.RowCategories),
                _ => violation.Property,
            };
            return Result<CommitImportOutcome?>.Invalid(FieldError.On(property, violation.Message));
        }

        // Saved for the account with the commit, with its categories, for its next statement (spec IMP-2).
        if (await mappings.FindAsync(batch.AccountId, cancellationToken) is { } saved)
        {
            saved.Change(mapping, command.ExpenseCategoryId, command.IncomeCategoryId, clock.GetUtcNow());
        }
        else
        {
            mappings.Add(ImportMapping.Save(batch.AccountId, mapping, command.ExpenseCategoryId, command.IncomeCategoryId, clock.GetUtcNow()));
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
            // Another statement was committed into the account meanwhile: one batch at a time is. (Another commit that
            // saved the account's first mapping meanwhile is a concurrent change instead: the commit is made again.)
            unitOfWork.DiscardChanges();
            return new CommitImportOutcome.ImportInProgress();
        }

        return new CommitImportOutcome.Committing(ImportBatchDetails.Of(batch));
    }

    /// <summary>
    /// The rows' own categories by row number, when each row is given one once, on one of the user's categories that is
    /// not archived, of the kind the row takes; else the problem. Whether each row is chosen and read as a transaction is
    /// the batch's to say.
    /// </summary>
    private static (Dictionary<int, Guid> Categories, string? Problem) RowCategoriesOf(
        CommitImportCommand command, Statement statement, IReadOnlyDictionary<Guid, CategorySummary> userCategories)
    {
        var entries = statement.Entries.ToDictionary(row => row.Number, row => row.Entry!);
        var rowCategories = new Dictionary<int, Guid>();
        foreach (var (row, categoryId) in command.RowCategories ?? [])
        {
            if (!rowCategories.TryAdd(row, categoryId))
            {
                return (rowCategories, $"Give row {row} one category.");
            }

            if (!entries.TryGetValue(row, out var entry))
            {
                continue;
            }

            var expense = entry.Type == StatementEntryType.Expense;
            if (!userCategories.TryGetValue(categoryId, out var category) || category.IsArchived || category.IsExpense != expense)
            {
                return (rowCategories, $"Row {row} takes one of your {(expense ? "expense" : "income")} categories that is not archived.");
            }
        }

        return (rowCategories, null);
    }
}
