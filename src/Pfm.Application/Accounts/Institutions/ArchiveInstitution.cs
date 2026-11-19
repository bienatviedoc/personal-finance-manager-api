using FluentValidation;
using Pfm.Application.Abstractions;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>What deleting an institution does: it is archived, never removed.</summary>
public sealed record ArchiveInstitution(Guid Id);

public sealed class ArchiveInstitutionValidator : AbstractValidator<ArchiveInstitution>
{
    public ArchiveInstitutionValidator() => RuleFor(command => command.Id).NotEmpty();
}

public sealed class ArchiveInstitutionHandler(
    IValidator<ArchiveInstitution> validator, IInstitutionRepository institutions, IUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>False when the user has no institution with that id.</summary>
    public async Task<bool> HandleAsync(ArchiveInstitution command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var institution = await institutions.FindAsync(command.Id, cancellationToken);
        if (institution is null)
        {
            return false;
        }

        institution.Archive(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
