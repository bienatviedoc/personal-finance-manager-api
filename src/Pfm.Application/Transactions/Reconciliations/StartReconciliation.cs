using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Contracts.Identity;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Reconciliations;

/// <summary>
/// Starts reconciling an account against a statement (spec ACC-7): its closing date and balance. An account has
/// one reconciliation in progress at a time, and a statement is not dated before the last one reconciled.
/// </summary>
/// <param name="StatementDate">The statement's closing date, <c>YYYY-MM-DD</c>, not in the future.</param>
/// <param name="StatementBalance">The statement's closing balance, a decimal string in the account's currency; below zero for what is owed.</param>
public sealed record StartReconciliationCommand(Guid AccountId, string? StatementDate, string? StatementBalance)
{
    public DateOnly? ParsedStatementDate { get; } = DateText.Read(StatementDate);

    public decimal? ParsedStatementBalance { get; } = AmountText.TryParse(StatementBalance, out var balance) ? balance : null;
}

public sealed class StartReconciliationValidator : AbstractValidator<StartReconciliationCommand>
{
    public StartReconciliationValidator()
    {
        RuleFor(command => command.AccountId).Required();
        RuleFor(command => command.StatementDate).Required()
            .Must((command, _) => command.ParsedStatementDate is not null).WithMessage(DateText.Message);
        RuleFor(command => command.StatementBalance).Required()
            .Must((command, _) => command.ParsedStatementBalance is not null).WithMessage("Enter the balance as a decimal number, such as 125000.00.");
    }
}

/// <summary>What starting a reconciliation came to: the reconciliation, or nothing because one is already in progress.</summary>
public sealed record StartReconciliationOutcome(ReconciliationDetails? Started)
{
    /// <summary>The account already has a reconciliation in progress; nothing was started.</summary>
    public static readonly StartReconciliationOutcome InProgress = new((ReconciliationDetails?)null);
}

/// <summary>Answers null when the user has no account with that id.</summary>
public sealed class StartReconciliationHandler(
    IValidator<StartReconciliationCommand> validator, IAccountLookup accounts, IReconciliationRepository reconciliations, ReconciliationContext context,
    IUserCalendar calendar, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<StartReconciliationCommand, StartReconciliationOutcome?>(validator)
{
    protected override async Task<Result<StartReconciliationOutcome?>> ExecuteAsync(StartReconciliationCommand command, CancellationToken cancellationToken)
    {
        if (await accounts.FindAsync(command.AccountId, cancellationToken) is not { } account)
        {
            return (StartReconciliationOutcome?)null;
        }

        var latest = await reconciliations.FindLatestAsync(account.Id, cancellationToken);
        if (latest is { IsCompleted: false })
        {
            return StartReconciliationOutcome.InProgress;
        }

        var statementDate = command.ParsedStatementDate!.Value;
        if (latest is not null && statementDate < latest.StatementDate)
        {
            return Result<StartReconciliationOutcome?>.Invalid(FieldError.On(
                nameof(StartReconciliationCommand.StatementDate),
                $"This account is reconciled up to {DateText.Of(latest.StatementDate)}. Enter a statement dated on or after it, or reopen that reconciliation."));
        }

        Reconciliation reconciliation;
        try
        {
            reconciliation = Reconciliation.Start(
                account.Id, account.Currency, statementDate, command.ParsedStatementBalance!.Value, await calendar.TodayAsync(cancellationToken), clock.GetUtcNow());
        }
        catch (ReconciliationRuleViolation violation)
        {
            return Result<StartReconciliationOutcome?>.Invalid(FieldError.On(violation.Property, violation.Message));
        }

        reconciliations.Add(reconciliation);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (AlreadyExistsException)
        {
            // Another request started one on the account at the same moment.
            return StartReconciliationOutcome.InProgress;
        }

        return new StartReconciliationOutcome(await context.DetailsAsync(reconciliation, cancellationToken));
    }
}
