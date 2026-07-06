# Product Context - DocumentOCR

## Problem

Vietnamese accountants, SMEs, shop owners, and admin staff often manually enter invoice and receipt data into Excel or accounting systems.

Manual data entry is slow, repetitive, and error-prone.

## Target Users

- SME accountants
- Freelance accounting service providers
- Shop owners
- Admin staff
- Small business operators

## Product Goal

Help users reduce manual invoice/receipt data entry by uploading documents, extracting important fields automatically, reviewing the result, and exporting clean structured data to Excel.

## Core User Flow

1. User uploads an invoice, receipt, or PDF scan.
2. System stores the original document.
3. System processes the file in the background.
4. OCR provider extracts text/layout.
5. System extracts key fields.
6. System normalizes money, date, tax code, and currency.
7. System validates the result.
8. User reviews and corrects the fields.
9. User exports selected documents to Excel.

## MVP Fields

- SupplierName
- SupplierTaxCode
- InvoiceNumber
- InvoiceDate
- SubtotalAmount
- VatAmount
- TotalAmount
- Currency
- DocumentType
- Notes

## Business Principle

The app does not need to be 100% automatic.

The MVP goal is:

AI reads 70-80%.
User reviews/corrects 20-30%.
The app saves time and reduces repetitive data entry.

## MVP Success Criteria

The MVP is successful when:

- User can upload a document.
- System processes it asynchronously.
- System extracts at least 5-7 key fields.
- User can review and edit fields.
- System can export Excel.
- Fake OCR local flow works without cloud credentials.
- Azure OCR provider can be enabled by configuration.