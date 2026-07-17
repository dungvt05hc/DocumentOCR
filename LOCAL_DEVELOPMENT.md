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
| `prebuilt-invoice` | Vietnamese invoices (default) |
| `prebuilt-receipt` | Sales receipts |
| `prebuilt-read` | Plain text / unstructured documents |
| `prebuilt-layout` | Tables, checkboxes, structured forms |

---

### Choosing a provider: `Ocr:Provider`

Which `IDocumentOcrProvider` gets registered is controlled by configuration, not code:

```json
"Ocr": {
  "Provider": "Fake"
}
```

| Value | Behavior |
|---|---|
| `Fake` (default) | `FakeOcrProvider` — deterministic Vietnamese invoice result, no network calls, no credentials needed. |
| `Azure` | `AzureDocumentIntelligenceProvider` — requires `Endpoint`/`ApiKey` below. |

Override locally without editing `appsettings.json`:

```bash
# dotnet user-secrets
dotnet user-secrets set "Ocr:Provider" "Azure"

# or environment variable
export Ocr__Provider="Azure"
```

If `Ocr:Provider` is `Azure` but `Endpoint`/`ApiKey` are empty, `AzureDocumentIntelligenceProvider` logs a warning and returns an error result rather than throwing — it never crashes the app at startup.

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
   - `OCR provider AzureDocumentIntelligence (Model=prebuilt-invoice) completed for document <id>. Success=True, Pages=1, ...`
7. `GET /api/documents/{documentId}` and confirm `status` is `Processed` and extracted fields (SupplierName, InvoiceNumber, TotalAmount, etc.) are populated with plausible values.
8. Inspect the `OcrProviderLogs` table for that document (`SELECT * FROM "OcrProviderLogs" WHERE "DocumentId" = '<id>'`) and confirm `ProviderName`, `ModelId`, `PageCount`, `ProcessingTimeMs`, `EstimatedCost` and `RawResponseJson` are populated — `RawResponseJson` is the raw Azure response, useful for debugging field-mapping issues without re-calling Azure.
9. Switch `Ocr:Provider` back to `Fake` when done, so subsequent local runs don't incur Azure costs.