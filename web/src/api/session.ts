import type { AthleteDto } from './types';

const TOKEN_KEY = 'cadence.accessToken';
const ATHLETE_KEY = 'cadence.athlete';

export interface Session {
  accessToken: string;
  athlete: AthleteDto;
}

type Listener = () => void;

const listeners = new Set<Listener>();

/**
 * localStorage throws in private-browsing modes and when site data is blocked,
 * so every access is guarded; a browser that cannot persist the token still
 * works for the lifetime of the tab.
 */
function readItem(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeItem(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Non-fatal: the in-memory copy below keeps the session alive for this tab.
  }
}

function removeItem(key: string): void {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // Non-fatal.
  }
}

function parseAthlete(raw: string | null): AthleteDto | null {
  if (raw === null) {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null) {
      return null;
    }

    const candidate = parsed as Partial<AthleteDto>;
    if (typeof candidate.id !== 'string' || typeof candidate.email !== 'string') {
      return null;
    }

    return candidate as AthleteDto;
  } catch {
    return null;
  }
}

let current: Session | null = (() => {
  const token = readItem(TOKEN_KEY);
  const athlete = parseAthlete(readItem(ATHLETE_KEY));
  return token !== null && token !== '' && athlete !== null ? { accessToken: token, athlete } : null;
})();

export function getSession(): Session | null {
  return current;
}

export function getAccessToken(): string | null {
  return current?.accessToken ?? null;
}

export function setSession(session: Session): void {
  current = session;
  writeItem(TOKEN_KEY, session.accessToken);
  writeItem(ATHLETE_KEY, JSON.stringify(session.athlete));
  notify();
}

export function clearSession(): void {
  if (current === null) {
    return;
  }

  current = null;
  removeItem(TOKEN_KEY);
  removeItem(ATHLETE_KEY);
  notify();
}

export function subscribe(listener: Listener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function notify(): void {
  for (const listener of listeners) {
    listener();
  }
}
