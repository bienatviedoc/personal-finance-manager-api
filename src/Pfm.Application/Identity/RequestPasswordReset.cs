using FluentValidation;
using Pfm.Application.UseCases;

namespace Pfm.Application.Identity;

/// <summary>A user who has forgotten their password asks for a link to set a new one.</summary>
public sealed record RequestPasswordResetCommand(string Email)
{
    public string Email { get; } = EmailAddress.Normalize(Email);
}

public sealed class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetValidator()
    {
        RuleFor(command => command.Email).Must(EmailAddress.IsValid).WithMessage("Enter a valid email address.");
    }
}

/// <summary>
/// Emails a password reset link to the user with this email address. Always answers the same way, so it
/// cannot be used to find out which emails have a user: an address with no user is emailed too, to say so,
/// which also makes that answer take as long as the other.
/// </summary>
public sealed class RequestPasswordResetHandler(IValidator<RequestPasswordResetCommand> validator, IUsers users, IIdentityEmails emails)
    : CommandHandler<RequestPasswordResetCommand, Done>(validator)
{
    protected override async Task<Result<Done>> ExecuteAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(command.Email) is { } user)
        {
            var token = await users.CreatePasswordResetTokenAsync(user.Id);
            await emails.SendPasswordResetAsync(user, token, cancellationToken);
        }
        else
        {
            await emails.SendNoUserToResetAsync(command.Email, cancellationToken);
        }

        return new Done();
    }
}
