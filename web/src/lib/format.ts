import type { Sport } from '../api/types';

const EM_DASH = '–';

function isUsable(value: number | null | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

export function formatDistanceKm(meters: number, fractionDigits = 2): string {
  if (!Number.isFinite(meters)) {
    return EM_DASH;
  }

  return `${(meters / 1000).toFixed(fractionDigits)} km`;
}

export function formatElevation(meters: number): string {
  if (!Number.isFinite(meters)) {
    return EM_DASH;
  }

  return `${Math.round(meters).toLocaleString()} m`;
}

/** h:mm:ss above an hour, m:ss below it. */
export function formatDuration(seconds: number | null | undefined): string {
  if (!isUsable(seconds)) {
    return EM_DASH;
  }

  const total = Math.round(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const secs = total % 60;
  const paddedSeconds = String(secs).padStart(2, '0');

  if (hours > 0) {
    return `${String(hours)}:${String(minutes).padStart(2, '0')}:${paddedSeconds}`;
  }

  return `${String(minutes)}:${paddedSeconds}`;
}

export function formatPace(secondsPerKm: number | null | undefined): string {
  if (!isUsable(secondsPerKm)) {
    return EM_DASH;
  }

  return `${formatDuration(secondsPerKm)} /km`;
}

/** Signed pace difference, e.g. "+0:12" when the adjusted pace is slower. */
export function formatPaceDelta(secondsPerKm: number): string {
  if (!Number.isFinite(secondsPerKm) || Math.abs(secondsPerKm) < 0.5) {
    return '0:00';
  }

  const sign = secondsPerKm > 0 ? '+' : '-';
  return `${sign}${formatDuration(Math.abs(secondsPerKm))}`;
}

export function formatHeartRate(bpm: number | null | undefined): string {
  return isUsable(bpm) ? `${String(Math.round(bpm))} bpm` : EM_DASH;
}

export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return EM_DASH;
  }

  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function formatDate(iso: string): string {
  // DateOnly arrives as yyyy-MM-dd; parsing that as UTC and rendering it local
  // would shift it a day west of Greenwich, so build the date from its parts.
  const parts = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  const date =
    parts !== null
      ? new Date(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]))
      : new Date(iso);

  if (Number.isNaN(date.getTime())) {
    return EM_DASH;
  }

  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

export function formatShortDate(iso: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  const date =
    parts !== null
      ? new Date(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]))
      : new Date(iso);

  if (Number.isNaN(date.getTime())) {
    return EM_DASH;
  }

  return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

export function formatPercent(value: number): string {
  if (!Number.isFinite(value)) {
    return EM_DASH;
  }

  const sign = value > 0 ? '+' : '';
  return `${sign}${value.toFixed(1)}%`;
}

const SPORT_LABELS: Record<Sport, string> = {
  Unknown: 'Unknown',
  Running: 'Running',
  TrailRunning: 'Trail running',
  Cycling: 'Cycling',
  MountainBiking: 'Mountain biking',
  Swimming: 'Swimming',
  Walking: 'Walking',
  Hiking: 'Hiking',
  Rowing: 'Rowing',
  Skiing: 'Skiing',
};

export function sportLabel(sport: Sport | string): string {
  return SPORT_LABELS[sport as Sport] ?? String(sport);
}

export const placeholder = EM_DASH;
