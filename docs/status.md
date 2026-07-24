# DocumentOCR — Status

_Last reviewed: 2026-07-24, against the current state of the repository (branch `main`)._

## Product overview

DocumentOCR is a single-tenant SaaS MVP that turns Vietnamese invoices/receipts (PDF, JPG, PNG)
into structured, exportable data. A user uploads a document, the system OCRs and extracts key
fields (supplier, tax code, invoice number/date, subtotal/VAT/total, currency, document type,
notes) in the background, the user reviews and corrects the result in a browser UI, and the
reviewed documents are exported to an Excel workbook. Full product brief:
[product-context.md](product-context.md).

The guiding principle is **"AI reads 70–80%, the user corrects 20–30%"** — the product is not
trying to be 100% automatic; it's trying to save time over fully manual entry.

## Current workflow

1. User uploads one or more PDF/JPG/PNG files (drag-drop or file picker) — client-side type/size
   pre-filter, then server-side extension + MIME + magic-byte validation, 20 MB/file limit.
2. `DocumentService.UploadAsync` stores the file (`LocalDocumentStorageService`) and creates a
   `Document` row with `Status = Uploaded`.
3. A Hangfire job (`DocumentProcessingJob`, 3 automatic retries at 30s/120s/300s) is enqueued and
   runs `DocumentProcessingService.ProcessAsync` out of the HTTP request path.
4. The configured `IDocumentOcrProvider` (Fake / Azure / Paddle, chosen by `Ocr:Provider` config)
   analyzes the file and returns a provider-neutral `NormalizedOcrDocument`.
5. `FieldExtractionService` scores candidates from key-value pairs, provider structured fields,
   table footers, line-proximity keyword heuristics, and regex fallbacks, and picks the best
   candidate per field.
6. `FieldNormalizationService` normalizes money/date/tax-code/currency into `NormalizedValue`,
   keeping `RawValue` untouched.
7. `FieldValidationService` produces `ValidationWarning`s (missing required fields, bad tax code,
   bad/far-future date, total-mismatch, low confidence).
8. Document flips to `Processed`; the user opens the Review page (`FieldEditor`), sees the
   original file preview side-by-side with editable fields and inline warnings, edits values, and
   saves — flips the document to `Reviewed`.
9. User selects one or more `Processed`/`Reviewed` documents and exports them to a two-sheet
   Excel workbook (`Documents`, `Warnings`) with Vietnamese column headers.

Status machine: `Uploaded → Processing → Processed | Failed → Reviewed → Exported`. In the
current code, `Exported` is defined on the enum but nothing in the API/UI ever sets it — export
only produces a file download, it does not mutate document status (see Known issues).

## Completed features

- **Upload pipeline**: multi-file upload, per-file independent validation (extension + spoofable
  client MIME cross-check + magic-byte signature check), 20 MB limit, safe generated storage
  paths, batch endpoint (`POST /api/documents/upload`) that never lets one bad file block others.
- **Async processing via Hangfire**: OCR never runs in the request thread; Hangfire dashboard at
  `/hangfire` is local-only (custom `IDashboardAuthorizationFilter`); automatic retry (3 attempts).
- **Three working OCR providers**, swappable purely via `Ocr:Provider` config, no code change:
  - `FakeOcrProvider` — deterministic Vietnamese VAT invoice fixture, no network calls.
  - `AzureDocumentIntelligenceProvider` — real Azure Document Intelligence integration
    (`prebuilt-layout` + `keyValuePairs` add-on by default; other prebuilt models selectable).
  - `PaddleOcrProvider` — HTTP client for a self-hosted PaddleOCR service (service itself is not
    part of this repo; contract documented in [LOCAL_DEVELOPMENT.md](../LOCAL_DEVELOPMENT.md)).
  - Fail-fast startup validation: booting with `Ocr:Provider=Azure`/`Paddle` but missing
    credentials/`BaseUrl` throws `OptionsValidationException` at host startup, not on first upload.
- **Field extraction**: multi-strategy candidate scoring (KeyValuePair, StructuredField, table
  footer, line-keyword proximity, top-line merchant heuristic for POS receipts, full-text/paragraph
  regex fallback, document-type and currency heuristics), diacritics-insensitive Vietnamese keyword
  matching, per-field confidence + `SourceType`/`ExtractionMethod`/`SourceText` audit trail.
- **Normalization**: money (dot/comma/space thousands separators, `VND`/`₫`/`đ` suffixes),
  Vietnamese date formats → ISO, tax code → digits-only, currency code → `VND`. `RawValue` and
  `NormalizedValue` always kept separate on `ExtractedField`.
- **Validation/warnings**: required-field checks (now driven by the resolved `DocumentProfile` —
  see "Dynamic document review profiles" below — tax-code requirement varies by category), tax-code
  format/length (10 or 13 digits), invalid/far-future date, subtotal+VAT≈total consistency
  (tolerance = max(1 VND, 0.5%)), low-confidence flag (<0.75).
- **Dynamic document review profiles**: `GET /api/documents/{id}/review` (`DocumentReviewResponse`,
  `DocumentReviewMappingService`) resolves a `DocumentCategory` (VatInvoice, SalesReceipt,
  PosReceipt, RestaurantBill, AppReceiptScreenshot, InternationalInvoice, CommercialInvoice, Unknown)
  and maps extracted fields onto that category's `DocumentProfile`
  (`Infrastructure/Profiles/DocumentProfileCatalog`) — sections, labels, data types, required-ness,
  and severities are all profile-driven rather than a fixed field list. Alias-aware (e.g.
  `SellerName`/`MerchantName`/`VendorName`/`StoreName` all resolve to the legacy `SupplierName`
  extracted field), so the existing 10-field extractor output satisfies the richer vocabulary
  without any extraction changes. Unmapped extracted fields land in an "Other detected fields"
  section rather than being dropped. See [decisions.md](decisions.md) for the full rationale.
- **Review UI**: original-file preview (PDF iframe or image) next to a dynamically-rendered field
  list grouped into the resolved profile's sections (`FieldEditor.tsx`, keyed by `FieldKey` not a
  hard-coded property name), per-field confidence/warning display, an OCR source/debug toggle
  (extraction method, source text, page number), save-edits flow that recomputes warnings
  server-side and marks the document `Reviewed`.
- **Excel export**: `ClosedXmlExportService` (ClosedXML), two sheets (`Documents`,
  `Warnings`) with Vietnamese headers, typed date/money cells, frozen header row, autofilter,
  auto-sized columns, alias-aware column lookup (same profile-catalog alias groups as the review
  response) so a document saved only under an alias field key still populates the right column.
- **OCR provider audit log**: `OcrProviderLog` per processing run (provider, model, page count,
  duration, estimated cost, success/error) plus optional persisted artifacts (raw provider
  response JSON, full normalized OCR result JSON) gated by `Ocr:StoreRawProviderResponse` /
  `StoreNormalizedOcrResult`.
- **Security baseline**: rate limiting on OCR-triggering and export endpoints (10 req/min/IP),
  CORS locked to explicit origins in production (permissive only for the Vite dev server),
  generic error responses via `GlobalExceptionMiddleware` with details only in logs, no secrets in
  committed config (Azure/Paddle credentials via user-secrets or env vars only).
- **Dev tooling**: `docker-compose.yml` (Postgres + API + frontend), a standalone
  `DocumentOCR.OcrBenchmark` console tool that runs Fake + every configured Azure model + Paddle
  over a sample folder and writes per-file debug JSON plus a comparison `summary.csv`.
- **Test suite**: 327 xUnit test methods across 29 files in `DocumentOCR.UnitTests`
  (normalization, extraction, validation, document profile catalog + review mapping, OCR
  providers/registry/options, export shape, storage, domain entities, benchmark tool helpers) plus
  end-to-end tests in `DocumentOCR.IntegrationTests` that drive upload → process (Fake) → review →
  export through the real WebApi host.

## Partially completed features

- **PaddleOCR integration** — the .NET-side HTTP client, response mapping, and options validation
  are complete and tested, but the actual PaddleOCR HTTP service is not part of this repo and has
  no reference implementation checked in; it must be stood up separately before `Ocr:Provider=Paddle`
  is usable. `PaddleOcrProvider` also never populates `Tables`/`KeyValuePairs`/`Fields` (line/text
  detection only), so extraction quality via Paddle is inherently lower than via Azure.
- **Document review UI** — supports single-document review/edit and save, but there's no bulk-edit,
  no "accept all", and no visual mapping from a field back to its location on the document preview
  (bounding boxes are captured in the data model but not rendered in the UI).
- **`ExtractedField.IsRequired`** — the DB column still exists but is never set by any code path;
  required-ness is now computed at request time from the resolved `DocumentProfile`
  (`FieldValidationService`/`DocumentReviewMappingService`), not read from this column, so it
  remains effectively dead data at the persistence layer.
- **Document status lifecycle** — `DocumentStatus.Exported` exists on the enum and is used in the
  Excel `ReviewedStatus` column text ("Đã duyệt"/"Chưa duyệt"), but no code path ever transitions a
  document's `Status` to `Exported` after a successful export.

## Missing features

- **Line-item (per-product) extraction** — deliberately out of scope for now; see
  [decisions.md](decisions.md).
- **Authentication / authorization / multi-tenancy** — `DocumentsController`/`ExportsController`
  hardcode `DefaultOrganizationId`; there is no login, no user model beyond `Organization`, no
  per-user data isolation.
- **OCR Debug Viewer UI** — raw provider responses and normalized OCR results are persisted (DB +
  file artifacts, path recorded on `OcrProviderLog`) and the benchmark tool writes comparable debug
  JSON, but there is no API endpoint or frontend screen to browse/download them; today that data is
  only reachable by querying Postgres or reading files directly on disk.
- **CI pipeline** — no `.github/workflows` or other CI config in the repo; build/test only runs
  locally via `dotnet build`/`dotnet test`.
- **Document deletion / archival** — no delete endpoint; uploaded files and DB rows are permanent.
- **Push/real-time status updates** — the frontend polls `GET /api/documents` every 5 seconds; no
  SignalR/websocket push for processing completion.
- **Non-Vietnamese locales** — extraction keywords, date formats, and currency defaults are
  Vietnamese-specific by design; no i18n layer.

## OCR status

Three providers are implemented behind `IDocumentOcrProvider`, selected by `Ocr:Provider` config
(`Fake` default / `Azure` / `Paddle`) — see `OcrProviderRegistry`. Azure is the primary intended
production path, using `prebuilt-layout` with the `keyValuePairs` add-on by default (configurable
model ID and feature list). Extraction logic reads from whichever of
`Fields`/`KeyValuePairs`/`Tables`/`Pages.Lines`/`FullText` the active provider actually populates,
so the same downstream pipeline works across all three without provider-specific branches outside
`Infrastructure/Ocr`.

## Azure status

Fully wired: `AzureDocumentIntelligenceProvider` (Azure.AI.DocumentIntelligence SDK), configurable
endpoint/API key (via user-secrets or `AzureDocumentIntelligence__*` env vars — never committed),
configurable model ID and add-on features, SDK-level retry for transient failures (429/503/network),
fail-fast startup validation when selected but unconfigured. Not currently exercised against a real
Azure resource as part of this session's review — the manual end-to-end test procedure is
documented in [LOCAL_DEVELOPMENT.md](../LOCAL_DEVELOPMENT.md) but requires live credentials to run.

## Benchmark status

`apps/api/tools/DocumentOCR.OcrBenchmark` (dev-only console app, not part of the shipped API) can
run Fake, every Azure model in `AzureDocumentIntelligence:BenchmarkModelIds` (default:
`prebuilt-read`, `prebuilt-layout`, `prebuilt-invoice`, `prebuilt-receipt`), and Paddle over a
local sample folder in one pass, writing per-target debug JSON and a comparison `summary.csv`. No
sample data or benchmark output is committed (`.gitignore`'d by design — see
[decisions.md](decisions.md)), so there is currently no checked-in benchmark run to point to; a
real comparison run has not yet been executed and recorded as part of this review.

## Known issues

- `DocumentStatus.Exported` is unreachable in practice (see Partially completed).
- `ExtractedField.IsRequired` is dead data (see Partially completed).
- No automated CI — regressions can reach `main` without `dotnet build`/`dotnet test` having run.
- No document/file deletion path — storage grows unbounded over time in the current design.
- `PaddleOcrProvider` has no reference service to test end-to-end in this repo; it's currently
  effectively unverified beyond mapping/unit-test level.
- Frontend has no automated tests (no `apps/web` test runner configured) — coverage relies entirely
  on backend tests and manual verification.

## Next priorities

Suggested order, based on what's built vs. what's declared out of scope:

1. Decide whether `DocumentStatus.Exported` should actually be set post-export, or remove it from
   the enum if it's intentionally unused — currently ambiguous.
2. Run and record a real Azure benchmark pass (`prebuilt-layout` vs `prebuilt-invoice`/`receipt`)
   against a small representative sample set, per the benchmark-before-adding-providers decision.
3. Stand up a minimal PaddleOCR reference service (or drop the provider) so `Ocr:Provider=Paddle`
   is actually testable end-to-end, not just unit-tested at the mapping layer.
4. Add a CI workflow running `dotnet build` + `dotnet test` (and `npm run lint`/`build` for the
   frontend) on push/PR.
5. Decide on an OCR Debug Viewer surface (even a simple authenticated API endpoint to fetch a
   document's raw/normalized artifacts) now that the data is already being persisted.

## Manual test checklist

Since there is no CI and the frontend has no automated tests, verify the golden path manually
before considering a change release-ready:

- [ ] `docker-compose up --build` starts Postgres, API (`:5000`), and web (`:3000`) cleanly.
- [ ] Upload a PDF, a JPG, and a PNG (all under 20 MB) — all three reach `Processed` via
      `FakeOcrProvider` (default config) without touching Azure.
- [ ] Upload a file with a mismatched extension/content-type (e.g. rename a `.txt` to `.pdf`) —
      rejected with a generic client error, not a 500.
- [ ] Upload a file over 20 MB — rejected with the size-limit message.
- [ ] Upload two files in one request where one is invalid — the valid one still succeeds.
- [ ] Watch the Hangfire dashboard at `/hangfire` (local only) and confirm the job runs.
- [ ] Open a processed document in Review — original preview renders (PDF and image), dynamic
      sections render per the resolved `DocumentCategory` (e.g. a POS receipt shows Merchant/
      Receipt/Amounts/Notes with no tax-code field, a VAT invoice shows Seller/Buyer/Invoice/
      Amounts/Notes with a required tax code), fields show confidence and any warnings, editing
      (including filling in a field the profile shows as missing) and saving flips status to
      `Reviewed`.
- [ ] Trigger a manual re-process (`POST /api/documents/{id}/process`) on a `Failed` document.
- [ ] Select one or more `Processed`/`Reviewed` documents and export — download an `.xlsx` with a
      `Documents` sheet and a `Warnings` sheet, Vietnamese headers, correctly typed date/money cells.
- [ ] Switch `Ocr:Provider` to `Azure` with real credentials (user-secrets) and repeat the upload →
      process → review → export flow against one real Vietnamese invoice (see
      [LOCAL_DEVELOPMENT.md](../LOCAL_DEVELOPMENT.md) step-by-step).
- [ ] Confirm no secrets appear in `appsettings*.json` committed to the repo, and no full invoice
      text is written to application logs (only counts/metadata).
