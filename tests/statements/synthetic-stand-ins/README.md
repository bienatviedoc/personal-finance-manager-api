# SYNTHETIC stand-in statements: replace with anonymised real ones

**These are not real bank exports.** They were generated on 27 November 2026 to stand in for Vietcombank and
Techcombank statements until anonymised real samples are available (issue #22; spec section 5 asks for real sample
files, and section 15 for golden-file tests "for the formats we see"). Every name, account number and reference in
them is invented.

They aim at the shapes the import must handle, from how the two banks usually lay out their exports:

| File | Encoding | Separator | Preamble | Amount columns | Number format | Date format | Extras |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `vietcombank-sample.csv` | UTF-8 with BOM | `,` | 6 rows (bank, title, account, period, blank) | separate debit and credit | `1,500,000` (comma thousands, quoted) | `dd/MM/yyyy` | row number and reference columns, Vietnamese headers, a footer total row |
| `techcombank-sample.csv` | UTF-8, no BOM | `;` | 5 rows (title, holder, account, period, blank) | one signed amount, plus balance | `1.500.000,00` (dot thousands, comma decimals) | `dd-MM-yyyy` | bilingual headers, quoted details, a remitter column |

Each covers August to October 2026, with 56 transactions.

## How the golden-file tests use them

`GoldenStatementTests` (in `Pfm.Domain.UnitTests`) reads every `<name>.csv` in this folder with the mapping a user
would choose for it, `<name>.mapping.json` (the same choices the import screen offers, plus the account's
`currency` and the `today` the rows are read on), and compares what it reads, row by row, with
`<name>.expected.csv`: each row's number, what it is (`header`, `skipped`, `parsed`, `failed`, ...), and for a
parsed row its date, type, amount and description, or for a failed one what is wrong with it.
`ImportTests` in `Pfm.Api.IntegrationTests` also uploads each file through the API and imports it.

## Replacing them with real statements

1. Anonymise a real export: change names, account and card numbers and references, keep the layout, encoding,
   separators, number and date formats exactly as the bank writes them.
2. Put it here in place of the stand-in (or beside it, under a new name) and write its `.mapping.json`.
3. Run the golden tests once with `PFM_WRITE_GOLDEN=1` to write its `.expected.csv`, then **check that file by hand
   against the statement** (dates, amounts, which way the money went) before committing it: it is the source of truth.
4. Delete this README's warning and the stand-ins, and rename the folder.
