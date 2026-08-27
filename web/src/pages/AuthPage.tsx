import { useState, type FormEvent, type ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { apiBaseUrl, errorMessage, type RegisterPayload } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { ErrorNotice } from '../components/Ui';

type Mode = 'signin' | 'register';

interface LocationState {
  from?: string;
}

function parseOptionalBpm(raw: string): number | undefined {
  const trimmed = raw.trim();
  if (trimmed === '') {
    return undefined;
  }

  const value = Number(trimmed);
  return Number.isFinite(value) && value > 0 ? Math.round(value) : undefined;
}

export function AuthPage(): ReactNode {
  const { isAuthenticated, signIn, register } = useAuth();
  const location = useLocation();
  const state = location.state as LocationState | null;

  const [mode, setMode] = useState<Mode>('signin');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [maxHeartRate, setMaxHeartRate] = useState('');
  const [restingHeartRate, setRestingHeartRate] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (isAuthenticated) {
    return <Navigate to={state?.from ?? '/activities'} replace />;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      if (mode === 'signin') {
        await signIn({ email: email.trim(), password });
      } else {
        const payload: RegisterPayload = {
          email: email.trim(),
          password,
          displayName: displayName.trim(),
        };

        const max = parseOptionalBpm(maxHeartRate);
        if (max !== undefined) {
          payload.maxHeartRate = max;
        }

        const resting = parseOptionalBpm(restingHeartRate);
        if (resting !== undefined) {
          payload.restingHeartRate = resting;
        }

        await register(payload);
      }
    } catch (cause) {
      setError(errorMessage(cause));
    } finally {
      setBusy(false);
    }
  }

  function switchMode(next: Mode): void {
    setMode(next);
    setError(null);
  }

  return (
    <div className="auth">
      <div className="auth__panel">
        <div className="brand brand--large">
          <span className="brand__mark" aria-hidden="true" />
          <span className="brand__name">Cadence</span>
        </div>
        <p className="auth__tagline">Spatial and time-series analysis of everything you record.</p>

        <div className="segmented" role="tablist" aria-label="Authentication mode">
          <button
            type="button"
            role="tab"
            aria-selected={mode === 'signin'}
            className={mode === 'signin' ? 'segmented__item segmented__item--active' : 'segmented__item'}
            onClick={() => {
              switchMode('signin');
            }}
          >
            Sign in
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={mode === 'register'}
            className={mode === 'register' ? 'segmented__item segmented__item--active' : 'segmented__item'}
            onClick={() => {
              switchMode('register');
            }}
          >
            Create account
          </button>
        </div>

        <form className="form" onSubmit={(event) => void handleSubmit(event)}>
          {mode === 'register' ? (
            <label className="field">
              <span className="field__label">Display name</span>
              <input
                className="input"
                type="text"
                required
                autoComplete="name"
                value={displayName}
                onChange={(event) => {
                  setDisplayName(event.target.value);
                }}
              />
            </label>
          ) : null}

          <label className="field">
            <span className="field__label">Email</span>
            <input
              className="input"
              type="email"
              required
              autoComplete="email"
              value={email}
              onChange={(event) => {
                setEmail(event.target.value);
              }}
            />
          </label>

          <label className="field">
            <span className="field__label">Password</span>
            <input
              className="input"
              type="password"
              required
              minLength={8}
              autoComplete={mode === 'signin' ? 'current-password' : 'new-password'}
              value={password}
              onChange={(event) => {
                setPassword(event.target.value);
              }}
            />
          </label>

          {mode === 'register' ? (
            <div className="field-row">
              <label className="field">
                <span className="field__label">Max heart rate (optional)</span>
                <input
                  className="input"
                  type="number"
                  min={100}
                  max={240}
                  value={maxHeartRate}
                  onChange={(event) => {
                    setMaxHeartRate(event.target.value);
                  }}
                />
              </label>
              <label className="field">
                <span className="field__label">Resting heart rate (optional)</span>
                <input
                  className="input"
                  type="number"
                  min={30}
                  max={120}
                  value={restingHeartRate}
                  onChange={(event) => {
                    setRestingHeartRate(event.target.value);
                  }}
                />
              </label>
            </div>
          ) : null}

          {error !== null ? <ErrorNotice message={error} /> : null}

          <button type="submit" className="button button--primary" disabled={busy}>
            {busy ? 'Working…' : mode === 'signin' ? 'Sign in' : 'Create account'}
          </button>
        </form>

        <p className="auth__api">API: {apiBaseUrl}</p>
      </div>
    </div>
  );
}
