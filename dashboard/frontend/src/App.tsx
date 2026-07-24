import { useCallback, useEffect, useMemo, useState } from "react";
import { api, type AuditRecord, type AuditStats } from "./api";
import { StatTile } from "./components/StatTile";
import "./dashboard.css";

// The actions CosmosAuditService can write, for the Action filter dropdown.
const ACTIONS = [
  "MarkedFormerEmployee",
  "AssetCheckedIn",
  "LicenseReclaimed",
  "AccessoryReclaimed",
  "SkippedNoMatch",
  "UnmatchedAfterReconciliation",
  "ReconciledMatch",
  "Created",
  "Rehired",
];

const FUNCTIONS = [
  "FormerEmployeeSync",
  "OnboardEmployeeSync",
  "ReconciliationQueueProcessor",
];

const TABLE_LIMIT = 500;
const DAY_MS = 86_400_000;

function isoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function daysAgo(n: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() - n);
  return isoDate(d);
}

/** Sum the counts of a set of actions out of a stats payload. */
function sumActions(stats: AuditStats | null, actions: string[]): number {
  if (!stats) return 0;
  return stats.byAction
    .filter((b) => b.key !== null && actions.includes(b.key))
    .reduce((acc, b) => acc + b.count, 0);
}

interface Filters {
  from: string;
  to: string;
  action: string; // "all" or an action name
  func: string; // "all" or a function name
  dryRun: string; // "all" | "true" | "false"
}

export function App() {
  const [filters, setFilters] = useState<Filters>({
    from: daysAgo(30),
    to: isoDate(new Date()),
    action: "all",
    func: "all",
    dryRun: "all",
  });

  const [stats, setStats] = useState<AuditStats | null>(null);
  const [prevStats, setPrevStats] = useState<AuditStats | null>(null);
  const [records, setRecords] = useState<AuditRecord[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    const common = {
      from: filters.from || undefined,
      to: filters.to || undefined,
      function: filters.func === "all" ? undefined : filters.func,
      dryRun: filters.dryRun === "all" ? undefined : filters.dryRun === "true",
    };

    // Previous period of equal length, immediately before [from, to] — powers the
    // period-over-period deltas on the tiles. Only computed when both ends are set.
    const hasRange = Boolean(filters.from && filters.to);
    let prevPromise: Promise<AuditStats | null> = Promise.resolve(null);
    if (hasRange) {
      const from = new Date(filters.from);
      const to = new Date(filters.to);
      const span = to.getTime() - from.getTime();
      const prevTo = new Date(from.getTime() - DAY_MS);
      const prevFrom = new Date(prevTo.getTime() - span);
      prevPromise = api.stats({
        ...common,
        from: isoDate(prevFrom),
        to: isoDate(prevTo),
      });
    }

    try {
      const [s, p, r] = await Promise.all([
        api.stats(common),
        prevPromise,
        api.records({
          from: common.from,
          to: common.to,
          action: filters.action === "all" ? undefined : filters.action,
          function: common.function,
          limit: TABLE_LIMIT,
        }),
      ]);
      setStats(s);
      setPrevStats(p);
      // /api/audit has no dryRun filter, so honor it client-side for the table.
      setRecords(
        filters.dryRun === "all"
          ? r
          : r.filter((x) => x.dryRun === (filters.dryRun === "true")),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setStats(null);
      setPrevStats(null);
      setRecords([]);
    } finally {
      setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    void load();
  }, [load]);

  const tiles = useMemo(
    () => [
      {
        label: "Total decisions",
        value: stats?.total ?? 0,
        previous: prevStats?.total ?? 0,
        tone: "var(--accent, #3b6cf6)",
        hint: "all audited actions",
      },
      {
        label: "Offboarded",
        value: sumActions(stats, ["MarkedFormerEmployee"]),
        previous: sumActions(prevStats, ["MarkedFormerEmployee"]),
        tone: "#e06c4f",
        hint: "flagged former employee",
      },
      {
        label: "Onboarded",
        value: sumActions(stats, ["Created", "Rehired"]),
        previous: sumActions(prevStats, ["Created", "Rehired"]),
        tone: "#4f9de0",
        hint: "created or rehired",
      },
      {
        label: "Unmatched",
        value: sumActions(stats, ["SkippedNoMatch", "UnmatchedAfterReconciliation"]),
        previous: sumActions(prevStats, ["SkippedNoMatch", "UnmatchedAfterReconciliation"]),
        tone: "#c9a227",
        invertDelta: true, // fewer unmatched users is the good outcome
        hint: "no Snipe-IT match",
      },
    ],
    [stats, prevStats],
  );

  const set = (patch: Partial<Filters>) => setFilters((f) => ({ ...f, ...patch }));

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar__inner">
          <div className="brand">
            <span className="brand__mark">S</span>
            <h1 className="brand__title">SnipeSync Audit Dashboard</h1>
          </div>
        </div>
      </header>

      <main className="content">
        <section className="filters">
          <label>
            From
            <input type="date" value={filters.from} onChange={(e) => set({ from: e.target.value })} />
          </label>
          <label>
            To
            <input type="date" value={filters.to} onChange={(e) => set({ to: e.target.value })} />
          </label>
          <label>
            Action
            <select value={filters.action} onChange={(e) => set({ action: e.target.value })}>
              <option value="all">All actions</option>
              {ACTIONS.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
          </label>
          <label>
            Function
            <select value={filters.func} onChange={(e) => set({ func: e.target.value })}>
              <option value="all">All functions</option>
              {FUNCTIONS.map((f) => (
                <option key={f} value={f}>
                  {f}
                </option>
              ))}
            </select>
          </label>
          <label>
            Run type
            <select value={filters.dryRun} onChange={(e) => set({ dryRun: e.target.value })}>
              <option value="all">All runs</option>
              <option value="false">Live only</option>
              <option value="true">Dry-run only</option>
            </select>
          </label>
          <button className="btn" onClick={() => void load()} disabled={loading}>
            {loading ? "Loading…" : "Refresh"}
          </button>
        </section>

        {error && <div className="error">⚠ {error}</div>}

        <section className="tiles">
          {tiles.map((t) => (
            <StatTile
              key={t.label}
              label={t.label}
              value={t.value}
              previous={t.previous}
              tone={t.tone}
              invertDelta={t.invertDelta}
              hint={t.hint}
            />
          ))}
        </section>

        <section className="panel">
          <div className="panel__head">
            <h2>Audit records</h2>
            <span className="muted">
              {records.length} row{records.length === 1 ? "" : "s"}
              {records.length >= TABLE_LIMIT ? ` (capped at ${TABLE_LIMIT})` : ""}
            </span>
          </div>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Time (UTC)</th>
                  <th>Function</th>
                  <th>User</th>
                  <th>Action</th>
                  <th>Detail</th>
                  <th>Run</th>
                </tr>
              </thead>
              <tbody>
                {records.map((r) => (
                  <tr key={r.id}>
                    <td>{r.timestampUtc.replace("T", " ").slice(0, 19)}</td>
                    <td>{r.function}</td>
                    <td>{r.user}</td>
                    <td>
                      <span className="tag">{r.action}</span>
                    </td>
                    <td className="muted">{r.detail ?? r.newValue ?? ""}</td>
                    <td>{r.dryRun ? "dry-run" : "live"}</td>
                  </tr>
                ))}
                {records.length === 0 && !loading && (
                  <tr>
                    <td colSpan={6} className="muted center">
                      No records for these filters.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>
      </main>
    </div>
  );
}
