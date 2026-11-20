# Exchange rates come from Frankfurter, stored for every pair of currencies

Totals, budgets and reports convert amounts into the user's base currency (decision record 0002), so the app needs a daily rate between every pair of the currencies it supports: VND, USD and EUR. The worker fetches them from [Frankfurter](https://frankfurter.dev) (`GET /v2/rates`), the open-source API that blends the reference rates central banks publish. It needs no key and has no quota, covers the dong back to 1998, quotes every calendar day including weekends, and answers a range of days in one request, so the first fetch and catching up after downtime are each a single call. It can be self-hosted if the public instance ever becomes a problem (`ExchangeRates__SourceUrl`).

Each fetched day is stored as the rate of every ordered pair (six rows a day for three currencies), worked out from that day's quotes against the dollar. A lookup is then one indexed read of the pair's latest row on or before a date, with no inverting or crossing at read time. A stored day is never fetched again: Frankfurter's blended rate for the current day can still move in its last decimals, and an amount converted at a stored rate must keep matching it.

The source sits behind `IExchangeRateSource`; the rest of the app only sees `ICurrencyConversion`.

## Considered options

- **Frankfurter v1 / the European Central Bank feed directly.** Rejected: the ECB does not quote the dong.
- **ExchangeRate-API's open endpoint (`open.er-api.com`).** Rejected: it covers the dong, but the keyless endpoint serves only the latest rates, so a missed day or a new installation could not be backfilled from it.
- **fawazahmed0/currency-api (dated files on a CDN).** Rejected: it covers the dong and keeps history, but as one file per day, so a year of history is hundreds of requests, and it is a community data set with no service behind it.
- **Keyed services such as Open Exchange Rates.** Not taken for v1: a secret to manage in every environment, where Frankfurter needs none.
- **Rates published by Vietnamese banks.** Not evaluated beyond noting they are quoted against the dong only; the source must also give USD to EUR.
- **Storing only what the source quotes (USD to each) and crossing at read time.** Rejected: every reader would repeat the inverse and cross arithmetic, and "the nearest earlier date" would have to be resolved for two rows that may fall on different days.

## Consequences

- Rates are mid-market reference rates, not what a bank charged; a converted amount is an estimate, which is what budgets and reports need.
- The rate of "today" is the one first fetched that day (the worker fetches every six hours), not the day's final blend.
- The first fetch goes back one year (`ExchangeRates__DaysBackWhenEmpty`). An amount dated before the earliest stored rate is not converted.
- Adding a currency means checking Frankfurter covers it; rates for it start on the next fetched day, and earlier days have none unless they are backfilled by hand.
- If Frankfurter is unreachable, nothing is stored and conversions fall back to the latest stored day; the next fetch asks for every day since.
