import { useEffect, useMemo, useState } from 'react';
import { downloadOriginal, updateFields } from '../services/api';
import type { DocumentReviewResponse, FieldUpdateItem, ReviewField } from '../types';

interface Props {
  document: DocumentReviewResponse;
  onSaved: () => void;
  onBack: () => void;
}

function isIsoDate(value: string | null): boolean {
  return !!value && /^\d{4}-\d{2}-\d{2}$/.test(value);
}

function fieldInputType(field: ReviewField, value: string): 'textarea' | 'select' | 'date' | 'text' {
  if (field.dataType === 'MultilineText') return 'textarea';
  if ((field.dataType === 'Enum' || field.dataType === 'Currency') && field.options && field.options.length > 0) {
    return 'select';
  }
  if (field.dataType === 'Date' && isIsoDate(value)) return 'date';
  return 'text';
}

export function FieldEditor({ document: doc, onSaved, onBack }: Props) {
  const allFields = useMemo(() => doc.sections.flatMap((section) => section.fields), [doc.sections]);

  const initialValues = useMemo(
    () => Object.fromEntries(allFields.map((field) => [field.fieldKey, field.value ?? ''])),
    [allFields]
  );

  const [values, setValues] = useState<Record<string, string>>(initialValues);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showDebug, setShowDebug] = useState(false);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;

    downloadOriginal(doc.documentId)
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
  }, [doc.documentId, doc.contentType]);

  const warningsByFieldKey = useMemo(() => {
    const map = new Map<string, typeof doc.warnings>();
    for (const warning of doc.warnings) {
      if (!warning.fieldKey) continue;
      map.set(warning.fieldKey, [...(map.get(warning.fieldKey) ?? []), warning]);
    }
    return map;
  }, [doc.warnings]);

  const handleSave = async () => {
    setSaving(true);
    setError(null);

    try {
      const updates: FieldUpdateItem[] = allFields.map((field) => ({
        fieldName: field.fieldKey,
        normalizedValue: values[field.fieldKey] || null,
      }));

      await updateFields(doc.documentId, { fields: updates });
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
          <h2>{doc.fileName}</h2>
          <p className="muted">
            {doc.status} · {doc.documentCategory} · {doc.warnings.length} warning
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
              <img src={previewUrl} alt={doc.fileName} />
            )
          ) : (
            <div className="preview-empty">Preview unavailable</div>
          )}
        </section>

        <section className="fields-pane" aria-label="Extracted fields">
          {doc.warnings.length > 0 && (
            <div className="warnings">
              {doc.warnings.map((warning, index) => (
                <div key={index} className={`warning ${warning.severity.toLowerCase()}`}>
                  <strong>{warning.severity}</strong>
                  {warning.fieldKey ? ` · ${warning.fieldKey}` : ''}: {warning.message}
                </div>
              ))}
            </div>
          )}

          {doc.sections
            .slice()
            .sort((a, b) => a.displayOrder - b.displayOrder)
            .map((section) => (
              <div key={section.sectionKey} className="review-section">
                <h3>{section.title}</h3>
                {section.description && <p className="muted">{section.description}</p>}

                <div className="field-grid">
                  {section.fields
                    .slice()
                    .sort((a, b) => a.displayOrder - b.displayOrder)
                    .map((field) => {
                      const fieldWarnings = warningsByFieldKey.get(field.fieldKey) ?? [];
                      const value = values[field.fieldKey] ?? '';
                      const inputType = fieldInputType(field, value);
                      const missingRequired = field.isMissing && field.isRequired;

                      return (
                        <label
                          key={field.fieldKey}
                          className={[
                            'field-editor',
                            fieldWarnings.length > 0 ? 'has-warning' : '',
                            missingRequired ? 'is-missing-required' : '',
                          ]
                            .filter(Boolean)
                            .join(' ')}
                        >
                          <span className="field-label-row">
                            <span>
                              {field.label}
                              {field.isRequired ? ' *' : ''}
                            </span>
                            <span className="field-meta">
                              {field.confidence !== null ? `${Math.round(field.confidence * 100)}%` : 'No confidence'}
                              {field.isEditedByUser ? ' · edited' : ''}
                            </span>
                          </span>

                          {inputType === 'textarea' ? (
                            <textarea
                              value={value}
                              onChange={(event) =>
                                setValues((current) => ({ ...current, [field.fieldKey]: event.target.value }))
                              }
                            />
                          ) : inputType === 'select' ? (
                            <select
                              value={value}
                              onChange={(event) =>
                                setValues((current) => ({ ...current, [field.fieldKey]: event.target.value }))
                              }
                            >
                              <option value="">—</option>
                              {field.options?.map((option) => (
                                <option key={option} value={option}>
                                  {option}
                                </option>
                              ))}
                            </select>
                          ) : (
                            <input
                              type={inputType === 'date' ? 'date' : 'text'}
                              value={value}
                              onChange={(event) =>
                                setValues((current) => ({ ...current, [field.fieldKey]: event.target.value }))
                              }
                            />
                          )}

                          {fieldWarnings.map((warning, index) => (
                            <span key={index} className="field-warning">
                              {warning.message}
                            </span>
                          ))}

                          {showDebug && (field.sourceText || field.sourceType || field.extractionMethod) && (
                            <span className="field-debug">
                              {[field.sourceType, field.extractionMethod, field.sourcePageNumber ? `p.${field.sourcePageNumber}` : null]
                                .filter(Boolean)
                                .join(' · ')}
                              {field.sourceText ? ` — "${field.sourceText}"` : ''}
                            </span>
                          )}
                        </label>
                      );
                    })}
                </div>
              </div>
            ))}

          {error && <p className="message error">{error}</p>}

          <div className="review-actions">
            <label className="debug-toggle">
              <input
                type="checkbox"
                checked={showDebug}
                onChange={(event) => setShowDebug(event.target.checked)}
              />
              Show OCR source/debug info
            </label>
            <button type="button" className="primary" onClick={handleSave} disabled={saving}>
              {saving ? 'Saving...' : 'Save fields'}
            </button>
          </div>
        </section>
      </div>
    </main>
  );
}
