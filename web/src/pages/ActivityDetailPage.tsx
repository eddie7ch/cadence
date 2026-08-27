import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, errorMessage, isAbort } from '../api/client';
import type { ActivityDetailDto, TimeSeriesDto } from '../api/types';
import { Card, EmptyState, ErrorNotice, Spinner, Stat, StatusPill } from '../components/Ui';
import { DistanceChart, hasChannel, useSeriesPoints } from '../features/activity/SeriesCharts';
import { RouteMap } from '../features/activity/RouteMap';
import { SplitsTable } from '../features/activity/SplitsTable';
import { ZoneBar } from '../features/activity/ZoneBar';
import {
  formatDateTime,
  formatDistanceKm,
  formatDuration,
  formatElevation,
  formatHeartRate,
  formatPace,
  sportLabel,
} from '../lib/format';

// A long ride can carry tens of thousands of samples; a 220px-tall chart can
// resolve a few hundred. Cap the request rather than the render.
const MAX_SERIES_POINTS = 1200;
const POLL_INTERVAL_MS = 4000;

export function ActivityDetailPage(): ReactNode {
  const { id } = useParams<{ id: string }>();

  const [detail, setDetail] = useState<ActivityDetailDto | null>(null);
  const [series, setSeries] = useState<TimeSeriesDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshToken, setRefreshToken] = useState(0);

  const refresh = useCallback(() => {
    setRefreshToken((value) => value + 1);
  }, []);

  useEffect(() => {
    if (id === undefined) {
      return;
    }

    const controller = new AbortController();
    setLoading(true);

    api.activities
      .detail(id, controller.signal)
      .then(async (result) => {
        setDetail(result);
        setError(null);

        if (result.summary.status !== 'Ready') {
          setSeries(null);
          return;
        }

        // A missing series is not a broken page - the map, splits, and zones all
        // still render - so its failure is swallowed rather than escalated.
        try {
          setSeries(await api.activities.series(id, MAX_SERIES_POINTS, controller.signal));
        } catch (cause) {
          if (!isAbort(cause)) {
            setSeries(null);
          }
        }
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
  }, [id, refreshToken]);

  const status = detail?.summary.status;
  const isProcessing = status === 'Pending' || status === 'Processing';

  useEffect(() => {
    if (!isProcessing) {
      return;
    }

    const handle = window.setInterval(refresh, POLL_INTERVAL_MS);
    return () => {
      window.clearInterval(handle);
    };
  }, [isProcessing, refresh]);

  const points = useSeriesPoints(series);

  if (id === undefined) {
    return <EmptyState title="Unknown activity" />;
  }

  if (loading && detail === null) {
    return <Spinner label="Loading activity" />;
  }

  if (error !== null && detail === null) {
    return (
      <div className="page">
        <ErrorNotice message={error} onRetry={refresh} />
        <Link className="link" to="/activities">
          Back to activities
        </Link>
      </div>
    );
  }

  if (detail === null) {
    return <EmptyState title="Activity not found" />;
  }

  const { summary } = detail;

  return (
    <div className="page">
      <div className="page__head">
        <div>
          <Link className="breadcrumb" to="/activities">
            ← Activities
          </Link>
          <h1 className="page__title">{summary.name}</h1>
          <p className="page__subtitle">
            {sportLabel(summary.sport)} · {formatDateTime(summary.startedAt)}
          </p>
        </div>
        <StatusPill status={summary.status} />
      </div>

      {summary.status === 'Failed' ? (
        <ErrorNotice
          message={
            summary.error !== null && summary.error !== ''
              ? summary.error
              : 'This file could not be processed.'
          }
        />
      ) : null}

      {isProcessing ? (
        <EmptyState
          title="Still processing"
          description="This file is being parsed and analysed. The page refreshes itself when it is ready."
        />
      ) : null}

      {summary.status === 'Ready' ? (
        <>
          <div className="stats">
            <Stat label="Distance" value={formatDistanceKm(summary.distanceMeters)} />
            <Stat
              label="Moving time"
              value={formatDuration(summary.movingSeconds)}
              hint={`${formatDuration(summary.elapsedSeconds)} elapsed`}
            />
            <Stat
              label="Pace"
              value={formatPace(summary.paceSecondsPerKm)}
              hint={`${formatPace(summary.gradeAdjustedPaceSecondsPerKm)} grade-adjusted`}
            />
            <Stat label="Elevation gain" value={formatElevation(summary.elevationGainMeters)} />
            <Stat
              label="Average heart rate"
              value={formatHeartRate(summary.averageHeartRateBpm)}
              hint={`${detail.sampleCount.toLocaleString()} samples${
                detail.discardedSampleCount > 0
                  ? `, ${detail.discardedSampleCount.toLocaleString()} discarded`
                  : ''
              }`}
            />
          </div>

          <Card title="Route">
            {detail.route !== null ? (
              <RouteMap route={detail.route} />
            ) : (
              <p className="muted">This activity has no route data.</p>
            )}
          </Card>

          <div className="grid grid--split">
            <Card title="Heart-rate zones">
              <ZoneBar zoneSeconds={detail.heartRateZoneSeconds} />
            </Card>

            <Card title="Profiles">
              {points.length === 0 ? (
                <p className="muted">No time series is available for this activity.</p>
              ) : (
                <>
                  {hasChannel(points, 'altitude') ? (
                    <>
                      <h3 className="chart__title">Elevation</h3>
                      <DistanceChart
                        points={points}
                        channel="altitude"
                        colour="#7aa2f7"
                        unit="m"
                        name="Elevation"
                      />
                    </>
                  ) : null}

                  {hasChannel(points, 'heartRate') ? (
                    <>
                      <h3 className="chart__title">Heart rate</h3>
                      <DistanceChart
                        points={points}
                        channel="heartRate"
                        colour="#f7768e"
                        unit="bpm"
                        name="Heart rate"
                      />
                    </>
                  ) : null}

                  {!hasChannel(points, 'altitude') && !hasChannel(points, 'heartRate') ? (
                    <p className="muted">This device recorded neither altitude nor heart rate.</p>
                  ) : null}
                </>
              )}
            </Card>
          </div>

          <Card title="Splits">
            <SplitsTable splits={detail.splits} />
          </Card>
        </>
      ) : null}
    </div>
  );
}
