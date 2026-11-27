using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>
/// An uploaded statement read with a mapping, before anything is saved (spec IMP-1): every row, the ones that
/// cannot be read with why, and the likely duplicates (IMP-3) marked and unticked. Nothing changes, but the mapping
/// is the user's input, so it is a command with a validator.
/// </summary>
/// <param name="Mapping">The mapping as the user sent it.</param>
public sealed record PreviewImportCommand(Guid Id, MappingInput? Mapping);

public sealed class PreviewImportValidator : AbstractValidator<PreviewImportCommand>
{
    public PreviewImportValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.Mapping).NotNull().WithMessage("Send the mapping to read the statement with.");
        // The preview's query string sends each choice on its own, so each error is on the choice.
        RuleFor(command => command.Mapping).MappingInRange();
    }
}

/// <summary>What previewing a batch came to.</summary>
public abstract record PreviewImportOutcome
{
    private PreviewImportOutcome()
    {
    }

    public sealed record Ready(ImportPreview Preview) : PreviewImportOutcome;

    /// <summary>The batch has been committed: its file is no longer kept, so there is nothing to preview.</summary>
    public sealed record AlreadyCommitted : PreviewImportOutcome;
}

/// <summary>A statement as the preview shows it.</summary>
/// <param name="Header">The cells of the header row; none without one.</param>
/// <param name="ColumnCount">The most cells any row has: the columns there are to map.</param>
/// <param name="Currency">The account's, which every amount is in.</param>
public sealed record ImportPreview(IReadOnlyList<string> Header, int ColumnCount, Currency Currency, IReadOnlyList<PreviewRow> Rows)
{
    public int Parsed => Rows.Count(row => row.Row.Kind == StatementRowKind.Parsed);

    public int Failed => Rows.Count(row => row.Row.Kind == StatementRowKind.Failed);

    public int LikelyDuplicates => Rows.Count(row => row.LikelyDuplicate);

    /// <summary>The rows ticked to import at first: every row read as a transaction but the likely duplicates.</summary>
    public int Selected => Rows.Count(row => row.Selected);
}

/// <summary>One row of the preview.</summary>
/// <param name="Selected">Ticked to import at first: read as a transaction and not a likely duplicate.</param>
public sealed record PreviewRow(StatementRow Row, bool LikelyDuplicate, bool Selected);

/// <summary>Answers null when the user has no such batch.</summary>
public sealed class PreviewImportHandler(IValidator<PreviewImportCommand> validator, IImportBatchRepository batches, StatementReader reader)
    : CommandHandler<PreviewImportCommand, PreviewImportOutcome?>(validator)
{
    protected override async Task<Result<PreviewImportOutcome?>> ExecuteAsync(PreviewImportCommand command, CancellationToken cancellationToken)
    {
        if (await batches.FindAsync(command.Id, cancellationToken) is not { } batch)
        {
            return (PreviewImportOutcome?)null;
        }

        if (batch.Status != ImportBatchStatus.Uploaded)
        {
            return new PreviewImportOutcome.AlreadyCommitted();
        }

        var read = await reader.ReadAsync(batch, command.Mapping!.Read()!, cancellationToken);
        var rows = read.Statement.Rows
            .Select(row =>
            {
                var duplicate = read.LikelyDuplicates.Contains(row.Number);
                return new PreviewRow(row, duplicate, row.Kind == StatementRowKind.Parsed && !duplicate);
            })
            .ToList();
        return new PreviewImportOutcome.Ready(new ImportPreview(read.Statement.Header, read.Statement.ColumnCount, read.Account.Currency, rows));
    }
}
