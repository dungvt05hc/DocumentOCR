const SEPARATOR = '|';

export function tableCellEditKey(rowIndex: number, columnKey: string | null): string {
  return `${rowIndex}${SEPARATOR}${columnKey ?? ''}`;
}

export function parseTableCellEditKey(key: string): { rowIndex: number; columnKey: string | null } {
  const [rowIndexRaw, columnKey] = key.split(SEPARATOR);
  return { rowIndex: Number(rowIndexRaw), columnKey: columnKey || null };
}
