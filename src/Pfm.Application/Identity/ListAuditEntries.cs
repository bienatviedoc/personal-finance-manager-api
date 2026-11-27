using System.Diagnostics.CodeAnalysis;
using Pfm.Application.UseCases;
using Pfm.Domain.Identity;

namespace Pfm.Application.Identity;

/// <summary>The signed-in user's audit log, the newest entry first, a page at a time.</summary>
/// <param name="After">Where the page before ended.</param>
public sealed record ListAuditEntries(int Limit = Paging.DefaultLimit, AuditEntryCursor? After = null);

public sealed class ListAuditEntriesHandler(IAuditEntryRepository entries) : IQueryHandler
{
    public async Task<Page<AuditEntryDetails>> HandleAsync(ListAuditEntries query, CancellationToken cancellationToken = default)
    {
        // One more than the page holds says whether another page follows.
        var found = await entries.ListNewestFirstAsync(query.Limit + 1, query.After?.Id, cancellationToken);
        var page = found.Take(query.Limit).ToList();
        return new Page<AuditEntryDetails>(
            [.. page.Select(AuditEntryDetails.Of)],
            found.Count > query.Limit ? new AuditEntryCursor(page[^1].Id).ToString() : null);
    }
}

/// <summary>One audit entry, as the user reads it.</summary>
public sealed record AuditEntryDetails(Guid Id, AuditAction Action, AuditEntityType EntityType, Guid? EntityId, DateTimeOffset At, string Summary)
{
    public static AuditEntryDetails Of(AuditEntry entry) => new(entry.Id, entry.Action, entry.EntityType, entry.EntityId, entry.At, entry.Summary);
}

/// <summary>Where a page of audit entries ends: the id of its last one, opaque to clients.</summary>
public sealed record AuditEntryCursor(Guid Id)
{
    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out AuditEntryCursor? parsed)
    {
        parsed = null;
        if (!CursorText.TryDecode(cursor, out var text) || !Guid.TryParseExact(text, "N", out var id))
        {
            return false;
        }

        parsed = new AuditEntryCursor(id);
        return true;
    }

    public override string ToString() => CursorText.Encode(Id.ToString("N"));
}
