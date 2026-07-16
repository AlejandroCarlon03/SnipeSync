/** Types mirror the Flask API in backend/. Keep them in step with models.py. */

export type SyncFunctionName = "OnboardEmployeeSync" | "FormerEmployeeSync";
export type ActionName = "user_created" | "title_changed" | "skipped" | "failed";
export type StatusName = "success" | "failure" | "skipped";
export type HealthState = "good" | "warning" | "critical" | "unknown";

export interface Metric {
  value: number;
  previous: number;
}

export interface SyncRun {
  id: string;
  function: SyncFunctionName;
  started_at: string;
  finished_at: string;
  dry_run: boolean;
  users_scanned: number;
  created: number;
  title_changed: number;
  skipped: number;
  failed: number;
  status: StatusName;
  duration_seconds: number;
}

export interface Summary {
  window_days: number;
  is_sample_data: boolean;
  users_created: Metric;
  titles_changed: Metric;
  failures: Metric;
  sync_runs: Metric;
  users_scanned: number;
  last_run: SyncRun | null;
  health: { state: HealthState; label: string };
}

export interface ActivityPoint {
  date: string;
  users_created: number;
  titles_changed: number;
  failures: number;
}

export interface SyncEvent {
  id: string;
  run_id: string;
  timestamp: string;
  function: SyncFunctionName;
  action: ActionName;
  status: StatusName;
  display_name: string;
  email: string;
  detail: string;
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) {
    throw new Error(`${path} failed: ${res.status} ${res.statusText}`);
  }
  return (await res.json()) as T;
}

export const api = {
  summary: (days: number) => get<Summary>(`/api/summary?days=${days}`),
  activity: (days: number) => get<ActivityPoint[]>(`/api/activity?days=${days}`),
  runs: (limit = 20) => get<SyncRun[]>(`/api/runs?limit=${limit}`),
  events: (opts: { limit?: number; action?: string; search?: string } = {}) => {
    const params = new URLSearchParams();
    params.set("limit", String(opts.limit ?? 100));
    if (opts.action && opts.action !== "all") params.set("action", opts.action);
    if (opts.search) params.set("search", opts.search);
    return get<SyncEvent[]>(`/api/events?${params}`);
  },
};
