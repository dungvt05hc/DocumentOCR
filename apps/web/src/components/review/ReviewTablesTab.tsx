import type { ReviewTable } from '../../types';
import { tableCellEditKey } from './tableEditKey';

interface Props {
  tables: ReviewTable[];
  tableEdits: Record<string, Record<string, string>>;
  onCellChange: (tableId: string, rowIndex: number, columnKey: string | null, text: string) => void;
}

export function ReviewTablesTab({ tables, tableEdits, onCellChange }: Props) {
  return (
    <>
      {tables.map((table, tableIndex) => (
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
                          const editKey = tableCellEditKey(row.rowIndex, column.columnKey);
                          const value = tableEdits[table.tableId]?.[editKey] ?? cell?.text ?? '';
                          return (
                            <td key={column.columnKey}>
                              <input
                                type="text"
                                value={value}
                                onChange={(event) =>
                                  onCellChange(table.tableId, row.rowIndex, column.columnKey, event.target.value)
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
    </>
  );
}
