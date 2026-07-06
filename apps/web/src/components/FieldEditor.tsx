import { useEffect, useMemo, useState } from 'react';
import { downloadOriginal, updateFields } from '../services/api';
import type { DocumentDetailDto, FieldName, FieldUpdateItem, ValidationWarningDto } from '../types';

interface Props {
  document: DocumentDetailDto;
  onSaved: () => void;
  onBack: () => void;
}

const fields: FieldName[] = [
  'SupplierName',
  'SupplierTaxCode',
  'InvoiceNumber',
  'InvoiceDate',
  'SubtotalAmount',
  'VatAmount',
  'TotalAmount',
  'Currency',
  'DocumentType',
  'Notes',
];

const labels: Record<FieldName, string> = {
  SupplierName: 'Supplier name',
  SupplierTaxCode: 'Tax code',
  InvoiceNumber: 'Invoice number',
  InvoiceDate: 'Invoice date',
  SubtotalAmount: 'Subtotal',
  VatAmount: 'VAT amount',
  TotalAmount: 'Total amount',
  Currency: 'Currency',
  DocumentType: 'Document type',
  Notes: 'Notes',
};

export function FieldEditor({ document: doc, onSaved, onBack }: Props) {
  const initialValues = useMemo(
    () =>
      Object.fromEntries(
        fields.map((fieldName) => {
          const field = doc.fields.find((item) => item.fieldName === fieldName);
          return [fieldName, field?.normalizedValue ?? field?.rawValue ?? ''];
        })
      ) as Record<FieldName, string>,
    [doc.fields]
  );

  const [values, setValues] = useState<Record<FieldName, string>>(initialValues);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;

    downloadOriginal(doc.id)
      .then((response) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(new Blob([response.data], { type: doc.contentType }));
        setPreviewUrl(objectUrl);
      })
      .catch(() => setPreviewUrl(null));

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [doc.id, doc.contentType]);

  const warningsByField = useMemo(() => {
    const map = new Map<FieldName, ValidationWarningDto[]>();
    for (const warning of doc.warnings) {
      if (!warning.fieldName) continue;
      map.set(warning.fieldName, [...(map.get(warning.fieldName) ?? []), warning]);
    }
    return map;
  }, [doc.warnings]);

  const handleSave = async () => {
    setSaving(true);
    setError(null);

    try {
      const updates: FieldUpdateItem[] = fields.map((fieldName) => ({
        fieldName,
        normalizedValue: values[fieldName] || null,
      }));

      await updateFields(doc.id, { fields: updates });
      onSaved();
    } catch {
      setError('Failed to save field edits.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <main className="review-page">
      <div className="review-header">
        <button type="button" onClick={onBack}>
          Back
        </button>
        <div>
          <h2>{doc.originalFileName}</h2>
          <p className="muted">
            {doc.status} · {doc.documentType} · {doc.warnings.length} warning
            {doc.warnings.length === 1 ? '' : 's'}
          </p>
        </div>
      </div>

      <div className="review-layout">
        <section className="preview-pane" aria-label="Original document preview">
          {previewUrl ? (
            doc.contentType === 'application/pdf' ? (
              <iframe title="Original document preview" src={previewUrl} />
            ) : (
              <img src={previewUrl} alt={doc.originalFileName} />
            )
          ) : (
            <div className="preview-empty">Preview unavailable</div>
          )}
        </section>

        <section className="fields-pane" aria-label="Extracted fields">
          {doc.warnings.length > 0 && (
            <div className="warnings">
              {doc.warnings.map((warning) => (
                <div key={warning.id} className={`warning ${warning.severity.toLowerCase()}`}>
                  <strong>{warning.severity}</strong>
                  {warning.fieldName ? ` · ${labels[warning.fieldName]}` : ''}: {warning.message}
                </div>
              ))}
            </div>
          )}

          <div className="field-grid">
            {fields.map((fieldName) => {
              const field = doc.fields.find((item) => item.fieldName === fieldName);
              const confidence = field?.confidence;
              const fieldWarnings = warningsByField.get(fieldName) ?? [];

              return (
                <label
                  key={fieldName}
                  className={`field-editor${fieldWarnings.length > 0 ? ' has-warning' : ''}`}
                >
                  <span className="field-label-row">
                    <span>{labels[fieldName]}</span>
                    <span className="field-meta">
                      {confidence !== null && confidence !== undefined
                        ? `${Math.round(confidence * 100)}%`
                        : 'No confidence'}
                      {field?.isEditedByUser ? ' · edited' : ''}
                    </span>
                  </span>
                  <input
                    value={values[fieldName] ?? ''}
                    onChange={(event) =>
                      setValues((current) => ({ ...current, [fieldName]: event.target.value }))
                    }
                  />
                  {fieldWarnings.map((warning) => (
                    <span key={warning.id} className="field-warning">
                      {warning.message}
                    </span>
                  ))}
                </label>
              );
            })}
          </div>

          {error && <p className="message error">{error}</p>}

          <div className="review-actions">
            <button type="button" className="primary" onClick={handleSave} disabled={saving}>
              {saving ? 'Saving...' : 'Save fields'}
            </button>
          </div>
        </section>
      </div>
    </main>
  );
}
