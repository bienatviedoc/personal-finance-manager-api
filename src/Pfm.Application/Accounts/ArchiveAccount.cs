using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Accounts;

/// <summary>What deleting an account does: it is archived, at any balance, never removed.</summary>
public sealed record ArchiveAccountCommand(Guid Id);

public sealed class ArchiveAccountValidator : AbstractValidator<ArchiveAccountCommand>
{
    public ArchiveAccountValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no account with that id.</summary>
public sealed class ArchiveAccountHandler(
    IValidator<ArchiveAccountCommand> validator, IAccountRepository accounts, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ArchiveAccountCommand, Done?>(validator)
{
    /// <summary>Archiving names no row version, so one that meets an edit made at the same moment is made again on what was saved.</summary>
    protected override Task<Result<Done?>> ExecuteAsync(ArchiveAccountCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync<Result<Done?>>(async () =>
        {
            var account = await accounts.FindAsync(command.Id, cancellationToken);
            if (account is null)
            {
                return (Done?)null;
            }

            account.Archive(clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new Done();
        });
}
