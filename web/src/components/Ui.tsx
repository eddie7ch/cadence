import type { ReactNode } from 'react';
import type { ActivityStatus } from '../api/types';

export function Spinner({ label = 'Loading' }: { label?: string }): ReactNode {
  return (
    <div className="spinner" role="status" aria-live="polite">
      <span className="spinner__dot" />
      <span className="spinner__label">{label}</span>
    </div>
  );
}

export function ErrorNotice({
  message,
  onRetry,
}: {
  message: string;
  onRetry?: () => void;
}): ReactNode {
  return (
    <div className="notice notice--error" role="alert">
      <p className="notice__text">{message}</p>
      {onRetry !== undefined ? (
        <button type="button" className="button button--ghost" onClick={onRetry}>
          Try again
        </button>
      ) : null}
    </div>
  );
}

export function EmptyState({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children?: ReactNode;
}): ReactNode {
  return (
    <div className="empty">
      <h2 className="empty__title">{title}</h2>
      {description !== undefined ? <p className="empty__text">{description}</p> : null}
      {children}
    </div>
  );
}

export function Card({
  title,
  actions,
  children,
}: {
  title?: string;
  actions?: ReactNode;
  children: ReactNode;
}): ReactNode {
  return (
    <section className="card">
      {title !== undefined || actions !== undefined ? (
        <header className="card__header">
          {title !== undefined ? <h2 className="card__title">{title}</h2> : <span />}
          {actions}
        </header>
      ) : null}
      <div className="card__body">{children}</div>
    </section>
  );
}

export function Stat({
  label,
  value,
  hint,
}: {
  label: string;
  value: string;
  hint?: string;
}): ReactNode {
  return (
    <div className="stat">
      <span className="stat__label">{label}</span>
      <span className="stat__value">{value}</span>
      {hint !== undefined ? <span className="stat__hint">{hint}</span> : null}
    </div>
  );
}

const STATUS_TONE: Record<ActivityStatus, string> = {
  Pending: 'pill--pending',
  Processing: 'pill--processing',
  Ready: 'pill--ready',
  Failed: 'pill--failed',
};

export function StatusPill({ status }: { status: ActivityStatus }): ReactNode {
  const tone = STATUS_TONE[status] ?? 'pill--pending';
  const isBusy = status === 'Pending' || status === 'Processing';
  return (
    <span className={`pill ${tone}`}>
      {isBusy ? <span className="pill__pulse" aria-hidden="true" /> : null}
      {status}
    </span>
  );
}
