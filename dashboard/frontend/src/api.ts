/**
 * Typed client for the SnipeSync audit endpoints. Shapes mirror the C# records:
 *  - AuditRecord      -> Models/AuditRecord.cs        (GET /api/audit)
 *  - AuditStats       -> Functions/AuditStatsFunction  (GET /api/audit/stats)
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

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${config.baseUrl}${path}`);
  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(`${path} failed: ${res.status} ${res.statusText}${body ? ` — ${body}` : ""}`);
  }
  return (await res.json()) as T;
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
};
