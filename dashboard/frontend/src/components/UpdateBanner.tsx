import { useCallback, useEffect, useRef, useState } from "react";
import { api, type UpdateStatus } from "../api";

/** The host caches its GitHub answer for hours; this only picks that answer up in a long-open window. */
const CHECK_MS = 30 * 60_000;
/** While the installer runs, how often to see whether the new version is answering yet. */
const RESTART_POLL_MS = 2_000;
/** Past this the relaunch has probably failed (or was blocked); say how to reopen by hand. */
const RESTART_GIVE_UP_MS = 3 * 60_000;

/** Remembers "Later" per version, so the next release still gets announced. */
const DISMISS_KEY = "snipesync.update.dismissed";

function dismissedVersion(): string | null {
  try {
    return localStorage.getItem(DISMISS_KEY);
  } catch {
    return null;
  }
}

/**
 * "A new version is out" strip above the filters. Update downloads the release's installer and runs
 * it silently; the host exits, the installer replaces it and starts it again, and this page reloads
 * itself once the new version answers.
 */
export function UpdateBanner() {
  const [status, setStatus] = useState<UpdateStatus | null>(null);
  const [dismissed, setDismissed] = useState(dismissedVersion);
  const [phase, setPhase] = useState<"idle" | "starting" | "restarting" | "stuck">("idle");
  const [error, setError] = useState<string | null>(null);
  const fromVersion = useRef<string | null>(null);

  const check = useCallback(async () => {
    try {
      setStatus(await api.updateStatus());
    } catch {
      // An update check is never worth an error on the dashboard.
    }
  }, []);

  useEffect(() => {
    void check();
    const id = window.setInterval(() => void check(), CHECK_MS);
    return () => window.clearInterval(id);
  }, [check]);

  // Wait for the host to come back as the new version, then load its SPA.
  useEffect(() => {
    if (phase !== "restarting") return;
    const startedAt = Date.now();
    const id = window.setInterval(async () => {
      if (Date.now() - startedAt > RESTART_GIVE_UP_MS) {
        setPhase("stuck");
        return;
      }
      try {
        const s = await api.updateStatus();
        if (s.current !== fromVersion.current) window.location.reload();
      } catch {
        // Expected while the old host is gone and the new one isn't up yet.
      }
    }, RESTART_POLL_MS);
    return () => window.clearInterval(id);
  }, [phase]);

  const install = async () => {
    if (!status) return;
    setPhase("starting");
    setError(null);
    fromVersion.current = status.current;
    try {
      await api.installUpdate();
      setPhase("restarting");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setPhase("idle");
    }
  };

  const later = () => {
    if (!status?.latest) return;
    try {
      localStorage.setItem(DISMISS_KEY, status.latest);
    } catch {
      // Storage off: hide it for this session only.
    }
    setDismissed(status.latest);
  };

  if (!status?.available || !status.latest) return null;
  if (phase === "idle" && dismissed === status.latest) return null;

  return (
    <div className="update" role="status">
      <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true">
        <path
          d="M8 2v8M8 10 5 7M8 10l3-3M2.5 13.5h11"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.5"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>

      <p className="update__text">
        {phase === "restarting" ? (
          <>
            <strong>Installing SnipeSync {status.latest}…</strong> This page reloads by itself when
            it's done.
          </>
        ) : phase === "stuck" ? (
          <>
            <strong>SnipeSync {status.latest} should be installed.</strong> If this page doesn't come
            back, open SnipeSync from the Start menu.
          </>
        ) : (
          <>
            <strong>SnipeSync {status.latest} is available.</strong> You have {status.current}.{" "}
            {status.releaseUrl && (
              <a href={status.releaseUrl} target="_blank" rel="noreferrer">
                What's new
              </a>
            )}
          </>
        )}
        {error && <span className="update__error"> {error}</span>}
      </p>

      {(phase === "idle" || phase === "starting") && (
        <>
          <button className="btn btn--ghost" onClick={later} disabled={phase === "starting"}>
            Later
          </button>
          <button className="btn btn--primary" onClick={() => void install()} disabled={phase === "starting"}>
            {phase === "starting" ? "Downloading…" : "Update now"}
          </button>
        </>
      )}
    </div>
  );
}
