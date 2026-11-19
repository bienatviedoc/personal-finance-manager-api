using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>An institution as the use cases return it.</summary>
public sealed record InstitutionDetails(Guid Id, string Name, string? Color, string? LogoKey, bool IsArchived)
{
    public static InstitutionDetails From(Institution institution) =>
        new(institution.Id, institution.Name, institution.Color?.Hex, institution.LogoKey, institution.IsArchived);
}
