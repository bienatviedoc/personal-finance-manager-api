namespace Pfm.Domain.Transactions;

/// <summary>
/// One movement of money on one account on one calendar date: income, an expense, or a leg of a
/// <see cref="Transfer"/>. Its amount is positive and its type gives the sign; its currency is its account's.
/// The one exception to the type giving the sign is a refund: an expense flagged as money coming back, which
/// raises the balance. Deleting it is soft: it leaves lists and balances at once and stays recoverable. Income
/// or an expense has one line, with its category and whole amount, or is split across several, which sum
/// exactly to its amount (spec section 3, rule 5). A leg of a transfer has no line and no category.
/// </summary>
/// <remarks>
/// Its amount is converted into the user's base currency once, at the exchange rate of its date, and never
/// revalued when rates move (spec section 3, rule 7; decision record 0002). Until a rate exists on or before
/// its date it is not converted.
/// </remarks>
public sealed class Transaction : IUserOwned
{
    public const int MaxPayeeLength = 200;

    public const int MaxNoteLength = 1000;

    /// <summary>What a leg of a transfer has for lines.</summary>
    private static readonly IReadOnlyList<SplitLine> NoLines = [];

    /// <summary>A split has at least this many lines: with one, the transaction is not split.</summary>
    public const int MinSplitLines = 2;

    public const int MaxLines = 50;

    /// <summary>How long a deleted transaction can be restored (spec section 3, rule 10). After that it is removed for good.</summary>
    public static readonly TimeSpan RecoveryPeriod = TimeSpan.FromDays(30);

    /// <summary>The most tags one transaction takes.</summary>
    public const int MaxTags = 20;

    private readonly List<TransactionLine> _lines = [];

    private readonly List<TransactionTag> _tags = [];

    /// <summary>For persistence, which sets every property itself.</summary>
    private Transaction()
    {
    }

    private Transaction(DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid AccountId { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>Always greater than zero; <see cref="Type"/> gives the sign, and <see cref="IsRefund"/> turns an expense's round.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Whether the expense is a refund: money coming back from a purchase, which raises the account balance
    /// and lowers spent in its category instead of counting as income. Only an expense can be one.
    /// </summary>
    public bool IsRefund { get; private set; }

    /// <summary>Copied from the account the transaction is on.</summary>
    public Currency Currency { get; private set; }

    /// <summary>A calendar date in the user's time zone, never in the future.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>
    /// The amount in the user's base currency, or that it is not converted yet. Null only until the
    /// transaction is first saved, which converts it or flags it not converted.
    /// </summary>
    public BaseCurrencyConversion? Conversion { get; private set; }

    /// <summary>
    /// The category of the first line: the transaction's only category unless it is split. A leg of a transfer
    /// has none: it is neither income nor spending.
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>The transfer this transaction is a leg of, shared with the other leg; null for income and expenses.</summary>
    public Guid? TransferId { get; private set; }

    /// <summary>One line, or several when the transaction is split, in the order they were entered; none for a leg of a transfer.</summary>
    public IReadOnlyList<TransactionLine> Lines => [.. _lines.OrderBy(line => line.Position)];

    /// <summary>Whether the transaction is split across several lines, each with its own category.</summary>
    public bool IsSplit => _lines.Count > 1;

    /// <summary>The tags on the transaction (spec TXN-5), by name.</summary>
    public IReadOnlyList<TransactionTag> Tags => [.. _tags.OrderBy(tag => tag.Name, StringComparer.Ordinal)];

    /// <summary>Who was paid, or who paid; null when not entered.</summary>
    public string? Payee { get; private set; }

    public string? Note { get; private set; }

    /// <summary>
    /// The recurring rule that posted the transaction; null for one a user recorded, and once the rule is
    /// deleted.
    /// </summary>
    public Guid? RecurringRuleId { get; private set; }

    /// <summary>The import batch the transaction was created from (spec section 3); null for one a user recorded or a rule posted.</summary>
    public Guid? ImportBatchId { get; private set; }

    /// <summary>
    /// The reconciliation the transaction is ticked in; null while it is in none. It stays ticked when the
    /// reconciliation completes, which locks it (&lt;see cref="IsReconciled"/&gt;).
    /// </summary>
    public Guid? ReconciliationId { get; private set; }

    /// <summary>
    /// Whether the transaction is in a completed reconciliation, which locks it: its amount, date and account,
    /// and whatever else moves its account's balance (its type and whether it is a refund), cannot change, and it
    /// cannot be deleted, until the reconciliation is reopened (spec section 3, rule 11).
    /// </summary>
    public bool IsReconciled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the transaction was deleted; null while it counts.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Changes every time the transaction is saved; an edit names the version it was made from.</summary>
    public uint RowVersion { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>Whether this is one of the two legs of a transfer, which change and are deleted only together.</summary>
    public bool IsTransferLeg => TransferId is not null;

    /// <summary>Whether the amount has been converted into the base currency. Once it has, it stays as converted.</summary>
    public bool IsConverted => Conversion is { IsConverted: true };

    /// <summary>
    /// What the transaction does to its account's balance: nothing once deleted. An account's balance is its
    /// opening balance plus this of each of its transactions: income − expenses + refunds + transfers in −
    /// transfers out (spec section 3, rule 2).
    /// </summary>
    public decimal SignedAmount => IsDeleted ? 0 : Type.Signed(Amount, IsRefund);

    /// <summary>
    /// What the transaction does to spent in its category, in the base currency: an expense adds its converted
    /// amount and a refund takes its own away (spec section 6). Nothing for income, once deleted, or while
    /// not converted, which is left out of totals (spec section 3, rule 7).
    /// </summary>
    public decimal SpentAmount => !IsDeleted && Conversion is { Amount: { } converted } ? Spent.Signed(Type, converted, IsRefund) : 0;

    /// <summary>
    /// The part of <see cref="SpentAmount"/> that is spent in these categories: what the lines on them come to.
    /// A split's lines share its converted amount as they share its amount, each share rounded to the base
    /// currency's decimals and the last line taking what is left, so the shares sum exactly to it.
    /// </summary>
    public decimal SpentAmountIn(IReadOnlyCollection<Guid> categoryIds) => ShareIn(SpentAmount, categoryIds);

    /// <summary>
    /// What the transaction brings in, in the base currency: income adds its converted amount (spec RPT-3).
    /// Nothing for an expense or a leg of a transfer, which is not income (spec section 3, rule 3), once deleted,
    /// or while not converted (rule 7).
    /// </summary>
    public decimal IncomeAmount => !IsDeleted && Type == TransactionType.Income && Conversion is { Amount: { } converted } ? converted : 0;

    /// <summary>The part of <see cref="IncomeAmount"/> in these categories, shared among a split's lines as <see cref="SpentAmountIn"/> shares spent.</summary>
    public decimal IncomeAmountIn(IReadOnlyCollection<Guid> categoryIds) => ShareIn(IncomeAmount, categoryIds);

    /// <summary>
    /// The part of an amount in the base currency that the lines on these categories take: each line's share
    /// rounded to the base currency's decimals, and the last line taking what is left, so the shares sum exactly to it.
    /// </summary>
    private decimal ShareIn(decimal whole, IReadOnlyCollection<Guid> categoryIds)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        if (whole == 0)
        {
            return 0;
        }

        var decimals = Conversion!.BaseCurrency.DecimalPlaces();
        var lines = Lines;
        var (left, inThese) = (whole, 0m);
        for (var position = 0; position < lines.Count; position++)
        {
            var share = position == lines.Count - 1 ? left : decimal.Round(whole * lines[position].Amount / Amount, decimals, MidpointRounding.AwayFromZero);
            left -= share;
            if (categoryIds.Contains(lines[position].CategoryId))
            {
                inThese += share;
            }
        }

        return inThese;
    }

    public static bool IsValidPayee(string? payee) => payee is null || payee.Trim().Length <= MaxPayeeLength;

    public static bool IsValidNote(string? note) => note is null || note.Trim().Length <= MaxNoteLength;

    /// <summary>A payee or note as the transaction keeps it: trimmed, and null when nothing is left.</summary>
    public static string? Normalised(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// What is left of <paramref name="amount"/> after <paramref name="lineAmounts"/>: zero when the lines sum
    /// exactly to it, which a split must (spec section 3, rule 5), and below zero when they come to more.
    /// </summary>
    public static decimal RemainingToSplit(decimal amount, IEnumerable<decimal> lineAmounts) => amount - lineAmounts.Sum();

    /// <param name="moves">What it moves; it takes the account's currency.</param>
    /// <param name="category">One of the user's categories that is not archived, of the kind the type takes.</param>
    /// <param name="isRefund">Whether the expense is a refund; no other type can be.</param>
    public static Transaction Record(
        TransactionType type, AccountAmount moves, DateOnly date, Category category, string? payee, string? note, Moment at, bool isRefund = false) =>
        RecordOn(type, moves, date, OneLine(category, moves), payee, note, at, isRefund);

    /// <summary>
    /// One leg of a transfer, for <see cref="Transfer"/> alone to record: a leg never exists without the other.
    /// </summary>
    internal static Transaction RecordLeg(Guid transferId, TransactionType type, AccountAmount moves, DateOnly date, string? note, Moment at)
    {
        var leg = new Transaction(at.Now) { TransferId = transferId };
        leg.Set(type, moves, date, NoLines, payee: null, note, at, isRefund: false);
        return leg;
    }

    /// <summary>Changes a leg, for <see cref="Transfer"/> alone, which changes the other leg with it. Its type stays.</summary>
    internal void EditLeg(AccountAmount moves, DateOnly date, string? note, Moment at) =>
        Set(Type, moves, date, NoLines, payee: null, note, at, isRefund: false);

    /// <summary>
    /// Makes income or an expense a leg of a transfer, for <see cref="Transfer.Mark"/> alone, which makes the
    /// other leg with it. It keeps its account, amount, date, note and conversion, and drops what a leg has none
    /// of: its lines, category and payee. It is no longer an occurrence of the recurring rule that posted it,
    /// which posts income and expenses only.
    /// </summary>
    internal void BecomeLeg(Guid transferId, TransactionType type, DateTimeOffset now)
    {
        GuardNotDeleted();
        Type = type;
        TransferId = transferId;
        CategoryId = null;
        Payee = null;
        RecurringRuleId = null;
        UpdatedAt = now;
        _lines.Clear();
    }

    /// <summary>
    /// Makes a leg of a transfer income or an expense again, on one line with its whole amount, for
    /// <see cref="Transfer.Unmark"/> alone, which does the same to the other leg.
    /// </summary>
    internal void BecomeOrdinary(TransactionType type, Category category, DateTimeOffset now)
    {
        GuardNotDeleted();
        Type = type;
        TransferId = null;
        CategoryId = category.Id;
        UpdatedAt = now;
        Replace([new SplitLine(category, Amount)], now);
    }

    /// <summary>Records a transaction split across several categories.</summary>
    /// <param name="moves">What it moves; it takes the account's currency.</param>
    /// <param name="lines">
    /// <see cref="MinSplitLines"/> to <see cref="MaxLines"/>, each on one of the user's categories that is not
    /// archived, of the kind the type takes, and together exactly the amount.
    /// </param>
    /// <param name="isRefund">Whether the expense is a refund; no other type can be.</param>
    public static Transaction RecordSplit(
        TransactionType type, AccountAmount moves, DateOnly date, IReadOnlyList<SplitLine> lines, string? payee, string? note, Moment at,
        bool isRefund = false) =>
        RecordOn(type, moves, date, SplitAcross(lines), payee, note, at, isRefund);

    /// <summary>Records income or an expense on these lines: one, with the whole amount, or the lines of a split.</summary>
    private static Transaction RecordOn(
        TransactionType type, AccountAmount moves, DateOnly date, IReadOnlyList<SplitLine> lines, string? payee, string? note, Moment at, bool isRefund)
    {
        ArgumentNullException.ThrowIfNull(at);
        GuardRecordedOnItsOwn(type);
        var transaction = new Transaction(at.Now);
        transaction.Set(type, moves, date, lines, payee, note, at, isRefund);
        return transaction;
    }

    /// <summary>
    /// Changes the transaction, to another account (whose currency it then takes) if need be. It may keep
    /// a category that has since been archived, but not move to one. A split transaction becomes one line
    /// again, on <paramref name="category"/>.
    /// </summary>
    /// <param name="moves">What it now moves; it takes the account's currency.</param>
    /// <param name="isRefund">Whether the expense is a refund; no other type can be.</param>
    public void Edit(
        TransactionType type, AccountAmount moves, DateOnly date, Category category, string? payee, string? note, Moment at, bool isRefund = false) =>
        EditOn(type, moves, date, OneLine(category, moves), payee, note, at, isRefund);

    /// <summary>
    /// Changes the transaction as <see cref="Edit"/> does, and splits it across <paramref name="lines"/>, which
    /// replace the lines it had. A line may keep a category the transaction already has that has since been
    /// archived.
    /// </summary>
    /// <param name="moves">What it now moves; it takes the account's currency.</param>
    /// <param name="isRefund">Whether the expense is a refund; no other type can be.</param>
    public void EditSplit(
        TransactionType type, AccountAmount moves, DateOnly date, IReadOnlyList<SplitLine> lines, string? payee, string? note, Moment at,
        bool isRefund = false) =>
        EditOn(type, moves, date, SplitAcross(lines), payee, note, at, isRefund);

    private void EditOn(
        TransactionType type, AccountAmount moves, DateOnly date, IReadOnlyList<SplitLine> lines, string? payee, string? note, Moment at, bool isRefund)
    {
        GuardNotDeleted();
        GuardNotALeg();
        GuardRecordedOnItsOwn(type);
        Set(type, moves, date, lines, payee, note, at, isRefund);
    }

    /// <summary>The one line of a transaction that is not split: its category and whole amount.</summary>
    private static SplitLine[] OneLine(Category category, AccountAmount moves)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(moves);
        return [new SplitLine(category, moves.Amount)];
    }

    /// <summary>
    /// The lines of a split, which has more than one: with one, a transaction is not split. How many there are is
    /// checked with the other rules of its lines, after those of the transaction.
    /// </summary>
    private static SplitLines SplitAcross(IReadOnlyList<SplitLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new SplitLines(lines.ToArray());
    }

    /// <summary>Lines entered as a split, however many there are.</summary>
    private sealed class SplitLines(IList<SplitLine> lines) : System.Collections.ObjectModel.ReadOnlyCollection<SplitLine>(lines);

    /// <summary>
    /// Puts exactly these tags on the transaction: the ones it had and are not among them come off. Tags are
    /// labels across categories (spec TXN-5), so they change nothing the transaction adds up to.
    /// </summary>
    /// <param name="tags">At most <see cref="MaxTags"/> of the user's tags, each once.</param>
    /// <exception cref="TransactionRuleViolation">More than <see cref="MaxTags"/> tags.</exception>
    public void TagWith(IReadOnlyCollection<Tag> tags, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tags);
        GuardNotDeleted();
        if (tags.Any(tag => tag is null))
        {
            throw new ArgumentException("A tag is never null.", nameof(tags));
        }

        var ids = tags.Select(tag => tag.Id).ToHashSet();
        if (ids.Count != tags.Count)
        {
            throw new ArgumentException("A transaction has a tag once.", nameof(tags));
        }

        if (ids.Count > MaxTags)
        {
            throw new TransactionRuleViolation(nameof(Tags), $"A transaction has at most {MaxTags} tags.");
        }

        var removed = _tags.RemoveAll(tag => !ids.Contains(tag.TagId));
        var added = tags.Where(tag => !_tags.Any(has => has.TagId == tag.Id)).Select(tag => new TransactionTag(tag)).ToList();
        _tags.AddRange(added);
        if (removed > 0 || added.Count > 0)
        {
            UpdatedAt = now;
        }
    }

    /// <summary>
    /// Puts one more tag on the transaction, as a bulk action does (spec TXN-7); one it has already changes nothing.
    /// </summary>
    /// <exception cref="TransactionRuleViolation">It has <see cref="MaxTags"/> tags already.</exception>
    public void AddTag(Tag tag, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tag);
        GuardNotDeleted();
        if (_tags.Any(has => has.TagId == tag.Id))
        {
            return;
        }

        if (_tags.Count >= MaxTags)
        {
            throw new TransactionRuleViolation(nameof(Tags), $"A transaction has at most {MaxTags} tags.", BulkRefusal.TooManyTags);
        }

        _tags.Add(new TransactionTag(tag));
        UpdatedAt = now;
    }

    /// <summary>Takes one tag off the transaction; one it does not have changes nothing.</summary>
    public void RemoveTag(Guid tagId, DateTimeOffset now)
    {
        GuardNotDeleted();
        if (_tags.RemoveAll(tag => tag.TagId == tagId) > 0)
        {
            UpdatedAt = now;
        }
    }

    /// <summary>
    /// Puts the whole of income or an expense on one category, as a bulk action does (spec TXN-7). It keeps its
    /// line's note. A split keeps its lines: their categories are changed by editing it. A reconciled transaction
    /// can change its category. Put on the category it is on already, it does not change.
    /// </summary>
    /// <param name="category">One of the user's categories of the kind the type takes, not archived unless it is the one the transaction is on.</param>
    /// <exception cref="TransactionRuleViolation">It is a leg of a transfer or split, or the category is of the other kind or archived.</exception>
    public void Recategorise(Category category, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);
        GuardNotDeleted();
        RefuseALeg();
        if (IsSplit)
        {
            throw new TransactionRuleViolation(nameof(Lines), "A split transaction keeps its lines: edit it to change their categories.", BulkRefusal.Split);
        }

        if (CategoryRuleBroken(category, Type, keeps: category.Id == CategoryId) is { } categoryRule)
        {
            throw new TransactionRuleViolation(
                nameof(CategoryId), categoryRule, category.Kind != Type.CategoryKind() ? BulkRefusal.CategoryKind : null);
        }

        if (category.Id == CategoryId)
        {
            return;
        }

        CategoryId = category.Id;
        UpdatedAt = now;
        Replace([new SplitLine(category, Amount, Lines[0].Note)], now);
    }

    /// <summary>
    /// Moves income or an expense to another account in its currency, as a bulk action does (spec TXN-7). Its
    /// amount, date and currency stay, and so does what it was converted into the base currency. It leaves the
    /// reconciliation it was ticked in, and a reconciled one cannot move.
    /// </summary>
    /// <param name="currency">The currency of the account it moves to.</param>
    /// <exception cref="TransactionRuleViolation">It is a leg of a transfer or reconciled, or the account is in another currency.</exception>
    public void MoveTo(Guid accountId, Currency currency, DateTimeOffset now)
    {
        GuardNotDeleted();
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A transaction is on an account.", nameof(accountId));
        }

        if (accountId == AccountId)
        {
            return;
        }

        RefuseALeg();
        if (currency != Currency)
        {
            throw new TransactionRuleViolation(nameof(AccountId), "A transaction moves only to an account in its own currency.", BulkRefusal.OtherCurrency);
        }

        GuardUnlocked(Type, accountId, Amount, Date, IsRefund);
        ReconciliationId = null;
        AccountId = accountId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Moves every line on <paramref name="from"/> onto <paramref name="into"/>, as merging the one category into
    /// the other does (spec CAT-3). The amounts and notes stay; a deleted transaction moves too, so it comes back on
    /// the category merged into.
    /// </summary>
    public void MoveLines(Category from, Category into, DateTimeOffset now)
    {
        Category.GuardMerge(from, into);
        var moving = _lines.Where(line => line.CategoryId == from.Id).ToList();
        if (moving.Count == 0)
        {
            return;
        }

        foreach (var line in moving)
        {
            line.MoveTo(into);
        }

        CategoryId = Lines[0].CategoryId;
        UpdatedAt = now;
    }

    private void GuardNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted transaction cannot be edited.");
        }
    }

    private void GuardNotALeg()
    {
        if (IsTransferLeg)
        {
            throw new InvalidOperationException("A leg of a transfer changes only with its transfer, so the other leg changes with it.");
        }
    }

    /// <summary>Refuses a change a bulk action makes to a leg of a transfer, which has no category and changes only with its transfer.</summary>
    private void RefuseALeg()
    {
        if (IsTransferLeg)
        {
            throw new TransactionRuleViolation(
                nameof(Type), "This is a leg of a transfer: it has no category, and changes only with its transfer.", BulkRefusal.TransferLeg);
        }
    }

    private static void GuardRecordedOnItsOwn(TransactionType type)
    {
        if (type.IsTransferLeg())
        {
            throw new TransactionRuleViolation(nameof(Type), "A transfer is recorded as a transfer, both legs together.");
        }
    }

    /// <summary>Marks the transaction as created from an import batch, which it belongs to from then on.</summary>
    public void ImportedBy(Guid importBatchId)
    {
        if (IsTransferLeg || RecurringRuleId is not null)
        {
            throw new InvalidOperationException("An import creates income and expenses of its own, never a leg of a transfer or an occurrence of a rule.");
        }

        if (importBatchId == Guid.Empty)
        {
            throw new ArgumentException("An import batch has an id.", nameof(importBatchId));
        }

        ImportBatchId = importBatchId;
    }

    /// <summary>Says which recurring rule posted the transaction, once: it is the occurrence of that rule due on its date.</summary>
    /// <exception cref="InvalidOperationException">Another rule already posted it, or it is a leg of a transfer: a rule posts income and expenses only.</exception>
    public void PostedBy(Guid recurringRuleId)
    {
        if (IsTransferLeg)
        {
            throw new InvalidOperationException("A recurring rule posts income and expenses, never a leg of a transfer.");
        }

        if (recurringRuleId == Guid.Empty)
        {
            throw new ArgumentException("A transaction is posted by a recurring rule.", nameof(recurringRuleId));
        }

        if (RecurringRuleId is { } already && already != recurringRuleId)
        {
            throw new InvalidOperationException("A transaction is posted by one recurring rule.");
        }

        RecurringRuleId = recurringRuleId;
    }

    /// <summary>Whether the amount is already converted into this base currency, and so is not converted again.</summary>
    public bool IsConvertedInto(Currency baseCurrency) => Conversion is { IsConverted: true } converted && converted.BaseCurrency == baseCurrency;

    /// <summary>
    /// Keeps the amount as converted into the user's base currency at the exchange rate of the transaction's
    /// date, or of the nearest earlier day that has one. It is converted once: only an edit of the amount, the
    /// date or the account, or another base currency, has it converted again.
    /// </summary>
    /// <param name="amount">The amount in <paramref name="baseCurrency"/>, rounded to its decimals.</param>
    /// <param name="exchangeRate">What one unit of the transaction's currency is worth in <paramref name="baseCurrency"/>.</param>
    /// <param name="exchangeRateDate">The day the rate is of, on or before the transaction's date.</param>
    /// <exception cref="InvalidOperationException">The amount is already converted into <paramref name="baseCurrency"/>.</exception>
    public void ConvertInto(Currency baseCurrency, decimal amount, decimal exchangeRate, DateOnly exchangeRateDate)
    {
        GuardNotConvertedInto(baseCurrency);
        if (exchangeRateDate > Date)
        {
            throw new ArgumentException("An amount is converted at the rate of its date or of an earlier day.", nameof(exchangeRateDate));
        }

        Conversion = BaseCurrencyConversion.Converted(baseCurrency, amount, exchangeRate, exchangeRateDate);
    }

    /// <summary>
    /// Flags the transaction as not converted: no exchange rate exists on or before its date, so it waits
    /// for one, in the base currency it will be converted into.
    /// </summary>
    /// <exception cref="InvalidOperationException">The amount is already converted into <paramref name="baseCurrency"/>.</exception>
    public void FlagNotConverted(Currency baseCurrency)
    {
        GuardNotConvertedInto(baseCurrency);
        Conversion = BaseCurrencyConversion.NotConverted(baseCurrency);
    }

    private void GuardNotConvertedInto(Currency baseCurrency)
    {
        if (IsConvertedInto(baseCurrency))
        {
            throw new InvalidOperationException("An amount is converted once and is not revalued.");
        }
    }

    /// <summary>What was converted is no longer what the transaction says, so it waits to be converted again.</summary>
    private void DropConversion() => Conversion = Conversion is { } conversion ? BaseCurrencyConversion.NotConverted(conversion.BaseCurrency) : null;

    /// <summary>Takes the transaction out of lists and balances. Deleting it again changes nothing.</summary>
    /// <exception cref="InvalidOperationException">The transaction is a leg of a transfer, which is deleted as a transfer.</exception>
    /// <exception cref="TransactionRuleViolation">The transaction is reconciled.</exception>
    public void Delete(DateTimeOffset now)
    {
        GuardNotALeg();
        DeleteWhateverItIs(now);
    }

    /// <summary>Deletes a leg, for <see cref="Transfer"/> alone, which deletes the other leg with it.</summary>
    internal void DeleteLeg(DateTimeOffset now) => DeleteWhateverItIs(now);

    /// <summary>Whether the transaction is deleted, and was deleted no longer than <see cref="RecoveryPeriod"/> before <paramref name="now"/>.</summary>
    public bool IsRecoverableAt(DateTimeOffset now) => DeletedAt is { } deletedAt && now - deletedAt <= RecoveryPeriod;

    /// <summary>
    /// Brings a deleted transaction back into lists and balances, as it was when it was deleted. Restoring one that is
    /// not deleted changes nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The transaction is a leg of a transfer, which is restored as a transfer; or it was deleted longer than
    /// <see cref="RecoveryPeriod"/> ago.
    /// </exception>
    public void Restore(DateTimeOffset now)
    {
        GuardNotALeg();
        RestoreWhateverItIs(now);
    }

    /// <summary>Restores a leg, for <see cref="Transfer"/> alone, which restores the other leg with it.</summary>
    internal void RestoreLeg(DateTimeOffset now) => RestoreWhateverItIs(now);

    private void RestoreWhateverItIs(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            return;
        }

        if (!IsRecoverableAt(now))
        {
            throw new InvalidOperationException($"A transaction deleted more than {RecoveryPeriod.TotalDays} days ago cannot be restored.");
        }

        DeletedAt = null;
        UpdatedAt = now;
    }

    private void DeleteWhateverItIs(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return;
        }

        if (IsReconciled)
        {
            throw new TransactionRuleViolation(
                nameof(IsReconciled), "This transaction is reconciled: reopen its reconciliation to move or delete it.", BulkRefusal.Reconciled);
        }

        // A deleted transaction is on no statement: it leaves the reconciliation it was ticked in.
        ReconciliationId = null;

        DeletedAt = now;
        UpdatedAt = now;
    }

    /// <param name="lines">
    /// None for a leg of a transfer. One for a transaction that is not split, with the whole amount: what is wrong
    /// with its category is wrong with the transaction's. The <see cref="SplitLines"/> of a split, each named by where it is.
    /// </param>
    private void Set(
        TransactionType type, AccountAmount moves, DateOnly date, IReadOnlyList<SplitLine> lines, string? payee, string? note, Moment at, bool isRefund)
    {
        ArgumentNullException.ThrowIfNull(moves);
        ArgumentNullException.ThrowIfNull(at);
        var (accountId, currency, amount) = moves;
        var (today, now) = at;
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a transaction type.");
        }

        GuardUnlocked(type, accountId, amount, date, isRefund);

        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Not a supported currency.");
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A transaction is on an account.", nameof(accountId));
        }

        if (!IsValidPayee(payee))
        {
            throw new ArgumentException($"A payee is at most {MaxPayeeLength} characters.", nameof(payee));
        }

        if (!IsValidNote(note))
        {
            throw new ArgumentException($"A note is at most {MaxNoteLength} characters.", nameof(note));
        }

        if (AmountRuleBroken(amount, currency) is { } amountRule)
        {
            throw new TransactionRuleViolation(nameof(Amount), amountRule);
        }

        if (date > today)
        {
            throw new TransactionRuleViolation(nameof(Date), "A transaction's date cannot be in the future.");
        }

        // A leg of a transfer is what every transaction is, an amount moved on an account on a date, and no more:
        // it has no lines.
        if (!type.IsTransferLeg())
        {
            GuardLines(type, currency, amount, lines);
        }

        if (isRefund && type != TransactionType.Expense)
        {
            throw new TransactionRuleViolation(nameof(IsRefund), "Only an expense can be a refund.");
        }

        // What was converted is no longer what the transaction says: it is converted again, at its date's rate.
        if (amount != Amount || date != Date || accountId != AccountId)
        {
            DropConversion();
        }

        // Moved to another account, it is on that account's statements, not on the one it was ticked against.
        if (accountId != AccountId)
        {
            ReconciliationId = null;
        }

        Type = type;
        AccountId = accountId;
        Currency = currency;
        Amount = amount;
        IsRefund = isRefund;
        Date = date;
        CategoryId = lines.Count > 0 ? lines[0].Category.Id : null;
        Payee = Normalised(payee);
        Note = Normalised(note);
        UpdatedAt = now;
        Replace(lines, now);
    }

    /// <summary>
    /// Refuses, while the transaction is reconciled, a change to what its account's balance depends on: the
    /// amount, the date and the account (spec section 3, rule 11), and the type and refund flag, which give the
    /// amount its sign.
    /// </summary>
    private void GuardUnlocked(TransactionType type, Guid accountId, decimal amount, DateOnly date, bool isRefund)
    {
        if (!IsReconciled)
        {
            return;
        }

        var changed =
            amount != Amount ? nameof(Amount)
            : date != Date ? nameof(Date)
            : accountId != AccountId ? nameof(AccountId)
            : type != Type ? nameof(Type)
            : isRefund != IsRefund ? nameof(IsRefund)
            : null;
        if (changed is not null)
        {
            throw new TransactionRuleViolation(changed, Locked, BulkRefusal.Reconciled);
        }
    }

    /// <summary>What a change refused on a reconciled transaction says.</summary>
    internal const string Locked = "This transaction is reconciled, so its amount, date, account and type cannot change until its reconciliation is reopened.";

    /// <summary>Ticks the transaction in a reconciliation, for &lt;see cref="Reconciliation"/&gt; alone.</summary>
    internal void TickIn(Guid reconciliationId) => ReconciliationId = reconciliationId;

    /// <summary>Unticks the transaction, for &lt;see cref="Reconciliation"/&gt; alone.</summary>
    internal void Untick() => ReconciliationId = null;

    /// <summary>Locks the transaction as its reconciliation completes, for &lt;see cref="Reconciliation"/&gt; alone.</summary>
    internal void Reconcile() => IsReconciled = true;

    /// <summary>Unlocks the transaction as its reconciliation is reopened, for &lt;see cref="Reconciliation"/&gt; alone.</summary>
    internal void Unreconcile() => IsReconciled = false;

    /// <summary>What rule an amount in this currency breaks, for a transaction, one of its lines or a recurring rule; null when it breaks none.</summary>
    internal static string? AmountRuleBroken(decimal amount, Currency currency) =>
        amount <= 0 ? "An amount is greater than zero."
        : !Amounts.IsValid(amount, currency) ? $"An amount in {currency} has at most {currency.DecimalPlaces()} decimals and {Amounts.MaxIntegerDigits} digits before them."
        : null;

    /// <summary>What rule income or an expense on this category breaks (spec section 3, rule 13); null when it breaks none.</summary>
    /// <param name="keeps">Whether it is on the category already, which an archived category keeps.</param>
    internal static string? CategoryRuleBroken(Category category, TransactionType type, bool keeps) =>
        category.IsArchived && !keeps ? "An archived category takes no new transactions."
        : category.Kind != type.CategoryKind() ? (type == TransactionType.Income ? "An income transaction takes an income category." : "An expense takes an expense category.")
        : null;

    /// <summary>The rules of a transaction's lines, each broken one named by where it is: <c>Lines[1].Amount</c>.</summary>
    private void GuardLines(TransactionType type, Currency currency, decimal amount, IReadOnlyList<SplitLine> lines)
    {
        // One line not entered as a split is a transaction that is not split: what is wrong with it is wrong with
        // the transaction.
        var split = lines is SplitLines;
        if (split && lines.Count is < MinSplitLines or > MaxLines)
        {
            throw new TransactionRuleViolation(nameof(Lines), $"A split has {MinSplitLines} to {MaxLines} lines.");
        }

        var categoriesItHas = _lines.Select(line => line.CategoryId).ToHashSet();
        for (var position = 0; position < lines.Count; position++)
        {
            var line = lines[position] ?? throw new ArgumentException("A line is never null.", nameof(lines));
            var category = line.Category ?? throw new ArgumentException("A line has a category.", nameof(lines));
            var at = split ? $"{nameof(Lines)}[{position}]." : string.Empty;
            if (!IsValidNote(line.Note))
            {
                throw new ArgumentException($"A note is at most {MaxNoteLength} characters.", nameof(lines));
            }

            if (AmountRuleBroken(line.Amount, currency) is { } amountRule)
            {
                throw new TransactionRuleViolation(at + nameof(Amount), amountRule);
            }

            if (CategoryRuleBroken(category, type, categoriesItHas.Contains(category.Id)) is { } categoryRule)
            {
                throw new TransactionRuleViolation(at + nameof(CategoryId), categoryRule);
            }
        }

        var remaining = RemainingToSplit(amount, lines.Select(line => line.Amount));
        if (remaining != 0)
        {
            throw new TransactionRuleViolation(
                nameof(Lines),
                remaining > 0
                    ? $"The lines of a split sum to the transaction's amount: {remaining} is left to split."
                    : $"The lines of a split sum to the transaction's amount: they come to {-remaining} more.");
        }
    }

    /// <summary>
    /// The line at each position says what was entered there: the ones the transaction has are changed where
    /// they stand, more are added after them and the ones past the last are removed.
    /// </summary>
    private void Replace(IReadOnlyList<SplitLine> lines, DateTimeOffset now)
    {
        var has = Lines;
        for (var position = 0; position < lines.Count; position++)
        {
            if (position < has.Count)
            {
                has[position].Become(lines[position]);
            }
            else
            {
                _lines.Add(new TransactionLine(position, lines[position], now));
            }
        }

        _lines.RemoveAll(line => line.Position >= lines.Count);
    }
}
