/**
 * Typed client for the SnipeSync host endpoints. Shapes mirror the C# types:
 *  - AuditRecord      -> SnipeSync.Core/Models/AuditRecord.cs   (GET /api/audit)
 *  - AuditStats       -> SnipeSync.Core/Models/AuditStats.cs    (GET /api/audit/stats)
 *  - SyncStatus       -> SnipeSync.Dashboard/SyncRunner.cs      (GET /api/sync/status)
 * Keep these in step with those types.
 */
import { config } from "./config";

/** One audit document, as returned by GET /api/audit. */
export interface AuditRecord {
  id: string;
  ym: string;
  function: string;
  user: string;
  action: string;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
  dryRun: boolean;
  timestampUtc: string;
}

/** One GROUP BY bucket from the stats endpoint. */
export interface CountBucket {
  key: string | null;
  count: number;
}

/** Aggregate response from GET /api/audit/stats. */
export interface AuditStats {
  from: string | null;
  to: string | null;
  function: string | null;
  dryRun: boolean | null;
  total: number;
  byAction: CountBucket[];
  byFunction: CountBucket[];
  byMonth: CountBucket[];
}

export type SyncStepName = "offboard" | "reconcile" | "onboard" | "licenses";
export type SyncStepState = "pending" | "running" | "done" | "skipped" | "failed";
export type SyncOutcome = "running" | "succeeded" | "completedWithErrors" | "failed";

/** The run-summary counters the host reports per step. */
export interface SyncStepCounts {
  processed: number;
  offboarded: number;
  onboarded: number;
  rehired: number;
  assetsCheckedIn: number;
  licensesReclaimed: number;
  licensesCheckedOut: number;
  accessoriesReclaimed: number;
  alreadyCurrent: number;
  failed: number;
  unmatched: string[];
  needsAttention: string[];
}

export interface SyncStep {
  step: SyncStepName;
  state: SyncStepState;
  startedAt: string | null;
  finishedAt: string | null;
  error: string | null;
  counts: SyncStepCounts | null;
}

export interface SyncLogLine {
  at: string;
  level: string;
  message: string;
}

export interface SyncRun {
  runId: string;
  requestedDryRun: boolean;
  dryRun: boolean;
  startedAt: string;
  finishedAt: string | null;
  outcome: SyncOutcome;
  error: string | null;
  steps: SyncStep[];
  log: SyncLogLine[];
}

/** GET /api/sync/status: whether the sync is set up, and the current or last run. */
export interface SyncStatus {
  settingsPath: string;
  missingSettings: string[];
  settingsError: string | null;
  running: boolean;
  run: SyncRun | null;
}

export interface AuditFilters {
  from?: string;
  to?: string;
  action?: string;
  function?: string;
  dryRun?: boolean;
  limit?: number;
}

function buildQuery(params: Record<string, string | undefined>): string {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== "") q.set(k, v);
  }
  // The endpoints are AuthorizationLevel.Function; the key rides along as ?code=.
  // In dev the key is "" (the proxy target is an unauthenticated local host).
  if (config.functionKey) q.set("code", config.functionKey);
  const s = q.toString();
  return s ? `?${s}` : "";
}

async function getJson<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${config.baseUrl}${path}`, init);
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    // The host answers its own refusals as { error } or a problem+json { title, detail }; show
    // that sentence rather than the raw payload.
    let message: string | undefined;
    try {
      const parsed = JSON.parse(body) as { error?: string; detail?: string; title?: string };
      message = parsed.error ?? parsed.detail ?? parsed.title;
    } catch {
      // Not JSON (e.g. a plain-text 400); fall through to the raw body.
    }
    if (message) throw new Error(message);
    throw new Error(`${path} failed: ${res.status} ${res.statusText}${body ? ` — ${body}` : ""}`);
  }
  return (await res.json()) as T;
}

/**
 * Writes carry a custom header. A page on another origin can't add one without a CORS
 * preflight the host never approves, so only this dashboard can start a sync.
 */
function post<T>(path: string, body: string, contentType = "application/json"): Promise<T> {
  return getJson<T>(path, {
    method: "POST",
    headers: { "Content-Type": contentType, "X-SnipeSync": "1" },
    body,
  });
}

const asString = (v?: boolean) => (v === undefined ? undefined : String(v));

export const api = {
  /** Aggregate counts for the summary tiles. Supports the dryRun filter. */
  stats: (f: AuditFilters = {}) =>
    getJson<AuditStats>(
      `/api/audit/stats${buildQuery({
        from: f.from,
        to: f.to,
        function: f.function,
        dryRun: asString(f.dryRun),
      })}`,
    ),

  /**
   * Detail rows for the table. NOTE: GET /api/audit has no dryRun filter (only
   * user/action/function/from/to/limit), so callers filter dryRun client-side.
   */
  records: (f: AuditFilters = {}) =>
    getJson<AuditRecord[]>(
      `/api/audit${buildQuery({
        from: f.from,
        to: f.to,
        action: f.action,
        function: f.function,
        limit: f.limit ? String(f.limit) : undefined,
      })}`,
    ),

  syncStatus: () => getJson<SyncStatus>("/api/sync/status"),

  /** Starts a full sync. Rejects with a 409 message when one is already running. */
  startSync: (dryRun: boolean) => post<SyncRun>("/api/sync", JSON.stringify({ dryRun })),

  /** Replaces the host's settings with the Values from an old Functions local.settings.json. */
  importSettings: (localSettingsJson: string) =>
    post<{ imported: string[]; missingSettings: string[] }>("/api/settings/import", localSettingsJson),
};
