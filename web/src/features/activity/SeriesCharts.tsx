import { useMemo, type ReactNode } from 'react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import type { TimeSeriesDto } from '../../api/types';

export interface SeriesPoint {
  distanceKm: number;
  altitude: number | null;
  heartRate: number | null;
}

/**
 * The series arrives column-oriented (one array per channel); Recharts wants one
 * object per sample, so the transpose happens once here rather than per render
 * of each chart.
 */
export function useSeriesPoints(series: TimeSeriesDto | null): SeriesPoint[] {
  return useMemo(() => {
    if (series === null) {
      return [];
    }

    const count = Math.min(series.distanceMeters.length, series.elapsedSeconds.length);
    const points: SeriesPoint[] = [];

    for (let index = 0; index < count; index += 1) {
      const distance = series.distanceMeters[index];
      if (distance === undefined || !Number.isFinite(distance)) {
        continue;
      }

      points.push({
        distanceKm: distance / 1000,
        altitude: series.altitudeMeters[index] ?? null,
        heartRate: series.heartRateBpm[index] ?? null,
      });
    }

    return points;
  }, [series]);
}

export function hasChannel(points: SeriesPoint[], channel: 'altitude' | 'heartRate'): boolean {
  return points.some((point) => point[channel] !== null);
}

const AXIS_STYLE = { fill: '#7d8b9c', fontSize: 11 } as const;

export function DistanceChart({
  points,
  channel,
  colour,
  unit,
  name,
}: {
  points: SeriesPoint[];
  channel: 'altitude' | 'heartRate';
  colour: string;
  unit: string;
  name: string;
}): ReactNode {
  return (
    <div className="chart">
      <ResponsiveContainer width="100%" height={220}>
        <LineChart data={points} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
          <CartesianGrid stroke="#1e2a36" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="distanceKm"
            type="number"
            domain={[0, 'dataMax']}
            tick={AXIS_STYLE}
            tickLine={false}
            axisLine={{ stroke: '#1e2a36' }}
            tickFormatter={(value) => `${Number(value).toFixed(1)}`}
            unit=" km"
            minTickGap={28}
          />
          <YAxis
            width={52}
            tick={AXIS_STYLE}
            tickLine={false}
            axisLine={false}
            domain={['dataMin - 5', 'dataMax + 5']}
            tickFormatter={(value) => `${Math.round(Number(value))}`}
          />
          <Tooltip
            contentStyle={{
              background: '#111b24',
              border: '1px solid #22303d',
              borderRadius: 8,
              color: '#e6edf3',
              fontSize: 12,
            }}
            labelFormatter={(label) => `${Number(label).toFixed(2)} km`}
            formatter={(value) => [`${Math.round(Number(value))} ${unit}`, name]}
          />
          <Line
            type="monotone"
            dataKey={channel}
            name={name}
            stroke={colour}
            strokeWidth={2}
            dot={false}
            activeDot={{ r: 3 }}
            connectNulls={false}
            isAnimationActive={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
