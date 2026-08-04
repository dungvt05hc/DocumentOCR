import type { ReactNode } from 'react';
import { AlertCircleIcon, AlertTriangleIcon, CheckCircleIcon, InfoIcon } from './icons';

type AlertVariant = 'error' | 'success' | 'warning' | 'info';

interface Props {
  variant: AlertVariant;
  children: ReactNode;
}

const icons: Record<AlertVariant, typeof InfoIcon> = {
  error: AlertCircleIcon,
  success: CheckCircleIcon,
  warning: AlertTriangleIcon,
  info: InfoIcon,
};

export function Alert({ variant, children }: Props) {
  const Icon = icons[variant];
  return (
    <div className={`alert alert-${variant}`} role={variant === 'error' ? 'alert' : 'status'}>
      <Icon size={17} className="alert-icon" />
      <span>{children}</span>
    </div>
  );
}
