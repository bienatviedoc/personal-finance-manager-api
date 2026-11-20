using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Accounts.Institutions;

public sealed record EditInstitutionCommand(Guid Id, string Name, string? Color, string? LogoKey)
{
    public string Name { get; } = Name?.Trim() ?? "";

    /// <summary>The colour sent, read once; null when none was sent or it is not a colour.</summary>
    public Domain.Color? ParsedColor { get; } = Domain.Color.TryParse(Color, out var color) ? color : null;
}

public sealed class EditInstitutionValidator : AbstractValidator<EditInstitutionCommand>
{
    public EditInstitutionValidator()
    {
        RuleFor(command => command.Id).Required();
        RuleFor(command => command.Name).IsName();
        RuleFor(command => command.Color).IsColor(command => command.ParsedColor, optional: true);
        RuleFor(command => command.LogoKey).InstitutionLogoKey();
    }
}

/// <summary>Answers the edited institution, or null when the user has no institution with that id.</summary>
public sealed class EditInstitutionHandler(
    IValidator<EditInstitutionCommand> validator, IInstitutionRepository institutions, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<EditInstitutionCommand, InstitutionDetails?>(validator)
{
    protected override async Task<Result<InstitutionDetails?>> ExecuteAsync(EditInstitutionCommand command, CancellationToken cancellationToken)
    {
        var institution = await institutions.FindAsync(command.Id, cancellationToken);
        if (institution is null)
        {
            return (InstitutionDetails?)null;
        }

        institution.Edit(command.Name, command.ParsedColor, command.LogoKey, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InstitutionDetails.From(institution);
    }
}
