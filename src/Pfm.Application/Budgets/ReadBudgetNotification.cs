using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Budgets;

/// <summary>Marks one of the user's budget notifications read.</summary>
public sealed record ReadBudgetNotificationCommand(Guid Id);

public sealed class ReadBudgetNotificationValidator : AbstractValidator<ReadBudgetNotificationCommand>
{
    public ReadBudgetNotificationValidator() => RuleFor(command => command.Id).Required();
}

/// <summary>Answers null when the user has no notification with that id. Reading one already read changes nothing.</summary>
public sealed class ReadBudgetNotificationHandler(
    IValidator<ReadBudgetNotificationCommand> validator, IBudgetNotificationRepository notifications, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ReadBudgetNotificationCommand, Done?>(validator)
{
    protected override async Task<Result<Done?>> ExecuteAsync(ReadBudgetNotificationCommand command, CancellationToken cancellationToken)
    {
        if (await notifications.FindAsync(command.Id, cancellationToken) is not { } notification)
        {
            return (Done?)null;
        }

        notification.MarkRead(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Done();
    }
}
