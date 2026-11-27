# Categorisation rules suggest each row's category in the preview, and the commit records the category sent for each row

Spec IMP-4 asks that imports and manual entry pick a category with the user's rules, and #24 that the user can still
change any row's category before committing. The commit of #22 took one expense and one income category for a whole
batch. Each row now needs a category of its own, and the rules could be applied in either of two places: when the
preview is read, or when the worker records the rows.

The rules are applied once, when the preview is read: each parsed row answers a `suggestedCategoryId`, which the SPA
shows in the row's category picker. The commit sends `rowCategories`, a category for any chosen row (the suggestion,
or what the user changed it to), and keeps the batch's `expenseCategoryId` and `incomeCategoryId` for the rows sent
without one, so a client that sends only those, as #22's did, commits as before. The batch stores the rows' categories
(`ImportBatches.RowCategories`, JSON by row number, beside `SelectedRows`), and the worker records each row on its own
category or the batch's. The commit applies no rule, and neither does the worker.

## Considered options

- **The worker applies the rules** to rows sent without a category. Rejected: what is imported would then depend on
  the rules as they are when the worker gets to the row, not as the user saw them in the preview; a rule edited
  between commit and import would change rows the user had checked. A user who clears a suggestion to use the batch's
  category could not, either.
- **The commit applies the rules** and stores the result. Rejected for the same reason in a smaller window, and because
  it makes "leave this row on the batch's category" impossible to say.
- **A category on every row, and no batch categories.** Rejected: it breaks #22's contract, and a statement of a few
  thousand rows mostly left on one category would send one entry for each.
- **The rules in the browser.** Rejected: the API owns every rule (spec section 9), and quick add needs them too.

## Consequences

- What a batch imports is exactly what its preview showed and the user sent; a rule changed later changes nothing
  already committed.
- A row's own category can be archived between commit and import; the batch then stops as `categoryArchived`, as it
  does for the batch's own categories.
- Quick add applies the same rules (`Categoriser`) on the server, but only for a payee with no history: what the user
  has recorded before wins over a rule.
