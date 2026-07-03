import type { DocumentDto } from '../types';

interface Props {
  documents: DocumentDto[];
  selectedIds: Set<string>;
  onToggleSelect: (id: string) => void;
  onViewDocument: (id: string) => void;
  onTriggerProcess: (id: string) => void;
}

const STATUS_COLORS: Record<string, string> = {
  Pending: '#888',
  Processing: '#f5a623',
  ReviewRequired: '#2D6A9F',
  Reviewed: '#27ae60',
  Failed: '#e74c3c',
};

export function DocumentTable({
  documents,
  selectedIds,
  onToggleSelect,
  onViewDocument,
  onTriggerProcess,
}: Props) {
  if (documents.length === 0)
    return <p style={{ color: '#888' }}>No documents yet. Upload files to begin.</p>;

  return (
    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
      <thead>
        <tr style={{ background: '#2D6A9F', color: 'white' }}>
          <th style={th}></th>
          <th style={th}>File Name</th>
          <th style={th}>Type</th>
          <th style={th}>Status</th>
          <th style={th}>Uploaded</th>
          <th style={th}>Actions</th>
        </tr>
      </thead>
      <tbody>
        {documents.map((doc) => (
          <tr key={doc.id} style={{ borderBottom: '1px solid #eee' }}>
            <td style={td}>
              <input
                type="checkbox"
                checked={selectedIds.has(doc.id)}
                onChange={() => onToggleSelect(doc.id)}
              />
            </td>
            <td style={td}>{doc.originalFileName}</td>
            <td style={td}>{doc.detectedType}</td>
            <td style={td}>
              <span
                style={{
                  background: STATUS_COLORS[doc.status] ?? '#888',
                  color: 'white',
                  padding: '2px 8px',
                  borderRadius: 12,
                  fontSize: '0.8rem',
                }}
              >
                {doc.status}
              </span>
            </td>
            <td style={td}>{new Date(doc.createdAt).toLocaleDateString()}</td>
            <td style={td}>
              <button onClick={() => onViewDocument(doc.id)} style={btn}>Review</button>
              {(doc.status === 'Pending' || doc.status === 'Failed') && (
                <button onClick={() => onTriggerProcess(doc.id)} style={{ ...btn, marginLeft: 4 }}>
                  Process
                </button>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

const th: React.CSSProperties = { padding: '8px 12px', textAlign: 'left' };
const td: React.CSSProperties = { padding: '8px 12px' };
const btn: React.CSSProperties = {
  padding: '4px 10px',
  border: 'none',
  borderRadius: 4,
  cursor: 'pointer',
  background: '#2D6A9F',
  color: 'white',
  fontSize: '0.8rem',
};
