# OCR Pipeline Rules

The OCR pipeline must remain provider-neutral.

Application layer should depend on IDocumentOcrProvider, not Azure SDK.

Provider-specific implementation belongs in Infrastructure.

Pipeline:

Upload
→ store file
→ create Document
→ enqueue processing job
→ OCR provider
→ internal OcrResult
→ field extraction
→ normalization
→ validation
→ save fields and warnings
→ review
→ export

Always keep RawValue and NormalizedValue separate.

Always keep confidence score when available.

Always keep FakeOcrProvider for local development and tests.