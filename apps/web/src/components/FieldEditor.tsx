import { useEffect, useMemo, useRef, useState } from 'react';
import { downloadOriginal, getOcrDebug, updateFields } from '../services/api';
import type {
  DocumentReviewResponse,
  FieldUpdateItem,
  OcrDebugResponse,
  ReviewLineItem,
  ReviewWarningDto,
  TableUpdateItem,
} from '../types';
import { ReviewWarningsBar } from './review/ReviewWarningsBar';
import { ReviewDetailsTab } from './review/ReviewDetailsTab';
import { ReviewTablesTab } from './review/ReviewTablesTab';
import { ReviewLineItemsTab } from './review/ReviewLineItemsTab';
import { ReviewDebugPanel } from './review/ReviewDebugPanel';
import { parseTableCellEditKey, tableCellEditKey } from './review/tableEditKey';

interface Props {
  document: DocumentReviewResponse;
  onSaved: () => void;
  onBack: () => void;
}

type ReviewTab = 'details' | 'tables' | 'lineItems' | 'debug';
type DebugTab = 'fulltext' | 'lines' | 'keyValuePairs' | 'paths';
type FieldControl = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

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
  const [activeTab, setActiveTab] = useState<ReviewTab>('details');

  // tableId -> "rowIndex|columnKey" -> edited text
  const [tableEdits, setTableEdits] = useState<Record<string, Record<string, string>>>({});
  const [lineItemEdits, setLineItemEdits] = useState<Record<number, Partial<ReviewLineItem>>>({});

  const [ocrDebug, setOcrDebug] = useState<OcrDebugResponse | null>(null);
  const [ocrDebugLoading, setOcrDebugLoading] = useState(false);
  const [ocrDebugUnavailable, setOcrDebugUnavailable] = useState(false);
  const [debugTab, setDebugTab] = useState<DebugTab>('fulltext');

  const fieldRefs = useRef(new Map<string, FieldControl>());

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
    const map = new Map<string, ReviewWarningDto[]>();
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

  useEffect(() => {
    if (activeTab === 'debug' && !showDebug) setActiveTab('details');
  }, [activeTab, showDebug]);

  const registerFieldRef = (fieldKey: string, el: FieldControl | null) => {
    if (el) fieldRefs.current.set(fieldKey, el);
    else fieldRefs.current.delete(fieldKey);
  };

  const focusField = (fieldKey: string) => {
    setActiveTab('details');
    requestAnimationFrame(() => {
      const el = fieldRefs.current.get(fieldKey);
      el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      el?.focus();
    });
  };

  const handleFieldChange = (fieldKey: string, value: string) => {
    setValues((current) => ({ ...current, [fieldKey]: value }));
  };

  const handleCellChange = (tableId: string, rowIndex: number, columnKey: string | null, text: string) => {
    setTableEdits((current) => ({
      ...current,
      [tableId]: { ...current[tableId], [tableCellEditKey(rowIndex, columnKey)]: text },
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
          const { rowIndex, columnKey } = parseTableCellEditKey(key);
          const cells = rowsByIndex.get(rowIndex) ?? [];
          cells.push({ columnKey, text });
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

  const tabs: { key: ReviewTab; label: string; count: number | null }[] = [
    { key: 'details', label: 'Details', count: null },
    ...(doc.tables.length > 0 ? [{ key: 'tables' as const, label: 'Tables', count: doc.tables.length }] : []),
    ...(doc.lineItems.length > 0
      ? [{ key: 'lineItems' as const, label: 'Line items', count: doc.lineItems.length }]
      : []),
    ...(showDebug ? [{ key: 'debug' as const, label: 'Debug', count: null }] : []),
  ];

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
          <div className="fields-pane-header">
            <div className="review-tabbar">
              <div className="review-tabs">
                {tabs.map((tab) => (
                  <button
                    key={tab.key}
                    type="button"
                    className={activeTab === tab.key ? 'active' : ''}
                    onClick={() => setActiveTab(tab.key)}
                  >
                    {tab.label}
                    {tab.count !== null ? ` (${tab.count})` : ''}
                  </button>
                ))}
              </div>

              <div className="review-tabbar-actions">
                <label className="debug-toggle">
                  <input
                    type="checkbox"
                    checked={showDebug}
                    onChange={(event) => setShowDebug(event.target.checked)}
                  />
                  Debug
                </label>
                <button type="button" className="primary" onClick={handleSave} disabled={saving}>
                  {saving ? 'Saving...' : 'Save fields'}
                </button>
              </div>
            </div>

            {doc.warnings.length > 0 && (
              <ReviewWarningsBar
                warnings={doc.warnings}
                onWarningClick={(warning) => warning.fieldKey && focusField(warning.fieldKey)}
              />
            )}

            {error && <p className="message error">{error}</p>}
          </div>

          <div className="fields-pane-body">
            <div hidden={activeTab !== 'details'}>
              <ReviewDetailsTab
                sections={doc.sections}
                values={values}
                onFieldChange={handleFieldChange}
                warningsByFieldKey={warningsByFieldKey}
                showDebug={showDebug}
                registerFieldRef={registerFieldRef}
              />
            </div>

            {doc.tables.length > 0 && (
              <div hidden={activeTab !== 'tables'}>
                <ReviewTablesTab tables={doc.tables} tableEdits={tableEdits} onCellChange={handleCellChange} />
              </div>
            )}

            {doc.lineItems.length > 0 && (
              <div hidden={activeTab !== 'lineItems'}>
                <ReviewLineItemsTab
                  lineItems={doc.lineItems}
                  lineItemEdits={lineItemEdits}
                  onLineItemChange={handleLineItemChange}
                />
              </div>
            )}

            {showDebug && (
              <div hidden={activeTab !== 'debug'}>
                <ReviewDebugPanel
                  loading={ocrDebugLoading}
                  data={ocrDebug}
                  debugTab={debugTab}
                  onDebugTabChange={setDebugTab}
                />
              </div>
            )}
          </div>
        </section>
      </div>
    </main>
  );
}
