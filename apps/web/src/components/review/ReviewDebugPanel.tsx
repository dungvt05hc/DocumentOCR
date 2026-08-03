import type { OcrDebugResponse } from '../../types';

type DebugTab = 'fulltext' | 'lines' | 'keyValuePairs' | 'paths';

interface Props {
  loading: boolean;
  data: OcrDebugResponse | null;
  debugTab: DebugTab;
  onDebugTabChange: (tab: DebugTab) => void;
}

export function ReviewDebugPanel({ loading, data, debugTab, onDebugTabChange }: Props) {
  if (loading) return <p className="muted">Loading OCR debug data...</p>;
  if (!data) return null;

  return (
    <div className="review-section ocr-debug-viewer">
      <h3>OCR debug data</h3>
      <div className="debug-tabs">
        {(['fulltext', 'lines', 'keyValuePairs', 'paths'] as DebugTab[]).map((tab) => (
          <button
            key={tab}
            type="button"
            className={debugTab === tab ? 'active' : ''}
            onClick={() => onDebugTabChange(tab)}
          >
            {tab === 'fulltext' && 'Full text'}
            {tab === 'lines' && 'Lines'}
            {tab === 'keyValuePairs' && 'Key-value pairs'}
            {tab === 'paths' && 'Raw JSON paths'}
          </button>
        ))}
      </div>

      {debugTab === 'fulltext' && <pre className="debug-pre">{data.fullText || '(no text)'}</pre>}

      {debugTab === 'lines' && (
        <pre className="debug-pre">
          {data.lines.length > 0 ? data.lines.join('\n') : '(no lines available — normalized OCR result was not stored for this document)'}
        </pre>
      )}

      {debugTab === 'keyValuePairs' && (
        <pre className="debug-pre">
          {data.keyValuePairs.length > 0
            ? data.keyValuePairs.map((kv) => `${kv.key ?? ''}: ${kv.value ?? ''}`).join('\n')
            : '(no key-value pairs available)'}
        </pre>
      )}

      {debugTab === 'paths' && (
        <pre className="debug-pre">
          {[
            `Raw provider response: ${data.rawProviderResponsePath ?? '(not stored)'}`,
            `Normalized OCR result: ${data.normalizedOcrResultPath ?? '(not stored)'}`,
            data.rawProviderResponseJson ? `\nRaw JSON:\n${data.rawProviderResponseJson}` : '',
          ]
            .filter(Boolean)
            .join('\n')}
        </pre>
      )}
    </div>
  );
}
