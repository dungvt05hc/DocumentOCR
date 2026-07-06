# Development Guide

Practical guide for running DocumentOCR locally and extending it. For product/business context see [product-context.md](product-context.md); for architectural rules and conventions see [CLAUDE.md](../CLAUDE.md).

---

## 1. Running the Project (API + Web)

### Prerequisites

- .NET 10 SDK
- Node.js (for `apps/web`)
- Docker Desktop (for Postgres — Hangfire also stores its job data there)

### Step 1 — Start Postgres

```bash
docker compose up -d postgres
```

This starts a `postgres:16-alpine` container (`document_ocr_db`) publishing port `5432`, with the database `document_ocr` auto-created.

> **Port 5432 conflict:** if you run other projects with their own Postgres containers, whichever container starts first claims host port 5432 — the loser starts anyway but silently ends up with *no* host port published, which looks like "wrong password" or "database does not exist" errors that have nothing to do with your actual config. Check with `docker port document_ocr_db` — it should print `5432/tcp -> 0.0.0.0:5432`. If it prints nothing, another container already has the port; stop it and run `docker compose up -d --force-recreate postgres`.

### Step 2 — Apply database migrations

```bash
cd apps/api/DocumentOCR.WebApi
dotnet ef database update --project ../DocumentOCR.Infrastructure --startup-project .
```

This creates all tables plus the seeded default `Organization` row (id `00000000-0000-0000-0000-000000000001`) that every request is scoped to for this single-tenant MVP.

### Step 3 — Run the backend API

```bash
# from repo root
dotnet run --project apps/api/DocumentOCR.WebApi
```

Runs on `http://localhost:5282` (see [launchSettings.json](../apps/api/DocumentOCR.WebApi/Properties/launchSettings.json)), `ASPNETCORE_ENVIRONMENT=Development`, which loads `appsettings.Development.json`. Swagger UI is available at `http://localhost:5282/swagger` in Development, and the Hangfire dashboard at `http://localhost:5282/hangfire` (restricted to local/loopback requests only).

By default the API is wired to `AzureDocumentIntelligenceProvider`. **For local dev without Azure credentials**, swap the DI registration in [DependencyInjection.cs](../apps/api/DocumentOCR.Infrastructure/DependencyInjection.cs) to `FakeOcrProvider` (deterministic Vietnamese invoice result, no network calls) — see [LOCAL_DEVELOPMENT.md](../LOCAL_DEVELOPMENT.md) for the full credential setup if you *do* want real Azure OCR.

### Step 4 — Run the frontend

```bash
cd apps/web
npm install
cp .env.example .env.local   # first time only; sets VITE_API_BASE_URL
npm run dev
```

Runs on `http://localhost:5173` (Vite dev server), pointed at the API via `VITE_API_BASE_URL` (defaults to `http://localhost:5000/api` in `.env.example` — **change this to `http://localhost:5282/api` for local `dotnet run`**, since 5000 is only correct when running the full stack through Docker Compose).

### Alternative — full stack via Docker Compose

```bash
docker-compose up --build
```

Runs Postgres, the API (published on host port **5000**, container port 8080), and the frontend (nginx, host port **3000**) all together. Use this to sanity-check the production-like build, not for day-to-day iteration (no hot reload).

### Ports reference

| Service | Local (`dotnet run` / `npm run dev`) | Docker Compose |
|---|---|---|
| API | `http://localhost:5282` | `http://localhost:5000` |
| Swagger | `http://localhost:5282/swagger` | n/a (Development-only) |
| Hangfire dashboard | `http://localhost:5282/hangfire` | `http://localhost:5000/hangfire` |
| Frontend | `http://localhost:5173` | `http://localhost:3000` |
| Postgres | `localhost:5432` | `localhost:5432` |

> **Note:** `scripts/setup.sh` and `scripts/dev.sh` in this repo are leftover reference scripts copied from a different project (they still say "SafeFamily" and reference a nonexistent `apps/api/SafeFamily.Api` folder and port 5050) — they document the *intended* `apps/api` / `apps/web` layout convention this repo now follows, but don't run them as-is against DocumentOCR yet.

### Running tests

```bash
dotnet test DocumentOCR.slnx                                    # everything
dotnet test apps/api/tests/DocumentOCR.UnitTests                # unit only
dotnet test apps/api/tests/DocumentOCR.IntegrationTests          # integration only (boots real WebApi host, fakes Postgres/OCR/Hangfire)
cd apps/web && npm run lint && npx tsc -b                        # frontend lint + typecheck
```

---

## 2. Implementing a New OCR Feature

### The pipeline, end to end

```
Upload → validate (ext + MIME + magic bytes) → store file → create Document (Uploaded)
  → enqueue Hangfire job (DocumentProcessingJob)
    → IDocumentOcrProvider.AnalyzeAsync → OcrResult (raw provider output)
    → IFieldExtractionService.Extract → ExtractedField[] (RawValue only)
    → IFieldNormalizationService.NormalizeFields → fills NormalizedValue
    → IFieldValidationService.Validate → ValidationWarning[]
    → save fields + warnings, Document.Status = Processed | Failed
  → user reviews/edits fields (DocumentService.UpdateFieldsAsync) → re-validates → Status = Reviewed
  → export selected documents (IExcelExportService) → .xlsx
```

Every stage above is a **provider-neutral interface in `DocumentOCR.Application`**, implemented in `DocumentOCR.Infrastructure`. The one hard rule (see [CLAUDE.md](../CLAUDE.md)): Application and WebApi must never reference Azure SDK types directly — only `Infrastructure/Ocr/AzureDocumentIntelligenceProvider.cs` and `AzureOcrOptions.cs` may.

### Cookbook: Add a new extracted field (e.g. `PaymentMethod`)

Fields flow through four layers — you generally need to touch all of them:

1. **Domain** — add the value to the enum: [`FieldName.cs`](../apps/api/DocumentOCR.Domain/Enums/FieldName.cs).
   ```csharp
   public enum FieldName
   {
       // ...existing values
       PaymentMethod = 10
   }
   ```
2. **Extraction** — teach [`FieldExtractionService.cs`](../apps/api/DocumentOCR.Infrastructure/Processing/FieldExtractionService.cs) how to find it. The extractor layers three strategies (pick whichever fits):
   - **Structured provider fields** — map the OCR provider's own field key to yours in `FieldKeyMap` (e.g. Azure's prebuilt-invoice model may already expose something usable).
   - **Keyword + nearby-line heuristic** — add a `PaymentMethodKeywords` string array (Vietnamese + English terms, lower-case/no-diacritics — see `NormalizeForSearch`) and a small `AddPaymentMethodLineCandidate` method following the exact shape of `AddNotesLineCandidate` (label-on-same-line via `ValueAfterLabel`, falling back to the next non-empty line via `TryGetNearbyLine`). Call it from `AddLineCandidates`.
   - **Full-text regex fallback** — add a pattern and call `AddRegexFallback` from `AddFullTextFallbackCandidates`, for when no line-structure is available at all.
   Every candidate needs a `SourcePriority` (structured fields win at 100; keyword-line candidates use 30–70 depending on how confident the heuristic is; regex fallback is lowest at 10) — when multiple candidates target the same field, the extractor keeps the one with the highest confidence, then priority.
3. **Normalization** — if the raw value needs cleanup (dates/money/tax codes already have dedicated normalizers), add a case to the `switch` in `NormalizeFields` in [`FieldNormalizationService.cs`](../apps/api/DocumentOCR.Infrastructure/Processing/FieldNormalizationService.cs). Simple text fields can fall through to the default `field.RawValue?.Trim()`.
4. **Validation** (optional) — if the field should trigger a warning when missing/invalid, add a check in [`FieldValidationService.cs`](../apps/api/DocumentOCR.Infrastructure/Processing/FieldValidationService.cs) (either add it to `BaseRequiredFields`, or write a dedicated `ValidatePaymentMethod` method following the shape of `ValidateTaxCode`/`ValidateInvoiceDate`, called from `Validate`).
5. **Export** — add a column in [`ClosedXmlExportService.cs`](../apps/api/DocumentOCR.Infrastructure/Export/ClosedXmlExportService.cs)'s `DocumentColumns` array (Vietnamese header text) and read it in `BuildDocumentsSheet`.
6. **Frontend** — two places, both required, neither automatic:
   - Add the literal to the `FieldName` union type in [`types/index.ts`](../apps/web/src/types/index.ts).
   - Add it to **both** the hardcoded `fields: FieldName[]` array and the `labels: Record<FieldName, string>` map at the top of [`FieldEditor.tsx`](../apps/web/src/components/FieldEditor.tsx) — the editor does *not* render whatever comes back in `document.fields` dynamically; a field missing from these two lists is silently uneditable in the review UI even though the backend extracted and returned it.
7. **Tests** — this is business-critical logic per [CLAUDE.md](../CLAUDE.md)'s testing rules; add cases to `FieldExtractionServiceTests`, the relevant file under [`Normalization/`](../apps/api/tests/DocumentOCR.UnitTests/Normalization) (`MoneyNormalizationTests`, `DateNormalizationTests`, `TaxCodeNormalizationTests`, or a new one if it's a new kind of value), and `FieldValidationTests`, all in [`apps/api/tests/DocumentOCR.UnitTests`](../apps/api/tests/DocumentOCR.UnitTests) — plus extend `FakeOcrProvider` and `VietnameseMvpTestData` if the new field should appear in the deterministic local/test fixture.

### Cookbook: Add a new OCR provider

1. Implement `IDocumentOcrProvider` ([`IDocumentOcrProvider.cs`](../apps/api/DocumentOCR.Application/Interfaces/IDocumentOcrProvider.cs)) in `Infrastructure/Ocr/`, mapping the provider's native response into `OcrResult`/`OcrFieldCandidate`/`OcrPageResult` — **only vendor SDK types may appear in this one file**; everything else in the pipeline must stay provider-neutral.
2. Register it in [`DependencyInjection.cs`](../apps/api/DocumentOCR.Infrastructure/DependencyInjection.cs) in place of (or alongside, behind config) `AzureDocumentIntelligenceProvider`.
3. Never let it run in automated tests — `FakeOcrProvider` (or fixed OCR sample text) is the only provider tests are allowed to depend on.

### Cookbook: Add a new validation rule

Add a private method to `FieldValidationService.Validate`'s call chain (`ValidateRequiredFields`, `ValidateTaxCode`, `ValidateInvoiceDate`, `ValidateTotalAmount`, `ValidateAmountConsistency`, `ValidateLowConfidence` are the existing ones) — each pushes `ValidationWarning` objects with a `WarningCode` (upper-snake-case, e.g. `INVALID_TAX_CODE_LENGTH`) and a `ValidationSeverity` (`Info` / `Warning` / `High` / `Error`). Remember: `DocumentService.UpdateFieldsAsync` re-runs the *entire* validation set after every user edit and replaces all warnings — you don't need to handle "clearing" your new warning yourself, that's already centralized.

---

## 3. Using the API

### From the web app (`apps/web`)

All API calls go through one thin axios wrapper: [`services/api.ts`](../apps/web/src/services/api.ts). Don't call `axios` directly from components — add a new exported function here instead, following the existing pattern:

```ts
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000/api',
});

export const getDocuments = () => api.get<DocumentDto[]>('/documents');
```

`VITE_API_BASE_URL` is a **build-time** env var (Vite inlines it) — set it in `apps/web/.env.local` for local dev, or via the `VITE_API_BASE_URL` build arg for the Docker image (see `docker-compose.yml`'s `frontend.build.args`). Changing it requires a rebuild, not just a restart.

Components call the exported functions and consume typed responses — types in [`types/index.ts`](../apps/web/src/types/index.ts) mirror the backend DTOs (`DocumentDto`, `DocumentDetailDto`, `UploadFileResult`, etc.) by hand; if you change a DTO shape on the backend, update this file too (nothing enforces the two staying in sync automatically).

### Directly (Swagger, curl, Postman)

With the API running locally, open `http://localhost:5282/swagger` for interactive docs, or call it directly:

| Action | Request |
|---|---|
| Upload files | `POST /api/documents/upload` — `multipart/form-data`, field name `files` (repeat for multiple) |
| List documents | `GET /api/documents` |
| Get one document (fields + warnings + OCR log) | `GET /api/documents/{id}` |
| Re-trigger OCR processing | `POST /api/documents/{id}/process` |
| Save reviewed field corrections | `PUT /api/documents/{id}/fields` — body `{ "fields": [{ "fieldName": "TotalAmount", "normalizedValue": "1234567" }] }` |
| Download the original file | `GET /api/documents/{id}/download-original` |
| Export selected documents to Excel | `POST /api/exports/excel` — body `{ "documentIds": ["guid1", "guid2"] }`, returns an `.xlsx` binary |

Example with curl:

```bash
curl -F "files=@invoice.pdf;type=application/pdf" http://localhost:5282/api/documents/upload

curl http://localhost:5282/api/documents

curl -X PUT http://localhost:5282/api/documents/<id>/fields \
  -H "Content-Type: application/json" \
  -d '{"fields":[{"fieldName":"SupplierName","normalizedValue":"CONG TY TNHH ABC"}]}'

curl -X POST http://localhost:5282/api/exports/excel \
  -H "Content-Type: application/json" \
  -d '{"documentIds":["<id>"]}' \
  -o export.xlsx
```

Notes that apply either way you call it:

- Upload/process endpoints are **rate-limited** (10 requests/minute per IP) — expect `429` if you script bulk testing.
- Only `application/pdf`, `image/jpeg`, `image/png` are accepted, up to 20 MB each, and the byte content is checked against the declared content type (magic-byte signature) — a mismatched/renamed file is rejected even if the extension looks right.
- The app is single-tenant for this MVP: every request implicitly scopes to one seeded `Organization` — there's no auth header to pass yet.
- Also useful: [`DocumentOCR.WebApi.http`](../apps/api/DocumentOCR.WebApi/DocumentOCR.WebApi.http) — a REST Client-compatible scratch file if your editor supports `.http` files.
