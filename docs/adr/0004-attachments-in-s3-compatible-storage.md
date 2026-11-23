# Attachments live in S3-compatible object storage, uploaded through the API and read through signed URLs

Receipts attached to transactions (spec TXN-6) are files of up to 5 MB that must sit in private object storage and be served only through short-lived signed URLs (spec section 8). The cloud provider is still an open question (spec section 15), so the storage is addressed through the one API nearly every provider and self-hosted server speaks: S3. The app uses the AWS SDK for .NET against a bucket named in configuration (`Storage__*`); with `Storage__ServiceUrl` it talks to anything S3-compatible, and without it to Amazon S3. The code that knows this is `S3ObjectStorage`, behind `IObjectStorage`.

Locally and in the integration tests the storage is the [Versity S3 Gateway](https://github.com/versity/versitygw) over a folder. It is a single small container, is actively maintained, and verifies request signatures, so the tests prove that a file cannot be read without a valid, unexpired signature rather than assuming it.

An upload goes through the API, which is the only place the rules can be enforced before the file exists: the size, and the type as read from the file's own first bytes. A download never goes through the API: `GET /attachments/{id}` checks that the attachment is the signed-in user's and answers a URL signed for five minutes, which the browser opens directly.

## Considered options

- **Azure Blob Storage with Azurite as the emulator.** Rejected for now: it would choose the cloud provider by the back door. If Azure is chosen later, `IObjectStorage` gets a second implementation, or the S3 one points at a gateway.
- **MinIO as the emulator.** Rejected: its container images are no longer published, so neither Compose nor CI could pull it.
- **Files in PostgreSQL (`bytea`).** Rejected: the spec asks for object storage, and files in the database bloat backups and are served through the API's memory.
- **The browser uploads straight to storage with a signed PUT or POST policy.** Rejected: storage would accept the bytes before the API has seen them, so the type check (which reads the file) would have to happen after the fact, with cleanup for what fails it. At 5 MB the API can afford to take the upload itself.
- **The API streams downloads itself.** Rejected: the spec asks for signed URLs, and it keeps file traffic off the API.
- **`GET /attachments/{id}` answers a redirect to the signed URL.** Rejected: the SPA calls the API with `fetch`, which would follow the redirect to another origin; an answer that carries the URL lets the SPA open it in a new tab and show a failure itself.

## Consequences

- A signed URL works for anyone who holds it until it expires, sign-in or not. That is what "signed" buys, and why it lasts minutes and is never stored or logged (request logs leave out the query string, and the URL is in a response body, not a path).
- Removing an attachment deletes its row, then its file. If the second step fails, a file is left that nothing points at; nothing sweeps those up yet. The other order would leave an attachment that is listed and cannot be opened.
- The storage serves files from its own origin, never the app's, so an uploaded file cannot run as a page of the app. Files are served `inline` with the content type the API read from them.
- The bucket and its privacy are the deployment's to set up; the app only refuses to start when no bucket is named.
- A stored file is not scanned for malware. It is only ever handed back to the user who uploaded it.
