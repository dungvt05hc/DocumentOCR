import { useState } from 'react';
import { updateFields } from '../services/api';
import type { DocumentDetailDto, FieldName, FieldUpdateItem } from '../types';

interface Props {
  document: DocumentDetailDto;
  onSaved: () => void;
  onBack: () => void;
}

const FIELD_LABELS: Record<FieldName, string> = {
  SupplierName: 'Supplier Name',
  SupplierTaxCode: 'Tax Code',
  InvoiceNumber: 'Invoice Number',
  InvoiceDate: 'Invoice Date',
  SubtotalAmount: 'Subtotal',
  VatAmount: 'VAT Amount',
  TotalAmount: 'Total Amount',
  Currency: 'Currency',
  DocumentType: 'Document Type',
  Notes: 'Notes',
};

export function FieldEditor({ document: doc, onSaved, onBack }: Props) {
  const initialValues = Object.fromEntries(
    doc.fields.map((f) => [f.fieldName, f.normalizedValue ?? ''])
  ) as Record<FieldName, string>;

  const [values, setValues] = useState<Record<FieldName, string>>(initialValues);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const updates: FieldUpdateItem[] = (Object.keys(values) as FieldName[]).map((key) => ({
        fieldName: key,
        normalizedValue: values[key] || null,
      }));
      await updateFields(doc.id, { fields: updates });
      onSaved();
    } catch {
      setError('Failed to save. Please try again.');
    } finally {
      setSaving(false);
    }
  };

  const confidenceFor = (field: FieldName) =>
    doc.fields.find((f) => f.fieldName === field)?.confidenceScore;

  const isEdited = (field: FieldName) =>
    doc.fields.find((f) => f.fieldName === field)?.isEditedByUser ?? false;

  return (
    <div>
      <button onClick={onBack} style={{ marginBottom: 16, cursor: 'pointer' }}>← Back</button>
      <h2 style={{ marginBottom: 4 }}>{doc.originalFileName}</h2>
      <p style={{ color: '#888', marginTop: 0 }}>Status: {doc.status}</p>

      {/* Warnings */}
      {doc.warnings.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          {doc.warnings.map((w) => (
            <div
              key={w.id}
              style={{
                padding: '6px 12px',
                marginBottom: 4,
                borderRadius: 4,
                background: w.severity === 'Error' ? '#fce4e4' : w.severity === 'Warning' ? '#fff3cd' : '#e8f4fd',
                color: w.severity === 'Error' ? '#c0392b' : w.severity === 'Warning' ? '#856404' : '#2c5f7a',
                fontSize: '0.85rem',
              }}
            >
              <strong>[{w.severity}]</strong>{w.relatedField ? ` ${w.relatedField}:` : ''} {w.message}
            </div>
          ))}
        </div>
      )}

      {/* Field Editor Grid */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
        {(Object.keys(FIELD_LABELS) as FieldName[]).map((fieldName) => {
          const conf = confidenceFor(fieldName);
          const edited = isEdited(fieldName);
          return (
            <div key={fieldName}>
              <label style={{ display: 'block', fontSize: '0.8rem', color: '#555', marginBottom: 2 }}>
                {FIELD_LABELS[fieldName]}
                {conf !== null && conf !== undefined && (
                  <span
                    style={{
                      marginLeft: 6,
                      fontSize: '0.75rem',
                      color: conf < 0.7 ? 'orange' : 'green',
                    }}
                  >
                    {(conf * 100).toFixed(0)}%
                  </span>
                )}
                {edited && (
                  <span style={{ marginLeft: 6, fontSize: '0.7rem', color: '#2D6A9F' }}>edited</span>
                )}
              </label>
              <input
                type="text"
                value={values[fieldName] ?? ''}
                onChange={(e) => setValues({ ...values, [fieldName]: e.target.value })}
                style={{
                  width: '100%',
                  padding: '6px 8px',
                  border: '1px solid #ccc',
                  borderRadius: 4,
                  boxSizing: 'border-box',
                  background: edited ? '#fffde7' : 'white',
                }}
              />
            </div>
          );
        })}
      </div>

      {error && <p style={{ color: 'red', marginTop: 8 }}>{error}</p>}

      <div style={{ marginTop: 16, display: 'flex', gap: 8 }}>
        <button
          onClick={handleSave}
          disabled={saving}
          style={{
            padding: '8px 24px',
            background: '#2D6A9F',
            color: 'white',
            border: 'none',
            borderRadius: 4,
            cursor: 'pointer',
            fontSize: '0.95rem',
          }}
        >
          {saving ? 'Saving…' : 'Save & Mark Reviewed'}
        </button>
      </div>

      {/* OCR log */}
      {doc.ocrLog && (
        <div style={{ marginTop: 24, fontSize: '0.8rem', color: '#888' }}>
          OCR: {doc.ocrLog.provider} | {doc.ocrLog.pageCount} page(s) |{' '}
          {doc.ocrLog.processingTimeMs.toFixed(0)}ms | est. cost ${doc.ocrLog.estimatedCost.toFixed(4)}
        </div>
      )}
    </div>
  );
}
