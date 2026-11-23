using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.UseCases;
using Pfm.Domain;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions.Recurring;

/// <summary>
/// Reads what a recurring rule is set up against: the account and category its command names. Shared by
/// setting one up and editing it. It also answers a rule's currency, which is its account's.
/// </summary>
public sealed class RecurringRuleContext(IAccountLookup accounts, ICategoryRepository categories)
{
    /// <summary>The account and category, or the error, keyed by command property, when one is not the user's or is archived.</summary>
    /// <param name="current">The rule being edited, which may keep the account it is on after the account is archived.</param>
    internal async Task<Result<(AccountSummary Account, Category Category)>> ReadAsync(
        IRecurringRuleCommand command, RecurringRule? current, CancellationToken cancellationToken)
    {
        if (await accounts.FindChoosableAsync(command.AccountId, current?.AccountId, cancellationToken) is not { } account)
        {
            return Result<(AccountSummary, Category)>.Invalid(FieldError.On(nameof(IRecurringRuleCommand.AccountId), Choices.NotAnAccount));
        }

        var category = await categories.FindAsync(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result<(AccountSummary, Category)>.Invalid(FieldError.On(nameof(IRecurringRuleCommand.CategoryId), Choices.NotACategory));
        }

        return (account, category);
    }

    /// <summary>The rule with the currency of its account.</summary>
    public async Task<RecurringRuleDetails> DetailsAsync(RecurringRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var account = await accounts.FindAsync(rule.AccountId, cancellationToken)
            ?? throw new InvalidOperationException("A recurring rule's account is one of its user's.");
        return RecurringRuleDetails.Of(rule, account.Currency);
    }
}
