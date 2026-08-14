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

Which `IDocumentOcrProvider` gets registered is controlled by configuration, not code — see
`OcrProviderRegistry` in `apps/api/DocumentOCR.Infrastructure/Ocr/`:

```json
"Ocr": {
  "Provider": "Fake",
  "StoreRawProviderResponse": true,
  "StoreNormalizedOcrResult": true
}
```

| Value | Behavior |
|---|---|
| `Fake` (default) | `FakeOcrProvider` — deterministic Vietnamese invoice result, no network calls, no credentials needed. |
| `Azure` | `AzureDocumentIntelligenceProvider` — requires `Endpoint`/`ApiKey` below. |
| `Paddle` | `PaddleOcrProvider` — free/open-source baseline, calls a separate PaddleOCR HTTP service; requires `PaddleOcr:BaseUrl` below. |

`StoreRawProviderResponse` (default `true`) controls whether the provider's raw response JSON
gets persisted (both inline into `OcrProviderLogs.RawResponseJson` and, when
`StoreNormalizedOcrResult` is also enabled, as a file via `IDocumentStorageService` — its path is
recorded in `OcrProviderLogs.RawResponsePath`/`NormalizedResultPath`). Useful for debugging
field-mapping issues without re-calling the provider; set both `false` to reduce storage once the
integration is trusted.

Override locally without editing `appsettings.json`:

```bash
# dotnet user-secrets
dotnet user-secrets set "Ocr:Provider" "Azure"

# or environment variable
export Ocr__Provider="Azure"
```

**PDF text-layer-first path**: whichever provider you pick above only actually runs for PDF uploads
when the PDF turns out to be a scan (or the read fails) — `PdfProviderRouter` (the provider
`OcrProviderRegistry` actually registers) tries `PdfTextLayerProvider` first for software-generated
PDFs, since those already carry an exact text layer that's pointless to re-OCR. Set
`Ocr:PdfTextLayer:Enabled` to `false` to force every PDF through the configured provider above (e.g.
to compare extraction quality between the two paths). JPG/PNG uploads are unaffected either way.

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

## Configuring PaddleOCR (optional, free/open-source baseline)

`PaddleOcrProvider` does **not** run PaddleOCR in-process — it calls a separate HTTP service that
you run yourself (Docker or a local Python process; see "Running the PaddleOCR service" below).
This repo does not include that service; `PaddleOcrProvider` is only the .NET-side HTTP client
and response mapper.

```json
"PaddleOcr": {
  "BaseUrl": "http://localhost:8866",
  "AnalyzeEndpointPath": "/ocr/analyze",
  "TimeoutSeconds": 60
}
```

Set via user-secrets or environment variable, same pattern as Azure:
```bash
dotnet user-secrets set "PaddleOcr:BaseUrl" "http://localhost:8866"
# or
export PaddleOcr__BaseUrl="http://localhost:8866"
```

**Startup validation**: same fail-fast behavior as Azure — if `Ocr:Provider` is `Paddle` but
`PaddleOcr:BaseUrl` is missing, the app refuses to start with a clear error instead of failing on
the first upload.

### Expected PaddleOCR service contract

`PaddleOcrProvider` posts the uploaded file as `multipart/form-data` (field name `file`) to
`POST {BaseUrl}{AnalyzeEndpointPath}`, and expects this JSON shape back:

```json
{
  "success": true,
  "errorMessage": null,
  "pageCount": 1,
  "fullText": "MOTA CAFE\nTổng: 85.000",
  "averageConfidence": 0.93,
  "pages": [
    {
      "pageNumber": 1,
      "width": 800.0,
      "height": 1200.0,
      "unit": "pixel",
      "lines": [
        {
          "text": "MOTA CAFE",
          "confidence": 0.95,
          "boundingBox": [[10, 20], [200, 20], [200, 50], [10, 50]],
          "words": [
            { "text": "MOTA", "confidence": 0.96, "boundingBox": [[10, 20], [80, 20], [80, 50], [10, 50]] },
            { "text": "CAFE", "confidence": 0.94, "boundingBox": [[90, 20], [200, 20], [200, 50], [90, 50]] }
          ]
        }
      ]
    }
  ]
}
```

Notes:
- `words` is optional per line — PaddleOCR's default detection+recognition pipeline is
  line-level; omit it (or send `[]`) when word-level segmentation isn't available. Field
  extraction only reads `Lines`/`FullText`, so this has no functional impact.
- `boundingBox` is a 4-point polygon `[[x,y], [x,y], [x,y], [x,y]]`, pixel (or PDF-point)
  coordinates — same concept as Azure's polygon, different JSON shape.
- On failure, either return a non-2xx HTTP status, or 200 with `"success": false` and an
  `errorMessage` — `PaddleOcrProvider` handles both the same way (marks the `NormalizedOcrDocument`
  as failed, never throws).
- `PaddleOcrProvider` never populates `Tables`, `KeyValuePairs`, or `Fields` — PaddleOCR in this
  contract is text/line detection only, no layout analysis. It's a free baseline for benchmarking
  against Azure, not a layout-aware replacement.

### Running the PaddleOCR service locally

Not included in this repo — implement only if/when needed. Two common options:

**Option A — Docker**, wrapping the official `paddleocr` PyPI package with a small HTTP layer
(e.g. FastAPI) that exposes `POST /ocr/analyze` per the contract above. A minimal reference
`Dockerfile` shape:
```dockerfile
FROM python:3.11-slim
RUN pip install paddlepaddle paddleocr fastapi uvicorn python-multipart
COPY app.py .
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8866"]
```

**Option B — local Python process** (no Docker):
```bash
pip install paddlepaddle paddleocr fastapi uvicorn python-multipart
uvicorn app:app --host 0.0.0.0 --port 8866
```

In both cases, `app.py` needs to: accept the uploaded file, run it through
`paddleocr.PaddleOCR(lang="vi")` (or your chosen language model), and shape the result into the
JSON contract above before responding. `PaddleOcrProvider` only depends on that HTTP contract —
it doesn't care how the service is implemented internally.

### Manual test: processing one file through Paddle

1. Start your PaddleOCR service locally (see above) and confirm it's reachable at the configured `BaseUrl`.
2. Set `Ocr:Provider` to `Paddle` and `PaddleOcr:BaseUrl` (Option 1 or 2 above).
3. Start Postgres and the API, upload a sample file, and confirm it reaches `Processed` (or `Failed` with a clear `ErrorMessage` if the service isn't responding as expected).
4. Watch the API log for `Calling OCR provider Paddle for document <id>` / `PaddleOCR analysis completed...`.
5. Switch `Ocr:Provider` back to `Fake` when done.

---

## Configuring the LLM extraction path (Gemini)

For software-generated PDFs, `PdfTextLayerLlmStrategy` can read the PDF's text layer and hand it
to Gemini for field extraction instead of (or before falling back to) the OCR path — see the
2026-08-13 entry in [docs/decisions.md](docs/decisions.md). Off by default (`Llm:Enabled=false`);
no external calls or cost until explicitly turned on.

### Set up an API key

```bash
cd apps/api/DocumentOCR.WebApi

dotnet user-secrets init
dotnet user-secrets set "Llm:ApiKey" "<your-gemini-api-key>"
dotnet user-secrets set "Llm:Enabled" "true"
```

Or via environment variable:
```bash
export Llm__ApiKey="<your-gemini-api-key>"
export Llm__Enabled="true"
```

### Configuration

```json
"Llm": {
  "Enabled": false,
  "Provider": "Gemini",
  "Model": "gemini-3.5-flash-lite",
  "Tier": "Free",
  "MaxConcurrency": 2,
  "RetryDelaysSeconds": [2, 5, 10]
}
```

| Setting | Default | Notes |
|---|---|---|
| `Model` | `gemini-3.5-flash-lite` | If unavailable for your API key/region, set to `gemini-3.1-flash-lite`. Never use `gemini-2.5-flash-lite` — Google ends support for it 2026-10-16. |
| `Tier` | `Free` | `Free` or `Paid`. While `Free` and `Enabled`, the app logs a startup Warning that submitted content may be used by the provider to improve their product — only use the free tier for test data, never real customer documents. Set to `Paid` once billing is enabled. |
| `MaxConcurrency` | `2` | Caps concurrent Gemini requests across all in-flight processing jobs — keeps a batch of documents from blowing through the free tier's ~5-15 requests/minute limit. |
| `RetryDelaysSeconds` | `[2, 5, 10]` | On HTTP 429 (rate limited), retries with these backoff delays (one retry per entry). If all retries are also rate limited, the strategy falls through to `OcrStrategy` — a document is never left stuck in `Processing` because Gemini is rate limited. |

Every response is verified against the source PDF text before being trusted (see
`PdfTextLayerLlmStrategy`) and it always uses `temperature = 0` with Gemini's native
`responseSchema`/`responseMimeType: application/json` structured output — never a "reply with
JSON" prompt parsed by hand.

### Manual test

1. Set `Llm:ApiKey`/`Llm:Enabled` (above).
2. Start Postgres and the API.
3. Upload a software-generated Vietnamese VAT-invoice PDF (not a scan).
4. Watch the log for `Gemini extraction completed. Model=...` and confirm the document reaches
   `Processed` with plausible field values.
5. Set `Llm:Enabled` back to `false` when done, so subsequent local runs don't call Gemini.

---

## OCR Benchmark Tool (dev-only)

`apps/api/tools/DocumentOCR.OcrBenchmark` is a console app that runs `FakeOcrProvider`,
`AzureDocumentIntelligenceProvider` (for one or more Azure model IDs at once), **and**
`PaddleOcrProvider` over a folder of sample PDF/JPG/PNG invoices in a single pass, and writes
per-file/per-target debug JSON plus a `summary.csv` for side-by-side comparison. It is dev-only
tooling — not part of the shipped API/WebApi project, and not wired into `DocumentOCR.WebApi` in
any way.

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
3. Optionally configure `PaddleOcr:BaseUrl` the same way if you have a PaddleOCR service running
   locally (see "Configuring PaddleOCR" above). If not configured, the Paddle row in the summary
   simply records a failure — the run still completes for Fake/Azure.

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
one subfolder per input file, each containing a `Fake/` subfolder, a `Paddle/` subfolder, and one
`AzureDocumentIntelligence-<modelId>/` subfolder per model run — each with `raw-response.json`,
`ocr-result.json` (the full `NormalizedOcrDocument`), `extracted-fields.json`,
`validation-warnings.json` — plus a `summary.csv` at the run root with one row per (file,
target): `FileName, DocumentCategory, ProviderName, ModelId, Features, ProcessingDurationMs,
PageCount, FullTextLength, LineCount, WordCount, ParagraphCount, TableCount, KeyValuePairCount,
AverageConfidence, ExtractedSupplierName, ExtractedSupplierTaxCode, ExtractedInvoiceNumber,
ExtractedInvoiceDate, ExtractedSubtotalAmount, ExtractedVatAmount, ExtractedTotalAmount,
ExtractedCurrency, WarningCount, RawProviderResponsePath, NormalizedOcrResultPath, ErrorMessage`.

⚠️ **Cost caution**: this runs Azure Document Intelligence against *every* file in `--input`,
once per model, not a single manual test call — a folder of 20 invoices against all 4 default
models is 80 billed Azure calls per run. Keep sample folders and `--models` lists small and
re-run sparingly — pass `--models <one-id>` to cut cost down to a single model per run.

Note: `FakeOcrProvider` ignores file content and always returns the same fixed result, so its
rows in `summary.csv` are identical for every input file — it's included so the two providers'
output can be diffed in the same shape, not as a meaningful per-file OCR-quality baseline.