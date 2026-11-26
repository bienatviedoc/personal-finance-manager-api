using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.UseCases;

namespace Pfm.Application.Budgets;

/// <summary>Marks every unread budget notification of the user read.</summary>
public sealed record ReadAllBudgetNotificationsCommand;

/// <summary>The command carries nothing to check.</summary>
public sealed class ReadAllBudgetNotificationsValidator : AbstractValidator<ReadAllBudgetNotificationsCommand>;

public sealed class ReadAllBudgetNotificationsHandler(
    IValidator<ReadAllBudgetNotificationsCommand> validator, IBudgetNotificationRepository notifications, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<ReadAllBudgetNotificationsCommand, Done>(validator)
{
    protected override async Task<Result<Done>> ExecuteAsync(ReadAllBudgetNotificationsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var notification in await notifications.ListUnreadAsync(cancellationToken))
        {
            notification.MarkRead(now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Done();
    }
}
