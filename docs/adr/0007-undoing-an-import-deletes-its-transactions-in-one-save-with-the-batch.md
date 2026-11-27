# Undoing an import deletes its transactions in one save with the batch, which stops a commit still in progress

Undoing an import batch (spec IMP-5) removes every transaction it created, edited or not; they stay recoverable for 30
days like any deleted transaction, and the undo is refused while any of them is reconciled. Three things make this more
than a bulk delete: the worker may still be importing the batch when the user undoes it, a transaction may have become
a leg of a transfer since it was imported (mark as transfer, IMP-7), and deleting money moves budgets.

`UndoImport` is one use case. It asks Transactions, through `IImportedTransactions.DeleteAsync`, to delete softly every
transaction whose `ImportBatchId` is the batch's, refusing, with nothing deleted, when any is reconciled; marks the batch
`Undone` (dropping its file, if the worker had not); and saves both at once. The save is guarded by the batch's row
version, as the worker's saves are (decision record 0006). So an undo and the worker importing the next rows never both
save: when the worker saves first, the undo's save fails as a concurrent change and is made again, finding the rows just
imported too; when the undo saves first, the worker's save fails on the batch, saves none of its rows, and the next run
finds the batch no longer `Committing`. An undo can therefore be made at any time after a commit, and stops the import.

A transaction marked as a leg of a transfer keeps its `ImportBatchId`, and undoing deletes the whole transfer, the other
leg with it, as deleting one leg does (spec section 3, rule 3); a reconciled other leg refuses the undo. Restoring either
leg restores the transfer. Once saved, the budget notifications of the dates of the income and expenses deleted are
checked, as a delete does (an imported expense edited into a refund raises spent again when it goes).

The batch stays, `Undone`, with its counts and `UndoneAt`, in the list of past imports. The transactions are deleted
like any other: they are listed as recently deleted, restorable one by one, and purged after 30 days. The mapping saved
for the account (IMP-2) is left as it was.

## Considered options

- **Refuse to undo while the batch is committing.** Simpler to explain, but the user who sees the wrong rows going in
  would have to wait for all of them before undoing. The batch's row version already serialises the two, so stopping
  costs nothing more.
- **Delete the transactions for good, or delete the batch.** Rejected: the spec keeps them recoverable for 30 days, and
  the batch is the history the user sees.
- **Refuse to undo while a transaction of the batch is a leg of a transfer**, or unmark it first. Refusing breaks "edited
  or not": marking is an edit. Unmarking needs a category for the other leg, which nothing can choose for the user.
  Deleting the transfer is what deleting that transaction alone does already.
- **A bulk SQL update by `ImportBatchId`.** Rejected: it would pass by the domain's rules (reconciled transactions,
  transfers deleted both legs together) and the row versions that guard every transaction.

## Consequences

- An undo reads every transaction of the batch, at most the 10,000 rows a statement has, and saves them in one
  transaction.
- An undo meeting the worker is made again, up to three times (`ConcurrentChanges.Attempts`), like any delete.
- Undo is one call (`UndoImportHandler`), so whatever must happen on every undo, such as an audit entry (#45), is added
  there.
