# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Product Context

This project is an MVP SaaS product for the Vietnamese market.

The product helps Vietnamese accountants, SMEs, shop owners, and admin staff upload invoices, receipts, and PDF scan files, automatically extract important fields, review/correct the extracted data, and export clean data to Excel or API.

This is NOT a generic OCR app.

Primary value proposition:

"Upload Vietnamese invoice / receipt / PDF scan → extract important fields → review quickly → export Excel."

Business principle: AI reads 70-80% of fields, the user reviews/corrects the remaining 20-30%. The app does not need to be 100% automatic — it saves time and reduces repetitive data entry. See [docs/product-context.md](docs/product-context.md) for the full product brief.

## MVP Scope

The MVP supports:

- PDF, JPG, PNG
- Vietnamese invoices, receipts, expense documents, PDF scans, phone-captured document images

The MVP extracts these fields (see `FieldName` enum):

- SupplierName, SupplierTaxCode, InvoiceNumber, InvoiceDate
- SubtotalAmount, VatAmount, TotalAmount, Currency, DocumentType, Notes

Do not implement these yet unless explicitly requested:

- Payment, mobile app, complex dashboard, custom OCR model training, table line-item extraction,
  multi-step approval workflow, enterprise multi-tenancy, accounting software integrations,
  chatbot over documents, microservices, Kubernetes.

## Commands

### Backend (.NET 10 / SDK-style projects — use `dotnet`, not `msbuild`)

```bash
# Build everything
dotnet build DocumentOCR.slnx

# Run the API (from repo root or src/DocumentOCR.WebApi)
dotnet run --project src/DocumentOCR.WebApi

# Run all tests
dotnet test DocumentOCR.slnx

# Run a single test project
dotnet test tests/DocumentOCR.UnitTests

# Run a single test by fully-qualified name or filter
dotnet test tests/DocumentOCR.UnitTests --filter "FullyQualifiedName~MoneyNormalizationTests"

# EF Core migrations (run from src/DocumentOCR.WebApi so appsettings/connection string resolve)
dotnet ef migrations add <Name> --project ../DocumentOCR.Infrastructure --startup-project .
dotnet ef database update --project ../DocumentOCR.Infrastructure --startup-project .
```

Requires a local Postgres instance (see `docker-compose.yml`, or run just the `postgres` service). Hangfire also stores its job data in the same Postgres database.

### Frontend (`frontend/`, React 19 + TypeScript + Vite)

```bash
cd frontend
npm install
npm run dev       # Vite dev server on :5173
npm run build      # tsc -b && vite build
npm run lint       # oxlint
npm run preview
```

### Full stack via Docker Compose

```bash
docker-compose up --build
```

Runs Postgres, the API (port 5000), and the frontend (port 3000).

### Running without Azure credentials

The app must always run locally without cloud dependency via `FakeOcrProvider` (returns a deterministic Vietnamese invoice result). To switch providers for local dev, swap the DI registration in [DependencyInjection.cs](src/DocumentOCR.Infrastructure/DependencyInjection.cs) between `AzureDocumentIntelligenceProvider` and `FakeOcrProvider` — do not merge that swap to main. See [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md) for full credential setup (user-secrets vs. env vars) and supported Azure model IDs (`prebuilt-invoice`, `prebuilt-receipt`, `prebuilt-read`, `prebuilt-layout`).

## Architecture

Clean Architecture / modular monolith, four projects under `src/`:

1. **Domain** (`DocumentOCR.Domain`) — Entities (`Document`, `DocumentPage`, `ExtractedField`, `ValidationWarning`, `ExportJob`, `Organization`, `AppUser`, `UsageLog`, `OcrProviderLog`), enums (`DocumentStatus`, `DocumentType`, `FieldName`, `OcrProviderType`, `WarningSeverity`, `ExportJobStatus`), `BaseEntity`. No dependency on EF Core, Azure SDK, file system, or UI.

2. **Application** (`DocumentOCR.Application`) — Use cases, DTOs, and the provider-neutral interfaces that Infrastructure implements: `IDocumentOcrProvider`, `IDocumentStorageService`, `IDocumentProcessingService`, `IFieldExtractionService`, `IFieldNormalizationService`, `IFieldValidationService`, `IExcelExportService`, `IUsageTrackingService`, `IApplicationDbContext`. `DocumentService` and `ExportService` are the main orchestration services consumed by controllers.

3. **Infrastructure** (`DocumentOCR.Infrastructure`) — All concrete implementations: EF Core (`ApplicationDbContext`, Npgsql, migrations), `LocalDocumentStorageService`, OCR providers (`AzureDocumentIntelligenceProvider`, `FakeOcrProvider`), the processing pipeline (`FieldExtractionService`, `FieldNormalizationService`, `FieldValidationService`, `DocumentProcessingService`, `UsageTrackingService`), `ClosedXmlExportService`, and the Hangfire job (`DocumentProcessingJob`). All DI wiring lives in `DependencyInjection.cs`. **Azure SDK types must not leak outside this layer.**

4. **WebApi** (`DocumentOCR.WebApi`) — Thin controllers (`DocumentsController`, `ExportsController`), `GlobalExceptionMiddleware`, rate limiting, CORS, Hangfire dashboard (local-only auth filter). Business logic belongs in Application services, not controllers.

The OCR pipeline must remain provider-neutral: Application depends on `IDocumentOcrProvider`, never directly on the Azure SDK.

### Core workflow

```
Upload file → validate (extension + MIME + magic bytes) → store original file → create Document record
→ enqueue Hangfire job (DocumentProcessingJob) → OCR provider analyzes file → map provider response to internal OcrResult
→ extract fields → normalize fields → validate fields → create warnings
→ user reviews and edits fields → export selected documents to Excel (ClosedXML)
```

OCR processing must never run directly inside the HTTP request — always via the Hangfire background job.

Document statuses: `Uploaded → Processing → Processed | Failed → Reviewed → Exported`.

### Frontend structure (`frontend/src/`)

- `components/` — `UploadZone`, `DocumentTable`, `FieldEditor`, `ExportPanel`
- `services/api.ts` — axios client for the backend API
- `types/index.ts` — shared TS types mirroring backend DTOs

The API is currently single-tenant for the MVP: `DocumentsController` uses a hardcoded `DefaultOrganizationId` rather than deriving org from auth claims — this is intentional for MVP scope, not a bug.

## Data Model Rules

Each `ExtractedField` stores: `FieldName`, `RawValue`, `NormalizedValue`, `Confidence`, `PageNumber`, `BoundingBoxJson`, `IsRequired`, `IsEditedByUser`, `EditedAt`. Keep `RawValue` and `NormalizedValue` separate always; keep confidence score when available.

Track OCR usage (`UsageLog`): `ProviderName`, `PageCount`, `ProcessingDurationMs`, `EstimatedCost`, `CreatedAt`.

## Validation & Normalization Rules

**SupplierTaxCode**: normalize to digits only; usually 10 or 13 digits in Vietnam; fix OCR mistakes such as O → 0 in numeric context.

**InvoiceDate**: normalize to ISO date; support `dd/MM/yyyy`, `d/M/yyyy`, `dd-MM-yyyy`, `yyyy-MM-dd`; should not be far in the future.

**Money**: normalize Vietnamese formats — `1.234.567`, `1,234,567`, `1 234 567`, `1.234.567 VND`, `₫1.234.567`.

**TotalAmount**: required, must be positive. If Subtotal + VatAmount both exist, they should approximately equal TotalAmount (allow small rounding differences).

**Warnings**: create warnings for low confidence, missing required fields, invalid tax code, invalid date, or total mismatch.

## Security Rules

Apply when touching file upload, storage, OCR provider integration, logging, or API endpoints:

- Never hard-code API keys/secrets — use env vars or `dotnet user-secrets`.
- Do not log full invoice content unless explicitly needed for local debugging.
- Do not expose uploaded files through public URLs.
- Validate file extension, MIME type, *and* magic bytes (client-supplied Content-Type/extension are spoofable — see `DocumentsController.ValidateFileSignatureAsync`).
- Only allow PDF/JPG/PNG for MVP; enforce the 20 MB file size limit.
- Store files under controlled storage paths; prevent path traversal (`PathTraversalException`); do not trust user-supplied file names; generate safe internal stored file names.
- Return generic error messages to users; keep detailed errors in logs only (`GlobalExceptionMiddleware`).

## Development Rules

Before writing code: inspect existing code, explain current state, propose a small plan, and wait for confirmation on large refactors.

When coding: keep implementation simple for MVP, prefer readable/testable code, avoid overengineering, do not add features outside MVP scope unless requested.

## Testing Rules

Prioritize tests for: money normalization, Vietnamese date normalization, tax code normalization, OCR mistake correction in numeric fields, VAT total matching, missing required field warnings, low confidence warnings, `FakeOcrProvider` deterministic output, Excel export columns/sheets.

Automated tests must use `FakeOcrProvider` or fixed OCR sample text — never rely on Azure OCR in automated tests. `DocumentOCR.UnitTests` covers normalization/validation/extraction/export logic in isolation (EFCore InMemory); `DocumentOCR.IntegrationTests` exercises the WebApi host end-to-end.

## Manual Test Flow

1. Upload a sample PDF/JPG/PNG.
2. Process it using FakeOcrProvider.
3. See extracted fields.
4. See validation warnings.
5. Edit fields manually.
6. Save review.
7. Export Excel.
