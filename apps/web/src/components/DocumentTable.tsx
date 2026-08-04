import { EmptyState } from './EmptyState';
import {
  AlertTriangleIcon,
  CheckIcon,
  ClockIcon,
  DocumentsIcon,
  FileTypeIcon,
  RefreshIcon,
} from './icons';
import type { DocumentDto, DocumentStatus } from '../types';

interface Props {
  documents: DocumentDto[];
  isLoading: boolean;
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

const statusIcons: Record<DocumentStatus, typeof ClockIcon> = {
  Uploaded: ClockIcon,
  Processing: RefreshIcon,
  Processed: DocumentsIcon,
  Failed: AlertTriangleIcon,
  Reviewed: CheckIcon,
  Exported: CheckIcon,
};

const canExport = (status: DocumentStatus) =>
  status === 'Processed' || status === 'Reviewed';

export function DocumentTable({
  documents,
  isLoading,
  selectedIds,
  onToggleSelect,
  onViewDocument,
  onTriggerProcess,
}: Props) {
  if (isLoading && documents.length === 0) {
    return (
      <div className="table-wrap">
        <table className="documents-table">
          <thead>
            <tr>
              <th aria-label="Select for export" />
              <th>File name</th>
              <th>Client</th>
              <th>Status</th>
              <th>Created</th>
              <th>Document type</th>
              <th>Warnings</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {Array.from({ length: 4 }).map((_, rowIndex) => (
              <tr key={rowIndex}>
                {Array.from({ length: 8 }).map((__, cellIndex) => (
                  <td key={cellIndex}>
                    <div className="skeleton-block" style={{ width: cellIndex === 1 ? '70%' : '80%' }} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  if (documents.length === 0) {
    return (
      <EmptyState
        icon={<DocumentsIcon size={22} />}
        title="No documents match the current filter"
        description="Try a different status or client filter, or upload a new document."
      />
    );
  }

  return (
    <div className="table-wrap">
      <table className="documents-table">
        <thead>
          <tr>
            <th aria-label="Select for export" />
            <th>File name</th>
            <th>Client</th>
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
            const StatusIcon = statusIcons[doc.status];

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
                  <div className="file-name-cell">
                    <FileTypeIcon size={16} className="file-name-cell-icon" />
                    <div>
                      <div className="file-name">{doc.originalFileName}</div>
                      <div className="muted">{formatBytes(doc.fileSizeBytes)}</div>
                    </div>
                  </div>
                </td>
                <td>{doc.clientProfileName ?? '—'}</td>
                <td>
                  <span className={statusClasses[doc.status]}>
                    <StatusIcon size={12} className={doc.status === 'Processing' ? 'status-spin' : undefined} />
                    {doc.status}
                  </span>
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
