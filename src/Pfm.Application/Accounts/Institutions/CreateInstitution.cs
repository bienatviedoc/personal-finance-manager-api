using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts.Institutions;

public sealed record CreateInstitutionCommand(string Name, string? Color, string? LogoKey);

public sealed class CreateInstitutionValidator : AbstractValidator<CreateInstitutionCommand>
{
    public CreateInstitutionValidator()
    {
        RuleFor(command => command.Name).InstitutionName();
        RuleFor(command => command.Color).InstitutionColor();
        RuleFor(command => command.LogoKey).InstitutionLogoKey();
    }
}

public sealed class CreateInstitutionHandler(
    IValidator<CreateInstitutionCommand> validator, IInstitutionRepository institutions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateInstitutionCommand, InstitutionDetails>(validator)
{
    protected override async Task<Result<InstitutionDetails>> ExecuteAsync(CreateInstitutionCommand command, CancellationToken cancellationToken)
    {
        var institution = Institution.Create(command.Name, command.Color, command.LogoKey, clock.GetUtcNow());
        institutions.Add(institution);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InstitutionDetails.From(institution);
    }
}
