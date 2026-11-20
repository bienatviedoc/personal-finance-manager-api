using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts.Institutions;

public sealed record CreateInstitutionCommand(string Name, string? Color, string? LogoKey)
{
    public string Name { get; } = Name?.Trim() ?? "";

    /// <summary>The colour sent, read once; null when none was sent or it is not a colour.</summary>
    public Domain.Color? ParsedColor { get; } = Domain.Color.TryParse(Color, out var color) ? color : null;
}

public sealed class CreateInstitutionValidator : AbstractValidator<CreateInstitutionCommand>
{
    public CreateInstitutionValidator()
    {
        RuleFor(command => command.Name).IsName();
        RuleFor(command => command.Color).IsColor(command => command.ParsedColor, optional: true);
        RuleFor(command => command.LogoKey).InstitutionLogoKey();
    }
}

public sealed class CreateInstitutionHandler(
    IValidator<CreateInstitutionCommand> validator, IInstitutionRepository institutions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<CreateInstitutionCommand, InstitutionDetails>(validator)
{
    protected override async Task<Result<InstitutionDetails>> ExecuteAsync(CreateInstitutionCommand command, CancellationToken cancellationToken)
    {
        var institution = Institution.Create(command.Name, command.ParsedColor, command.LogoKey, clock.GetUtcNow());
        institutions.Add(institution);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InstitutionDetails.From(institution);
    }
}
