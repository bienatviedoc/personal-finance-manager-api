using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Pfm.Application.UseCases;
using Pfm.Domain.Imports;

namespace Pfm.Application.Imports;

/// <summary>
/// A page of the user's past import batches: every statement they committed, whatever became of it (committing,
/// committed, failed or undone), the last uploaded first. A statement uploaded and not committed is not one yet.
/// </summary>
/// <param name="AccountId">Only the batches of this account; every account's when null.</param>
public sealed record ListImportBatches(Guid? AccountId = null, int Limit = Paging.DefaultLimit, ImportBatchCursor? After = null);

public sealed class ListImportBatchesHandler(IImportBatchRepository batches) : IQueryHandler
{
    public async Task<Page<ImportBatchDetails>> HandleAsync(ListImportBatches query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // One more than the page holds says whether another page follows.
        var found = await batches.ListPastAsync(query.AccountId, query.Limit + 1, query.After, cancellationToken);
        var next = found.Count > query.Limit ? ImportBatchCursor.At(found[query.Limit - 1]).ToString() : null;
        return new Page<ImportBatchDetails>([.. found.Take(query.Limit).Select(batch => ImportBatchDetails.Of(batch))], next);
    }
}

/// <summary>Where a page of past import batches ends: when its last one was uploaded, and its id; opaque to clients.</summary>
public sealed record ImportBatchCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public static ImportBatchCursor At(ImportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return new(batch.CreatedAt, batch.Id);
    }

    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out ImportBatchCursor? parsed)
    {
        parsed = null;
        if (!CursorText.TryDecode(cursor, out var text)
            || text.Length <= 32
            || !Guid.TryParseExact(text[..32], "N", out var id)
            || !long.TryParse(text[32..], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        parsed = new ImportBatchCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }

    public override string ToString() =>
        CursorText.Encode($"{Id:N}{CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}");
}
