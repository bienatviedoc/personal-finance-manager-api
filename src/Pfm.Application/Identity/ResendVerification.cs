using FluentValidation;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

public sealed record ResendVerificationCommand(string Email)
{
    public string Email { get; } = EmailAddress.Normalize(Email);
}

public sealed class ResendVerificationValidator : AbstractValidator<ResendVerificationCommand>
{
    public ResendVerificationValidator()
    {
        RuleFor(command => command.Email).Required();
    }
}

/// <summary>
/// Sends a new verification link to a user who has not confirmed their email address yet. Always
/// answers the same way, so it cannot be used to find out which emails have a user.
/// </summary>
public sealed class ResendVerificationHandler(IValidator<ResendVerificationCommand> validator, IUsers users, IIdentityEmails emails)
    : CommandHandler<ResendVerificationCommand, Done>(validator)
{
    protected override async Task<Result<Done>> ExecuteAsync(ResendVerificationCommand command, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(command.Email) is { EmailConfirmed: false } user)
        {
            var token = await users.CreateEmailConfirmationTokenAsync(user.Id);
            await emails.SendVerificationAsync(user, token, cancellationToken);
        }

        return new Done();
    }
}
