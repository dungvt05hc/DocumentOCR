export interface TaxBreakdownRowState {
  key: string;
  id: string | null;
  vatRate: string;
  taxableAmount: string;
  taxAmount: string;
  confidence: number | null;
}

interface Props {
  rows: TaxBreakdownRowState[];
  onRowChange: (key: string, patch: Partial<TaxBreakdownRowState>) => void;
  onAddRow: () => void;
  onRemoveRow: (key: string) => void;
}

const VAT_RATE_OPTIONS = ['0%', '5%', '8%', '10%', 'KCT', 'KKKNT'];

export function TaxBreakdownTable({ rows, onRowChange, onAddRow, onRemoveRow }: Props) {
  return (
    <div className="review-section" data-testid="tax-breakdown">
      <h3>Bảng thuế suất</h3>
      <table className="editable-table tax-breakdown-table">
        <thead>
          <tr>
            <th>Thuế suất</th>
            <th>Tiền chưa thuế</th>
            <th>Tiền thuế</th>
            <th aria-label="Xoá dòng" />
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key}>
              <td>
                <select value={row.vatRate} onChange={(e) => onRowChange(row.key, { vatRate: e.target.value })}>
                  <option value="">—</option>
                  {VAT_RATE_OPTIONS.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              </td>
              <td>
                <input
                  type="number"
                  value={row.taxableAmount}
                  onChange={(e) => onRowChange(row.key, { taxableAmount: e.target.value })}
                />
              </td>
              <td>
                <input
                  type="number"
                  value={row.taxAmount}
                  onChange={(e) => onRowChange(row.key, { taxAmount: e.target.value })}
                />
              </td>
              <td>
                <button type="button" className="btn-icon" onClick={() => onRemoveRow(row.key)} aria-label="Xoá dòng thuế suất">
                  ×
                </button>
              </td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={4} className="muted">
                Chưa đọc được bảng thuế suất
              </td>
            </tr>
          )}
        </tbody>
      </table>
      <button type="button" className="btn-ghost" onClick={onAddRow}>
        + Thêm dòng thuế suất
      </button>
    </div>
  );
}
