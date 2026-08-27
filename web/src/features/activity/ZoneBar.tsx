import type { ReactNode } from 'react';
import { formatDuration } from '../../lib/format';

const ZONE_LABELS: Record<number, string> = {
  1: 'Recovery',
  2: 'Aerobic',
  3: 'Tempo',
  4: 'Threshold',
  5: 'VO2 max',
};

interface Zone {
  key: string;
  index: number;
  seconds: number;
}

/**
 * Keys arrive as the serialised enum ("Zone1".."Zone5"), but the numeric suffix
 * is the only part that carries the ordering, so sort on that rather than on the
 * string - "Zone10" would otherwise sort before "Zone2".
 */
function orderZones(zoneSeconds: Record<string, number>): Zone[] {
  return Object.entries(zoneSeconds)
    .map(([key, seconds]) => {
      const digits = /(\d+)/.exec(key);
      return {
        key,
        index: digits === null ? Number.MAX_SAFE_INTEGER : Number(digits[1]),
        seconds: Number.isFinite(seconds) ? seconds : 0,
      };
    })
    .sort((left, right) => left.index - right.index);
}

export function ZoneBar({ zoneSeconds }: { zoneSeconds: Record<string, number> }): ReactNode {
  const zones = orderZones(zoneSeconds);
  const total = zones.reduce((sum, zone) => sum + zone.seconds, 0);

  if (total <= 0) {
    return <p className="muted">No heart-rate data was recorded for this activity.</p>;
  }

  return (
    <div className="zones">
      <div className="zones__bar" role="img" aria-label="Time in each heart-rate zone">
        {zones.map((zone) =>
          zone.seconds <= 0 ? null : (
            <div
              key={zone.key}
              className={`zones__segment zones__segment--${String(zone.index)}`}
              style={{ flexGrow: zone.seconds }}
              title={`${ZONE_LABELS[zone.index] ?? zone.key}: ${formatDuration(zone.seconds)}`}
            />
          ),
        )}
      </div>

      <ul className="zones__legend">
        {zones.map((zone) => (
          <li key={zone.key} className="zones__legend-item">
            <span className={`zones__swatch zones__segment--${String(zone.index)}`} aria-hidden="true" />
            <span className="zones__legend-label">
              Z{String(zone.index)} · {ZONE_LABELS[zone.index] ?? ''}
            </span>
            <span className="zones__legend-value">
              {formatDuration(zone.seconds)}
              <span className="zones__legend-share">
                {` ${((zone.seconds / total) * 100).toFixed(0)}%`}
              </span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
