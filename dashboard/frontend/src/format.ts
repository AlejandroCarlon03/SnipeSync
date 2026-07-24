/**
 * Timestamp helpers shared by the table, the worklist and the Excel export.
 * Adapted from EntraSecurityWatcher.Dashboard/ClientApp/src/format.ts.
 */

/**
 * Parse an audit timestamp as UTC. Cosmos usually hands back a trailing "Z",
 * but a bare "yyyy-MM-ddTHH:mm:ss" would be read as *local* time by Date, which
 * would silently shift every value — so pin the offset explicitly.
 */
export function toUtcDate(ts: string): Date | null {
  const normalized = /(?:Z|[+-]\d{2}:?\d{2})$/.test(ts) ? ts : `${ts}Z`;
  const d = new Date(normalized);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** "2026-07-23T19:45:00.123Z" -> "2026-07-23 19:45:00". */
export function formatUtc(ts: string): string {
  return ts.replace("T", " ").slice(0, 19);
}

/**
 * "42s ago" / "5m ago" / "3h ago" / "2d ago". Falsy input reads as "never".
 * `now` is injectable so a caller ticking a clock in state gets a value that
 * actually changes when the clock does.
 */
export function relativeFromNow(
  value: string | Date | null | undefined,
  now: number = Date.now(),
): string {
  if (!value) return "never";
  const d = value instanceof Date ? value : toUtcDate(value);
  if (!d) return "never";

  const seconds = Math.max(0, Math.round((now - d.getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

/** Initials for the avatar chip — "Noah Bright" -> "NB". */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  const first = parts[0][0] ?? "";
  const last = parts.length > 1 ? (parts[parts.length - 1][0] ?? "") : "";
  return (first + last).toUpperCase();
}
