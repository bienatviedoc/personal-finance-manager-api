# End-to-end tests live in the SPA repository and run against the API's Compose stack

Spec section 13 asks for one Playwright journey (sign up, add income and expense, transfer, set a budget, view a
report, import a CSV) and axe checks in CI, against the whole product. The product is two repositories: this API, with
the `compose.yaml` that starts PostgreSQL, migrations, the API, the worker, Mailpit and the storage emulator, and the
SPA, with the nginx image that serves the app and passes `/api` on to the API. A journey needs both, and one of them
has to own it.

The suite lives in the SPA repository (`e2e/`, run with `npm run e2e`). `scripts/e2e.sh` there layers
`e2e/compose.yaml` over this repository's `compose.yaml`: it adds the SPA's own nginx image in front of the API, as a
deployment serves it, gives the stack its own Compose project and images (`pfm-e2e`) so a development stack keeps
running beside it, points the links in emails at the SPA, and lifts the sign-in rate limit, which one browser would
otherwise meet. The email verification link is read from Mailpit's API. The accessibility checks sign in as a user the
`seed` command gives a year of sample data, so they see full screens rather than empty states. In CI the SPA's
workflow checks this repository out at `main` and builds its images from source, the same `main` the SPA's
generated client is already checked against.

## Consequences

- An SPA pull request is tested against the API as it is on `main`. An API change that breaks the journey shows up
  on the next SPA pull request (or a re-run), not on the API's own pull request; the API's integration tests remain
  its gate.
- This repository's `compose.yaml` is part of the SPA's test setup: a renamed service, or a new one the API needs
  to start, is a change the e2e stack takes on by itself, since it layers on the file rather than copying it. A
  service the API comes to depend on must keep working without published ports.
- Each CI run builds the API and worker images from source (a few minutes), as no image is published yet.

## Considered options

- **The suite in this repository**, building the SPA from its `main`. Rejected: the journey and the axe checks are
  about the screens. Their selectors (roles and accessible names) and the violations they find change with SPA code,
  and an accessibility fix belongs in the same pull request as the check that found it.
- **A third repository for the suite.** Rejected: a third place to change for every screen, and nobody's pull request
  would run it.
- **Testing against published images** instead of building from source. Not possible yet: CI builds images but
  publishes none (spec section 13 defers deployment until a cloud provider is chosen). Once `main` publishes the API
  and worker images, the e2e stack can pull them instead of building them.
- **The Vite dev server instead of the nginx image.** Rejected: the image is what is deployed, with its security
  headers and content security policy, which a check of the real app should include.
