# Security Rules

Apply these rules when working on file upload, storage, OCR provider integration, logging, and API endpoints.

- Never hard-code API keys or secrets.
- Use environment variables or secure configuration.
- Do not log full invoice content unless explicitly needed for local debugging.
- Do not expose uploaded files through public URLs.
- Validate file extensions and MIME types.
- Only allow PDF, JPG, PNG for MVP.
- Enforce file size limits.
- Store files under controlled storage paths.
- Prevent path traversal.
- Do not trust file names from users.
- Generate safe internal stored file names.
- Return generic error messages to users.
- Keep detailed errors in logs only.