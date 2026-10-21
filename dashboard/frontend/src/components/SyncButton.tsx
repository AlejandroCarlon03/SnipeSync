import { useCallback, useEffect, useRef, useState } from "react";
import { api, type SyncRun, type SyncStatus, type SyncStep, type SyncStepCounts } from "../api";
import { relativeFromNow } from "../format";

/** Fast enough that the step list visibly advances; a run takes minutes, not seconds. */
const POLL_MS = 2_000;

const STEP_LABELS: Record<SyncStep["step"], string> = {
  offboard: "Offboard former employees",
  reconcile: "Retry unmatched users",
  onboard: "Onboard new hires and rehires",
  licenses: "Check out M365 license seats",
};

const OUTCOME_LABELS: Record<SyncRun["outcome"], string> = {
  running: "Running",
  succeeded: "Finished",
  completedWithErrors: "Finished with errors",
  failed: "Did not run",
};

/** "2 offboarded · 3 assets in" — only the counters that moved, so a quiet step reads "no changes". */
function describeCounts(c: SyncStepCounts | null): string {
  if (!c) return "";
  const parts: [number, string, string][] = [
    [c.offboarded, "offboarded", "offboarded"],
    [c.onboarded, "created", "created"],
    [c.rehired, "rehired", "rehired"],
    [c.assetsCheckedIn, "asset in", "assets in"],
    [c.licensesReclaimed, "seat reclaimed", "seats reclaimed"],
    [c.accessoriesReclaimed, "accessory in", "accessories in"],
    [c.licensesCheckedOut, "seat checked out", "seats checked out"],
    [c.unmatched.length, "unmatched", "unmatched"],
    [c.failed, "failed", "failed"],
  ];
  const moved = parts
    .filter(([n]) => n > 0)
    .map(([n, one, many]) => `${n} ${n === 1 ? one : many}`);
  const checked = c.processed > 0 ? `${c.processed} checked` : "";
  return [checked, ...(moved.length ? moved : ["no changes"])].filter(Boolean).join(" · ");
}

/**
 * Manual sync: a topbar button that opens a panel to start a full sync (optionally as a dry
 * run), then follows it step by step. Replaces the Azure timers; the nightly scheduled task,
 * when installed, runs the same thing headless.
 */
export function SyncButton({ onFinished }: { onFinished: () => void }) {
  const [open, setOpen] = useState(false);
  const [status, setStatus] = useState<SyncStatus | null>(null);
  const [dryRun, setDryRun] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showLog, setShowLog] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const wasRunning = useRef(false);

  const refresh = useCallback(async () => {
    try {
      const s = await api.syncStatus();
      setStatus(s);
      // Reload the audit data once, as a run finishes, so its new rows appear.
      if (wasRunning.current && !s.running) onFinished();
      wasRunning.current = s.running;
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [onFinished]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const running = status?.running ?? false;

  useEffect(() => {
    if (!running) return;
    const id = window.setInterval(() => void refresh(), POLL_MS);
    return () => window.clearInterval(id);
  }, [running, refresh]);

  // Close on Escape or a click outside — the same contract as the action reference.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    const onClick = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("mousedown", onClick);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("mousedown", onClick);
    };
  }, [open]);

  const start = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.startSync(dryRun);
      wasRunning.current = true;
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const importSettings = async (file: File) => {
    setBusy(true);
    setError(null);
    try {
      await api.importSettings(await file.text());
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  };

  const run = status?.run ?? null;
  const notConfigured = Boolean(status && (status.missingSettings.length > 0 || status.settingsError));
  const doneSteps = run?.steps.filter((s) => s.state !== "pending" && s.state !== "running").length ?? 0;

  return (
    <div className="sync" ref={rootRef}>
      <button
        className="btn sync__btn"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-haspopup="dialog"
      >
        <svg
          className={running ? "spin" : undefined}
          viewBox="0 0 16 16"
          width="14"
          height="14"
          aria-hidden="true"
        >
          <path
            d="M13.2 6.2A5.4 5.4 0 0 0 3.3 5M2.8 9.8a5.4 5.4 0 0 0 9.9 1.2M3 2.4v2.8h2.8M13 13.6v-2.8h-2.8"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.6"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
        {running ? `Syncing ${Math.min(doneSteps + 1, 4)}/4` : "Sync"}
      </button>

      {open && (
        <div className="sync__panel" role="dialog" aria-label="Sync with Snipe-IT">
          <div className="help__head">
            <h3>Sync Entra ID → Snipe-IT</h3>
            <button className="help__close" onClick={() => setOpen(false)} aria-label="Close">
              ×
            </button>
          </div>

          {notConfigured && status ? (
            <div className="sync__setup">
              <p>
                {status.settingsError ??
                  `Not set up yet. Missing: ${status.missingSettings.join(", ")}.`}
              </p>
              <p className="muted">
                Import the <code>local.settings.json</code> from the old Functions project, or edit{" "}
                <code>{status.settingsPath}</code> by hand.
              </p>
              <input
                ref={fileRef}
                type="file"
                accept=".json,application/json"
                hidden
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) void importSettings(f);
                }}
              />
              <button className="btn" disabled={busy} onClick={() => fileRef.current?.click()}>
                Import settings file…
              </button>
            </div>
          ) : (
            <>
              <ol className="sync__steps">
                {(run?.steps ?? emptySteps()).map((s) => (
                  <li key={s.step} className={`sync__step sync__step--${run ? s.state : "pending"}`}>
                    <span className="sync__dot" aria-hidden="true" />
                    <div className="sync__step-main">
                      <span className="sync__step-label">{STEP_LABELS[s.step]}</span>
                      {run && (s.error || s.counts || (s.state === "skipped" && run.outcome !== "failed")) && (
                        <span className="sync__step-meta">
                          {s.error ?? (s.state === "skipped" ? "skipped (turned off in settings)" : describeCounts(s.counts))}
                        </span>
                      )}
                    </div>
                  </li>
                ))}
              </ol>

              {run && (
                <p className={`sync__outcome sync__outcome--${run.outcome}`}>
                  {OUTCOME_LABELS[run.outcome]}
                  {run.dryRun ? (run.outcome === "running" ? " (dry run)" : " (dry run, nothing changed)") : ""}
                  {run.finishedAt ? ` · ${relativeFromNow(run.finishedAt)}` : ""}
                  {run.error && run.outcome === "failed" ? `: ${run.error}` : ""}
                </p>
              )}

              {run && run.log.length > 0 && (
                <>
                  <button className="sync__log-toggle" onClick={() => setShowLog((v) => !v)}>
                    {showLog ? "Hide log" : "Show log"}
                  </button>
                  {showLog && (
                    <pre className="sync__log">
                      {run.log
                        .slice(-80)
                        .map((l) => `${l.at.slice(11, 19)} ${l.level === "Information" ? "" : `${l.level}: `}${l.message}`)
                        .join("\n")}
                    </pre>
                  )}
                </>
              )}

              {!running && (
                <div className="sync__actions">
                  <label className="toggle" title="Runs every step and records what it would do, without changing Snipe-IT">
                    <input type="checkbox" checked={dryRun} onChange={(e) => setDryRun(e.target.checked)} />
                    Dry run (no changes)
                  </label>
                  <button className="btn" disabled={busy} onClick={() => void start()}>
                    {dryRun ? "Start dry run" : "Sync now"}
                  </button>
                </div>
              )}
            </>
          )}

          {error && <p className="sync__error">{error}</p>}
        </div>
      )}
    </div>
  );
}

function emptySteps(): SyncStep[] {
  return (Object.keys(STEP_LABELS) as SyncStep["step"][]).map((step) => ({
    step,
    state: "pending",
    startedAt: null,
    finishedAt: null,
    error: null,
    counts: null,
  }));
}
