using FluentValidation;
using Pfm.Application.Abstractions;
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
    /// <summary>More rows than a 5 MB file holds.</summary>
    public const int MaxRows = 200_000;

    public CommitImportValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.Mapping).NotNull().WithMessage("Send the mapping the statement was previewed with.");
        RuleFor(command => command.Mapping).Custom((mapping, context) =>
        {
            foreach (var (property, messages) in mapping?.Errors() ?? new Dictionary<string, string[]>())
            {
                foreach (var message in messages)
                {
                    context.AddFailure($"{nameof(CommitImportCommand.Mapping)}.{property}", message);
                }
            }
        });
        RuleFor(command => command.Rows).NotNull().WithMessage("Choose the rows to import.")
            .Must(rows => rows!.Count <= MaxRows).WithMessage($"Choose at most {MaxRows} rows.");
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
}

/// <summary>
/// Answers null when the user has no such batch. The statement is read again with the mapping, as the preview read
/// it, so the rows chosen are the rows previewed; the likely duplicates left out are counted. The rows are imported by
/// <see cref="ImportCommits"/>, which the worker runs.
/// </summary>
public sealed class CommitImportHandler(
    IValidator<CommitImportCommand> validator, IImportBatchRepository batches, StatementReader reader, ICategoryLookup categories, IUnitOfWork unitOfWork,
    TimeProvider clock)
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
            return Result<CommitImportOutcome?>.Invalid(FieldError.On("AccountId", UploadStatementHandler.NotAnAccount));
        }

        try
        {
            batch.Commit(mapping, read.Statement, command.Rows!, read.LikelyDuplicates, command.ExpenseCategoryId, command.IncomeCategoryId, clock.GetUtcNow());
        }
        catch (ImportRuleViolation violation)
        {
            var property = violation.Property == nameof(ImportBatch.SelectedRows) ? nameof(command.Rows) : violation.Property;
            return Result<CommitImportOutcome?>.Invalid(FieldError.On(property, violation.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CommitImportOutcome.Committing(ImportBatchDetails.Of(batch));
    }
}
