using FluentValidation;
using Pfm.Application.Abstractions;
using Pfm.Application.Contracts.Accounts;
using Pfm.Application.Transactions.Categories;
using Pfm.Application.Transactions.Tags;
using Pfm.Application.UseCases;
using Pfm.Domain.Transactions;

namespace Pfm.Application.Transactions;

/// <summary>What a bulk action does to every transaction it selects (spec TXN-7).</summary>
public enum BulkAction
{
    /// <summary>Puts each on one category.</summary>
    SetCategory,

    /// <summary>Puts one tag on each.</summary>
    AddTag,

    /// <summary>Takes one tag off each.</summary>
    RemoveTag,

    /// <summary>Moves each to one account.</summary>
    MoveToAccount,

    /// <summary>Deletes each, softly; a leg of a transfer takes its transfer with it.</summary>
    Delete,
}

/// <summary>
/// One action on many of the user's transactions, which all take it or none does (spec TXN-7, section 11): the
/// ones named by <paramref name="TransactionIds"/>, or every one that passes <paramref name="Filter"/>, never both.
/// </summary>
/// <param name="Action">A <see cref="BulkAction"/> as it travels: <c>setCategory</c>, <c>addTag</c>, ...</param>
/// <param name="CategoryId">The category <see cref="BulkAction.SetCategory"/> puts them on.</param>
/// <param name="Tag">The name of the tag <see cref="BulkAction.AddTag"/> and <see cref="BulkAction.RemoveTag"/> put on and take off.</param>
/// <param name="AccountId">The account <see cref="BulkAction.MoveToAccount"/> moves them to.</param>
public sealed record BulkEditTransactionsCommand(
    string? Action, IReadOnlyList<Guid>? TransactionIds, TransactionFilter? Filter, Guid? CategoryId = null, string? Tag = null, Guid? AccountId = null)
{
    /// <summary>The most transactions one bulk action takes.</summary>
    public const int MaxSelected = 1000;

    public BulkAction? ParsedAction { get; } = EnumText<BulkAction>.Read(Action);
}

public sealed class BulkEditTransactionsValidator : AbstractValidator<BulkEditTransactionsCommand>
{
    public BulkEditTransactionsValidator()
    {
        RuleFor(command => command.Action)
            .Must((command, _) => command.ParsedAction is not null)
            .WithMessage($"Choose one of: {string.Join(", ", EnumText<BulkAction>.Names)}.");
        RuleFor(command => command.TransactionIds)
            .Must((command, ids) => (ids is null) != (command.Filter is null))
            .WithMessage("Select transactions by their ids or by a filter, one of the two.")
            .DependentRules(() => RuleFor(command => command.TransactionIds)
                .Must(ids => ids is null || ids.Distinct().Count() is >= 1 and <= BulkEditTransactionsCommand.MaxSelected)
                .WithMessage($"Select 1 to {BulkEditTransactionsCommand.MaxSelected} transactions."));
        RuleFor(command => command.CategoryId).NotNull().Required().When(command => command.ParsedAction == BulkAction.SetCategory);
        RuleFor(command => command.AccountId).NotNull().Required().When(command => command.ParsedAction == BulkAction.MoveToAccount);
        RuleFor(command => command.Tag)
            .Must(Domain.Transactions.Tag.IsValidName)
            .When(command => command.ParsedAction is BulkAction.AddTag or BulkAction.RemoveTag)
            .WithMessage($"Enter a tag of 1 to {Domain.Transactions.Tag.MaxNameLength} characters.");
    }
}

/// <summary>What a bulk action came to: done to every transaction it selected, or refused, with nothing changed.</summary>
public abstract record BulkEditOutcome
{
    private BulkEditOutcome()
    {
    }

    /// <param name="Count">How many transactions it selected, each of which took it.</param>
    public sealed record Done(int Count) : BulkEditOutcome;

    /// <summary>Some of the transactions cannot take the action, so none did.</summary>
    /// <param name="Rows">Each transaction refused and why, in the order they were selected.</param>
    public sealed record Refused(IReadOnlyList<RefusedTransaction> Rows) : BulkEditOutcome;
}

/// <summary>A transaction a bulk action selected that cannot take it.</summary>
public sealed record RefusedTransaction(Guid TransactionId, BulkRefusal Reason)
{
    /// <summary>Why, in words.</summary>
    public string Message => Reason switch
    {
        BulkRefusal.NotFound => "You have no such transaction, or it was deleted.",
        BulkRefusal.TransferLeg => "This is a leg of a transfer: it has no category, and changes only with its transfer.",
        BulkRefusal.Split => "This transaction is split: edit it to change the categories of its lines.",
        BulkRefusal.CategoryKind => "This category is of the other kind: income takes an income category, an expense an expense one.",
        BulkRefusal.Reconciled => "This transaction is reconciled: reopen its reconciliation to move or delete it.",
        BulkRefusal.OtherCurrency => "This transaction is in another currency than the account: a transaction moves only to an account in its currency.",
        BulkRefusal.TooManyTags => $"This transaction has {Transaction.MaxTags} tags already.",
        _ => throw new ArgumentOutOfRangeException(nameof(Reason), Reason, "Not a reason a bulk action is refused."),
    };
}

/// <summary>
/// Acts on every selected transaction in one save, or on none. A delete of a leg of a transfer deletes the
/// transfer, as deleting one alone does. Like a delete, it names no row version: one that meets a change made at
/// the same moment is made again on what was saved.
/// </summary>
public sealed class BulkEditTransactionsHandler(
    IValidator<BulkEditTransactionsCommand> validator, ITransactionRepository transactions, ICategoryRepository categories, IAccountLookup accounts,
    TransactionTags tags, ITagRepository tagRepository, IUnitOfWork unitOfWork, TimeProvider clock)
    : CommandHandler<BulkEditTransactionsCommand, BulkEditOutcome>(validator)
{
    protected override Task<Result<BulkEditOutcome>> ExecuteAsync(BulkEditTransactionsCommand command, CancellationToken cancellationToken) =>
        unitOfWork.RetryOnConcurrentChangeAsync(() => ActAsync(command, cancellationToken));

    private async Task<Result<BulkEditOutcome>> ActAsync(BulkEditTransactionsCommand command, CancellationToken cancellationToken)
    {
        var refused = new List<RefusedTransaction>();
        IReadOnlyList<Transaction> selected;
        if (command.Filter is { } filter)
        {
            selected = await transactions.ListAsync(filter, TransactionSort.NewestFirst, BulkEditTransactionsCommand.MaxSelected + 1, after: null, cancellationToken);
            if (selected.Count > BulkEditTransactionsCommand.MaxSelected)
            {
                return Result<BulkEditOutcome>.Invalid(FieldError.On(
                    nameof(BulkEditTransactionsCommand.Filter),
                    $"More than {BulkEditTransactionsCommand.MaxSelected} transactions pass this filter: narrow it, or act on them in parts."));
            }
        }
        else
        {
            var ids = command.TransactionIds!.Distinct().ToList();
            var found = (await transactions.FindManyAsync(ids, cancellationToken)).ToDictionary(transaction => transaction.Id);
            refused.AddRange(ids.Where(id => !found.ContainsKey(id)).Select(id => new RefusedTransaction(id, BulkRefusal.NotFound)));
            selected = [.. ids.Where(found.ContainsKey).Select(id => found[id])];
        }

        var now = clock.GetUtcNow();
        var act = await PlanAsync(command, selected, refused, cancellationToken);
        if (!act.IsValid)
        {
            return Result<BulkEditOutcome>.Invalid(act.Errors);
        }

        if (refused.Count > 0)
        {
            var order = (command.TransactionIds ?? [.. selected.Select(transaction => transaction.Id)]).Distinct().ToList();
            return new BulkEditOutcome.Refused([.. refused.OrderBy(row => order.IndexOf(row.TransactionId))]);
        }

        await act.Outcome(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BulkEditOutcome.Done(selected.Count);
    }

    /// <summary>
    /// What the action does to the selected transactions, once none is refused; adds to <paramref name="refused"/>
    /// each that cannot take it. Invalid when what the action names (a category, an account) cannot be used at all.
    /// </summary>
    private async Task<Result<Func<DateTimeOffset, Task>>> PlanAsync(
        BulkEditTransactionsCommand command, IReadOnlyList<Transaction> selected, List<RefusedTransaction> refused, CancellationToken cancellationToken)
    {
        void Refuse(Transaction transaction, BulkRefusal reason) => refused.Add(new RefusedTransaction(transaction.Id, reason));

        switch (command.ParsedAction!.Value)
        {
            case BulkAction.SetCategory:
                if (await categories.FindAsync(command.CategoryId!.Value, cancellationToken) is not { IsArchived: false } category)
                {
                    return Result<Func<DateTimeOffset, Task>>.Invalid(FieldError.On(nameof(command.CategoryId), Choices.NotACategory));
                }

                foreach (var transaction in selected)
                {
                    if (transaction.IsTransferLeg)
                    {
                        Refuse(transaction, BulkRefusal.TransferLeg);
                    }
                    else if (category.Kind != transaction.Type.CategoryKind())
                    {
                        Refuse(transaction, BulkRefusal.CategoryKind);
                    }
                    else if (transaction.IsSplit)
                    {
                        Refuse(transaction, BulkRefusal.Split);
                    }
                }

                return Plan(now => Each(selected, transaction => transaction.Recategorise(category, now)));

            case BulkAction.AddTag:
                var name = Domain.Transactions.Tag.Normalised(command.Tag)!;
                foreach (var transaction in selected.Where(transaction =>
                    transaction.Tags.Count >= Transaction.MaxTags && !transaction.Tags.Any(tag => tag.Name == name)))
                {
                    Refuse(transaction, BulkRefusal.TooManyTags);
                }

                return Plan(async now =>
                {
                    var tag = (await tags.FindOrCreateAsync([name], cancellationToken))[0];
                    await Each(selected, transaction => transaction.AddTag(tag, now));
                });

            case BulkAction.RemoveTag:
                var named = await tagRepository.FindByNamesAsync([Domain.Transactions.Tag.Normalised(command.Tag)!], cancellationToken);
                return Plan(now => Each(selected, transaction =>
                {
                    foreach (var tag in named)
                    {
                        transaction.RemoveTag(tag.Id, now);
                    }
                }));

            case BulkAction.MoveToAccount:
                if (await accounts.FindChoosableAsync(command.AccountId!.Value, keeps: null, cancellationToken) is not { } account)
                {
                    return Result<Func<DateTimeOffset, Task>>.Invalid(FieldError.On(nameof(command.AccountId), Choices.NotAnAccount));
                }

                foreach (var transaction in selected.Where(transaction => transaction.AccountId != account.Id))
                {
                    if (transaction.IsTransferLeg)
                    {
                        Refuse(transaction, BulkRefusal.TransferLeg);
                    }
                    else if (transaction.Currency != account.Currency)
                    {
                        Refuse(transaction, BulkRefusal.OtherCurrency);
                    }
                    else if (transaction.IsReconciled)
                    {
                        Refuse(transaction, BulkRefusal.Reconciled);
                    }
                }

                return Plan(now => Each(selected, transaction => transaction.MoveTo(account.Id, account.Currency, now)));

            case BulkAction.Delete:
                var transfers = new Dictionary<Guid, Transfer>();
                foreach (var transaction in selected)
                {
                    if (transaction.TransferId is { } transferId)
                    {
                        // The other leg deleted by another request in between fails the save, and the action is made again.
                        var transfer = transfers.TryGetValue(transferId, out var known)
                            ? known
                            : transfers[transferId] = (await transactions.FindTransferAsync(transferId, deletedToo: true, cancellationToken))!;
                        if (transfer.IsReconciled)
                        {
                            Refuse(transaction, BulkRefusal.Reconciled);
                        }
                    }
                    else if (transaction.IsReconciled)
                    {
                        Refuse(transaction, BulkRefusal.Reconciled);
                    }
                }

                return Plan(async now =>
                {
                    await Each(selected.Where(transaction => !transaction.IsTransferLeg).ToList(), transaction => transaction.Delete(now));
                    foreach (var transfer in transfers.Values)
                    {
                        transfer.Delete(now);
                    }
                });

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.ParsedAction, "Not a bulk action.");
        }
    }

    private static Result<Func<DateTimeOffset, Task>> Plan(Func<DateTimeOffset, Task> act) => act;

    private static Task Each(IReadOnlyList<Transaction> selected, Action<Transaction> act)
    {
        foreach (var transaction in selected)
        {
            act(transaction);
        }

        return Task.CompletedTask;
    }
}
