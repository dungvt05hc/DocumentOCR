import { useEffect, useMemo, useState } from 'react';
import { downloadOriginal, getOcrDebug, updateFields } from '../services/api';
import type {
  DocumentReviewResponse,
  FieldUpdateItem,
  OcrDebugResponse,
  ReviewField,
  ReviewLineItem,
  TableUpdateItem,
} from '../types';

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

const CELL_EDIT_KEY_SEPARATOR = '|';

type DebugTab = 'fulltext' | 'lines' | 'keyValuePairs' | 'paths';

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

  // tableId -> "rowIndex|columnKey" -> edited text
  const [tableEdits, setTableEdits] = useState<Record<string, Record<string, string>>>({});
  const [lineItemEdits, setLineItemEdits] = useState<Record<number, Partial<ReviewLineItem>>>({});

  const [ocrDebug, setOcrDebug] = useState<OcrDebugResponse | null>(null);
  const [ocrDebugLoading, setOcrDebugLoading] = useState(false);
  const [ocrDebugUnavailable, setOcrDebugUnavailable] = useState(false);
  const [debugTab, setDebugTab] = useState<DebugTab>('fulltext');

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

  useEffect(() => {
    if (!showDebug || ocrDebug || ocrDebugLoading || ocrDebugUnavailable) return;

    setOcrDebugLoading(true);
    getOcrDebug(doc.documentId)
      .then((response) => setOcrDebug(response.data))
      .catch(() => setOcrDebugUnavailable(true))
      .finally(() => setOcrDebugLoading(false));
  }, [showDebug, ocrDebug, ocrDebugLoading, ocrDebugUnavailable, doc.documentId]);

  const cellEditKey = (rowIndex: number, columnKey: string | null) =>
    `${rowIndex}${CELL_EDIT_KEY_SEPARATOR}${columnKey ?? ''}`;

  const handleCellChange = (tableId: string, rowIndex: number, columnKey: string | null, text: string) => {
    setTableEdits((current) => ({
      ...current,
      [tableId]: { ...current[tableId], [cellEditKey(rowIndex, columnKey)]: text },
    }));
  };

  const handleLineItemChange = (lineNumber: number, patch: Partial<ReviewLineItem>) => {
    setLineItemEdits((current) => ({ ...current, [lineNumber]: { ...current[lineNumber], ...patch } }));
  };

  const buildTableUpdates = (): TableUpdateItem[] =>
    Object.entries(tableEdits)
      .map(([tableId, edits]) => {
        const rowsByIndex = new Map<number, { columnKey: string | null; text: string }[]>();
        for (const [key, text] of Object.entries(edits)) {
          const [rowIndexRaw, columnKey] = key.split(CELL_EDIT_KEY_SEPARATOR);
          const rowIndex = Number(rowIndexRaw);
          const cells = rowsByIndex.get(rowIndex) ?? [];
          cells.push({ columnKey: columnKey || null, text });
          rowsByIndex.set(rowIndex, cells);
        }

        return {
          tableId,
          rows: Array.from(rowsByIndex.entries()).map(([rowIndex, cells]) => ({ rowIndex, cells })),
        };
      })
      .filter((table) => table.rows.length > 0);

  const handleSave = async () => {
    setSaving(true);
    setError(null);

    try {
      const updates: FieldUpdateItem[] = allFields.map((field) => ({
        fieldName: field.fieldKey,
        normalizedValue: values[field.fieldKey] || null,
      }));

      const lineItems = doc.lineItems.map((item) => ({
        lineNumber: item.lineNumber,
        description: lineItemEdits[item.lineNumber]?.description ?? item.description,
        quantity: lineItemEdits[item.lineNumber]?.quantity ?? item.quantity,
        unit: lineItemEdits[item.lineNumber]?.unit ?? item.unit,
        unitPrice: lineItemEdits[item.lineNumber]?.unitPrice ?? item.unitPrice,
        amount: lineItemEdits[item.lineNumber]?.amount ?? item.amount,
        currency: lineItemEdits[item.lineNumber]?.currency ?? item.currency,
      }));

      await updateFields(doc.documentId, { fields: updates, tables: buildTableUpdates(), lineItems });
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

          {doc.tables.length > 0 && (
            <div className="review-section">
              <h3>Detected tables</h3>
              {doc.tables.map((table, tableIndex) => (
                <div key={table.tableId} className="detected-table">
                  <p className="muted">
                    {table.title ?? `Detected table ${tableIndex + 1}`}
                    {table.pageNumber ? ` · page ${table.pageNumber}` : ''}
                    {table.confidence !== null ? ` · ${Math.round(table.confidence * 100)}%` : ''}
                  </p>

                  {table.columns.length === 0 ? (
                    <p className="muted">No cells detected in this table.</p>
                  ) : (
                    <table className="editable-table">
                      <thead>
                        <tr>
                          {table.columns.map((column) => (
                            <th key={column.columnKey}>{column.label || column.columnKey}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {table.rows
                          .filter((row) => row.rowType !== 'Header')
                          .map((row) => {
                            const cellsByColumn = new Map(row.cells.map((cell) => [cell.columnIndex, cell]));
                            return (
                              <tr key={row.rowIndex}>
                                {table.columns.map((column) => {
                                  const cell = cellsByColumn.get(column.columnIndex);
                                  const editKey = cellEditKey(row.rowIndex, column.columnKey);
                                  const value = tableEdits[table.tableId]?.[editKey] ?? cell?.text ?? '';
                                  return (
                                    <td key={column.columnKey}>
                                      <input
                                        type="text"
                                        value={value}
                                        onChange={(event) =>
                                          handleCellChange(table.tableId, row.rowIndex, column.columnKey, event.target.value)
                                        }
                                      />
                                    </td>
                                  );
                                })}
                              </tr>
                            );
                          })}
                      </tbody>
                    </table>
                  )}
                </div>
              ))}
            </div>
          )}

          {doc.lineItems.length > 0 && (
            <div className="review-section">
              <h3>Line item candidates</h3>
              <p className="muted">Simple candidates derived from detected tables — not guaranteed to be complete or correct.</p>
              <table className="editable-table">
                <thead>
                  <tr>
                    <th>Line #</th>
                    <th>Description</th>
                    <th>Quantity</th>
                    <th>Unit</th>
                    <th>Unit price</th>
                    <th>Amount</th>
                    <th>Currency</th>
                    <th>Confidence</th>
                  </tr>
                </thead>
                <tbody>
                  {doc.lineItems.map((item) => {
                    const edits = lineItemEdits[item.lineNumber] ?? {};
                    const isLowConfidence = (item.confidence ?? 1) < 0.6;
                    return (
                      <tr key={item.lineNumber} className={isLowConfidence ? 'is-experimental' : undefined}>
                        <td>{item.lineNumber}</td>
                        <td>
                          <input
                            type="text"
                            value={edits.description ?? item.description ?? ''}
                            onChange={(event) => handleLineItemChange(item.lineNumber, { description: event.target.value })}
                          />
                        </td>
                        <td>
                          <input
                            type="text"
                            value={(edits.quantity ?? item.quantity) ?? ''}
                            onChange={(event) =>
                              handleLineItemChange(item.lineNumber, { quantity: event.target.value === '' ? null : Number(event.target.value) })
                            }
                          />
                        </td>
                        <td>
                          <input
                            type="text"
                            value={edits.unit ?? item.unit ?? ''}
                            onChange={(event) => handleLineItemChange(item.lineNumber, { unit: event.target.value })}
                          />
                        </td>
                        <td>
                          <input
                            type="text"
                            value={(edits.unitPrice ?? item.unitPrice) ?? ''}
                            onChange={(event) =>
                              handleLineItemChange(item.lineNumber, { unitPrice: event.target.value === '' ? null : Number(event.target.value) })
                            }
                          />
                        </td>
                        <td>
                          <input
                            type="text"
                            value={(edits.amount ?? item.amount) ?? ''}
                            onChange={(event) =>
                              handleLineItemChange(item.lineNumber, { amount: event.target.value === '' ? null : Number(event.target.value) })
                            }
                          />
                        </td>
                        <td>
                          <input
                            type="text"
                            value={edits.currency ?? item.currency ?? ''}
                            onChange={(event) => handleLineItemChange(item.lineNumber, { currency: event.target.value })}
                          />
                        </td>
                        <td>
                          {item.confidence !== null ? `${Math.round(item.confidence * 100)}%` : '—'}
                          {isLowConfidence ? ' · experimental' : ''}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

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

          {showDebug && ocrDebugLoading && <p className="muted">Loading OCR debug data...</p>}

          {showDebug && ocrDebug && (
            <div className="review-section ocr-debug-viewer">
              <h3>OCR debug data</h3>
              <div className="debug-tabs">
                {(['fulltext', 'lines', 'keyValuePairs', 'paths'] as DebugTab[]).map((tab) => (
                  <button
                    key={tab}
                    type="button"
                    className={debugTab === tab ? 'active' : ''}
                    onClick={() => setDebugTab(tab)}
                  >
                    {tab === 'fulltext' && 'Full text'}
                    {tab === 'lines' && 'Lines'}
                    {tab === 'keyValuePairs' && 'Key-value pairs'}
                    {tab === 'paths' && 'Raw JSON paths'}
                  </button>
                ))}
              </div>

              {debugTab === 'fulltext' && <pre className="debug-pre">{ocrDebug.fullText || '(no text)'}</pre>}

              {debugTab === 'lines' && (
                <pre className="debug-pre">
                  {ocrDebug.lines.length > 0 ? ocrDebug.lines.join('\n') : '(no lines available — normalized OCR result was not stored for this document)'}
                </pre>
              )}

              {debugTab === 'keyValuePairs' && (
                <pre className="debug-pre">
                  {ocrDebug.keyValuePairs.length > 0
                    ? ocrDebug.keyValuePairs.map((kv) => `${kv.key ?? ''}: ${kv.value ?? ''}`).join('\n')
                    : '(no key-value pairs available)'}
                </pre>
              )}

              {debugTab === 'paths' && (
                <pre className="debug-pre">
                  {[
                    `Raw provider response: ${ocrDebug.rawProviderResponsePath ?? '(not stored)'}`,
                    `Normalized OCR result: ${ocrDebug.normalizedOcrResultPath ?? '(not stored)'}`,
                    ocrDebug.rawProviderResponseJson ? `\nRaw JSON:\n${ocrDebug.rawProviderResponseJson}` : '',
                  ]
                    .filter(Boolean)
                    .join('\n')}
                </pre>
              )}
            </div>
          )}
        </section>
      </div>
    </main>
  );
}
