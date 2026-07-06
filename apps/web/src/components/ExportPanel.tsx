import { useState } from 'react';
import { exportToExcel } from '../services/api';
import type { DocumentDto } from '../types';

interface Props {
  documents: DocumentDto[];
  selectedIds: Set<string>;
  onClearSelection: () => void;
}

export function ExportPanel({ documents, selectedIds, onClearSelection }: Props) {
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
    <section className="panel" id="export">
      <div className="section-heading">
        <h2>Export Excel</h2>
        <span className="muted">{exportableDocuments.length} processed or reviewed</span>
      </div>

      <div className="export-row">
        <span>
          {selectedExportableIds.length} selected for export
        </span>
        <button
          type="button"
          className="primary"
          onClick={handleExport}
          disabled={exporting || selectedExportableIds.length === 0}
        >
          {exporting ? 'Exporting...' : 'Download Excel'}
        </button>
        <button type="button" onClick={onClearSelection} disabled={selectedIds.size === 0}>
          Clear selection
        </button>
      </div>

      {error && <p className="message error">{error}</p>}
    </section>
  );
}
