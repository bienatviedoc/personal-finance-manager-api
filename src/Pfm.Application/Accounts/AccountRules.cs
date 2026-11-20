using FluentValidation;
using Pfm.Application.Accounts.Institutions;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Accounts;

namespace Pfm.Application.Accounts;

/// <summary>
/// What creating and editing an account both send. Values arrive as sent and are read once, in
/// <see cref="AccountFields"/>, so a value that is not supported is a validation error naming its field and
/// the handler uses exactly what the validator accepted.
/// </summary>
internal interface IAccountCommand
{
    string Name { get; }

    string? Type { get; }

    Guid? InstitutionId { get; }

    string? Currency { get; }

    string? OpeningBalance { get; }

    string? AmountOwed { get; }

    string? OpeningDate { get; }

    string? Last4 { get; }

    string? CreditLimit { get; }

    int? StatementDay { get; }

    int? PaymentDueDay { get; }

    AccountFields Parsed { get; }
}

/// <summary>An account command's values, read: null where one was left out or is not what it should be.</summary>
/// <param name="OpeningAmount">
/// What the domain takes: the amount owed for a credit card or loan, the opening balance for any other type.
/// </param>
/// <param name="CreditLimit">Null when none was sent, and when what was sent is not an amount.</param>
public sealed record AccountFields(AccountType? Type, Currency? Currency, decimal? OpeningAmount, DateOnly? OpeningDate, decimal? CreditLimit)
{
    public static AccountFields Read(string? type, string? currency, string? openingBalance, string? amountOwed, string? openingDate, string? creditLimit)
    {
        var parsedType = EnumText<AccountType>.Read(type);
        var amountSent = parsedType?.HoldsAmountOwed() == true ? amountOwed : openingBalance;

        return new AccountFields(
            parsedType,
            Currencies.TryParse(currency, out var parsedCurrency) ? parsedCurrency : null,
            AmountText.TryParse(amountSent, out var amount) ? amount : null,
            DateText.Read(openingDate),
            AmountText.TryParse(creditLimit, out var limit) ? limit : null);
    }

    /// <summary>The last 4 digits as sent, without the spaces around them; none when left out or blank.</summary>
    public static string? ReadLast4(string? last4) => string.IsNullOrWhiteSpace(last4) ? null : last4.Trim();

    /// <summary>What the domain takes: a credit card's terms, and none for every other type.</summary>
    public CreditCardTerms? CreditCardTerms(int? statementDay, int? paymentDueDay) =>
        Type == AccountType.CreditCard ? new CreditCardTerms(CreditLimit, statementDay, paymentDueDay) : null;
}

/// <summary>The field rules of an account, shared by creating and editing one and taken from the domain's.</summary>
internal static class AccountRules
{
    private const string OnlyCreditCardMessage = "Only a credit card has a credit limit, a statement day and a payment due day.";

    private static readonly string DayMessage = $"Enter a day of the month, 1 to {CreditCardTerms.LastDay}.";

    private const string AmountMessage = "Enter an amount as a decimal string such as \"125000.00\", with no more decimals than the currency has.";

    public static void AccountFieldRules<T>(this AbstractValidator<T> validator)
        where T : IAccountCommand
    {
        validator.RuleFor(command => command.Name).IsName();
        validator.RuleFor(command => command.Type)
            .Must((command, _) => command.Parsed.Type is not null)
            .WithMessage($"Choose one of: {string.Join(", ", EnumText<AccountType>.Names)}.");
        validator.RuleFor(command => command.Currency)
            .Must((command, _) => command.Parsed.Currency is not null)
            .WithMessage($"Choose one of: {string.Join(", ", Enum.GetNames<Currency>())}.");
        validator.RuleFor(command => command.OpeningDate)
            .Must((command, _) => command.Parsed.OpeningDate is not null)
            .WithMessage(DateText.Message);
        validator.RuleFor(command => command.Last4)
            .Must(last4 => last4 is null || Last4Digits.IsValid(last4))
            .WithMessage("Enter only the last 4 digits. A full account or card number is never stored.");

        validator.When(command => command.Parsed.Type == AccountType.CreditCard, () =>
        {
            validator.RuleFor(command => command.StatementDay).Must(CreditCardTerms.IsValidDay).WithMessage(DayMessage);
            validator.RuleFor(command => command.PaymentDueDay).Must(CreditCardTerms.IsValidDay).WithMessage(DayMessage);
        });
        validator.When(command => command.Parsed.Type is not (null or AccountType.CreditCard), () =>
        {
            validator.RuleFor(command => command.CreditLimit).Null().WithMessage(OnlyCreditCardMessage);
            validator.RuleFor(command => command.StatementDay).Null().WithMessage(OnlyCreditCardMessage);
            validator.RuleFor(command => command.PaymentDueDay).Null().WithMessage(OnlyCreditCardMessage);
        });

        // Which amount an account takes depends on its type, and what fits depends on its currency.
        validator.When(command => command.Parsed is { Type: not null, Currency: not null }, () =>
        {
            validator.When(command => command.Parsed.Type == AccountType.CreditCard && command.CreditLimit is not null, () =>
            {
                validator.RuleFor(command => command.CreditLimit)
                    .Must((command, _) => command.Parsed.CreditLimit is { } limit && CreditCardTerms.IsValidCreditLimit(limit, command.Parsed.Currency!.Value))
                    .WithMessage($"{AmountMessage} A credit limit is not negative.");
            });

            validator.When(command => command.Parsed.Type!.Value.HoldsAmountOwed(), () =>
            {
                validator.RuleFor(command => command.AmountOwed)
                    .Must((command, _) => command.Parsed.OpeningAmount is { } owed && owed >= 0 && Amounts.IsValid(owed, command.Parsed.Currency!.Value))
                    .WithMessage($"{AmountMessage} An amount owed is a positive number.");
                validator.RuleFor(command => command.OpeningBalance).Null()
                    .WithMessage("A credit card or loan takes the amount owed instead.");
            }).Otherwise(() =>
            {
                validator.RuleFor(command => command.OpeningBalance)
                    .Must((command, _) => command.Parsed.OpeningAmount is { } opening && Amounts.IsValid(opening, command.Parsed.Currency!.Value))
                    .WithMessage(AmountMessage);
                validator.RuleFor(command => command.AmountOwed).Null()
                    .WithMessage("Only a credit card or loan takes an amount owed.");
            });
        });
    }

    /// <summary>
    /// Null when the account may be put with the institution: none, one of the user's that is not archived,
    /// or the one the account is already with. Otherwise the error, keyed by the command property.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string[]>?> InstitutionErrorAsync(
        this IInstitutionRepository institutions, Guid? institutionId, Guid? current, CancellationToken cancellationToken)
    {
        if (institutionId is not { } id || id == current)
        {
            return null;
        }

        return await institutions.FindAsync(id, cancellationToken) is { IsArchived: false }
            ? null
            : FieldError.On(nameof(IAccountCommand.InstitutionId), "Choose one of your institutions that is not archived.");
    }

    /// <summary>A broken account rule, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this AccountRuleViolation violation) =>
        FieldError.On(violation.Property, violation.Message);
}
