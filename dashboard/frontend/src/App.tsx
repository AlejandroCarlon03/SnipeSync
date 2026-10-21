import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ACTIONS } from "./actions";
import { api, type AuditRecord, type AuditStats } from "./api";
import { ActionHelp } from "./components/ActionHelp";
import { ActivityChart } from "./components/ActivityChart";
import { AuditTable } from "./components/AuditTable";
import { RecordInspector } from "./components/RecordInspector";
import { StatTile } from "./components/StatTile";
import { SyncButton } from "./components/SyncButton";
import { ThemeToggle } from "./components/ThemeToggle";
import { UnmatchedPanel } from "./components/UnmatchedPanel";
import { formatUtc, relativeFromNow, toUtcDate } from "./format";
import { isUnmatched, LINES } from "./lines";
import { applyTableView, DEFAULT_SORT, type Sort } from "./lib/tableView";
import { currentTheme, type Theme } from "./lib/theme";
import { downloadWorkbook, type CellValue } from "./lib/xlsx";

const FUNCTIONS = [
  "FormerEmployeeSync",
  "OnboardEmployeeSync",
  "ReconciliationQueueProcessor",
  "LicenseSync",
];

const TABLE_LIMIT = 500;
const DAY_MS = 86_400_000;

/** Syncs are occasional (button or nightly task), so a slow poll is plenty — this isn't a live feed. */
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

function defaultFilters(): Filters {
  return { from: daysAgo(30), to: isoDate(new Date()), action: "all", func: "all", dryRun: "all" };
}

interface Filters {
  from: string;
  to: string;
  action: string; // "all" or an action name
  func: string; // "all" or a function name
  dryRun: string; // "all" | "true" | "false"
}

export function App() {
  const [filters, setFilters] = useState<Filters>(defaultFilters);

  const [stats, setStats] = useState<AuditStats | null>(null);
  const [prevStats, setPrevStats] = useState<AuditStats | null>(null);
  const [records, setRecords] = useState<AuditRecord[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Table view state lives here rather than in AuditTable so the Excel export can
  // hand off exactly the rows on screen instead of the raw fetch.
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);
  const [selectedId, setSelectedId] = useState<string | null>(null);

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

  const unmatchedPeople = useMemo(
    () =>
      records.length === 0
        ? null
        : new Set(records.filter((r) => isUnmatched(r.action)).map((r) => r.user)).size,
    [records],
  );

  const tiles = useMemo(
    () => [
      {
        label: "Total decisions",
        value: stats?.total ?? 0,
        previous: prevStats?.total ?? 0,
        tone: "var(--ink-2)",
        hint: "all audited actions",
      },
      {
        label: "Offboarded",
        value: sumActions(stats, ["MarkedFormerEmployee"]),
        previous: sumActions(prevStats, ["MarkedFormerEmployee"]),
        tone: LINES.offboard.color,
        hint: "flagged former employee",
      },
      {
        label: "Onboarded",
        value: sumActions(stats, ["Created", "Rehired"]),
        previous: sumActions(prevStats, ["Created", "Rehired"]),
        tone: LINES.onboard.color,
        hint: "created or rehired",
      },
      {
        label: "Unmatched",
        value: sumActions(stats, ["SkippedNoMatch", "UnmatchedAfterReconciliation"]),
        previous: sumActions(prevStats, ["SkippedNoMatch", "UnmatchedAfterReconciliation"]),
        tone: "var(--anomaly)",
        invertDelta: true, // fewer unmatched users is the good outcome
        anomaly: true,
        // The value counts decisions (one person can miss on both passes); say how many people.
        hint:
          unmatchedPeople === null
            ? "no-match decisions"
            : `no-match decisions · ${unmatchedPeople} ${unmatchedPeople === 1 ? "person" : "people"} in loaded rows`,
      },
    ],
    [stats, prevStats, unmatchedPeople],
  );

  const set = (patch: Partial<Filters>) => setFilters((f) => ({ ...f, ...patch }));

  // The selected row may drop out of view on a refetch; the inspector follows the data.
  const selected = useMemo(
    () => (selectedId ? (records.find((r) => r.id === selectedId) ?? null) : null),
    [records, selectedId],
  );

  // Hand focus back to the row that opened the inspector once it has unmounted; focusing
  // before React commits would land on the heading that is about to disappear.
  const returnFocusTo = useRef<string | null>(null);
  const closeInspector = useCallback(() => {
    returnFocusTo.current = selectedId;
    setSelectedId(null);
  }, [selectedId]);

  useEffect(() => {
    if (selected || !returnFocusTo.current) return;
    document.querySelector<HTMLElement>(`tr[data-id="${returnFocusTo.current}"]`)?.focus();
    returnFocusTo.current = null;
  }, [selected]);

  // Escape closes the inspector from anywhere, unless a popover is open to take it first.
  useEffect(() => {
    if (!selected) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || document.querySelector(".pop")) return;
      closeInspector();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [selected, closeInspector]);

  const resetView = () => {
    if (search) setSearch("");
    else setFilters(defaultFilters());
  };

  const firstLoad = loading && !stats;
  const rowCount = search ? visible.length : records.length;

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
      <header className="rail">
        <div className="rail__inner">
          <div className="brand">
            <img className="brand__logo" src="./favicon.svg" alt="" width={28} height={28} />
            <h1 className="brand__title">SnipeSync</h1>
            <span className="brand__sub">Audit trail</span>
          </div>

          <div className="rail__tools">
            <span
              className="rail__status"
              title={lastLoaded ? `${formatUtc(lastLoaded.toISOString())} UTC` : undefined}
            >
              <span className={`pulse ${autoRefresh ? "pulse--on" : ""}`} aria-hidden="true" />
              updated {relativeFromNow(lastLoaded, now)}
            </span>
            <label className="toggle toggle--rail" title={`Re-check every ${REFRESH_MS / 1000}s`}>
              <input
                type="checkbox"
                checked={autoRefresh}
                onChange={(e) => setAutoRefresh(e.target.checked)}
              />
              <span className="toggle__track" aria-hidden="true" />
              Auto-refresh
            </label>
            <span className="rail__sep" aria-hidden="true" />
            <ActionHelp />
            <ThemeToggle theme={theme} onChange={setTheme} />
            <SyncButton onFinished={() => void load(true)} />
          </div>
        </div>
      </header>

      <main className="workspace">
        <section className="filters" aria-label="Filters">
          <label className="field">
            <span className="field-label">From</span>
            <input type="date" value={filters.from} onChange={(e) => set({ from: e.target.value })} />
          </label>
          <label className="field">
            <span className="field-label">To</span>
            <input type="date" value={filters.to} onChange={(e) => set({ to: e.target.value })} />
          </label>
          <label className="field field--grow">
            <span className="field-label">Action</span>
            <select value={filters.action} onChange={(e) => set({ action: e.target.value })}>
              <option value="all">All actions</option>
              {ACTIONS.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
          </label>
          <label className="field field--grow">
            <span className="field-label">Function</span>
            <select value={filters.func} onChange={(e) => set({ func: e.target.value })}>
              <option value="all">All functions</option>
              {FUNCTIONS.map((f) => (
                <option key={f} value={f}>
                  {f}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span className="field-label">Run type</span>
            <select value={filters.dryRun} onChange={(e) => set({ dryRun: e.target.value })}>
              <option value="all">All runs</option>
              <option value="false">Live only</option>
              <option value="true">Dry-run only</option>
            </select>
          </label>
          <button
            className="btn btn--ghost filters__refresh"
            onClick={() => void load()}
            disabled={loading}
          >
            <svg
              className={loading ? "spin" : undefined}
              viewBox="0 0 16 16"
              width="13"
              height="13"
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
            {loading ? "Loading…" : "Refresh"}
          </button>
        </section>

        {error && (
          <div className="alert" role="alert">
            <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true">
              <path
                d="M8 1.8 15 14H1L8 1.8Z"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.4"
                strokeLinejoin="round"
              />
              <path d="M8 6v3.6" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
              <circle cx="8" cy="11.8" r=".9" fill="currentColor" />
            </svg>
            <p className="alert__text">
              <strong>Couldn't load the audit trail.</strong> {error}
            </p>
            <button className="btn btn--ghost" onClick={() => void load()} disabled={loading}>
              Try again
            </button>
          </div>
        )}

        <section className="board" aria-label="This range against the previous period">
          {tiles.map((t) => (
            <StatTile
              key={t.label}
              label={t.label}
              value={t.value}
              previous={t.previous}
              tone={t.tone}
              invertDelta={t.invertDelta}
              anomaly={t.anomaly}
              pending={firstLoad}
              hint={t.hint}
            />
          ))}
        </section>

        <div className={`desk ${selected ? "desk--inspecting" : ""}`}>
          <section className="panel ledger" aria-label="Audit records">
            <div className="ledger__head">
              <h2>Audit trail</h2>
              <span className="search">
                <svg viewBox="0 0 14 14" width="14" height="14" aria-hidden="true">
                  <circle cx="6" cy="6" r="4.2" fill="none" stroke="currentColor" strokeWidth="1.5" />
                  <path d="m9.2 9.2 3.1 3.1" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
                </svg>
                <input
                  type="search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search a person, action, email, detail…"
                  aria-label="Search audit records"
                />
              </span>
              <span className="ledger__count" aria-live="polite">
                {search ? `${visible.length} of ${records.length}` : `${records.length}`} row
                {rowCount === 1 ? "" : "s"}
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
                    strokeWidth="1.5"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
                Export to Excel
              </button>
            </div>

            {records.length >= TABLE_LIMIT && (
              <p className="ledger__cap">
                Showing the newest {TABLE_LIMIT} rows. Older rows in this range aren't loaded;
                narrow the dates to reach them.
              </p>
            )}

            <AuditTable
              records={visible}
              sort={sort}
              onSortChange={setSort}
              loading={loading}
              searching={search.trim().length > 0}
              selectedId={selected?.id ?? null}
              onSelect={setSelectedId}
              onReset={resetView}
              now={now}
            />
          </section>

          <aside className="side" aria-label={selected ? "Record details" : "Overview"}>
            {selected ? (
              <>
                <div className="side__scrim" aria-hidden="true" onClick={closeInspector} />
                <RecordInspector
                  record={selected}
                  records={records}
                  onSelect={setSelectedId}
                  onClose={closeInspector}
                  now={now}
                />
              </>
            ) : (
              <>
                <UnmatchedPanel records={records} onPick={setSearch} now={now} />
                <ActivityChart stats={stats} theme={theme} loading={loading} />
              </>
            )}
          </aside>
        </div>
      </main>
    </div>
  );
}
