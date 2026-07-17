# User Guide — DocumentOCR

How to go from "I uploaded a file" to "I have a clean Excel export."

## 1. Upload

Go to **Upload**, drop or pick your PDF/JPG/PNG invoice, receipt, or scan (max 20 MB). The file is stored and a Document record is created with status **Uploaded**. Processing is queued automatically — you don't need to trigger anything else for a normal upload.

## 2. Documents list

Go to **Documents** to see everything you've uploaded, with status, document type, and warning count. A document moves through these statuses:

| Status | Meaning |
|---|---|
| Uploaded | File stored, waiting to be processed |
| Processing | OCR + field extraction running in the background |
| Processed | Fields extracted, ready for you to review |
| Failed | OCR/extraction failed — use **Process** to retry |
| Reviewed | You saved field edits |
| Exported | Included in an Excel export |

Use **Refresh** to poll for status updates, and the **Status** filter to narrow the list. If a document is stuck on **Uploaded** or shows **Failed**, click **Process** to (re)run it.

## 3. Review — click "Review"

Clicking **Review** on a document opens the review screen: the original file preview on the left, extracted fields on the right.

- **Extracted fields**: SupplierName, SupplierTaxCode, InvoiceNumber, InvoiceDate, SubtotalAmount, VatAmount, TotalAmount, Currency, DocumentType, Notes — one input box each, pre-filled with the OCR provider's best guess (normalized value if available, otherwise the raw OCR text).
- **Confidence**: each field shows a confidence percentage from the OCR provider, or **"No confidence"** if the provider didn't return one for that field (this is expected and normal — not every field always has a score).
- **Warnings**: fields with problems (low confidence, missing a required value, invalid tax code/date, or a subtotal+VAT vs. total mismatch) are highlighted and show the warning message inline, plus a summary list at the top.
- Correct any wrong or missing values directly in the input boxes — this is the manual review step that covers the ~20-30% the AI doesn't get exactly right.

### "Save fields"

The **Save fields** button at the bottom submits your edits. It sends only the fields you see on screen back to the API, which:

- Stores your corrected value as the field's `NormalizedValue` (the original OCR output is preserved separately as `RawValue`, so nothing is lost).
- Marks each changed field as edited by you (shown as **"· edited"** next to the confidence badge afterward).
- Moves the document's status to **Reviewed**.

You can leave a field blank and save — it just means that value isn't set. Click **Save fields** once you're done correcting the document, then **Back** to return to the list.

## 4. Export

Once a document is **Processed** or **Reviewed**, its checkbox becomes selectable in the Documents list. Select the documents you want, go to **Export**, and click **Download Excel** to get a `.xlsx` with the extracted fields for all selected documents.

## Typical flow, end to end

1. **Upload** → file(s) land as **Uploaded**, processing starts automatically.
2. **Documents** → wait/refresh until status is **Processed** (or click **Process** if it shows **Failed**).
3. **Review** → check the fields against the preview image, fix anything wrong or flagged with a warning, click **Save fields** (status becomes **Reviewed**).
4. **Documents** → select the reviewed document(s).
5. **Export** → **Download Excel**.
