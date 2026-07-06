# Testing Rules

Prioritize tests for business-critical logic.

Required test areas:

- Money normalization
- Vietnamese date normalization
- Tax code normalization
- OCR mistake correction in numeric fields
- VAT total matching
- Missing required field warning
- Low confidence warning
- FakeOcrProvider deterministic output
- Excel export columns and sheets

Do not rely on Azure OCR in automated tests.

Automated tests should use FakeOcrProvider or fixed OCR sample text.