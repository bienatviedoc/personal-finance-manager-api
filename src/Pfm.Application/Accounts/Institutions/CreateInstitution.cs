using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts.Institutions;

public sealed record CreateInstitution(string Name, string? Color, string? LogoKey);

public sealed class CreateInstitutionValidator : AbstractValidator<CreateInstitution>
{
    public CreateInstitutionValidator()
    {
        RuleFor(command => command.Name).InstitutionName();
        RuleFor(command => command.Color).InstitutionColor();
        RuleFor(command => command.LogoKey).InstitutionLogoKey();
    }
}

public sealed class CreateInstitutionHandler(
    IValidator<CreateInstitution> validator, IInstitutionRepository institutions, IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<InstitutionDetails> HandleAsync(CreateInstitution command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var institution = Institution.Create(command.Name, command.Color, command.LogoKey, clock.GetUtcNow());
        institutions.Add(institution);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InstitutionDetails.From(institution);
    }
}
