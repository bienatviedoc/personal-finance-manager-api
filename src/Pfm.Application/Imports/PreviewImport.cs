using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>
/// An uploaded statement read with a mapping, before anything is saved (spec IMP-1): every row, the ones that
/// cannot be read with why, and the likely duplicates (IMP-3) marked and unticked.
/// </summary>
public sealed record PreviewImport(Guid Id, StatementMapping Mapping);

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
public sealed class PreviewImportHandler(IImportBatchRepository batches, StatementReader reader) : IQueryHandler
{
    public async Task<PreviewImportOutcome?> HandleAsync(PreviewImport query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await batches.FindAsync(query.Id, cancellationToken) is not { } batch)
        {
            return null;
        }

        if (batch.Status != ImportBatchStatus.Uploaded)
        {
            return new PreviewImportOutcome.AlreadyCommitted();
        }

        var read = await reader.ReadAsync(batch, query.Mapping, cancellationToken);
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
