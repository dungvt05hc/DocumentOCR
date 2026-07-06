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

### Developing without an Azure account

Leave `Endpoint` and `ApiKey` empty. `AzureDocumentIntelligenceProvider` will log a warning and return an error result.  
To process documents locally without any cloud dependency, swap the DI registration in `DependencyInjection.cs`:

```csharp
// In DependencyInjection.cs — for local dev only, do not merge to main
services.AddSingleton<IDocumentOcrProvider, FakeOcrProvider>();
```

`FakeOcrProvider` returns a deterministic Vietnamese invoice result with no network calls.

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
