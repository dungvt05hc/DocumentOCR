# DocumentOCR — Decisions

Lightweight architecture/product decision log. Each entry captures a decision already reflected
in the codebase as of this review, why it was made, what it affects, and what would make it worth
revisiting. New entries go at the bottom.

---

## 2026-07-24 — Modular monolith, not microservices

**Decision:**
Ship DocumentOCR as a single Clean Architecture solution (`Domain` / `Application` /
`Infrastructure` / `WebApi`) deployed as one API process, not as separate services.

**Reason:**
MVP with one team, one deployable, one database. Microservices would add network hops,
distributed-transaction complexity, and operational overhead (multiple deploy pipelines, service
discovery) with no corresponding benefit at current scale.

**Impact:**
All processing — validation, extraction, normalization, export — runs in-process. Clear layer
boundaries (Domain has no EF/Azure/IO; Azure SDK types never leave `Infrastructure`) preserve the
option to split later without a full rewrite.

**Revisit when:**
A specific subsystem (e.g. OCR processing) needs independent scaling, deployment cadence, or a
different runtime/language than the rest of the API.

---

## 2026-07-24 — PostgreSQL for app data and Hangfire storage

**Decision:**
Use a single PostgreSQL instance both as the application's EF Core database and as Hangfire's job
storage (`UsePostgreSqlStorage`), rather than a separate queue/broker.

**Reason:**
One database to operate for an MVP-scale system is simpler than running Postgres plus Redis/RabbitMQ
just for job queuing. Hangfire's Postgres storage is mature enough for this workload (OCR jobs are
not high-frequency).

**Impact:**
`docker-compose.yml` only needs one stateful service besides the app/frontend containers. Job
throughput and durability are bounded by Postgres, which is acceptable at MVP volume.

**Revisit when:**
Job volume or latency requirements exceed what Postgres-backed Hangfire can comfortably handle, or
Hangfire itself needs to be replaced.

---

## 2026-07-24 — No line-item (per-product) extraction yet

**Decision:**
Extract only document-level fields (supplier, tax code, invoice number/date, subtotal/VAT/total,
currency, document type, notes) — not individual line items/products from invoice tables.

**Reason:**
MVP success criteria (per [product-context.md](product-context.md)) is 5–7 header-level fields
extracted and reviewable; line items are a materially harder extraction problem (variable table
shapes, multi-row items, unit price/quantity reconciliation) that isn't required to hit that bar.

**Impact:**
`OcrTable` data is already captured from providers (used today only for the table-footer totals
heuristic in `FieldExtractionService`), but there is no `LineItem` domain entity, no per-row
extraction, and Excel export has no line-item sheet.

**Revisit when:**
Users need per-product data (e.g. for accounting-system line-item import) rather than just
document totals — likely a distinct, larger extraction effort with its own table-parsing strategy.

---

## 2026-07-24 — Azure prebuilt-layout + keyValuePairs as the primary OCR direction

**Decision:**
Default `AzureDocumentIntelligence:DefaultModelId` to `prebuilt-layout` with the `keyValuePairs`
add-on feature enabled, and make `FieldExtractionService`'s primary extraction path the
`KeyValuePairs`-driven candidate matcher (`AddKeyValuePairCandidates`).

**Reason:**
`prebuilt-layout` works across arbitrary Vietnamese invoice/receipt layouts without requiring a
document to match Azure's built-in invoice/receipt schema, and `keyValuePairs` gives label/value
structure that's more reliable than pure line-proximity heuristics while remaining
document-shape-agnostic.

**Impact:**
The keyword vocabulary in `FieldExtractionService` (Vietnamese label synonyms) is shared between
the KeyValuePair path and the line-heuristic fallback, so it only needs to be maintained once.
`AddStructuredFieldCandidates` still exists to consume `prebuilt-invoice`/`prebuilt-receipt`
output when that model is selected.

**Revisit when:**
Benchmark data (see the benchmark-before-adding-providers decision below) shows `prebuilt-layout`
missing fields that a specific prebuilt model captures more reliably for a meaningful share of
real documents.

---

## 2026-07-24 — prebuilt-invoice / prebuilt-receipt reserved for benchmark/fallback use

**Decision:**
Keep `AzureDocumentIntelligenceProvider` able to run `prebuilt-invoice`/`prebuilt-receipt` (both
are in `AzureDocumentIntelligence:BenchmarkModelIds` and `FieldExtractionService` still maps their
structured `Fields`), but do not make either the default production model.

**Reason:**
Prebuilt invoice/receipt models are trained on schemas that don't guarantee a fit for Vietnamese
invoice conventions; `prebuilt-layout` + `keyValuePairs` was judged the safer general-purpose
default (see the decision above). Invoice/receipt models remain useful as a benchmark comparison
point and as a manual fallback if layout-based extraction underperforms on a specific document
type.

**Impact:**
`DocumentOCR.OcrBenchmark` runs all four default model IDs per file specifically so this tradeoff
can be measured with real data rather than assumed.

**Revisit when:**
Benchmark results show one of these models consistently outperforming `prebuilt-layout` on a
document category (e.g. POS receipts) — could justify per-document-type model selection.

---

## 2026-07-24 — Provider-neutral OCR core

**Decision:**
`Application` and everything downstream of OCR (extraction, normalization, validation, export)
depends only on `IDocumentOcrProvider` and the provider-neutral `NormalizedOcrDocument` model —
never on a specific provider's SDK types or response shape.

**Reason:**
Enables swapping/adding OCR providers (Fake, Azure, Paddle, and any future one) via a single
`OcrProviderRegistry` branch and DI registration, with zero changes to extraction/normalization/
validation/export code. Documented as a hard rule in
[.claude/rules/ocr-pipeline.md](../.claude/rules/ocr-pipeline.md).

**Impact:**
Adding PaddleOCR required only a new `Infrastructure/Ocr/PaddleOcrProvider.cs` + options class;
`FieldExtractionService` needed no changes since it already worked against `NormalizedOcrDocument`.

**Revisit when:**
A future provider needs a capability the neutral model can't represent (would require extending
`NormalizedOcrDocument`, not bypassing it).

---

## 2026-07-24 — Azure SDK stays in Infrastructure

**Decision:**
`Azure.AI.DocumentIntelligence` and all other Azure SDK types are referenced only inside
`DocumentOCR.Infrastructure`; `Domain`, `Application`, and `WebApi` never reference them directly.

**Reason:**
Hard architectural rule (see [CLAUDE.md](../CLAUDE.md) and
[.claude/rules/ocr-pipeline.md](../.claude/rules/ocr-pipeline.md)) — keeps the provider-neutral
core enforceable at the project-reference level, not just by convention.

**Impact:**
`DocumentOCR.Application.csproj` and `DocumentOCR.Domain.csproj` have no Azure package references;
only `DocumentOCR.Infrastructure.csproj` does. A build-time project-reference violation is the
enforcement mechanism, not a runtime check.

**Revisit when:**
Not expected to change — this is a foundational boundary, not a temporary state.

---

## 2026-07-24 — Keep FakeOcrProvider for local dev and tests

**Decision:**
`FakeOcrProvider` (deterministic Vietnamese VAT invoice fixture, zero network calls) stays as the
default `Ocr:Provider` and is never removed, even after Azure/Paddle integration matured.

**Reason:**
Local development and the entire automated test suite (unit + integration) must run without cloud
credentials or incurring Azure cost, and must be deterministic (same input → same OCR output) for
tests to be reliable. Documented as a hard rule in
[.claude/rules/testing.md](../.claude/rules/testing.md).

**Impact:**
`DocumentLifecycleTests` (integration) and most extraction/normalization unit tests run entirely
against `FakeOcrProvider` output. `CLAUDE.md` explicitly warns never to merge a DI swap that makes
a non-Fake provider the test-time default.

**Revisit when:**
Not expected to change.

---

## 2026-07-24 — Human review is mandatory

**Decision:**
Every document must pass through the `Processed → Reviewed` step in the UI before being treated as
finalized data; there is no "auto-accept extracted fields" path.

**Reason:**
Product principle from [product-context.md](product-context.md): the MVP explicitly targets
70–80% automation with the user correcting the remainder, not full automation. Warnings are
surfaced precisely so a human looks at low-confidence/inconsistent fields before they're trusted.

**Impact:**
`FieldEditor` is the only path that transitions a document to `Reviewed`
(`DocumentService.UpdateFieldsAsync`); export is available for `Processed` or `Reviewed` documents,
but nothing in the system claims a document is "correct" without a human having opened the review
screen and saved.

**Revisit when:**
If/when confidence in unattended extraction is high enough (backed by benchmark data) to offer an
optional "auto-approve high-confidence documents" mode — would be an explicit opt-in, not a
default change.

---

## 2026-07-24 — No hard-coded VietnameseInvoice entity yet

**Decision:**
Model extracted data as a generic `ExtractedField` (string `FieldName` + raw/normalized value +
confidence + source metadata) keyed by the `FieldName` enum, rather than a strongly-typed
`VietnameseInvoice` entity with dedicated columns per field.

**Reason:**
The generic shape lets one code path (extraction/normalization/validation/export) handle every
`DocumentType` (VAT invoice, POS receipt, restaurant bill, generic invoice/receipt) without a
schema migration every time a document category needs a different field set, and keeps
`RawValue`/`NormalizedValue`/confidence/audit metadata uniform across all fields.

**Impact:**
Adding a new field to the MVP set means adding an enum value plus extraction/normalization/
validation logic — not a new column and migration. The tradeoff is weaker type safety (field
values are strings until normalized) and no DB-level constraint tying a field to its expected type.

**Revisit when:**
The field set stabilizes and query/reporting needs (e.g. "sum TotalAmount across documents in
SQL") make a typed, columnar shape worth the migration cost.

---

## 2026-07-24 — Tax code required for VAT invoice, optional for POS receipt

**Decision:**
`FieldValidationService` requires `SupplierTaxCode` for `DocumentType.VatInvoice` and the generic
`Invoice`/`ExpenseDocument`/`Unknown` fallback, but treats it as optional (no `REQUIRED_FIELD_MISSING`
warning) for `Receipt`, `PosReceipt`, and `RestaurantBill`.

**Reason:**
Matches real Vietnamese document conventions: a formal VAT invoice ("hóa đơn giá trị gia tăng")
always carries a tax code (MST), while POS/sales receipts and restaurant bills commonly omit it.
Flagging every receipt as "missing required field" would create warning noise the user has to
dismiss on documents where it's expected to be absent.

**Impact:**
`TaxCodeOptionalCategories` in `FieldValidationService` is the single source of truth for this
rule; `FieldExtractionService.AddDocumentTypeCandidate` must classify `DocumentType` correctly
(via title/keyword heuristics) for the right rule to apply, so document-type detection accuracy
directly affects tax-code warning accuracy.

**Revisit when:**
Document-type detection proves unreliable enough that this conditional is flagging/missing
incorrectly in practice — would need better classification signal, not a rule change.

---

## 2026-07-24 — Benchmark before adding PaddleOCR / AWS / Google as alternatives

**Decision:**
Before treating any non-Azure OCR provider as a real alternative (not just a proof-of-concept),
run it through `DocumentOCR.OcrBenchmark` against the same sample set used for Azure, and compare
via `summary.csv` rather than deciding on assumptions.

**Reason:**
OCR provider quality on Vietnamese documents varies enough (diacritics, layout conventions, table
structure) that it isn't safe to assume a provider is "good enough" without measuring it against
representative samples — cost and integration effort for a provider swap are only justified by
data.

**Impact:**
`PaddleOcrProvider` was built specifically to be benchmarkable this way (free/open-source
baseline) even though there's no production plan to replace Azure with it yet. The benchmark tool
runs Fake + all configured Azure models + Paddle in the same pass precisely so comparisons are
apples-to-apples.

**Revisit when:**
A real benchmark run against a representative Vietnamese document sample has been executed and
recorded — see "Benchmark status" and "Next priorities" in [status.md](status.md), since this
hasn't happened yet as of this review.

---

## 2026-07-24 — Do not commit secrets or sensitive raw data

**Decision:**
Azure/Paddle credentials are never placed in committed `appsettings*.json`; OCR benchmark sample
invoices and their raw output are never committed; uploaded documents live outside the repo in
configured storage paths.

**Reason:**
Standard secret-hygiene practice, made explicit because this project handles real invoice content
(potentially containing PII/business-sensitive data) and would otherwise be tempting to commit
"just one sample invoice" for convenience during development.

**Impact:**
`.gitignore` excludes `appsettings.Local.json`, `.env`/`.env.local`,
`apps/api/tools/DocumentOCR.OcrBenchmark/data/` and `.../benchmark-output/`, and upload storage
paths. `LOCAL_DEVELOPMENT.md` documents `dotnet user-secrets`/env-var configuration exclusively —
no committed file ever contains a real endpoint or API key. Logging deliberately avoids writing
full invoice content (see [.claude/rules/security.md](../.claude/rules/security.md)).

**Revisit when:**
Not expected to change — this is a standing rule, not a temporary state.

---

## 2026-07-24 — Excel export is the primary MVP output

**Decision:**
The only export format is a `.xlsx` workbook (`ClosedXmlExportService`, two sheets: `Documents`
and `Warnings`, Vietnamese headers) — no CSV, PDF report, JSON export, or accounting-system
integration (e.g. direct API push) in the MVP.

**Reason:**
Matches the target user (SME accountants, shop owners) who already work in Excel day-to-day per
[product-context.md](product-context.md); Excel is both the success criterion and the simplest
format to review/adjust by hand after export, with no integration work required on the user's side.

**Impact:**
`IExcelExportService` is the only export interface; `ExportsController` exposes a single
`POST /api/exports/excel` endpoint. Money/date cells are typed (not just formatted strings) so the
output is immediately usable in further Excel calculations.

**Revisit when:**
A specific accounting-system integration (e.g. direct import format) or a non-Excel export need is
requested by real users — not anticipated speculatively.

---

## 2026-07-24 — OCR Debug Viewer is a developer tool

**Decision:**
Raw OCR provider responses and normalized OCR results are persisted (DB fields on
`OcrProviderLog`, optional JSON artifacts on disk via `IDocumentStorageService`) strictly as
debugging/audit data for developers — not as an end-user-facing feature, and not surfaced through
any API endpoint or frontend screen today.

**Reason:**
Field-mapping issues (why did extraction pick the wrong candidate for a field?) are much faster to
diagnose with the actual raw provider JSON in hand than by reasoning about the pipeline
abstractly, but end users reviewing their own invoices have no need to see raw OCR internals — the
`FieldEditor` review screen is the user-facing surface, not this data.

**Impact:**
`Ocr:StoreRawProviderResponse`/`StoreNormalizedOcrResult` config flags control whether this data is
written at all (can be disabled once a provider integration is trusted, to reduce storage).
**Update 2026-07-27:** a minimal viewer now exists — `GET /api/documents/{id}/ocr-debug` (gated by
the new `OcrDebug:Enabled` config flag, off by default) plus a "Show OCR source/debug info" toggle
in the review UI — but it remains a development/debug surface, not an end-user feature: raw
provider JSON content is only included when `OcrDebug:ExposeRawJson` is also set, and the endpoint
is disabled entirely (404) unless explicitly turned on.

**Revisit when:**
Not expected to change further — the "developer tool, not end-user feature" framing still holds
even with the endpoint built.

---

## 2026-07-24 — Review UI uses dynamic document profiles instead of hard-coded invoice fields

**Decision:**
Replace the fixed 10-field review model (`FieldName` enum, one hard-coded field grid in
`FieldEditor.tsx`) with a code-defined "document profile" system: a `DocumentCategory` enum (8
values — VatInvoice, SalesReceipt, PosReceipt, RestaurantBill, AppReceiptScreenshot,
InternationalInvoice, CommercialInvoice, Unknown) resolves to a `DocumentProfile`
(`Infrastructure/Profiles/DocumentProfileCatalog`) describing sections, field labels, data types,
required-ness, and severities. A new `GET /api/documents/{id}/review` endpoint
(`DocumentReviewMappingService`) maps whatever `ExtractedField` rows exist onto that profile,
producing a `DocumentReviewResponse` the frontend renders generically — no per-field-name code in
React.

**Reason:**
Different document types need different fields (a POS receipt has no buyer/tax-code section; an
app-screenshot receipt has an order code and QR value instead of an invoice number). The product
must support VAT invoices, POS receipts, app screenshots, restaurant bills, and international
invoices without a frontend rewrite every time a new document shape is added.

**Impact:**
- `Document.DocumentType` (the legacy 7-value enum) is untouched — it still drives
  `DocumentProcessingService`'s persisted status field. `DocumentCategory` is resolved fresh each
  request from the same "DocumentType" pseudo-field signal (parsed against the richer enum first,
  falling back through a `DocumentType → DocumentCategory` table), so introducing new categories
  never required an EF migration.
- Profile fields declare `AliasFieldNames` (e.g. `SellerName`/`MerchantName`/`VendorName`/`StoreName`
  all alias the legacy `SupplierName`) so the 10 fields the extractor actually produces today
  satisfy the richer, renamed profile vocabulary without touching `FieldExtractionService`'s
  extraction logic itself. Profile-only fields with no extractor yet (`BuyerName`, `PONumber`,
  `DueDate`, …) always render `IsMissing = true` — expected, not a bug.
- `FieldValidationService.ValidateRequiredFields` is now profile-driven (iterates the resolved
  profile's required fields) instead of the old hard-coded `BaseRequiredFields`/
  `TaxCodeOptionalCategories`; every other validation rule (money/date parsing, tax-code format,
  amount consistency, low-confidence) is untouched.
- `DocumentService.UpdateFieldsAsync` no longer rejects field keys outside the legacy `FieldName`
  enum — a deliberate, spec-required loosening so a user can fill in a profile-only field the review
  UI shows as missing.
- `ClosedXmlExportService` resolves each export column via the same alias groups (single source of
  truth in the profile catalog) so a document saved only under an alias key (e.g. `MerchantName`)
  still populates the "Tên nhà cung cấp" column.
- `GET /api/documents/{id}` (`DocumentDetailDto`) and `PUT /api/documents/{id}/fields` are
  untouched — purely additive, so any other consumer of the flat DTO keeps working.
- `ExtractedField.IsRequired` remains unset/unused at the DB-column level (as noted above under
  "Partially completed features" in [status.md](status.md)) — required-ness is now computed from
  the profile at request time, not read from that column.

**Revisit when:**
`AppReceiptScreenshot`/`InternationalInvoice`/`CommercialInvoice` category auto-detection (added as
two new keyword-based branches in `FieldExtractionService.AddDocumentTypeCandidate`) proves too
coarse for real samples — would need richer heuristics, not a profile-system change. Also revisit if
profiles need to move to DB/config-driven management (explicitly out of scope for this iteration —
profiles are static in-code data).

---

## 2026-07-27 — Review response includes detected OCR tables separately from extracted header fields

**Decision:**
Extend `DocumentReviewResponse` with `Tables: List<ReviewTable>` and a derived
`LineItems: List<ReviewLineItem>`, distinct from the existing `Sections`/`Fields` header-field
model, rather than trying to fold table data into the flat field list.

**Reason:**
Invoices and receipts often carry a detail table (line items, unit prices, totals breakdown) that
a flat key-value field model has no way to represent. Header fields alone (supplier, tax code,
invoice number/date, subtotal/VAT/total) are not enough for a user to fully review a document or
for Excel export to reflect what's actually on the page — the table structure itself needs to
survive review and land in the export, even before any per-row product extraction is trusted.

**Impact:**
- `NormalizedOcrDocument.Tables` (already correctly populated by
  `AzureDocumentIntelligenceProvider.BuildTableResult` from Azure prebuilt-layout) previously died
  at the end of `DocumentProcessingService.ProcessAsync` — used transiently only by
  `FieldExtractionService.AddTableFooterCandidates` to mine a totals row, then discarded. It is now
  persisted as `Document.TablesJson`, a single nullable `jsonb` column holding the serialized
  `List<OcrTable>`, written once during processing and re-read at review/export/debug time.
- Deliberately a JSON column, not a relational `DocumentTable`/`DocumentTableCell` schema — the
  brief calls for MVP-friendly scope ("do not implement complex line-item extraction yet"), and
  `OcrTable`/`OcrTableCell` are already plain Application-layer POCOs, so this is a direct
  serialize/deserialize round-trip with no new mapping code. A full relational shape would add two
  tables, FK plumbing, and EF configuration for a feature whose row-level data (line items) is
  explicitly a "candidate, not guaranteed" concept for now.
- `IReviewTableBuilder`/`ReviewTableBuilder` (Infrastructure.Processing, same "interface in
  Application, OCR-shape logic in Infrastructure" pattern as `FieldExtractionService`) is the single
  place that reshapes raw table cells into a header+rows view with canonical column keys
  (Description/Quantity/UnitPrice/Amount, recognized from both English and Vietnamese headers) and
  derives line-item candidates — reused identically by `DocumentReviewMappingService` and
  `ClosedXmlExportService` so the normalization vocabulary exists exactly once.
- `ReviewTable`/`ReviewTableCell.Confidence` stays `null` for now — the Azure `DocumentTable`
  mapping carries no per-cell confidence today, a pre-existing gap not fixed as part of this change.
  `ReviewLineItem.Confidence` is instead a synthetic heuristic flagging rows with unparsable numeric
  cells or fuzzy column matches as "experimental" in the UI.
- Table **cell** edits are persisted (patched into `TablesJson` via the extended
  `PUT /api/documents/{id}/fields` request); line-item edits are accepted by the same endpoint for
  API-contract completeness but not persisted, since line items have no backing store and are
  always re-derived from `TablesJson`.

**Revisit when:**
Real per-product line-item extraction (reconciling quantity × unit price ≈ amount, handling
multi-row items, structured accounting-system export) becomes a product requirement — that's a
materially larger effort than this candidate builder and was explicitly deferred (see "No line-item
(per-product) extraction yet" above). Also revisit the JSON-column choice if table data needs to be
queried/filtered at the database level rather than always loaded whole per document.
