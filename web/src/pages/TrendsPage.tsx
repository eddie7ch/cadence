import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api, errorMessage, isAbort } from '../api/client';
import type { TrendsDto } from '../api/types';
import { Card, EmptyState, ErrorNotice, Spinner, Stat } from '../components/Ui';
import { formatDistanceKm, formatDuration, formatElevation, formatPercent, formatShortDate } from '../lib/format';

const WINDOWS = [8, 12, 26, 52] as const;

type Metric = 'distance' | 'elevation' | 'time';

interface MetricConfig {
  label: string;
  unit: string;
  colour: string;
  decimals: number;
}

const METRICS: Record<Metric, MetricConfig> = {
  distance: { label: 'Distance', unit: 'km', colour: '#4fd1c5', decimals: 1 },
  elevation: { label: 'Elevation', unit: 'm', colour: '#7aa2f7', decimals: 0 },
  time: { label: 'Moving time', unit: 'h', colour: '#e0af68', decimals: 1 },
};

interface WeekPoint {
  week: string;
  value: number;
  activities: number;
}

export function TrendsPage(): ReactNode {
  const [weeks, setWeeks] = useState<number>(12);
  const [metric, setMetric] = useState<Metric>('distance');
  const [trends, setTrends] = useState<TrendsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshToken, setRefreshToken] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);

    api.trends
      .get(weeks, controller.signal)
      .then((result) => {
        setTrends(result);
        setError(null);
      })
      .catch((cause: unknown) => {
        if (!isAbort(cause)) {
          setError(errorMessage(cause));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => {
      controller.abort();
    };
  }, [weeks, refreshToken]);

  const points = useMemo<WeekPoint[]>(() => {
    if (trends === null) {
      return [];
    }

    return trends.weeks.map((week) => ({
      week: formatShortDate(week.weekStart),
      value:
        metric === 'distance'
          ? week.distanceMeters / 1000
          : metric === 'elevation'
            ? week.elevationGainMeters
            : week.movingSeconds / 3600,
      activities: week.activityCount,
    }));
  }, [trends, metric]);

  const config = METRICS[metric];

  return (
    <div className="page">
      <div className="page__head">
        <div>
          <h1 className="page__title">Trends</h1>
          <p className="page__subtitle">Weekly totals over the selected window.</p>
        </div>

        <div className="segmented segmented--small">
          {WINDOWS.map((option) => (
            <button
              key={option}
              type="button"
              className={option === weeks ? 'segmented__item segmented__item--active' : 'segmented__item'}
              onClick={() => {
                setWeeks(option);
              }}
            >
              {option}w
            </button>
          ))}
        </div>
      </div>

      {error !== null ? (
        <ErrorNotice
          message={error}
          onRetry={() => {
            setRefreshToken((value) => value + 1);
          }}
        />
      ) : null}

      {loading && trends === null ? <Spinner label="Loading trends" /> : null}

      {trends !== null ? (
        <>
          <div className="stats">
            <Stat label="Distance" value={formatDistanceKm(trends.totalDistanceMeters, 1)} />
            <Stat label="Elevation gain" value={formatElevation(trends.totalElevationGainMeters)} />
            <Stat label="Moving time" value={formatDuration(trends.totalMovingSeconds)} />
            <Stat label="Activities" value={String(trends.totalActivities)} />
            <Stat
              label="Distance trend"
              value={formatPercent(trends.distanceTrendPercent)}
              hint="second half vs first"
            />
          </div>

          <Card
            title={`${config.label} by week`}
            actions={
              <div className="segmented segmented--small">
                {(Object.keys(METRICS) as Metric[]).map((option) => (
                  <button
                    key={option}
                    type="button"
                    className={
                      option === metric ? 'segmented__item segmented__item--active' : 'segmented__item'
                    }
                    onClick={() => {
                      setMetric(option);
                    }}
                  >
                    {METRICS[option].label}
                  </button>
                ))}
              </div>
            }
          >
            {points.length === 0 ? (
              <EmptyState
                title="Nothing recorded yet"
                description="Import a few activities and weekly totals will appear here."
              />
            ) : (
              <div className="chart">
                <ResponsiveContainer width="100%" height={300}>
                  <BarChart data={points} margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
                    <CartesianGrid stroke="#1e2a36" strokeDasharray="3 3" vertical={false} />
                    <XAxis
                      dataKey="week"
                      tick={{ fill: '#7d8b9c', fontSize: 11 }}
                      tickLine={false}
                      axisLine={{ stroke: '#1e2a36' }}
                      minTickGap={16}
                    />
                    <YAxis
                      width={52}
                      tick={{ fill: '#7d8b9c', fontSize: 11 }}
                      tickLine={false}
                      axisLine={false}
                      tickFormatter={(value) => Number(value).toFixed(config.decimals)}
                    />
                    <Tooltip
                      cursor={{ fill: 'rgba(255,255,255,0.04)' }}
                      contentStyle={{
                        background: '#111b24',
                        border: '1px solid #22303d',
                        borderRadius: 8,
                        color: '#e6edf3',
                        fontSize: 12,
                      }}
                      formatter={(value) => [
                        `${Number(value).toFixed(config.decimals)} ${config.unit}`,
                        config.label,
                      ]}
                    />
                    <Bar dataKey="value" fill={config.colour} radius={[4, 4, 0, 0]} isAnimationActive={false} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            )}
          </Card>
        </>
      ) : null}
    </div>
  );
}
