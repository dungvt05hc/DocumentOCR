import { useState } from 'react';
import { exportToExcel } from '../services/api';

interface Props {
  selectedIds: Set<string>;
  onClearSelection: () => void;
}

export function ExportPanel({ selectedIds, onClearSelection }: Props) {
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleExport = async () => {
    if (selectedIds.size === 0) return;
    setExporting(true);
    setError(null);
    try {
      const res = await exportToExcel({ documentIds: Array.from(selectedIds) });
      const url = URL.createObjectURL(new Blob([res.data]));
      const a = window.document.createElement('a');
      a.href = url;
      a.download = `DocumentOCR_Export_${new Date().toISOString().slice(0, 10)}.xlsx`;
      a.click();
      URL.revokeObjectURL(url);
      onClearSelection();
    } catch {
      setError('Export failed. Please try again.');
    } finally {
      setExporting(false);
    }
  };

  return (
    <div
      style={{
        padding: '12px 16px',
        background: '#f0f4f8',
        borderRadius: 6,
        display: 'flex',
        alignItems: 'center',
        gap: 16,
      }}
    >
      <span style={{ fontSize: '0.9rem' }}>
        {selectedIds.size} document{selectedIds.size !== 1 ? 's' : ''} selected
      </span>
      <button
        onClick={handleExport}
        disabled={exporting || selectedIds.size === 0}
        style={{
          padding: '6px 18px',
          background: selectedIds.size > 0 ? '#27ae60' : '#ccc',
          color: 'white',
          border: 'none',
          borderRadius: 4,
          cursor: selectedIds.size > 0 ? 'pointer' : 'default',
        }}
      >
        {exporting ? 'Exporting…' : 'Export to Excel'}
      </button>
      {selectedIds.size > 0 && (
        <button
          onClick={onClearSelection}
          style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#888' }}
        >
          Clear
        </button>
      )}
      {error && <span style={{ color: 'red', fontSize: '0.85rem' }}>{error}</span>}
    </div>
  );
}
