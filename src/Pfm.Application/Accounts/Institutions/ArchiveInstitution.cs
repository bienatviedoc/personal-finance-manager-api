using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Accounts.Institutions;

/// <summary>What deleting an institution does: it is archived, never removed.</summary>
public sealed record ArchiveInstitutionCommand(Guid Id);

public sealed class ArchiveInstitutionValidator : AbstractValidator<ArchiveInstitutionCommand>
{
    public ArchiveInstitutionValidator() => RuleFor(command => command.Id).Required();
}

public enum ArchiveInstitutionOutcome
{
    /// <summary>Archived now, or already archived.</summary>
    Archived,

    /// <summary>The user has no institution with that id.</summary>
    NotFound,
}

public sealed class ArchiveInstitutionHandler(
    IValidator<ArchiveInstitutionCommand> validator, IInstitutionRepository institutions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ArchiveInstitutionCommand, ArchiveInstitutionOutcome>(validator)
{
    protected override async Task<Result<ArchiveInstitutionOutcome>> ExecuteAsync(ArchiveInstitutionCommand command, CancellationToken cancellationToken)
    {
        var institution = await institutions.FindAsync(command.Id, cancellationToken);
        if (institution is null)
        {
            return ArchiveInstitutionOutcome.NotFound;
        }

        institution.Archive(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ArchiveInstitutionOutcome.Archived;
    }
}
