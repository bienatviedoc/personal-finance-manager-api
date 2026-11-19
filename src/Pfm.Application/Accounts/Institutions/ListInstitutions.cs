using Pfm.Application.UseCases;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>
/// A page of the user's institutions by name, starting after <paramref name="After"/>. Archived ones are
/// left out unless asked for.
/// </summary>
public sealed record ListInstitutions(bool IncludeArchived = false, int Limit = Paging.DefaultLimit, NameCursor? After = null);

public sealed class ListInstitutionsHandler(IInstitutionRepository institutions) : IQueryHandler
{
    /// <summary>A user has dozens of institutions, not thousands, so the page is cut from all of them.</summary>
    public async Task<Page<InstitutionDetails>> HandleAsync(ListInstitutions query, CancellationToken cancellationToken = default)
    {
        var found = await institutions.ListAsync(query.IncludeArchived, cancellationToken);
        return found
            .OrderByName(institution => institution.Name, institution => institution.Id)
            .PageByName(institution => institution.Name, institution => institution.Id, query.Limit, query.After, InstitutionDetails.From);
    }
}
