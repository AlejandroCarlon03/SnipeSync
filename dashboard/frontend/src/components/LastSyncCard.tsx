import type { RunEntry } from "../api";
import { formatLong, formatUtc, relativeFromNow } from "../format";
import { OUTCOME_LABELS, STEP_LABELS, describeStep } from "../lib/syncText";

function duration(from: string, to: string | null): string | null {
  if (!to) return null;
  const s = Math.max(0, Math.round((Date.parse(to) - Date.parse(from)) / 1000));
  if (s < 60) return `${s}s`;
  const m = Math.round(s / 60);
  return m < 60 ? `${m}m` : `${Math.floor(m / 60)}h ${m % 60}m`;
}

/** The newest sync run and what each of its four steps did. */
export function LastSyncCard({
  run,
  failed,
  now,
}: {
  run: RunEntry | null;
  /** Nothing loaded, so "no runs yet" would be a guess. */
  failed: boolean;
  now: number;
}) {
  const took = run ? duration(run.startedAt, run.finishedAt) : null;

  return (
    <section className="panel" aria-label="Last sync">
      <div className="panel__head">
        <h2>Last sync</h2>
        {run && (
          <p className="panel__sub" title={`${formatUtc(run.startedAt)} UTC`}>
            {formatLong(run.startedAt)} · {relativeFromNow(run.startedAt, now)} ·{" "}
            {run.dryRun ? "dry run" : "live"}
            {took ? ` · took ${took}` : ""}
          </p>
        )}
      </div>

      {!run && (
        <p className="panel__clear">
          {failed ? "Unknown until the run history loads." : "No sync has run yet. Use Sync to start one."}
        </p>
      )}

      {run && run.steps && (
        <>
          <ol className="laststeps">
            {run.steps.map((s) => (
              <li key={s.step} className={`laststep laststep--${s.state}`}>
                <span className="laststep__icon" aria-hidden="true">
                  {s.state === "failed" ? (
                    <svg viewBox="0 0 10 10" width="9" height="9">
                      <path d="M3 3l4 4M7 3 3 7" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
                    </svg>
                  ) : s.state === "done" ? (
                    <svg viewBox="0 0 10 10" width="10" height="10">
                      <path d="M2.2 5.2 4.2 7.2 7.8 3" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  ) : null}
                </span>
                <span className="laststep__text">
                  <span className="laststep__name">{STEP_LABELS[s.step]}</span>
                  <span className="laststep__meta">{describeStep(s, run.outcome === "failed")}</span>
                </span>
              </li>
            ))}
          </ol>
          {run.outcome && run.outcome !== "succeeded" && (
            <p className="panel__note" style={{ paddingBottom: 14 }}>
              <strong>{OUTCOME_LABELS[run.outcome]}</strong>
              {run.error ? `: ${run.error}` : "."}
            </p>
          )}
        </>
      )}

      {run && !run.steps && (
        <p className="panel__note" style={{ paddingBottom: 16 }}>
          {run.changes.length} {run.changes.length === 1 ? "change" : "changes"} ·{" "}
          {run.declined.length} declined. This run is from before run history was kept, so its
          steps weren't recorded.
        </p>
      )}
    </section>
  );
}
