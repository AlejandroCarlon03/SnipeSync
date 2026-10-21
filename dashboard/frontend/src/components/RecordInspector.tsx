import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { ACTION_INFO } from "../actions";
import type { AuditRecord } from "../api";
import { formatUtc, relativeFromNow } from "../format";
import { actionColor, isUnmatched, routeFor, type Route, type Stop } from "../lines";

interface RecordInspectorProps {
  record: AuditRecord;
  /** Every loaded row, to list the rest of this person's trail. */
  records: AuditRecord[];
  onSelect: (id: string) => void;
  onClose: () => void;
  now: number;
}

/**
 * The detail view for one audit row: the route the person travelled on the sync's line,
 * where it stopped and why, every stored field, and the rest of that person's records.
 */
export function RecordInspector({ record: r, records, onSelect, onClose, now }: RecordInspectorProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const info = ACTION_INFO.find((a) => a.action === r.action);
  const unmatched = isUnmatched(r.action);

  // Move focus into the panel when a different record opens, so keyboard users land on it.
  useEffect(() => {
    headingRef.current?.focus({ preventScroll: true });
  }, [r.id]);

  const trail = useMemo(
    () =>
      records
        .filter((x) => x.user === r.user)
        .sort((a, b) => b.timestampUtc.localeCompare(a.timestampUtc)),
    [records, r.user],
  );
  const route = routeFor(r.action, r.function, trail);

  return (
    <section
      className="inspector"
      aria-labelledby="inspector-title"
      style={{ "--tone": route.line.color } as CSSProperties}
    >
      <header className="inspector__head">
        <div className="inspector__titles">
          <span
            className={`action ${unmatched ? "action--anomaly" : ""}`}
            style={{ "--tone": actionColor(r.action, r.function) } as CSSProperties}
          >
            {r.action}
          </span>
          <h2 id="inspector-title" ref={headingRef} tabIndex={-1}>
            {r.user}
          </h2>
          <p className="inspector__when" title={`${formatUtc(r.timestampUtc)} UTC`}>
            {relativeFromNow(r.timestampUtc, now)} · {r.dryRun ? "dry run" : "live run"}
          </p>
        </div>
        <button className="icon-btn" onClick={onClose} aria-label="Close details" title="Close (Esc)">
          <svg viewBox="0 0 14 14" width="13" height="13" aria-hidden="true">
            <path d="M3 3l8 8M11 3l-8 8" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
          </svg>
        </button>
      </header>

      <RouteDiagram key={r.id} route={route} dryRun={r.dryRun} onSelect={onSelect} />

      {info && (
        <div className={`verdict ${unmatched ? "verdict--anomaly" : ""}`}>
          <p className="verdict__lead">{info.description}</p>
          {unmatched && (
            <p className="verdict__body">
              The sync had no Snipe-IT user to act on, so nothing was reclaimed. A name that
              matches more than one Snipe-IT user is recorded the same way: the sync never
              guesses between them.
            </p>
          )}
        </div>
      )}

      {(r.oldValue || r.newValue) && (
        <div className="change" aria-label="Change">
          <div className="change__side">
            <span className="field-label">Old value</span>
            <span className={r.oldValue ? "change__old" : "muted"}>{r.oldValue || "—"}</span>
          </div>
          <svg className="change__arrow" viewBox="0 0 20 10" width="20" height="10" aria-hidden="true">
            <path d="M1 5h17M14 1.5 18 5l-4 3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          <div className="change__side">
            <span className="field-label">New value</span>
            <span className={r.newValue ? "" : "muted"}>{r.newValue || "—"}</span>
          </div>
        </div>
      )}

      <dl className="facts">
        <Fact label="Detail" value={r.detail} wide />
        <Fact label="Time (UTC)" value={formatUtc(r.timestampUtc)} data />
        <Fact label="Run" value={r.dryRun ? "dry-run" : "live"} />
        <Fact label="Function" value={r.function} />
        <Fact label="Emitted by" value={info?.source} />
        {!(r.oldValue || r.newValue) && (
          <>
            <Fact label="Old value" value={r.oldValue} />
            <Fact label="New value" value={r.newValue} />
          </>
        )}
        <Fact label="Month partition" value={r.ym} data />
        <Fact label="Record id" value={r.id} data wide />
      </dl>

      <div className="trail">
        <h3 className="trail__title">
          Everything for {r.user}
          <span className="muted"> · {trail.length} of the loaded rows</span>
        </h3>
        <ol className="trail__list">
          {trail.map((t) => (
            <li key={t.id}>
              <button
                className={`trail__item ${t.id === r.id ? "trail__item--on" : ""}`}
                onClick={() => onSelect(t.id)}
                aria-current={t.id === r.id || undefined}
                style={{ "--tone": actionColor(t.action, t.function) } as CSSProperties}
              >
                <span className="trail__dot" aria-hidden="true" />
                <span className="trail__action">{t.action}</span>
                <span className="trail__meta" title={`${formatUtc(t.timestampUtc)} UTC`}>
                  {t.detail ?? t.newValue ?? ""}
                </span>
                <span className="trail__time">{relativeFromNow(t.timestampUtc, now)}</span>
              </button>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}

function Fact({
  label,
  value,
  data,
  wide,
}: {
  label: string;
  value: string | null | undefined;
  data?: boolean;
  wide?: boolean;
}) {
  return (
    <div className={`fact ${wide ? "fact--wide" : ""}`}>
      <dt className="field-label">{label}</dt>
      <dd className={`${value ? "" : "muted"} ${data && value ? "fact--data" : ""}`}>{value || "—"}</dd>
    </div>
  );
}

const STOP_SR: Record<Stop["state"], string> = {
  passed: " (passed)",
  here: " (this record)",
  stopped: " (stopped here)",
  later: " (reached, recorded separately)",
  "not-reached": " (no loaded record shows this)",
  transfer: " (moved to the Reconcile line)",
};

/**
 * The line this record sits on, drawn as a route strip: stops passed, the stop this action
 * records, and the next stop, solid when another loaded record proves it was reached (and
 * linked to that record), dashed when nothing loaded shows it. Drawn in once per record.
 */
function RouteDiagram({
  route,
  dryRun,
  onSelect,
}: {
  route: Route;
  dryRun: boolean;
  onSelect: (id: string) => void;
}) {
  const [drawn, setDrawn] = useState(false);
  useEffect(() => {
    const id = requestAnimationFrame(() => setDrawn(true));
    return () => cancelAnimationFrame(id);
  }, []);

  const reachedIndex = route.stops.findIndex((s) => s.state === "here" || s.state === "stopped");

  return (
    <figure
      className={`route ${drawn ? "route--drawn" : ""} ${dryRun ? "route--dry" : ""}`}
      aria-label={`${route.line.name} line`}
    >
      <figcaption className="route__line-name">
        <span className="route__roundel" aria-hidden="true" />
        {route.line.name} line
        {dryRun && <span className="chip chip--dry">dry run · nothing changed</span>}
      </figcaption>
      <ol className="route__stops" style={{ "--count": route.stops.length } as CSSProperties}>
        {route.stops.map((s, i) => (
          <li
            key={s.label}
            className={`route__stop route__stop--${s.state}`}
            style={{ "--i": i, ...(s.color ? { "--stop-tone": s.color } : {}) } as CSSProperties}
          >
            <span className="route__seg" aria-hidden="true" data-reached={i <= reachedIndex || undefined} />
            <span className="route__node" aria-hidden="true">
              {s.state === "stopped" && (
                <svg viewBox="0 0 10 10" width="8" height="8">
                  <path d="M2.5 2.5l5 5M7.5 2.5l-5 5" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
                </svg>
              )}
            </span>
            {s.recordId ? (
              <button className="route__label route__label--link" onClick={() => onSelect(s.recordId!)}>
                {s.label}
                <span className="sr-only">{STOP_SR[s.state]}, open that record</span>
              </button>
            ) : (
              <span className="route__label">
                {s.label}
                <span className="sr-only">{STOP_SR[s.state]}</span>
              </span>
            )}
            {s.state === "transfer" && <span className="route__note">Reconcile line</span>}
          </li>
        ))}
      </ol>
    </figure>
  );
}
