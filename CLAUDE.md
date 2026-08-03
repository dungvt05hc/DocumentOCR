# CLAUDE.md

Vietnamese invoice/receipt OCR SaaS. Upload PDF/JPG/PNG → extract fields → user reviews/corrects → export Excel.
Full brief: [docs/product-context.md](docs/product-context.md)

## Commands

Backend (.NET 10, SDK-style — use `dotnet`, not `msbuild`):
```bash
dotnet build DocumentOCR.slnx
dotnet run --project apps/api/DocumentOCR.WebApi
dotnet test DocumentOCR.slnx
dotnet test apps/api/tests/DocumentOCR.UnitTests --filter "FullyQualifiedName~MoneyNormalizationTests"
```

EF migrations — run from `apps/api/DocumentOCR.WebApi`:
```bash
dotnet ef migrations add <Name> --project ../DocumentOCR.Infrastructure --startup-project .
dotnet ef database update --project ../DocumentOCR.Infrastructure --startup-project .
```

Frontend (`apps/web/`, React 19 + Vite): `npm run dev` | `build` | `lint` (oxlint)

Full stack: `docker-compose up --build` (Postgres, API :5000, web :3000)

## Architecture

Clean Architecture monolith under `apps/api/`:

- **Domain** — entities + enums only. No EF Core, Azure SDK, IO.
- **Application** — use cases, DTOs, interfaces (`IDocumentOcrProvider`, `IDocumentStorageService`, …), and the pure OCR-pipeline business logic that has no infrastructure dependency: `Processing/FieldExtractionService`, `Processing/FieldNormalizationService`, `Processing/FieldValidationService`, `Processing/ReviewTableBuilder`, `Profiles/DocumentProfileCatalog`. Orchestration in `DocumentService` / `ExportService`.
- **Infrastructure** — concrete impls with a real infrastructure dependency: EF Core/Npgsql, OCR providers, `DocumentProcessingService` (uses `IApplicationDbContext` + `IDocumentStorageService` to drive the pipeline), ClosedXML export, Hangfire job. DI wiring in `DependencyInjection.cs`.
- **WebApi** — thin controllers, `GlobalExceptionMiddleware`, rate limiting, CORS, Hangfire dashboard.

Hard rules:
- Azure SDK types never leave Infrastructure.
- Business logic in Application services, not controllers.
- OCR never runs in the HTTP request — always via `DocumentProcessingJob` (Hangfire).

Pipeline: upload → validate (ext + MIME + magic bytes) → store → Document record → enqueue job → OCR → map to `OcrResult` → extract → normalize → validate → warnings → user review → Excel export

Status: `Uploaded → Processing → Processed | Failed → Reviewed → Exported`

Single-tenant: `DocumentsController` hardcodes `DefaultOrganizationId`. Intentional, not a bug.

## Local dev without Azure

`FakeOcrProvider` returns a deterministic Vietnamese invoice. Swap DI registration in `DependencyInjection.cs` — **never merge that swap to main**. Credentials/model IDs: [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)

## Normalization rules

- **SupplierTaxCode** — digits only, 10 or 13 digits, fix O → 0 in numeric context
- **InvoiceDate** — ISO output; accept `dd/MM/yyyy`, `d/M/yyyy`, `dd-MM-yyyy`, `yyyy-MM-dd`; reject far-future
- **Money** — `1.234.567` / `1,234,567` / `1 234 567` / `… VND` / `₫…`
- **TotalAmount** — required, positive. Subtotal + VAT ≈ Total (allow rounding)
- `ExtractedField` keeps `RawValue` and `NormalizedValue` separate always; preserve confidence
- Warn on: low confidence, missing required field, invalid tax code, invalid date, total mismatch

## Security

Applies to upload, storage, OCR integration, logging, endpoints:

- Secrets via env vars or `dotnet user-secrets` — never hard-coded
- Validate extension + MIME + magic bytes (client Content-Type is spoofable)
- PDF/JPG/PNG only, 20 MB limit
- Safe generated file names, controlled paths, no path traversal, no public file URLs
- Generic errors to users, detail to logs only
- Don't log full invoice content

## Working style

Before large refactors: inspect existing code, explain current state, propose a plan, wait for confirmation.
Prefer readable/testable over clever. Don't add unrequested features.

## Testing

`FakeOcrProvider` or fixed sample text only — never live Azure in automated tests.
Priority coverage: money/date/tax-code normalization, OCR digit correction, VAT total match, warning generation, `FakeOcrProvider` determinism, Excel export shape.

Before making significant changes, read:
- docs/status.md
- docs/decisions.md