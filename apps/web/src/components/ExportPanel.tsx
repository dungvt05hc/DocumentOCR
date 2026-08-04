import { useState } from 'react';
import { exportToExcel } from '../services/api';
import { Alert } from './Alert';
import { DownloadIcon } from './icons';
import type { DocumentDto } from '../types';

interface Props {
  documents: DocumentDto[];
  selectedIds: Set<string>;
  onClearSelection: () => void;
}

export function ExportPanel({ documents, selectedIds, onClearSelection }: Props) {
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (selectedIds.size === 0) return null;

  const exportableDocuments = documents.filter(
    (doc) => doc.status === 'Processed' || doc.status === 'Reviewed'
  );
  const selectedExportableIds = Array.from(selectedIds).filter((id) =>
    exportableDocuments.some((doc) => doc.id === id)
  );

  const handleExport = async () => {
    if (selectedExportableIds.length === 0) return;

    setExporting(true);
    setError(null);

    try {
      const response = await exportToExcel({ documentIds: selectedExportableIds });
      const url = URL.createObjectURL(new Blob([response.data]));
      const anchor = window.document.createElement('a');
      anchor.href = url;
      anchor.download = `DocumentOCR_Export_${new Date().toISOString().slice(0, 10)}.xlsx`;
      anchor.click();
      URL.revokeObjectURL(url);
      onClearSelection();
    } catch {
      setError('Export failed. Please try again.');
    } finally {
      setExporting(false);
    }
  };

  return (
    <div className="export-bar">
      <div className="export-row">
        <span className="badge badge-primary">{selectedExportableIds.length} selected for export</span>
        <button
          type="button"
          className="btn-primary"
          onClick={handleExport}
          disabled={exporting || selectedExportableIds.length === 0}
        >
          <DownloadIcon size={16} />
          {exporting ? 'Exporting...' : 'Download Excel'}
        </button>
        <button type="button" onClick={onClearSelection}>
          Clear selection
        </button>
      </div>

      {error && <Alert variant="error">{error}</Alert>}
    </div>
  );
}
