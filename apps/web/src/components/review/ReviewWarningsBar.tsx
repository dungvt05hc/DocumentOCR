import type { ReviewWarningDto } from '../../types';

interface Props {
  warnings: ReviewWarningDto[];
  onWarningClick: (warning: ReviewWarningDto) => void;
}

export function ReviewWarningsBar({ warnings, onWarningClick }: Props) {
  return (
    <div className="warnings">
      {warnings.map((warning, index) => (
        <button
          key={index}
          type="button"
          className={`warning ${warning.severity.toLowerCase()}${warning.fieldKey ? ' is-clickable' : ''}`}
          onClick={() => warning.fieldKey && onWarningClick(warning)}
          disabled={!warning.fieldKey}
        >
          <strong>{warning.severity}</strong>
          {warning.fieldKey ? ` · ${warning.fieldKey}` : ''}: {warning.message}
        </button>
      ))}
    </div>
  );
}
