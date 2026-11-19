using FluentValidation;
using Pfm.Application.Abstractions;

namespace Pfm.Application.Accounts.Institutions;

public sealed record EditInstitution(Guid Id, string Name, string? Color, string? LogoKey);

public sealed class EditInstitutionValidator : AbstractValidator<EditInstitution>
{
    public EditInstitutionValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).InstitutionName();
        RuleFor(command => command.Color).InstitutionColor();
        RuleFor(command => command.LogoKey).InstitutionLogoKey();
    }
}

public sealed class EditInstitutionHandler(
    IValidator<EditInstitution> validator, IInstitutionRepository institutions, IUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>The edited institution, or null when the user has no institution with that id.</summary>
    public async Task<InstitutionDetails?> HandleAsync(EditInstitution command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var institution = await institutions.FindAsync(command.Id, cancellationToken);
        if (institution is null)
        {
            return null;
        }

        institution.Edit(command.Name, command.Color, command.LogoKey, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InstitutionDetails.From(institution);
    }
}
