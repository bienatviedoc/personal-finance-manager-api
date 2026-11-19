using Pfm.Application.UseCases;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>One of the user's institutions, archived or not, so accounts that use it can still show it.</summary>
public sealed record GetInstitution(Guid Id);

public sealed class GetInstitutionHandler(IInstitutionRepository institutions) : IQueryHandler
{
    public async Task<InstitutionDetails?> HandleAsync(GetInstitution query, CancellationToken cancellationToken = default) =>
        await institutions.FindAsync(query.Id, cancellationToken) is { } institution ? InstitutionDetails.From(institution) : null;
}
