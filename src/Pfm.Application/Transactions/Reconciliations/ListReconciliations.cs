using System.Diagnostics.CodeAnalysis;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>A page of an account's reconciliations, the last started first, starting after <paramref name="After"/>.</summary>
public sealed record ListReconciliations(Guid AccountId, int Limit = Paging.DefaultLimit, ReconciliationCursor? After = null);

public sealed class ListReconciliationsHandler(IAccountLookup accounts, IReconciliationRepository reconciliations, ReconciliationContext context) : IQueryHandler
{
    /// <summary>Null when the user has no account with that id.</summary>
    public async Task<Page<ReconciliationDetails>?> HandleAsync(ListReconciliations query, CancellationToken cancellationToken = default)
    {
        if (await accounts.FindAsync(query.AccountId, cancellationToken) is null)
        {
            return null;
        }

        // One more than the page holds says whether another page follows.
        var found = await reconciliations.ListAsync(query.AccountId, query.Limit + 1, query.After?.Id, cancellationToken);
        var items = new List<ReconciliationDetails>();
        foreach (var reconciliation in found.Take(query.Limit))
        {
            items.Add(await context.DetailsAsync(reconciliation, cancellationToken));
        }

        return new Page<ReconciliationDetails>(items, found.Count > query.Limit ? new ReconciliationCursor(found[query.Limit - 1].Id).ToString() : null);
    }
}

/// <summary>Where a page of reconciliations ends: the id of its last one, opaque to clients. Ids are in the order reconciliations were started.</summary>
public sealed record ReconciliationCursor(Guid Id)
{
    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out ReconciliationCursor? parsed)
    {
        parsed = null;
        if (!CursorText.TryDecode(cursor, out var text) || !Guid.TryParseExact(text, "N", out var id))
        {
            return false;
        }

        parsed = new ReconciliationCursor(id);
        return true;
    }

    public override string ToString() => CursorText.Encode(Id.ToString("N"));
}
