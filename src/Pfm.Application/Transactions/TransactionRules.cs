using System.Globalization;
using System.Text.Json;
using FluentValidation;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>
/// What recording and editing a transaction both send. Values arrive as sent and are read once, in
/// <see cref="TransactionFields"/>, so a value that is not supported is a validation error naming its field
/// and the handler uses exactly what the validator accepted.
/// </summary>
internal interface ITransactionCommand
{
    string? Type { get; }

    Guid AccountId { get; }

    string? Amount { get; }

    string? Date { get; }

    Guid CategoryId { get; }

    string? Payee { get; }

    string? Note { get; }

    TransactionFields Parsed { get; }
}

/// <summary>A transaction command's values, read: null where one was left out or is not what it should be.</summary>
public sealed record TransactionFields(TransactionType? Type, decimal? Amount, DateOnly? Date)
{
    /// <summary>Transaction types as they travel: their names in camelCase, like every enum in JSON.</summary>
    private static readonly IReadOnlyDictionary<string, TransactionType> Types =
        Enum.GetValues<TransactionType>().ToDictionary(type => JsonNamingPolicy.CamelCase.ConvertName(type.ToString()));

    public static readonly IReadOnlyCollection<string> TypeNames = [.. Types.Keys];

    public static TransactionFields Read(string? type, string? amount, string? date) =>
        new(
            type is not null && Types.TryGetValue(type, out var known) ? known : null,
            AmountText.TryParse(amount, out var parsed) ? parsed : null,
            DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null);
}

/// <summary>
/// The rules of a transaction, shared by recording and editing one and taken from the domain's. The ones
/// that need only the command are the validator's; the ones that need the account, the category or today's
/// date are checked while building the transaction.
/// </summary>
internal static class TransactionRules
{
    public static void TransactionFieldRules<T>(this AbstractValidator<T> validator)
        where T : ITransactionCommand
    {
        validator.RuleFor(command => command.Type)
            .Must((command, _) => command.Parsed.Type is not null)
            .WithMessage($"Choose one of: {string.Join(", ", TransactionFields.TypeNames)}.");
        validator.RuleFor(command => command.AccountId).Required();
        validator.RuleFor(command => command.Amount)
            .Must((command, _) => command.Parsed.Amount is > 0)
            .WithMessage("Enter an amount greater than zero as a decimal string such as \"125000.00\".");
        validator.RuleFor(command => command.Date)
            .Must((command, _) => command.Parsed.Date is not null)
            .WithMessage("Enter a date as YYYY-MM-DD.");
        validator.RuleFor(command => command.CategoryId).Required();
        validator.RuleFor(command => command.Payee).Must(Transaction.IsValidPayee)
            .WithMessage($"Enter a payee of at most {Transaction.MaxPayeeLength} characters.");
        validator.RuleFor(command => command.Note).Must(Transaction.IsValidNote)
            .WithMessage($"Enter a note of at most {Transaction.MaxNoteLength} characters.");
    }

    public static IReadOnlyDictionary<string, string[]> Error(string property, string message) =>
        new Dictionary<string, string[]> { [property] = [message] };

    /// <summary>A broken transaction rule, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this TransactionRuleViolation violation) =>
        Error(violation.Property, violation.Message);
}
