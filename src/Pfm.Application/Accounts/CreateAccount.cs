using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Accounts.Institutions;
using Pfm.Application.UseCases;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>Adds an account. A credit card or loan sends <paramref name="AmountOwed"/>; any other type, <paramref name="OpeningBalance"/>.</summary>
/// <param name="Type">An account type in camelCase, such as <c>creditCard</c>.</param>
/// <param name="Currency">An ISO 4217 code the app supports, such as <c>VND</c>.</param>
/// <param name="OpeningBalance">A decimal string, such as <c>"125000.00"</c>.</param>
/// <param name="AmountOwed">A decimal string, positive.</param>
/// <param name="OpeningDate">A calendar date, <c>YYYY-MM-DD</c>.</param>
/// <param name="Last4">The last 4 digits of the account or card number, never more of it.</param>
/// <param name="CreditLimit">A decimal string, not negative. A credit card's only, like the two days of the month.</param>
public sealed record CreateAccountCommand(
    string Name, string? Type, Guid? InstitutionId, string? Currency, string? OpeningBalance, string? AmountOwed,
    string? OpeningDate, string? Last4 = null, string? CreditLimit = null, int? StatementDay = null, int? PaymentDueDay = null)
    : IAccountCommand
{
    public string Name { get; } = Name?.Trim() ?? "";

    public string? Last4 { get; } = AccountFields.ReadLast4(Last4);

    public AccountFields Parsed { get; } = AccountFields.Read(Type, Currency, OpeningBalance, AmountOwed, OpeningDate, CreditLimit);
}

public sealed class CreateAccountValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountValidator() => this.AccountFieldRules();
}

/// <summary>Answers the new account. An institution that is not one of the user's, or is archived, is an error on <c>InstitutionId</c>.</summary>
public sealed class CreateAccountHandler(
    IValidator<CreateAccountCommand> validator, IAccountRepository accounts, IInstitutionRepository institutions, IUnitOfWork unitOfWork,
    TimeProvider clock)
    : CommandHandler<CreateAccountCommand, AccountDetails>(validator)
{
    protected override async Task<Result<AccountDetails>> ExecuteAsync(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        if (await institutions.InstitutionErrorAsync(command.InstitutionId, current: null, cancellationToken) is { } errors)
        {
            return Result<AccountDetails>.Invalid(errors);
        }

        var fields = command.Parsed;
        var account = Account.Create(
            command.Name, fields.Type!.Value, command.InstitutionId, fields.Currency!.Value, fields.OpeningAmount!.Value, fields.OpeningDate!.Value,
            clock.GetUtcNow(), command.Last4, fields.CreditCardTerms(command.StatementDay, command.PaymentDueDay));
        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A new account has no transactions.
        return AccountDetails.Of(account, netTransactions: 0);
    }
}
