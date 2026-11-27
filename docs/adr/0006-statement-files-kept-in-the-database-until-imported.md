# Statement files are kept in the database until imported, and committed batches are the worker's queue

An import has three steps the spec names (section 11): upload the file, preview it, commit it. The user maps the file
after uploading it, and previews it again with every change to the mapping, so the API must keep the file between
requests. Committing must run as a background job with progress (spec section 5), and the specification's job runner,
Hangfire (section 9), is not in the repository yet: every job so far is a `BackgroundService` on a timer.

The uploaded file is kept as text in the import batch's own row (`ImportBatches.Content`) until its last row is
imported, then dropped. Committing records the mapping and the chosen row numbers on the batch and marks it
`Committing`; the worker's `ImportCommitJob` looks for such batches every two seconds (through a partial index that is
empty nearly always), reads the file again with the stored mapping and imports the next 250 chosen rows. Those
transactions and the batch's `ImportedCount` are one save, guarded by the batch's row version. Progress is that count
against `SelectedCount`, which the SPA reads with `GET /imports/{id}` every second.

## Considered options

- **The file in object storage, as attachments are** (decision record 0004). Rejected: a statement is working
  material, not a document the user keeps. It is read whole on every preview, so storage would add a round trip to
  each, and the batch row and the file would have to be created, cleaned up and erased together across two systems.
  At 5 MB at most and gone once imported, it costs the database little.
- **Keeping the file after the import**, to show or re-read it later. Rejected: a statement can carry the full account
  number, which the app never stores (spec ACC-8). The mapping and the transactions are what #23 (undo, saved mapping)
  needs.
- **Rows parsed into a table of their own at upload.** Rejected: the rows depend on the separator and the rows skipped,
  which the user chooses after uploading.
- **Parsing in the browser**, sending only the rows. Rejected: the API owns every rule (spec section 9), and the
  preview's likely duplicates need the account's transactions.
- **Hangfire now**, for this one job. Rejected for this slice: it brings its own schema and dashboard for one queue that
  a row status and an index already give. When Hangfire arrives, `ImportCommits.ImportNextAsync` is the job it runs.
- **Importing in the commit request itself.** Rejected: 5,000 rows, each converted into the base currency, take longer
  than a request should, and the spec asks for a background job with progress.
- **Server-sent events or a WebSocket for progress.** Rejected: polling one small row every second for the minute an
  import lasts is simpler and works through any proxy.

## Consequences

- An uploaded file that is never committed stays in its row. Nothing sweeps those up yet; #23, which lists past
  batches, is where abandoned uploads can be discarded.
- The worker reads the file again for every save. At 250 rows a save that is 20 reads of a statement of 5,000 rows,
  each a few milliseconds.
- A user waits up to two seconds after committing before the first rows go in (`Imports__CommitEvery`).
- A batch is exported with the user's data, its file with it while it is kept, and erased with the user.
