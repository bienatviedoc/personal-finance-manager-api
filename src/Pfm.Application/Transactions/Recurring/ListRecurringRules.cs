using System.Diagnostics.CodeAnalysis;
using Pfm.Application.UseCases;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>A page of the user's recurring rules, the last set up first, starting after <paramref name="After"/>.</summary>
public sealed record ListRecurringRules(int Limit = Paging.DefaultLimit, RecurringRuleCursor? After = null);

public sealed class ListRecurringRulesHandler(IRecurringRuleRepository rules, RecurringRuleContext context) : IQueryHandler
{
    public async Task<Page<RecurringRuleDetails>> HandleAsync(ListRecurringRules query, CancellationToken cancellationToken = default)
    {
        // One more than the page holds says whether another page follows.
        var found = await rules.ListAsync(query.Limit + 1, query.After?.Id, cancellationToken);
        var items = new List<RecurringRuleDetails>();
        foreach (var rule in found.Take(query.Limit))
        {
            items.Add(await context.DetailsAsync(rule, cancellationToken));
        }

        return new Page<RecurringRuleDetails>(items, found.Count > query.Limit ? new RecurringRuleCursor(found[query.Limit - 1].Id).ToString() : null);
    }
}

/// <summary>Where a page of recurring rules ends: the id of its last one, opaque to clients. Ids are in the order rules were set up.</summary>
public sealed record RecurringRuleCursor(Guid Id)
{
    /// <summary>A cursor this list gave out; anything else is not one.</summary>
    public static bool TryParse(string? cursor, [NotNullWhen(true)] out RecurringRuleCursor? parsed)
    {
        parsed = null;
        if (!CursorText.TryDecode(cursor, out var text) || !Guid.TryParseExact(text, "N", out var id))
        {
            return false;
        }

        parsed = new RecurringRuleCursor(id);
        return true;
    }

    public override string ToString() => CursorText.Encode(Id.ToString("N"));
}
