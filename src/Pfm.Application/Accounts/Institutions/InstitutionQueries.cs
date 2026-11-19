namespace Pfm.Application.Accounts.Institutions;

/// <summary>The user's institutions by name. Archived ones are left out unless asked for.</summary>
public sealed record ListInstitutions(bool IncludeArchived = false);

public sealed class ListInstitutionsHandler(IInstitutionRepository institutions)
{
    public async Task<IReadOnlyList<InstitutionDetails>> HandleAsync(ListInstitutions query, CancellationToken cancellationToken = default)
    {
        var found = await institutions.ListAsync(query.IncludeArchived, cancellationToken);
        return [.. found
            .OrderBy(institution => institution.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(institution => institution.Id)
            .Select(InstitutionDetails.From)];
    }
}

/// <summary>One of the user's institutions, archived or not, so accounts that use it can still show it.</summary>
public sealed record GetInstitution(Guid Id);

public sealed class GetInstitutionHandler(IInstitutionRepository institutions)
{
    public async Task<InstitutionDetails?> HandleAsync(GetInstitution query, CancellationToken cancellationToken = default) =>
        await institutions.FindAsync(query.Id, cancellationToken) is { } institution ? InstitutionDetails.From(institution) : null;
}
