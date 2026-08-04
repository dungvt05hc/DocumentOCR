import type { ReviewWarningDto } from '../../types';
import { AlertCircleIcon, AlertTriangleIcon, ChevronRightIcon, InfoIcon } from '../icons';

interface Props {
  warnings: ReviewWarningDto[];
  onWarningClick: (warning: ReviewWarningDto) => void;
}

const severityIcons: Record<string, typeof InfoIcon> = {
  info: InfoIcon,
  warning: AlertTriangleIcon,
  high: AlertTriangleIcon,
  error: AlertCircleIcon,
};

export function ReviewWarningsBar({ warnings, onWarningClick }: Props) {
  return (
    <div className="warnings">
      {warnings.map((warning, index) => {
        const severity = warning.severity.toLowerCase();
        const Icon = severityIcons[severity] ?? InfoIcon;
        return (
          <button
            key={index}
            type="button"
            className={`warning ${severity}${warning.fieldKey ? ' is-clickable' : ''}`}
            onClick={() => warning.fieldKey && onWarningClick(warning)}
            disabled={!warning.fieldKey}
          >
            <Icon size={15} className="warning-icon" />
            <span>
              <strong>{warning.severity}</strong>
              {warning.fieldKey ? ` · ${warning.fieldKey}` : ''}: {warning.message}
            </span>
            {warning.fieldKey && <ChevronRightIcon size={14} className="warning-icon" />}
          </button>
        );
      })}
    </div>
  );
}
