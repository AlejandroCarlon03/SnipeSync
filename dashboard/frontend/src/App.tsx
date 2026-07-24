import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ACTIONS } from "./actions";
import { api, type AuditRecord, type AuditStats } from "./api";
import { ActionHelp } from "./components/ActionHelp";
import { ActivityChart } from "./components/ActivityChart";
import { AuditTable } from "./components/AuditTable";
import { StatTile } from "./components/StatTile";
import { ThemeToggle } from "./components/ThemeToggle";
import { UnmatchedPanel } from "./components/UnmatchedPanel";
import { relativeFromNow, toUtcDate } from "./format";
import { applyTableView, DEFAULT_SORT, type Sort } from "./lib/tableView";
import { currentTheme, type Theme } from "./lib/theme";
import { downloadWorkbook, type CellValue } from "./lib/xlsx";
import "./dashboard.css";

const FUNCTIONS = [
  "FormerEmployeeSync",
  "OnboardEmployeeSync",
  "ReconciliationQueueProcessor",
];

const TABLE_LIMIT = 500;
const DAY_MS = 86_400_000;

/** The syncs run twice a day, so a slow poll is plenty — this isn't a live feed. */
const REFRESH_MS = 60_000;
/** How often the "updated Xs ago" labels re-render. */
const CLOCK_MS = 15_000;

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

  // Table view state lives here rather than in AuditTable so the Excel export can
  // hand off exactly the rows on screen instead of the raw fetch.
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);

  const [theme, setTheme] = useState<Theme>(currentTheme);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastLoaded, setLastLoaded] = useState<Date | null>(null);
  const [now, setNow] = useState(() => Date.now());

  // A background poll must never land on top of an in-flight request, or a slow
  // response could overwrite a newer one.
  const inFlight = useRef(false);

  const load = useCallback(
    async (silent = false) => {
      if (inFlight.current) return;
      inFlight.current = true;
      if (!silent) setLoading(true);
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
        setLastLoaded(new Date());
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
        setStats(null);
        setPrevStats(null);
        setRecords([]);
      } finally {
        inFlight.current = false;
        if (!silent) setLoading(false);
      }
    },
    [filters],
  );

  useEffect(() => {
    void load();
  }, [load]);

  // Background poll. Silent, so the Refresh button doesn't flicker every minute.
  useEffect(() => {
    if (!autoRefresh) return;
    const id = setInterval(() => void load(true), REFRESH_MS);
    return () => clearInterval(id);
  }, [autoRefresh, load]);

  // Independent clock so relative timestamps keep counting up between fetches.
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), CLOCK_MS);
    return () => clearInterval(id);
  }, []);

  const visible = useMemo(
    () => applyTableView(records, search, sort),
    [records, search, sort],
  );

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

  /** Export the rows currently on screen — search and sort included. */
  const exportExcel = useCallback(() => {
    const rows: CellValue[][] = visible.map((r) => [
      toUtcDate(r.timestampUtc) ?? r.timestampUtc,
      r.function,
      r.user,
      r.action,
      r.oldValue ?? "",
      r.newValue ?? "",
      r.detail ?? "",
      r.dryRun ? "dry-run" : "live",
    ]);

    const range = [filters.from, filters.to].filter(Boolean).join("_to_") || "all";
    downloadWorkbook(
      `snipesync-audit-${range}.xlsx`,
      [
        { header: "Time (UTC)", width: 20 },
        { header: "Function" },
        { header: "User" },
        { header: "Action" },
        { header: "Old value", width: 28 },
        { header: "New value", width: 28 },
        { header: "Detail", width: 46 },
        { header: "Run", width: 10 },
      ],
      rows,
      "Audit records",
    );
  }, [visible, filters.from, filters.to]);

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar__inner">
          <div className="brand">
            <span className="brand__mark">S</span>
            <h1 className="brand__title">SnipeSync Audit Dashboard</h1>
          </div>

          <div className="topbar__tools">
            <span className="muted" title={lastLoaded?.toISOString() ?? undefined}>
              updated {relativeFromNow(lastLoaded, now)}
            </span>
            <label className="toggle" title={`Re-check every ${REFRESH_MS / 1000}s`}>
              <input
                type="checkbox"
                checked={autoRefresh}
                onChange={(e) => setAutoRefresh(e.target.checked)}
              />
              Auto-refresh
            </label>
            <ThemeToggle theme={theme} onChange={setTheme} />
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

        <ActivityChart stats={stats} theme={theme} />

        <UnmatchedPanel records={records} />

        <section className="panel">
          <div className="panel__head">
            <h2>Audit records</h2>
            <div className="panel__actions">
              <span className="search">
                <svg viewBox="0 0 14 14" width="13" height="13" aria-hidden="true">
                  <circle cx="6" cy="6" r="4.2" fill="none" stroke="currentColor" strokeWidth="1.4" />
                  <path d="m9.2 9.2 3.1 3.1" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
                </svg>
                <input
                  type="search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search user, action, detail…"
                  aria-label="Search audit records"
                />
              </span>
              <span className="muted">
                {search ? `${visible.length} of ${records.length}` : `${records.length}`} row
                {(search ? visible.length : records.length) === 1 ? "" : "s"}
                {records.length >= TABLE_LIMIT ? ` (capped at ${TABLE_LIMIT})` : ""}
              </span>
              <button
                className="btn btn--ghost"
                onClick={exportExcel}
                disabled={visible.length === 0}
                title="Download the rows shown below as an .xlsx workbook"
              >
                <svg viewBox="0 0 14 14" width="13" height="13" aria-hidden="true">
                  <path
                    d="M7 1v8M7 9 4 6M7 9l3-3M2 11.5h10"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.4"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
                Export to Excel
              </button>
            </div>
          </div>

          <AuditTable
            records={visible}
            sort={sort}
            onSortChange={setSort}
            loading={loading}
            searching={search.trim().length > 0}
          />
        </section>
      </main>

      <ActionHelp />
    </div>
  );
}
