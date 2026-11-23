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

    /// <summary>The lines of a split, which then names no category of its own; null for a transaction that is not split.</summary>
    IReadOnlyList<TransactionLineInput>? Lines { get; }
    /// <summary>Whether the expense is a refund. Only an expense can be one, which the transaction itself checks.</summary>
    bool IsRefund { get; }

    TransactionFields Parsed { get; }
}

/// <summary>A line of a split as a command carries it.</summary>
/// <param name="CategoryId">A category of the kind the transaction's type takes.</param>
/// <param name="Amount">A decimal string greater than zero; the lines sum exactly to the transaction's amount.</param>
public sealed record TransactionLineInput(Guid CategoryId, string? Amount, string? Note = null)
{
    /// <summary>The amount, read: null when it was left out or is not a decimal string.</summary>
    public decimal? ParsedAmount { get; } = AmountText.TryParse(Amount, out var parsed) ? parsed : null;
}

/// <summary>A transaction command's values, read: null where one was left out or is not what it should be.</summary>
public sealed record TransactionFields(TransactionType? Type, decimal? Amount, DateOnly? Date)
{
    public static TransactionFields Read(string? type, string? amount, string? date) =>
        new(EnumText<TransactionType>.Read(type), AmountText.TryParse(amount, out var parsed) ? parsed : null, DateText.Read(date));
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
            .WithMessage($"Choose one of: {string.Join(", ", EnumText<TransactionType>.Names)}.");
        validator.RuleFor(command => command.AccountId).Required();
        validator.RuleFor(command => command.Amount)
            .Must((command, _) => command.Parsed.Amount is > 0)
            .WithMessage("Enter an amount greater than zero as a decimal string such as \"125000.00\".");
        validator.RuleFor(command => command.Date)
            .Must((command, _) => command.Parsed.Date is not null)
            .WithMessage(DateText.Message);
        validator.RuleFor(command => command.CategoryId).Required().When(command => command.Lines is null);
        validator.RuleFor(command => command.CategoryId)
            .Empty().When(command => command.Lines is not null)
            .WithMessage("Leave out the category of a split: each of its lines names its own.");
        validator.RuleFor(command => command.Lines)
            .Must(lines => lines is null || (lines.Count is >= Transaction.MinSplitLines and <= Transaction.MaxLines && lines.All(line => line is not null)))
            .WithMessage($"Split a transaction across {Transaction.MinSplitLines} to {Transaction.MaxLines} lines.");
        validator.RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(entered => entered.CategoryId).Required();
            line.RuleFor(entered => entered.Amount)
                .Must((entered, _) => entered.ParsedAmount is > 0)
                .WithMessage("Enter an amount greater than zero as a decimal string such as \"125000.00\".");
            line.RuleFor(entered => entered.Note).Must(Transaction.IsValidNote)
                .WithMessage($"Enter a note of at most {Transaction.MaxNoteLength} characters.");
        });
        validator.RuleFor(command => command.Payee).Must(Transaction.IsValidPayee)
            .WithMessage($"Enter a payee of at most {Transaction.MaxPayeeLength} characters.");
        validator.RuleFor(command => command.Note).Must(Transaction.IsValidNote)
            .WithMessage($"Enter a note of at most {Transaction.MaxNoteLength} characters.");
    }

    /// <summary>A broken transaction rule, as errors keyed by the command property at fault.</summary>
    public static IReadOnlyDictionary<string, string[]> AsErrors(this TransactionRuleViolation violation) =>
        FieldError.On(violation.Property, violation.Message);
}
