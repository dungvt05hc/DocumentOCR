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

---

## 2026-08-03 — TT78 XML e-invoices are parsed directly, bypassing OCR entirely

**Decision:**
When an uploaded file is a Vietnamese TT78 (Thông tư 78/2021/TT-BTC) e-invoice XML, skip
`IDocumentOcrProvider` and `FieldExtractionService` entirely and parse the XML directly into
`ExtractedField`s via a new `IStructuredInvoiceParser` (`TT78XmlInvoiceParser`,
`DocumentOCR.Application.Processing`). The branch lives in `DocumentProcessingService.ProcessAsync`
(`_structuredInvoiceParser.CanParse(...)` checked before building the OCR `DocumentInput`), **not**
as an `IDocumentOcrProvider` implementation.

**Reason:**
A TT78 e-invoice is already a structured, schema-defined XML document — running OCR and heuristic
field-guessing (`FieldExtractionService`) on it would be strictly worse than reading the tags
directly: slower, costs money against the configured OCR provider, and less accurate than a direct
read. `IDocumentOcrProvider` returns a `NormalizedOcrDocument` that downstream extraction has to
*guess* fields from; that guessing step is exactly what XML doesn't need, so putting the seam inside
`IDocumentOcrProvider` would force the pipeline through a lossy layer it doesn't require. The seam
belongs one level up, where `DocumentProcessingService` already decides how to turn a stored file
into `ExtractedField`s.

**Impact:**
- `IStructuredInvoiceParser`/`StructuredInvoiceResult` live in `DocumentOCR.Application`
  (`Interfaces`/`Models`) alongside `IDocumentOcrProvider`; `TT78XmlInvoiceParser` lives in
  `Application/Processing`, next to `FieldExtractionService`/`FieldNormalizationService`, using only
  `System.Xml.Linq` — no new NuGet dependency.
- `DocumentProcessingService.ProcessAsync` now branches into `ProcessViaOcrAsync` (unchanged OCR
  body, extracted verbatim into its own method) or `ProcessStructuredInvoiceAsync`. The XML path
  still runs `FieldNormalizationService.NormalizeFields` and `FieldValidationService.Validate` — the
  parser only supplies raw field values, all money/date/tax-code normalization and warning
  generation stays shared with the OCR path.
- An `OcrProviderLog` row is still written for every XML document (`ProviderName = "TT78Xml"`,
  `EstimatedCost = 0`, `PageCount = 1`) so cost/audit reporting doesn't need an XML-specific
  branch. Unlike the OCR path, no `DocumentPage` rows are created (XML has no page concept).
- `DocumentsController` accepts `text/xml`/`application/xml` uploads (`.xml` extension, 5 MB limit —
  tighter than the 20 MB PDF/JPG/PNG limit). Because XML has no fixed magic bytes, file-signature
  validation was refactored from a single `Dictionary<contentType, byte[]>` indexer (which would
  throw `KeyNotFoundException` for a content type with no magic-byte entry) into an explicit
  per-content-type dispatch: magic bytes for PDF/JPG/PNG, a bounded (64 KB peek, never the whole
  file) well-formedness + root-element check for XML. Both the upload-time peek and the parser's
  full read use `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }` — XXE
  protection against a file type sourced entirely from user upload.
- TT78 XML is always treated as `DocumentType.VatInvoice` — `TT78XmlInvoiceParser` synthesizes a
  `FieldName.DocumentType` field (not read from any XML tag) so
  `DocumentProcessingService.GetDetectedDocumentType` (unchanged) resolves the VAT-invoice
  `DocumentProfile` instead of falling back to `Unknown`.
- **Assumed XML structure** (no official XSD is checked into this repo): `HDon > DLHDon >
  (TTChung | NDHDon > (NBan | NMua | TToan))`, with `SHDon`/`KHMSHDon`/`KHHDon`/`NLap`/`DVTTe` as
  direct children of `TTChung`, `MST`/`Ten` as direct children of `NDHDon/NBan` (not `NMua`, the
  buyer), and `TgTCThue`/`TgTThue`/`TgTTTBSo` as direct children of `NDHDon/TToan`. A file wrapped in
  a digital-signature envelope may carry `DLHDon` alongside a sibling `Signature` (XML-DSig) block;
  the parser locates `DLHDon` by local name anywhere in the tree but explicitly excludes any match
  nested under a `Signature` ancestor. Element lookups match by `XName.LocalName` only (namespace/
  prefix-agnostic) for the same reason. If a real TT78 sample differs from this assumed shape, only
  `TT78XmlInvoiceParser` needs to change — the rest of the pipeline (normalization, validation,
  review, export) is schema-agnostic.
- `KHMSHDon`+`KHHDon` (invoice template code + serial) have no dedicated `FieldName` enum value; they
  are combined into a human-readable string on the existing `FieldName.Notes` field, and also
  surfaced raw on `StructuredInvoiceResult.InvoiceTemplateCode`/`InvoiceSerial` for any future
  caller that wants them unformatted.
- Frontend (`UploadZone.tsx`, `FieldEditor` file-preview panel) was not updated to accept or render
  `.xml` — out of scope for this change; the backend API accepts XML uploads correctly, but the
  existing UI's client-side file-type filter still blocks them and the preview panel has no XML
  renderer.

**Revisit when:**
A real TT78 XML sample (or the official Tổng cục Thuế XSD) becomes available and its structure
differs from the assumed shape above — update `TT78XmlInvoiceParser` only. Also revisit if a second
structured-format source (e.g. a different country's e-invoice standard) is needed:
`IStructuredInvoiceParser` currently has exactly one registered implementation
(`TT78XmlInvoiceParser`, singleton in `Infrastructure/DependencyInjection.cs`); supporting multiple
would need `DocumentProcessingService` to resolve from `IEnumerable<IStructuredInvoiceParser>`
instead of a single injected instance.

---

## 2026-08-03 — ClientProfile added; auto-suggest only matches on seller (SupplierTaxCode)

**Decision:**
Added a `ClientProfile` entity (`OrganizationId`, `Name`, `TaxCode`, `ClientType`, `Address`,
`IsActive`) so an accounting-service user can group/filter documents by which of their own clients
(household business/enterprise/individual) a document belongs to. `Document.ClientProfileId` is a
nullable FK — existing documents and newly uploaded ones with no client chosen remain valid with
`ClientProfileId = null`. A new `IClientAutoSuggestService` runs after `DocumentProcessingService`
finishes (called from `DocumentProcessingJob.ProcessDocumentAsync`, not from inside the pipeline
itself) and assigns a `ClientProfile` automatically when the document's extracted
`SupplierTaxCode` (digits-only normalized, via the existing `IFieldNormalizationService`) matches
an active client's `TaxCode`.

**Reason:**
A kế toán dịch vụ (accounting-service user) manages books for 30–80 separate household
businesses/small enterprises; without a client concept, documents can't be filtered or exported
per end-client. Auto-suggest saves the manual assignment step for the common case where a
document's seller tax code is itself one of the user's clients — i.e. an "output invoice"
(hóa đơn đầu ra) the client issued.

**Impact:**
- The extractor (`FieldExtractionService`/`FieldName` enum) was deliberately **not** touched — no
  "buyer tax code" (MST người mua) field exists today, only `SupplierTaxCode` (seller). Auto-suggest
  therefore only covers documents where the client is the *seller*; a purchase/expense document
  where the client is the *buyer* (hóa đơn đầu vào) is never auto-assigned and needs manual
  assignment via `PUT /api/documents/{id}/client`. Adding buyer-side matching would require
  extracting a new field from OCR output — an OCR-pipeline change explicitly out of scope for this
  iteration.
- `IClientAutoSuggestService`/`ClientAutoSuggestService` live in `Application/Services` (no
  infrastructure dependency — only `IApplicationDbContext` and `IFieldNormalizationService`), and
  are invoked as a distinct step after `IDocumentProcessingService.ProcessAsync` completes, inside
  `DocumentProcessingJob` — not inside `DocumentProcessingService` itself — so the OCR pipeline
  (`.claude/rules/ocr-pipeline.md`) remains untouched by this feature.
- Auto-suggest never overwrites an existing `ClientProfileId` (manual assignment or a prior
  auto-suggest always wins) and skips inactive (`IsActive = false`) clients.
- `ClientProfile.TaxCode` is stored digits-only (normalized on write in `ClientProfileService`, the
  same normalization `FieldNormalizationService.NormalizeTaxCode` applies to `SupplierTaxCode`) and
  has a partial unique index on `(OrganizationId, TaxCode)` (`HasFilter("\"TaxCode\" IS NOT NULL")`)
  so multiple clients with no tax code on file don't collide, but two clients in the same org can
  never share one real tax code.
- `GET /api/documents` gained `clientProfileId`/`from`/`to` query filters — the first server-side
  filters on this endpoint (previously all filtering, e.g. by status, was client-side in `App.tsx`).
  `from`/`to` filter by the document's normalized `InvoiceDate` extracted field (an ISO
  `yyyy-MM-dd` string), falling back to `Document.CreatedAt` for documents with no InvoiceDate yet;
  this is evaluated in-memory per-organization after an EF query (acceptable at MVP per-tenant
  document volume) rather than as a translated SQL predicate, since `InvoiceDate` lives on
  `ExtractedField`, not as a column on `Document`.

**Revisit when:**
A buyer-side tax code field is added to the extraction pipeline (a separate, larger effort per the
note above) — `ClientAutoSuggestService` would then need a second matching strategy for purchase/
expense documents where the client is the buyer, not the seller. Also revisit the in-memory
date-range filtering if per-organization document volume grows enough that it stops being cheap.

---

## 2026-08-05 — Software-generated PDFs read their text layer directly, as an IDocumentOcrProvider behind a router

**Decision:**
Vietnamese e-invoice PDFs (MISA/Viettel/VNPT/BKAV, ...) already carry an exact, provider-generated
text layer — running OCR on them is both slower and less accurate than reading the layer directly.
Added `PdfTextLayerProvider : IDocumentOcrProvider` (`Infrastructure/Ocr`, backed by PdfPig) that
reads words/lines per page straight from the PDF and maps them into the same `NormalizedOcrDocument`
shape every other provider produces — zero changes to `FieldExtractionService`. A new
`PdfProviderRouter : IDocumentOcrProvider` is registered as `OcrProviderRegistry`'s actual DI output
(gated by `Ocr:PdfTextLayer:Enabled`, default true): for `application/pdf` uploads it tries the text
layer first and falls back to the configured OCR provider only when the PDF turns out to be a scan
(< `Ocr:PdfTextLayer:MinExtractedCharacters`, default 100, extracted across all pages) or the read
fails; non-PDF uploads (JPG/PNG) go straight to the configured provider, unchanged.

**Reason:**
Unlike the TT78 XML fast path (see the 2026-08-03 entry above), a software-generated PDF has no
schema to parse directly — it still needs the same field-guessing extraction every OCR result goes
through. That ruled out an `IStructuredInvoiceParser`-style bypass and pointed at implementing this
as another `IDocumentOcrProvider`: `FieldExtractionService` already has a working line-proximity/
regex-fallback path (used whenever `Fields`/`KeyValuePairs` are empty, e.g. Azure `prebuilt-layout`
without the `keyValuePairs` add-on), so a provider that only populates `Pages`/`Lines`/`Words`/
`FullText` needs no extraction-layer changes at all.

**Impact:**
- `OcrProviderRegistry.Register` now registers the configured provider (Fake/Azure/Paddle) under its
  own concrete type first, then builds the final `IDocumentOcrProvider` via a factory that wraps it
  in `PdfProviderRouter` unless `PdfTextLayer:Enabled` is false. `DocumentProcessingService` still
  resolves a single `IDocumentOcrProvider` and never branches on content type itself.
- `DocumentProcessingService.ProcessViaOcrAsync`'s `OcrProviderLog.ProviderName` now reads from
  `ocrResult.ProviderName` (the value the branch that actually ran set) instead of
  `_ocrProvider.ProviderName` (the DI-resolved instance's own static name) — required so the audit
  trail says "PdfTextLayer" or the real fallback provider's name instead of always
  "PdfProviderRouter". Verified as a no-op for every prior provider, since each one already sets
  both to the same value.
- `PdfProviderRouter` depends on `IDocumentOcrProvider` for *both* the text-layer and OCR-fallback
  providers (not the concrete `PdfTextLayerProvider` type), purely for testability — production DI
  still passes the real `PdfTextLayerProvider`, which implements the same interface like everything
  else.
- `OcrProviderRegistryTests.Register_SelectsExpectedProviderImplementation` — which asserted the
  resolved `IDocumentOcrProvider` was the exact concrete provider type — was split into two theories
  (`PdfTextLayer:Enabled=false` → concrete type as before; default/enabled → `PdfProviderRouter`),
  since that assertion's premise changed on purpose.
- `DocumentOCR.OcrBenchmark` runs `PdfTextLayerProvider` as an extra per-file target, prepended only
  for `application/pdf` samples (JPG/PNG samples don't get it), alongside the existing Azure models.

**Revisit when:**
A real MISA e-invoice sample (`apps/api/tests/Fixtures/misa-einvoice-sample.pdf`, not committed —
real invoice content) is available to run `PdfTextLayerProviderTests`'
`AnalyzeAsync_MisaEInvoiceSample_ExtractsExpectedFieldsWhenFixturePresent` for real; until then it's
a no-op guarded by `File.Exists`. Also revisit the fixed 0.95 word confidence and the 100-character
scan threshold if real samples show either needs tuning.

---

## 2026-08-13 — Third extraction path: PDF text-layer + LLM, unified behind an `IDocumentExtractionStrategy`

**Decision:**
Added a third way to turn an upload into `ExtractedField`s — reading a software-generated PDF's
text layer (reusing `PdfTextLayerProvider`, see the 2026-08-05 entry) and handing that text to an
LLM (Google Gemini, native `responseSchema` structured output, `temperature = 0`) for field
recognition, instead of the heuristic `FieldExtractionService` the OCR path uses. Doing this
required first refactoring the two existing paths (structured XML, OCR) into a common
`IDocumentExtractionStrategy` (`Name`/`Priority`/`CanHandleAsync`/`ExtractAsync`) so
`DocumentProcessingService.ProcessAsync` has one selection/persistence loop instead of two
hand-written branches. Concrete strategies, in `Priority` order: `XmlInvoiceStrategy` (0, thin
adapter over the existing `IStructuredInvoiceParser`), `PdfTextLayerLlmStrategy` (50, new),
`OcrStrategy` (100, catch-all — wraps the existing `IDocumentOcrProvider` chain and
`FieldExtractionService` verbatim).

**Reason:**
A software-generated e-invoice PDF has no schema to parse directly (unlike TT78 XML), but its text
is exact — running an LLM over that exact text should out-perform `FieldExtractionService`'s
line-proximity/regex heuristics without the cost/latency of real OCR. Doing this as a fourth
`IDocumentOcrProvider` (like `PdfTextLayerProvider` itself) was rejected: an OCR provider only
returns `NormalizedOcrDocument` for the *existing* heuristic extractor to guess from, and there was
no clean way to swap in LLM-based field mapping at that layer without `FieldExtractionService`
somehow knowing to trust an LLM's opinion over its own heuristics. Promoting the seam one level up
— to "how does a whole document become fields" — needed a real strategy abstraction, which is why
the XML and OCR paths were unified into the same interface rather than adding a third special case
to `DocumentProcessingService`.

**Impact:**
- `StructuredInvoiceResult` was renamed/extended into `Application.Models.StructuredExtractionResult`
  — one shape covering XML's original fields (`RawSourceText`, `InvoiceTemplateCode`/`InvoiceSerial`)
  plus what `OcrStrategy` needs to persist (`Pages`, `Tables`, `ProviderName`/`ModelId`/`PageCount`/
  `ProcessingTimeMs`/`EstimatedCost`/`RawResponsePath`/`NormalizedResultPath`, mapping 1:1 onto
  `OcrProviderLog`) and what benchmarking needs (`RejectedFieldCount`). `IStructuredInvoiceParser`
  now returns this type directly — no separate mapping layer.
- `ProcessAsync` tries strategies in `Priority` order; a strategy returning `Success = false` from
  `ExtractAsync` (caught exceptions included) is treated the same as `CanHandleAsync` returning
  false — the loop just tries the next candidate. This is what makes "LLM times out/errors → falls
  through to `OcrStrategy`" work with no special-casing: `OcrStrategy.CanHandleAsync` is `true` for
  anything that isn't an XML upload (reusing `CreditPricing.IsXmlUpload`), so it's always the last
  resort and the document can never end up stuck in `Processing`. Every attempted strategy (not
  just the winner) gets its own `OcrProviderLog` row, so "LLM tried and failed, then Azure OCR
  succeeded" is visible in the audit trail as two rows, not one.
- `PdfTextLayerLlmStrategy` never trusts the LLM's output directly: every non-null field value must
  have a `sourceText` that verifies (whitespace-normalized substring match) against the PDF's own
  extracted text, or the field is dropped and only its name is logged — never the value. Confidence
  is always computed in code from verification + format checks (money/date parse, tax-code digit
  count) + a cross-field amount-consistency check (`|subtotal + vat − total| ≤ 1 VND`), never read
  from the LLM. **MST format, not checksum**: no authoritative published algorithm for the
  Vietnamese 10-digit tax-code check digit could be found (web search came up empty; a half-remembered
  weight table was deliberately not implemented rather than risk silently wrong pass/fail results) —
  confidence 0.9 requires 10-or-13-digit format only, matching `FieldValidationService`'s existing
  rule exactly. Revisit if an authoritative source turns up.
- Responses are cached by (SHA-256 of the whitespace-normalized extracted text, model) in a new
  `LlmExtractionCache` table/`ILlmExtractionCache` — a cache hit still re-runs the full
  verification/confidence pipeline on the current text, only the network call is skipped, so caching
  can never bypass anti-hallucination checks.
- Reuses the credit pre-charge/refund mechanism the 2026-08-05 `PdfTextLayer` entry established
  with zero new logic: a new `CreditOptions.PdfTextLayerLlm` (default 2, same as `OcrExtraction`)
  and `CreditPricing.PdfTextLayerLlmProviderName` slot into the existing
  `ResolveActualCost`/`RefundFreePathDifferenceAsync` switch.
- `Llm:Enabled` defaults `false` (checked in `PdfTextLayerLlmStrategy.CanHandleAsync`), so no
  behavior changes for any existing deployment until explicitly turned on — PDFs keep going through
  `PdfProviderRouter`/`OcrStrategy` exactly as before. `Llm:ApiKey` only via user-secrets/env, never
  committed, same as Azure/Paddle.
- `DocumentOCR.OcrBenchmark` gained a `--pairs <file>` pass (`pairs.csv`: `XmlFile,PdfFile`
  columns): for each pair, the XML is parsed with `TT78XmlInvoiceParser` (full confidence, direct
  schema read) and used as ground truth — instead of a hand-typed CSV — to score
  `PdfTextLayerLlmStrategy` and every configured Azure/Fake/Paddle target against a verified-correct
  baseline, writing `pairs-summary.csv`. `BenchmarkCsvRow` gained `EstimatedCost` and
  `RejectedFieldCount` columns (the latter populated only by the LLM strategy — 0 elsewhere).

**Revisit when:**
A real Vietnamese e-invoice PDF+XML pair sample is available to run the `--pairs` benchmark for
real and tune `Llm:Model`/`PricePerMillionInput|OutputTokensUsd` from actual Gemini pricing (both
default to 0 today — `EstimatedCost` reports 0 until set). Also revisit the MST format-only
confidence rule if an authoritative checksum algorithm becomes available.
