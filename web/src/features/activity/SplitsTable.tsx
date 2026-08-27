import type { ReactNode } from 'react';
import type { SplitDto } from '../../api/types';
import {
  formatDistanceKm,
  formatDuration,
  formatElevation,
  formatHeartRate,
  formatPace,
  formatPaceDelta,
  placeholder,
} from '../../lib/format';

/**
 * A grade-adjusted pace slower than the raw pace means the split was downhill -
 * the effort was easier than the clock suggests - and faster means it was uphill.
 * Showing the two columns next to the signed difference is the whole point of
 * the table, so the delta is coloured rather than left as another number.
 */
function deltaTone(seconds: number): string {
  if (Math.abs(seconds) < 2) {
    return 'delta';
  }

  return seconds > 0 ? 'delta delta--easier' : 'delta delta--harder';
}

export function SplitsTable({ splits }: { splits: SplitDto[] }): ReactNode {
  if (splits.length === 0) {
    return <p className="muted">No splits were derived for this activity.</p>;
  }

  return (
    <div className="table-scroll">
      <table className="table table--compact">
        <thead>
          <tr>
            <th scope="col">Split</th>
            <th scope="col" className="num">
              Distance
            </th>
            <th scope="col" className="num">
              Time
            </th>
            <th scope="col" className="num">
              Pace
            </th>
            <th scope="col" className="num">
              Grade-adjusted
            </th>
            <th scope="col" className="num">
              Δ
            </th>
            <th scope="col" className="num">
              Elev
            </th>
            <th scope="col" className="num">
              HR
            </th>
          </tr>
        </thead>
        <tbody>
          {splits.map((split) => {
            const delta = split.gradeAdjustedPaceSecondsPerKm - split.paceSecondsPerKm;
            const comparable =
              Number.isFinite(delta) && split.paceSecondsPerKm > 0 && split.gradeAdjustedPaceSecondsPerKm > 0;

            return (
              <tr key={split.number} className={split.isComplete ? undefined : 'row--partial'}>
                <th scope="row">
                  {split.number}
                  {split.isComplete ? null : <span className="table__sub">partial</span>}
                </th>
                <td className="num">{formatDistanceKm(split.distanceMeters)}</td>
                <td className="num">{formatDuration(split.durationSeconds)}</td>
                <td className="num">{formatPace(split.paceSecondsPerKm)}</td>
                <td className="num">{formatPace(split.gradeAdjustedPaceSecondsPerKm)}</td>
                <td className="num">
                  {comparable ? <span className={deltaTone(delta)}>{formatPaceDelta(delta)}</span> : placeholder}
                </td>
                <td className="num">{formatElevation(split.elevationGainMeters)}</td>
                <td className="num">{formatHeartRate(split.averageHeartRateBpm)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
