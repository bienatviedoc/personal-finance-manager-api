using Pfm.Application.UseCases;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>The user's institutions by name. Archived ones are left out unless asked for.</summary>
public sealed record ListInstitutions(bool IncludeArchived = false);

public sealed class ListInstitutionsHandler(IInstitutionRepository institutions) : IQueryHandler
{
    public async Task<IReadOnlyList<InstitutionDetails>> HandleAsync(ListInstitutions query, CancellationToken cancellationToken = default)
    {
        var found = await institutions.ListAsync(query.IncludeArchived, cancellationToken);
        return [.. found
            .OrderByName(institution => institution.Name, institution => institution.Id)
            .Select(InstitutionDetails.From)];
    }
}
