import type { DocumentDto, DocumentStatus } from '../types';

interface Props {
  documents: DocumentDto[];
  selectedIds: Set<string>;
  onToggleSelect: (id: string) => void;
  onViewDocument: (id: string) => void;
  onTriggerProcess: (id: string) => void;
}

const statusClasses: Record<DocumentStatus, string> = {
  Uploaded: 'status uploaded',
  Processing: 'status processing',
  Processed: 'status processed',
  Failed: 'status failed',
  Reviewed: 'status reviewed',
  Exported: 'status exported',
};

const canExport = (status: DocumentStatus) =>
  status === 'Processed' || status === 'Reviewed';

export function DocumentTable({
  documents,
  selectedIds,
  onToggleSelect,
  onViewDocument,
  onTriggerProcess,
}: Props) {
  if (documents.length === 0) {
    return <p className="empty-state">No documents match the current filter.</p>;
  }

  return (
    <div className="table-wrap">
      <table className="documents-table">
        <thead>
          <tr>
            <th aria-label="Select for export" />
            <th>File name</th>
            <th>Status</th>
            <th>Created</th>
            <th>Document type</th>
            <th>Warnings</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {documents.map((doc) => {
            const exportable = canExport(doc.status);

            return (
              <tr key={doc.id}>
                <td>
                  <input
                    type="checkbox"
                    checked={selectedIds.has(doc.id)}
                    disabled={!exportable}
                    aria-label={`Select ${doc.originalFileName} for export`}
                    onChange={() => onToggleSelect(doc.id)}
                  />
                </td>
                <td>
                  <div className="file-name">{doc.originalFileName}</div>
                  <div className="muted">{formatBytes(doc.fileSizeBytes)}</div>
                </td>
                <td>
                  <span className={statusClasses[doc.status]}>{doc.status}</span>
                </td>
                <td>{new Date(doc.createdAt).toLocaleString()}</td>
                <td>{doc.documentType}</td>
                <td>
                  <span className={doc.warningCount > 0 ? 'warning-count has-warning' : 'warning-count'}>
                    {doc.warningCount}
                  </span>
                </td>
                <td>
                  <div className="row-actions">
                    <button type="button" onClick={() => onViewDocument(doc.id)}>
                      Review
                    </button>
                    {(doc.status === 'Uploaded' || doc.status === 'Failed') && (
                      <button type="button" onClick={() => onTriggerProcess(doc.id)}>
                        Process
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
