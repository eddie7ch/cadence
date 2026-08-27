import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { ApiError, api, errorMessage, isAbort } from '../api/client';
import type { CoachingReportDto, TrainingLoadVerdict } from '../api/types';
import { Card, EmptyState, ErrorNotice, Spinner } from '../components/Ui';
import { formatDate, formatDateTime } from '../lib/format';

const VERDICT_TONE: Record<TrainingLoadVerdict, string> = {
  Unknown: 'verdict--unknown',
  Detraining: 'verdict--detraining',
  Maintaining: 'verdict--maintaining',
  Productive: 'verdict--productive',
  Overreaching: 'verdict--overreaching',
};

const VERDICT_BLURB: Record<TrainingLoadVerdict, string> = {
  Unknown: 'Not enough training history to judge the block.',
  Detraining: 'Load has fallen far enough that fitness is being lost.',
  Maintaining: 'Load is holding fitness steady rather than building it.',
  Productive: 'Load is building fitness at a rate the body can absorb.',
  Overreaching: 'Load is climbing faster than recovery is keeping up.',
};

function severityClass(severity: string): string {
  const normalised = severity.trim().toLowerCase();
  if (normalised === 'high' || normalised === 'critical' || normalised === 'severe') {
    return 'finding finding--high';
  }

  if (normalised === 'medium' || normalised === 'moderate' || normalised === 'warning') {
    return 'finding finding--medium';
  }

  return 'finding finding--low';
}

export function CoachingPage(): ReactNode {
  const [report, setReport] = useState<CoachingReportDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // A 503 means the advisor was never configured. That is a deployment fact, not
  // a failure of this request, so it gets an explanation instead of an error.
  const [unconfigured, setUnconfigured] = useState(false);

  useEffect(() => {
    const controller = new AbortController();

    api.coaching
      .latest(controller.signal)
      .then((result) => {
        setReport(result);
        setError(null);
      })
      .catch((cause: unknown) => {
        if (isAbort(cause)) {
          return;
        }

        if (cause instanceof ApiError && cause.isUnavailable) {
          setUnconfigured(true);
          return;
        }

        setError(errorMessage(cause));
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => {
      controller.abort();
    };
  }, []);

  const generate = useCallback(async () => {
    setGenerating(true);
    setError(null);

    try {
      const result = await api.coaching.generate();
      setReport(result);
      setUnconfigured(false);
    } catch (cause) {
      if (cause instanceof ApiError && cause.isUnavailable) {
        setUnconfigured(true);
      } else {
        setError(errorMessage(cause));
      }
    } finally {
      setGenerating(false);
    }
  }, []);

  return (
    <div className="page">
      <div className="page__head">
        <div>
          <h1 className="page__title">Coaching</h1>
          <p className="page__subtitle">A structured assessment of your recent training block.</p>
        </div>

        {unconfigured ? null : (
          <button
            type="button"
            className="button button--primary"
            disabled={generating}
            onClick={() => void generate()}
          >
            {generating ? 'Analysing…' : report === null ? 'Generate report' : 'Regenerate'}
          </button>
        )}
      </div>

      {error !== null ? <ErrorNotice message={error} /> : null}

      {loading ? <Spinner label="Loading report" /> : null}

      {unconfigured ? (
        <EmptyState
          title="Coaching is not configured"
          description="This deployment has no advisor API key set, so reports cannot be generated. Set Anthropic__ApiKey on the API and coaching will appear here - everything else works without it."
        />
      ) : null}

      {!loading && !unconfigured && report === null ? (
        <EmptyState
          title="No report yet"
          description="Generate one to see a summary, a training-load verdict, and the findings behind it."
        />
      ) : null}

      {report !== null && !unconfigured ? (
        <>
          <Card>
            <div className="report__head">
              <div>
                <span className={`verdict ${VERDICT_TONE[report.verdict] ?? 'verdict--unknown'}`}>
                  {report.verdict}
                </span>
                <p className="report__blurb">{VERDICT_BLURB[report.verdict] ?? ''}</p>
              </div>
              <dl className="report__meta">
                <div>
                  <dt>Period</dt>
                  <dd>
                    {formatDate(report.periodStart)} – {formatDate(report.periodEnd)}
                  </dd>
                </div>
                <div>
                  <dt>Activities</dt>
                  <dd>{report.activityCount}</dd>
                </div>
                <div>
                  <dt>Model</dt>
                  <dd>{report.modelId}</dd>
                </div>
                <div>
                  <dt>Generated</dt>
                  <dd>{formatDateTime(report.generatedAt)}</dd>
                </div>
              </dl>
            </div>

            <p className="report__summary">{report.summary}</p>
          </Card>

          <Card title={`Findings (${String(report.findings.length)})`}>
            {report.findings.length === 0 ? (
              <p className="muted">The advisor returned no specific findings for this period.</p>
            ) : (
              <ul className="findings">
                {report.findings.map((finding, index) => (
                  <li key={`${finding.title}-${String(index)}`} className={severityClass(finding.severity)}>
                    <div className="finding__head">
                      <h3 className="finding__title">{finding.title}</h3>
                      <span className="finding__severity">{finding.severity}</span>
                    </div>
                    <p className="finding__detail">{finding.detail}</p>
                    <span className="finding__metric">{finding.metric}</span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </>
      ) : null}
    </div>
  );
}
