import { useCallback, useEffect, useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api, errorMessage, isAbort, type ActivityListParams } from '../api/client';
import { SPORTS, type ActivitySummaryDto, type PagedDto, type Sport } from '../api/types';
import { EmptyState, ErrorNotice, Spinner, StatusPill } from '../components/Ui';
import {
  formatDateTime,
  formatDistanceKm,
  formatDuration,
  formatElevation,
  formatHeartRate,
  formatPace,
  placeholder,
  sportLabel,
} from '../lib/format';

const PAGE_SIZE = 20;
const POLL_INTERVAL_MS = 4000;

interface Filters {
  sport: Sport | '';
  from: string;
  to: string;
}

const EMPTY_FILTERS: Filters = { sport: '', from: '', to: '' };

/** Local calendar day to an explicit instant, so the server is never left guessing a zone. */
function dayBoundary(day: string, end: boolean): string | undefined {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (parts === null) {
    return undefined;
  }

  const date = new Date(
    Number(parts[1]),
    Number(parts[2]) - 1,
    Number(parts[3]),
    end ? 23 : 0,
    end ? 59 : 0,
    end ? 59 : 0,
  );

  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}

function toParams(filters: Filters, page: number): ActivityListParams {
  const params: ActivityListParams = { page, pageSize: PAGE_SIZE };

  if (filters.sport !== '') {
    params.sport = filters.sport;
  }

  const from = filters.from === '' ? undefined : dayBoundary(filters.from, false);
  if (from !== undefined) {
    params.from = from;
  }

  const to = filters.to === '' ? undefined : dayBoundary(filters.to, true);
  if (to !== undefined) {
    params.to = to;
  }

  return params;
}

export function ActivitiesPage(): ReactNode {
  const [draft, setDraft] = useState<Filters>(EMPTY_FILTERS);
  const [applied, setApplied] = useState<Filters>(EMPTY_FILTERS);
  const [page, setPage] = useState(1);
  const [refreshToken, setRefreshToken] = useState(0);

  const [data, setData] = useState<PagedDto<ActivitySummaryDto> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const [uploadError, setUploadError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);

    api.activities
      .list(toParams(applied, page), controller.signal)
      .then((result) => {
        setData(result);
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
  }, [applied, page, refreshToken]);

  const items = data?.items ?? [];
  const hasProcessing = items.some((item) => item.status === 'Pending' || item.status === 'Processing');

  // Imports are parsed off the request thread, so rows appear before they have
  // metrics. Poll only while at least one row is still waiting on the worker.
  useEffect(() => {
    if (!hasProcessing) {
      return;
    }

    const handle = window.setInterval(() => {
      setRefreshToken((value) => value + 1);
    }, POLL_INTERVAL_MS);

    return () => {
      window.clearInterval(handle);
    };
  }, [hasProcessing]);

  const refresh = useCallback(() => {
    setRefreshToken((value) => value + 1);
  }, []);

  function applyFilters(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    setPage(1);
    setApplied(draft);
  }

  function resetFilters(): void {
    setDraft(EMPTY_FILTERS);
    setApplied(EMPTY_FILTERS);
    setPage(1);
  }

  async function handleUpload(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.target.files?.[0];
    if (file === undefined) {
      return;
    }

    setUploadError(null);
    setUploading(true);

    try {
      await api.activities.upload(file);
      setPage(1);
      refresh();
    } catch (cause) {
      setUploadError(errorMessage(cause));
    } finally {
      setUploading(false);
      if (fileInput.current !== null) {
        fileInput.current.value = '';
      }
    }
  }

  const totalPages = data?.totalPages ?? 0;

  return (
    <div className="page">
      <div className="page__head">
        <div>
          <h1 className="page__title">Activities</h1>
          <p className="page__subtitle">
            {data === null ? placeholder : `${String(data.totalCount)} recorded`}
          </p>
        </div>

        <div className="page__actions">
          <label className={uploading ? 'button button--primary is-disabled' : 'button button--primary'}>
            {uploading ? 'Uploading…' : 'Import file'}
            <input
              ref={fileInput}
              type="file"
              accept=".gpx,.tcx,.fit"
              className="visually-hidden"
              disabled={uploading}
              onChange={(event) => void handleUpload(event)}
            />
          </label>
        </div>
      </div>

      {uploadError !== null ? <ErrorNotice message={uploadError} /> : null}

      <form className="filters" onSubmit={applyFilters}>
        <label className="field field--inline">
          <span className="field__label">Sport</span>
          <select
            className="input"
            value={draft.sport}
            onChange={(event) => {
              setDraft({ ...draft, sport: event.target.value as Sport | '' });
            }}
          >
            <option value="">All sports</option>
            {SPORTS.filter((sport) => sport !== 'Unknown').map((sport) => (
              <option key={sport} value={sport}>
                {sportLabel(sport)}
              </option>
            ))}
          </select>
        </label>

        <label className="field field--inline">
          <span className="field__label">From</span>
          <input
            className="input"
            type="date"
            value={draft.from}
            max={draft.to === '' ? undefined : draft.to}
            onChange={(event) => {
              setDraft({ ...draft, from: event.target.value });
            }}
          />
        </label>

        <label className="field field--inline">
          <span className="field__label">To</span>
          <input
            className="input"
            type="date"
            value={draft.to}
            min={draft.from === '' ? undefined : draft.from}
            onChange={(event) => {
              setDraft({ ...draft, to: event.target.value });
            }}
          />
        </label>

        <div className="filters__buttons">
          <button type="submit" className="button button--primary button--small">
            Apply
          </button>
          <button type="button" className="button button--ghost button--small" onClick={resetFilters}>
            Reset
          </button>
        </div>
      </form>

      {error !== null ? <ErrorNotice message={error} onRetry={refresh} /> : null}

      {loading && data === null ? <Spinner label="Loading activities" /> : null}

      {data !== null && items.length === 0 ? (
        <EmptyState
          title="No activities yet"
          description="Import a GPX, TCX, or FIT file to see it analysed here."
        />
      ) : null}

      {items.length > 0 ? (
        <div className="table-scroll">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">Activity</th>
                <th scope="col">Sport</th>
                <th scope="col" className="num">
                  Distance
                </th>
                <th scope="col" className="num">
                  Moving
                </th>
                <th scope="col" className="num">
                  Pace
                </th>
                <th scope="col" className="num">
                  Elevation
                </th>
                <th scope="col" className="num">
                  Avg HR
                </th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {items.map((activity) => {
                const ready = activity.status === 'Ready';
                return (
                  <tr key={activity.id}>
                    <td>
                      {ready ? (
                        <Link className="link" to={`/activities/${activity.id}`}>
                          {activity.name}
                        </Link>
                      ) : (
                        <span className="table__muted-name">{activity.name}</span>
                      )}
                      <span className="table__sub">{formatDateTime(activity.startedAt)}</span>
                      {activity.error !== null && activity.error !== '' ? (
                        <span className="table__error">{activity.error}</span>
                      ) : null}
                    </td>
                    <td>{sportLabel(activity.sport)}</td>
                    <td className="num">{ready ? formatDistanceKm(activity.distanceMeters) : placeholder}</td>
                    <td className="num">{ready ? formatDuration(activity.movingSeconds) : placeholder}</td>
                    <td className="num">{ready ? formatPace(activity.paceSecondsPerKm) : placeholder}</td>
                    <td className="num">{ready ? formatElevation(activity.elevationGainMeters) : placeholder}</td>
                    <td className="num">{ready ? formatHeartRate(activity.averageHeartRateBpm) : placeholder}</td>
                    <td>
                      <StatusPill status={activity.status} />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : null}

      {totalPages > 1 ? (
        <nav className="pager" aria-label="Pagination">
          <button
            type="button"
            className="button button--ghost button--small"
            disabled={page <= 1}
            onClick={() => {
              setPage((value) => Math.max(1, value - 1));
            }}
          >
            Previous
          </button>
          <span className="pager__label">
            Page {String(page)} of {String(totalPages)}
          </span>
          <button
            type="button"
            className="button button--ghost button--small"
            disabled={page >= totalPages}
            onClick={() => {
              setPage((value) => value + 1);
            }}
          >
            Next
          </button>
        </nav>
      ) : null}
    </div>
  );
}
