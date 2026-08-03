import type { ReviewLineItem } from '../../types';

interface Props {
  lineItems: ReviewLineItem[];
  lineItemEdits: Record<number, Partial<ReviewLineItem>>;
  onLineItemChange: (lineNumber: number, patch: Partial<ReviewLineItem>) => void;
}

export function ReviewLineItemsTab({ lineItems, lineItemEdits, onLineItemChange }: Props) {
  return (
    <>
      <p className="muted">Simple candidates derived from detected tables — not guaranteed to be complete or correct.</p>
      <table className="editable-table">
        <thead>
          <tr>
            <th>Line #</th>
            <th>Description</th>
            <th>Quantity</th>
            <th>Unit</th>
            <th>Unit price</th>
            <th>Amount</th>
            <th>Currency</th>
            <th>Confidence</th>
          </tr>
        </thead>
        <tbody>
          {lineItems.map((item) => {
            const edits = lineItemEdits[item.lineNumber] ?? {};
            const isLowConfidence = (item.confidence ?? 1) < 0.6;
            return (
              <tr key={item.lineNumber} className={isLowConfidence ? 'is-experimental' : undefined}>
                <td>{item.lineNumber}</td>
                <td>
                  <input
                    type="text"
                    value={edits.description ?? item.description ?? ''}
                    onChange={(event) => onLineItemChange(item.lineNumber, { description: event.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    value={(edits.quantity ?? item.quantity) ?? ''}
                    onChange={(event) =>
                      onLineItemChange(item.lineNumber, { quantity: event.target.value === '' ? null : Number(event.target.value) })
                    }
                  />
                </td>
                <td>
                  <input
                    type="text"
                    value={edits.unit ?? item.unit ?? ''}
                    onChange={(event) => onLineItemChange(item.lineNumber, { unit: event.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    value={(edits.unitPrice ?? item.unitPrice) ?? ''}
                    onChange={(event) =>
                      onLineItemChange(item.lineNumber, { unitPrice: event.target.value === '' ? null : Number(event.target.value) })
                    }
                  />
                </td>
                <td>
                  <input
                    type="text"
                    value={(edits.amount ?? item.amount) ?? ''}
                    onChange={(event) =>
                      onLineItemChange(item.lineNumber, { amount: event.target.value === '' ? null : Number(event.target.value) })
                    }
                  />
                </td>
                <td>
                  <input
                    type="text"
                    value={edits.currency ?? item.currency ?? ''}
                    onChange={(event) => onLineItemChange(item.lineNumber, { currency: event.target.value })}
                  />
                </td>
                <td>
                  {item.confidence !== null ? `${Math.round(item.confidence * 100)}%` : '—'}
                  {isLowConfidence ? ' · experimental' : ''}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </>
  );
}
