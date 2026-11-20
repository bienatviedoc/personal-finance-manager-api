using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Accounts.Institutions;
using Pfm.Application.Contracts.Transactions;
using Pfm.Application.UseCases;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>
/// Changes an account, from the row version the caller last read (<paramref name="RowVersion"/>). A credit
/// card or loan sends <paramref name="AmountOwed"/>; any other type, <paramref name="OpeningBalance"/>. The last
/// 4 digits and a credit card's fields are replaced like everything else: left out, they are removed.
/// </summary>
public sealed record EditAccountCommand(
    Guid Id, uint RowVersion, string Name, string? Type, Guid? InstitutionId, string? Currency, string? OpeningBalance, string? AmountOwed,
    string? OpeningDate, string? Last4 = null, string? CreditLimit = null, int? StatementDay = null, int? PaymentDueDay = null)
    : IAccountCommand
{
    public string Name { get; } = Name?.Trim() ?? "";

    public string? Last4 { get; } = AccountFields.TrimLast4(Last4);

    public Last4Digits? ParsedLast4 { get; } = AccountFields.ReadLast4(Last4);

    public AccountFields Parsed { get; } = AccountFields.Read(Type, Currency, OpeningBalance, AmountOwed, OpeningDate, CreditLimit);
}

public sealed class EditAccountValidator : AbstractValidator<EditAccountCommand>
{
    public EditAccountValidator()
    {
        RuleFor(command => command.Id).Required();
        this.AccountFieldRules();
    }
}

/// <summary>What editing an account came to: the edited account, or nothing saved because the account had changed.</summary>
/// <param name="Edited">Null when the edit was made from a stale row version.</param>
public sealed record EditAccountOutcome(AccountDetails? Edited)
{
    /// <summary>The account changed since the row version the edit was made from; nothing was saved.</summary>
    public static readonly EditAccountOutcome Stale = new((AccountDetails?)null);
}

/// <summary>
/// Answers the edited account with its balance, which follows a new opening balance at once; null when the
/// user has no account with that id.
/// </summary>
public sealed class EditAccountHandler(
    IValidator<EditAccountCommand> validator, IAccountRepository accounts, IInstitutionRepository institutions, IAccountTransactions transactions,
    IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<EditAccountCommand, EditAccountOutcome?>(validator)
{
    protected override async Task<Result<EditAccountOutcome?>> ExecuteAsync(EditAccountCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.Id, cancellationToken);
        if (account is null)
        {
            return (EditAccountOutcome?)null;
        }

        if (account.RowVersion != command.RowVersion)
        {
            return EditAccountOutcome.Stale;
        }

        if (await institutions.InstitutionErrorAsync(command.InstitutionId, current: account.InstitutionId, cancellationToken) is { } errors)
        {
            return Result<EditAccountOutcome?>.Invalid(errors);
        }

        var fields = command.Parsed;
        try
        {
            account.Edit(
                command.Name, fields.Type!.Value, command.InstitutionId, fields.Currency!.Value, fields.OpeningAmount!.Value, fields.OpeningDate!.Value,
                hasTransactions: await transactions.AnyOnAsync(account.Id, cancellationToken), clock.GetUtcNow(), command.ParsedLast4,
                fields.CreditCardTerms(command.StatementDay, command.PaymentDueDay));
        }
        catch (AccountRuleViolation violation)
        {
            return Result<EditAccountOutcome?>.Invalid(violation.AsErrors());
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            // Someone else saved the account between reading it here and saving it.
            return EditAccountOutcome.Stale;
        }

        var net = await transactions.NetByAccountAsync([account.Id], cancellationToken);
        return new EditAccountOutcome(AccountDetails.Of(account, net.GetValueOrDefault(account.Id)));
    }
}
