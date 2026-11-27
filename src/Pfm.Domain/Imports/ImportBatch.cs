namespace Pfm.Domain.Imports;

/// <summary>Where an import batch has got to.</summary>
public enum ImportBatchStatus
{
    /// <summary>The file is uploaded and can be previewed with any mapping; nothing is imported.</summary>
    Uploaded = 1,

    /// <summary>The user committed it: the worker is importing the chosen rows, a few at a time.</summary>
    Committing = 2,

    /// <summary>Every chosen row is a transaction now.</summary>
    Committed = 3,

    /// <summary>The worker stopped, for the reason given; the transactions it created stay.</summary>
    Failed = 4,

    /// <summary>The user undid it: every transaction it created is deleted, and recoverable for 30 days like any other (spec IMP-5).</summary>
    Undone = 5,
}

/// <summary>Why the worker stopped importing a batch: a code the user is shown in their own language.</summary>
public enum ImportFailure
{
    /// <summary>The account was archived after the statement was committed.</summary>
    AccountArchived = 1,

    /// <summary>A category chosen for the statement was archived after it was committed.</summary>
    CategoryArchived = 2,

    /// <summary>The account's currency changed after the statement was committed, so its rows no longer read as they did.</summary>
    AccountCurrencyChanged = 3,

    /// <summary>A row broke a rule every transaction keeps.</summary>
    TransactionRefused = 4,

    /// <summary>The worker failed on it, for no reason the user can act on, several times in a row.</summary>
    Unexpected = 5,
}

/// <summary>An import batch that would break a rule. Names the property at fault.</summary>
public sealed class ImportRuleViolation(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}

/// <summary>
/// One uploaded statement file together with the transactions created from it (spec section 3). The file is
/// kept only until its rows are imported: it can hold what the app never stores, such as a full account number
/// (spec ACC-8). The user previews it with any mapping, then commits the rows they chose; the worker imports them
/// in order, a few at a time, each import saved with the count it moves on, so progress shows and no row is
/// imported twice.
/// </summary>
public sealed class ImportBatch : IUserOwned
{
    /// <summary>The largest file taken: room for many more rows than a statement holds.</summary>
    public const long MaxFileSize = 5 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    public const string FileSizeMessage = "Upload a CSV file of at most 5 MB.";

    /// <summary>
    /// The most rows a statement may have: twice the 5,000 a statement is expected to hold at most (spec section 8), and
    /// what bounds the preview, which answers every row at once.
    /// </summary>
    public const int MaxRows = 10_000;

    public const string RowsMessage = "Upload a statement of at most 10,000 rows.";

    private List<int> _selectedRows = [];

    private Dictionary<int, Guid> _rowCategories = [];

    /// <summary>For persistence, which sets every property itself.</summary>
    private ImportBatch()
    {
        FileName = "";
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The account the statement is imported into: every transaction created is on it, in its currency.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>What the user's file was called.</summary>
    public string FileName { get; private set; }

    /// <summary>The file's text; null once its rows are imported, or the import stopped.</summary>
    public string? Content { get; private set; }

    public ImportBatchStatus Status { get; private set; }

    /// <summary>How the file was read when it was committed; null before.</summary>
    public StatementMapping? Mapping { get; private set; }

    /// <summary>The numbers of the rows the user chose to import, in the order of the file.</summary>
    public IReadOnlyList<int> SelectedRows => _selectedRows;

    /// <summary>
    /// The categories the user gave chosen rows of their own, by row number (spec IMP-4): such a row is recorded on its
    /// own category, and the rest on <see cref="ExpenseCategoryId"/> or <see cref="IncomeCategoryId"/>.
    /// </summary>
    public IReadOnlyDictionary<int, Guid> RowCategories => _rowCategories;

    /// <summary>The category money going out is recorded on; null when no row chosen without a category of its own takes money out.</summary>
    public Guid? ExpenseCategoryId { get; private set; }

    /// <summary>The category money coming in is recorded on; null when no row chosen without a category of its own brings money in.</summary>
    public Guid? IncomeCategoryId { get; private set; }

    /// <summary>The rows of the file that are transactions or failed to be read as one, once committed.</summary>
    public int RowCount { get; private set; }

    /// <summary>How many rows the user chose to import.</summary>
    public int SelectedCount { get; private set; }

    /// <summary>How many of the chosen rows are transactions now; it reaches <see cref="SelectedCount"/> when the batch is committed.</summary>
    public int ImportedCount { get; private set; }

    /// <summary>How many likely duplicates were left out: those the user did not tick.</summary>
    public int DuplicateCount { get; private set; }

    /// <summary>How many rows could not be read as a transaction, and so were not imported.</summary>
    public int FailedCount { get; private set; }

    /// <summary>How many times in a row the worker has failed on the batch unexpectedly; back to 0 once a save of its rows succeeds.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>Why the worker stopped, when it did.</summary>
    public ImportFailure? Failure { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Today in the user's time zone when the batch was committed. The worker reads the file, and records each row's
    /// transaction, as of that day, so a row the user saw as importable stays so whatever their time zone says later.
    /// </summary>
    public DateOnly? CommittedOn { get; private set; }

    /// <summary>When the last chosen row was imported.</summary>
    public DateTimeOffset? CommittedAt { get; private set; }

    /// <summary>When the user undid the batch; null unless they did.</summary>
    public DateTimeOffset? UndoneAt { get; private set; }

    /// <summary>Changes every time the batch is saved, so two workers never import the same rows.</summary>
    public uint RowVersion { get; private set; }

    /// <summary>The name a file is kept under: trimmed and at most <see cref="MaxFileNameLength"/> characters; null when it has none.</summary>
    public static string? FileNameFrom(string? name)
    {
        var trimmed = Path.GetFileName(name?.Trim());
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= MaxFileNameLength ? trimmed : trimmed[..MaxFileNameLength];
    }

    /// <param name="content">The file as text, of at most <see cref="MaxRows"/> rows.</param>
    public static ImportBatch Upload(Guid accountId, string fileName, string content, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        if (StatementText.MostRows(content) > MaxRows)
        {
            throw new ArgumentException(RowsMessage, nameof(content));
        }

        return new ImportBatch
        {
            Id = Guid.CreateVersion7(now),
            AccountId = accountId,
            FileName = FileNameFrom(fileName) ?? throw new ArgumentException("A file needs a name.", nameof(fileName)),
            Content = content,
            Status = ImportBatchStatus.Uploaded,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>The file read with this mapping. Only while the file is kept: before the batch is committed, and while it is imported.</summary>
    /// <param name="currency">The currency of <see cref="AccountId"/>.</param>
    /// <param name="today">Today in the user's time zone.</param>
    public Statement Read(StatementMapping mapping, Currency currency, DateOnly today) =>
        Statement.Read(Content ?? throw new InvalidOperationException("The file of this batch is no longer kept."), mapping, currency, today);

    /// <summary>
    /// Chooses the rows to import and queues them for the worker (spec IMP-1). Only rows the statement reads as
    /// transactions can be chosen, and at least one; a likely duplicate is imported only when it is chosen.
    /// </summary>
    /// <param name="statement">The file read with <paramref name="mapping"/> on <paramref name="today"/>, as the user previewed it.</param>
    /// <param name="likelyDuplicates">The rows of <paramref name="statement"/> that are likely duplicates.</param>
    /// <param name="today">Today in the user's time zone: the day the rows are read as of until they are imported.</param>
    /// <param name="rowCategories">
    /// Categories of their own for some of the chosen rows, by row number; the batch's categories are for the rest, so
    /// each is needed only when a chosen row without one of its own takes money that way.
    /// </param>
    public void Commit(
        StatementMapping mapping, Statement statement, IReadOnlyCollection<int> rows, IReadOnlySet<int> likelyDuplicates,
        Guid? expenseCategoryId, Guid? incomeCategoryId, DateOnly today, DateTimeOffset now, IReadOnlyDictionary<int, Guid> rowCategories)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(likelyDuplicates);
        ArgumentNullException.ThrowIfNull(rowCategories);
        if (Status != ImportBatchStatus.Uploaded)
        {
            throw new ImportRuleViolation(nameof(Status), "This statement has been committed already.");
        }

        if (!mapping.IsComplete)
        {
            throw new ImportRuleViolation(nameof(Mapping), "Choose the date column and the amount column, or the debit and credit columns.");
        }

        var entries = statement.Entries.ToDictionary(row => row.Number, row => row.Entry!);
        var chosen = rows.Distinct().Order().ToList();
        if (chosen.Count == 0)
        {
            throw new ImportRuleViolation(nameof(SelectedRows), "Choose at least one row to import.");
        }

        if (chosen.Where(row => !entries.ContainsKey(row)).Select(row => (int?)row).FirstOrDefault() is { } notRead)
        {
            throw new ImportRuleViolation(nameof(SelectedRows), $"Row {notRead} is not a transaction that can be imported.");
        }

        if (rowCategories.Keys.Where(row => !chosen.Contains(row)).Select(row => (int?)row).FirstOrDefault() is { } notChosen)
        {
            throw new ImportRuleViolation(nameof(RowCategories), $"Row {notChosen} is not chosen to import.");
        }

        var types = chosen.Where(row => !rowCategories.ContainsKey(row)).Select(row => entries[row].Type).ToHashSet();
        if (types.Contains(StatementEntryType.Expense) && expenseCategoryId is null)
        {
            throw new ImportRuleViolation(nameof(ExpenseCategoryId), "Choose the category money going out is recorded on.");
        }

        if (types.Contains(StatementEntryType.Income) && incomeCategoryId is null)
        {
            throw new ImportRuleViolation(nameof(IncomeCategoryId), "Choose the category money coming in is recorded on.");
        }

        Mapping = mapping;
        _selectedRows = chosen;
        _rowCategories = new Dictionary<int, Guid>(rowCategories);
        ExpenseCategoryId = types.Contains(StatementEntryType.Expense) ? expenseCategoryId : null;
        IncomeCategoryId = types.Contains(StatementEntryType.Income) ? incomeCategoryId : null;
        FailedCount = statement.Rows.Count(row => row.Kind == StatementRowKind.Failed);
        RowCount = entries.Count + FailedCount;
        SelectedCount = chosen.Count;
        DuplicateCount = likelyDuplicates.Count(row => !chosen.Contains(row));
        CommittedOn = today;
        Status = ImportBatchStatus.Committing;
        UpdatedAt = now;
    }

    /// <summary>
    /// Every chosen row as it is imported, in the order of the file, while the file is kept, read as of the day the
    /// batch was committed. A chosen row the file no longer reads as a transaction cannot be imported: the mapping and
    /// the day are the batch's own, so only the account's currency can have changed.
    /// </summary>
    /// <param name="currency">The currency of <see cref="AccountId"/>.</param>
    /// <exception cref="ImportRuleViolation">A chosen row is no longer read as a transaction.</exception>
    public IReadOnlyList<StatementEntry> ChosenEntries(Currency currency)
    {
        if (Mapping is null || CommittedOn is not { } committedOn)
        {
            throw new InvalidOperationException("The batch is not committed.");
        }

        var entries = Read(Mapping, currency, committedOn).Entries
            .ToDictionary(row => row.Number, row => row.Entry!);
        return [.. _selectedRows.Select(row => entries.TryGetValue(row, out var entry)
            ? entry
            : throw new ImportRuleViolation(nameof(SelectedRows), $"Row {row} can no longer be imported: the account's currency has changed."))];
    }

    /// <summary>The category the user gave the row of its own; null for one recorded on the batch's category.</summary>
    public Guid? CategoryOf(int row) => _rowCategories.TryGetValue(row, out var category) ? category : null;

    /// <summary>
    /// Counts the next rows as imported, saved together with their transactions. Once every chosen row is, the
    /// batch is committed and lets its file go.
    /// </summary>
    public void Imported(int count, DateTimeOffset now)
    {
        if (Status != ImportBatchStatus.Committing || count < 0 || ImportedCount + count > SelectedCount)
        {
            throw new InvalidOperationException("Only the chosen rows of a batch being committed are imported.");
        }

        ImportedCount += count;
        FailedAttempts = 0;
        UpdatedAt = now;
        if (ImportedCount == SelectedCount)
        {
            Status = ImportBatchStatus.Committed;
            CommittedAt = now;
            Content = null;
        }
    }

    /// <summary>
    /// Counts a failed attempt at importing the next rows, for no reason the batch itself gives. Once
    /// <paramref name="maxAttempts"/> have failed in a row, the batch is stopped as <see cref="ImportFailure.Unexpected"/>.
    /// </summary>
    public void AttemptFailed(int maxAttempts, DateTimeOffset now)
    {
        if (Status != ImportBatchStatus.Committing)
        {
            throw new InvalidOperationException("Only a batch being committed is imported.");
        }

        FailedAttempts++;
        UpdatedAt = now;
        if (FailedAttempts >= maxAttempts)
        {
            Fail(ImportFailure.Unexpected, now);
        }
    }

    /// <summary>
    /// Undoes the batch (spec IMP-5): whatever it got to, committing, committed or failed, it is undone, and the worker
    /// imports no more of it. Its transactions are deleted by Transactions, saved together with this, so a worker
    /// importing the next rows at the same moment fails on the batch's row version and saves none of them. Undoing it
    /// again changes nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The batch was never committed, so it has nothing to undo.</exception>
    public void Undo(DateTimeOffset now)
    {
        if (Status == ImportBatchStatus.Undone)
        {
            return;
        }

        if (Status == ImportBatchStatus.Uploaded)
        {
            throw new InvalidOperationException("A statement never committed has nothing to undo.");
        }

        Status = ImportBatchStatus.Undone;
        UndoneAt = now;
        Content = null;
        UpdatedAt = now;
    }

    /// <summary>Stops importing, for a reason the user is shown. What was imported stays; the file is let go.</summary>
    public void Fail(ImportFailure reason, DateTimeOffset now)
    {
        Status = ImportBatchStatus.Failed;
        Failure = reason;
        Content = null;
        UpdatedAt = now;
    }
}
