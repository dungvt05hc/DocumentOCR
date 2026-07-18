# Local Development Setup

## Configuring Azure Document Intelligence

Credentials **must never be committed** to source control. Use environment variables or `dotnet user-secrets` to supply them locally.

---

### Option 1 — dotnet user-secrets (recommended for development)

User secrets are stored outside the repository in your OS user profile.

```bash
cd apps/api/DocumentOCR.WebApi

dotnet user-secrets init
dotnet user-secrets set "AzureDocumentIntelligence:Endpoint" "https://<your-resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "AzureDocumentIntelligence:ApiKey"   "<your-api-key>"
```

Verify:
```bash
dotnet user-secrets list
```

---

### Option 2 — Environment variables (CI/CD, containers, local shell)

The .NET configuration system maps double-underscores `__` to JSON path separators.

**PowerShell (Windows):**
```powershell
$env:AzureDocumentIntelligence__Endpoint = "https://<your-resource>.cognitiveservices.azure.com/"
$env:AzureDocumentIntelligence__ApiKey   = "<your-api-key>"
```

**Bash / zsh (macOS / Linux):**
```bash
export AzureDocumentIntelligence__Endpoint="https://<your-resource>.cognitiveservices.azure.com/"
export AzureDocumentIntelligence__ApiKey="<your-api-key>"
```

**Docker Compose** (`docker-compose.yml`):
```yaml
services:
  api:
    environment:
      - AzureDocumentIntelligence__Endpoint=https://<your-resource>.cognitiveservices.azure.com/
      - AzureDocumentIntelligence__ApiKey=<your-api-key>
```

---

### Supported model IDs

Set `AzureDocumentIntelligence:DefaultModelId` (or the equivalent env var) to one of:

| Value | Use case |
|---|---|
| `prebuilt-layout` | Tables, checkboxes, structured forms — **default**; best general-purpose fit for Vietnamese invoices/receipts |
| `prebuilt-invoice` | Invoice-specific prebuilt model |
| `prebuilt-receipt` | Sales receipts |
| `prebuilt-read` | Plain text / unstructured documents |

### Add-on features: `AzureDocumentIntelligence:Features`

```json
"AzureDocumentIntelligence": {
  "Features": ["keyValuePairs"]
}
```

Optional Azure Document Intelligence add-on features requested on every analyze call.
`keyValuePairs` (the default) improves extraction of label/value pairs on Vietnamese
invoices/receipts when the selected model supports it. Other supported values: `barcodes`,
`formulas`, `languages`, `ocrHighResolution`, `queryFields`. Unrecognized values are logged and
skipped rather than failing the request.

---

### Choosing a provider: `Ocr:Provider`

Which `IDocumentOcrProvider` gets registered is controlled by configuration, not code:

```json
"Ocr": {
  "Provider": "Fake",
  "StoreRawProviderResponse": true
}
```

| Value | Behavior |
|---|---|
| `Fake` (default) | `FakeOcrProvider` — deterministic Vietnamese invoice result, no network calls, no credentials needed. |
| `Azure` | `AzureDocumentIntelligenceProvider` — requires `Endpoint`/`ApiKey` below. |

`StoreRawProviderResponse` (default `true`) controls whether `OcrResult.RawProviderResponseJson`
gets persisted into `OcrProviderLogs.RawResponseJson` — useful for debugging field-mapping
issues without re-calling Azure; set `false` to reduce row size once the integration is trusted.

Override locally without editing `appsettings.json`:

```bash
# dotnet user-secrets
dotnet user-secrets set "Ocr:Provider" "Azure"

# or environment variable
export Ocr__Provider="Azure"
```

**Startup validation**: if `Ocr:Provider` is `Azure` but `Endpoint`/`ApiKey` are missing, the app
now **fails fast at startup** with a clear `OptionsValidationException` message, rather than
starting normally and only failing on the first document upload. This is intentional — a
misconfigured deployment should never silently accept uploads it can't actually OCR. (When
`Ocr:Provider` is `Fake`, missing Azure credentials are fine and never validated.)

---

### Optional resilience tuning

These settings have sensible defaults and rarely need changing:

```json
"AzureDocumentIntelligence": {
  "NetworkTimeoutSeconds":   30,
  "OperationTimeoutSeconds": 120,
  "MaxRetries":              3,
  "RetryDelaySeconds":       1.0
}
```

The SDK retries only **transient** failures (HTTP 429, 503, network errors). Authentication errors (401/403) and bad requests (400) are never retried.

---

### Manual test: processing one real invoice through Azure

Use this to verify the Azure path end-to-end before relying on it.

1. Set credentials (Option 1 or 2 above) and set the provider to Azure:
   ```bash
   dotnet user-secrets set "Ocr:Provider" "Azure"
   ```
2. Start Postgres and the API (`dotnet run --project apps/api/DocumentOCR.WebApi`).
3. Open Swagger (`/swagger`) and `POST /api/documents` with one sample Vietnamese invoice (PDF or JPG/PNG, < 20 MB).
4. Confirm the response has `status: "Uploaded"` and note the returned `documentId`.
5. Open the Hangfire dashboard (`/hangfire`, local-only) and confirm `DocumentProcessingJob` ran and succeeded for that document.
6. Watch the API console/log output for:
   - `Calling OCR provider AzureDocumentIntelligence for document <id>`
   - `OCR provider AzureDocumentIntelligence (Model=prebuilt-layout) completed for document <id>. Success=True, Pages=1, ...`
7. `GET /api/documents/{documentId}` and confirm `status` is `Processed` and extracted fields (SupplierName, InvoiceNumber, TotalAmount, etc.) are populated with plausible values.
8. Inspect the `OcrProviderLogs` table for that document (`SELECT * FROM "OcrProviderLogs" WHERE "DocumentId" = '<id>'`) and confirm `ProviderName`, `ModelId`, `PageCount`, `ProcessingTimeMs`, `EstimatedCost` and `RawResponseJson` are populated — `RawResponseJson` is the raw Azure response, useful for debugging field-mapping issues without re-calling Azure.
9. Switch `Ocr:Provider` back to `Fake` when done, so subsequent local runs don't incur Azure costs.

---

## OCR Benchmark Tool (dev-only)

`apps/api/tools/DocumentOCR.OcrBenchmark` is a console app that runs `FakeOcrProvider` **and**
`AzureDocumentIntelligenceProvider` — for one or more Azure model IDs at once — over a folder of
sample PDF/JPG/PNG invoices in a single pass, and writes per-file/per-target debug JSON plus a
`summary.csv` for side-by-side comparison. It is dev-only tooling — not part of the shipped
API/WebApi project, and not wired into `DocumentOCR.WebApi` in any way.

### Setup

1. Create a local sample folder (never commit its contents — already covered by `.gitignore`):
   ```bash
   mkdir apps/api/tools/DocumentOCR.OcrBenchmark/data
   ```
   Drop a handful of representative Vietnamese invoices/receipts (PDF/JPG/PNG) there.
2. Configure Azure credentials exactly as in "Configuring Azure Document Intelligence" above
   (`dotnet user-secrets` from `apps/api/DocumentOCR.WebApi`, or `AzureDocumentIntelligence__*`
   env vars). The benchmark tool shares the **same** user-secrets store as the WebApi project
   (same `UserSecretsId`), so nothing extra to configure if Azure is already set up for the API.

### Run

```bash
dotnet run --project apps/api/tools/DocumentOCR.OcrBenchmark -- --input apps/api/tools/DocumentOCR.OcrBenchmark/data
```

`--input`/`--output` both default to `data`/`benchmark-output` under the tool's own project
folder (resolved from the tool's source location, not the current working directory), matching
the `.gitignore` entries — so running with no arguments at all is safe by default. Pass
`--output <folder>` to write elsewhere.

By default the tool runs every Azure model ID listed in `AzureDocumentIntelligence:BenchmarkModelIds`
(config default: `prebuilt-read`, `prebuilt-layout`, `prebuilt-invoice`, `prebuilt-receipt`) — or
just `DefaultModelId` alone if that list is empty. Override per-run with `--models`:

```bash
dotnet run --project apps/api/tools/DocumentOCR.OcrBenchmark -- --models prebuilt-invoice,prebuilt-layout
```

Results land under `apps/api/tools/DocumentOCR.OcrBenchmark/benchmark-output/<UTC-timestamp>/`,
one subfolder per input file, each containing a `Fake/` subfolder plus one
`AzureDocumentIntelligence-<modelId>/` subfolder per model run — each with `raw-response.json`,
`ocr-result.json`, `extracted-fields.json`, `validation-warnings.json` — plus a `summary.csv` at
the run root with one row per (file, target): `FileName, ProviderName, ModelId,
ProcessingDurationMs, PageCount, FullTextLength, AverageConfidence, SupplierTaxCode, InvoiceDate,
TotalAmount, WarningCount, ErrorMessage`.

⚠️ **Cost caution**: this runs Azure Document Intelligence against *every* file in `--input`,
once per model, not a single manual test call — a folder of 20 invoices against all 4 default
models is 80 billed Azure calls per run. Keep sample folders and `--models` lists small and
re-run sparingly — pass `--models <one-id>` to cut cost down to a single model per run.

Note: `FakeOcrProvider` ignores file content and always returns the same fixed result, so its
rows in `summary.csv` are identical for every input file — it's included so the two providers'
output can be diffed in the same shape, not as a meaningful per-file OCR-quality baseline.