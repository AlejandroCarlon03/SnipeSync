import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ACTIONS } from "./actions";
import { api, type Approval, type AuditRecord, type AuditStats, type RunEntry } from "./api";
import { ActionHelp } from "./components/ActionHelp";
import { AuditTable } from "./components/AuditTable";
import { LastSyncCard } from "./components/LastSyncCard";
import { RecordInspector } from "./components/RecordInspector";
import { RunsView } from "./components/RunsView";
import { SyncButton } from "./components/SyncButton";
import { ThemeToggle } from "./components/ThemeToggle";
import { UnmatchedPanel } from "./components/UnmatchedPanel";
import { UpdateBanner } from "./components/UpdateBanner";
import { formatUtc, relativeFromNow, toUtcDate } from "./format";
import { actionTag, lineForFunction } from "./lines";
import { approvalChecker } from "./lib/approvals";
import { applyTableView, DEFAULT_SORT, matchesSearch, type Sort } from "./lib/tableView";
import { currentTheme, type Theme } from "./lib/theme";
import { downloadWorkbook, type CellValue } from "./lib/xlsx";

const FUNCTIONS = [
  "FormerEmployeeSync",
  "OnboardEmployeeSync",
  "ReconciliationQueueProcessor",
  "LicenseSync",
];

const TABLE_LIMIT = 500;

/** Runs loaded per page of the run history; "Show older runs" adds another page. */
const RUN_PAGE = 30;

type TrailView = "runs" | "rows";

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

type RangePreset = "all" | "7d" | "30d" | "90d" | "thisMonth" | "lastMonth" | "custom";

const RANGE_PRESETS: [RangePreset, string][] = [
  ["all", "All time"],
  ["7d", "Last 7 days"],
  ["30d", "Last 30 days"],
  ["90d", "Last 90 days"],
  ["thisMonth", "This month"],
  ["lastMonth", "Last month"],
  ["custom", "Custom range"],
];

/**
 * The from/to a range resolves to. Presets are worked out at load time rather than when
 * picked, so a dashboard left open overnight still means "the last 7 days" in the morning.
 */
function resolveRange(f: Filters): { from: string; to: string } {
  const today = new Date();
  const y = today.getUTCFullYear();
  const m = today.getUTCMonth();
  switch (f.range) {
    case "all":
      return { from: "", to: "" };
    case "7d":
      return { from: daysAgo(7), to: isoDate(today) };
    case "30d":
      return { from: daysAgo(30), to: isoDate(today) };
    case "90d":
      return { from: daysAgo(90), to: isoDate(today) };
    case "thisMonth":
      return { from: isoDate(new Date(Date.UTC(y, m, 1))), to: isoDate(today) };
    case "lastMonth":
      return { from: isoDate(new Date(Date.UTC(y, m - 1, 1))), to: isoDate(new Date(Date.UTC(y, m, 0))) };
    case "custom":
      return { from: f.from, to: f.to };
  }
}

/** One bucket's count from a stats grouping: 0 when the bucket is absent, "–" when nothing loaded. */
function countOf(buckets: { key: string | null; count: number }[] | undefined, key: string): number | string {
  if (!buckets) return "–";
  return buckets.find((b) => b.key === key)?.count ?? 0;
}

function defaultFilters(): Filters {
  return { range: "30d", from: daysAgo(30), to: isoDate(new Date()), action: "all", func: "all", dryRun: "all" };
}

interface Filters {
  range: RangePreset;
  /** Only read for a custom range. */
  from: string;
  to: string;
  action: string; // "all" or an action name
  func: string; // "all" or a function name
  dryRun: string; // "all" | "true" | "false"
}

export function App() {
  const [filters, setFilters] = useState<Filters>(defaultFilters);

  const [stats, setStats] = useState<AuditStats | null>(null);
  const [records, setRecords] = useState<AuditRecord[]>([]);
  const [runs, setRuns] = useState<RunEntry[]>([]);
  const [lastRun, setLastRun] = useState<RunEntry | null>(null);
  const [runLimit, setRunLimit] = useState(RUN_PAGE);
  const [approvals, setApprovals] = useState<Approval[]>([]);
  const [approveBusy, setApproveBusy] = useState<string | null>(null);
  const [approveError, setApproveError] = useState<string | null>(null);
  const [view, setView] = useState<TrailView>("runs");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Table view state lives here rather than in AuditTable so the Excel export can
  // hand off exactly the rows on screen instead of the raw fetch.
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  // Remembered across rows: whoever wants the full record usually wants it for the next one too.
  const [advanced, setAdvanced] = useState(false);

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

      const { from, to } = resolveRange(filters);
      const common = {
        from: from || undefined,
        to: to || undefined,
        function: filters.func === "all" ? undefined : filters.func,
        dryRun: filters.dryRun === "all" ? undefined : filters.dryRun === "true",
      };

      try {
        const [s, r, runPage, newest, appr] = await Promise.all([
          api.stats(common),
          api.records({
            from: common.from,
            to: common.to,
            action: filters.action === "all" ? undefined : filters.action,
            function: common.function,
            limit: TABLE_LIMIT,
          }),
          api.runs({ from: common.from, to: common.to, dryRun: common.dryRun, limit: runLimit }),
          // The Last sync card shows the newest run whatever the filters say.
          api.runs({ limit: 1 }),
          api.approvals(),
        ]);
        setStats(s);
        setRuns(runPage);
        setLastRun(newest[0] ?? null);
        setApprovals(appr);
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
        setRecords([]);
        setRuns([]);
      } finally {
        inFlight.current = false;
        if (!silent) setLoading(false);
      }
    },
    [filters, runLimit],
  );

  useEffect(() => {
    void load();
  }, [load]);

  // A different range or run type starts the run history from its first page again.
  useEffect(() => {
    setRunLimit(RUN_PAGE);
  }, [filters]);

  const isApproved = useMemo(() => approvalChecker(approvals), [approvals]);

  const approve = useCallback(
    async (user: string, email: string | null) => {
      setApproveBusy(user);
      setApproveError(null);
      try {
        const added = await api.approve(user, email);
        setApprovals((list) => [...list.filter((a) => a.key !== added.key), added]
          .sort((a, b) => a.user.localeCompare(b.user)));
      } catch (e) {
        setApproveError(`Couldn't approve ${user}: ${e instanceof Error ? e.message : String(e)}`);
      } finally {
        setApproveBusy(null);
      }
    },
    [],
  );

  const unapprove = useCallback(async (key: string) => {
    setApproveBusy(key);
    setApproveError(null);
    try {
      await api.unapprove(key);
      setApprovals((list) => list.filter((a) => a.key !== key));
    } catch (e) {
      setApproveError(`Couldn't undo that approval: ${e instanceof Error ? e.message : String(e)}`);
    } finally {
      setApproveBusy(null);
    }
  }, []);

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

  const set = (patch: Partial<Filters>) => setFilters((f) => ({ ...f, ...patch }));

  // The selected row may drop out of view on a refetch; the inspector follows the data.
  // The open row may come from the flat table or from a run (whose rows can be older than the
  // table's newest 500), so look in both.
  const selected = useMemo(() => {
    if (!selectedId) return null;
    const fromTable = records.find((r) => r.id === selectedId);
    if (fromTable) return fromTable;
    for (const run of runs) {
      const hit =
        run.changes.find((r) => r.id === selectedId) ??
        run.declined.find((p) => p.record.id === selectedId)?.record;
      if (hit) return hit;
    }
    return null;
  }, [records, runs, selectedId]);

  // Hand focus back to the row that opened the inspector once it has unmounted; focusing
  // before React commits would land on the heading that is about to disappear.
  const returnFocusTo = useRef<string | null>(null);
  const closeInspector = useCallback(() => {
    returnFocusTo.current = selectedId;
    setSelectedId(null);
  }, [selectedId]);

  useEffect(() => {
    if (selected || !returnFocusTo.current) return;
    document.querySelector<HTMLElement>(`[data-id="${returnFocusTo.current}"]`)?.focus();
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

  const rowCount = search ? visible.length : records.length;

  // The run view filters on the client: action and function filters plus the search.
  const runMatches = useCallback(
    (r: AuditRecord) =>
      (filters.action === "all" || r.action === filters.action) &&
      (filters.func === "all" || r.function === filters.func) &&
      matchesSearch(r, search),
    [filters.action, filters.func, search],
  );
  const runsNarrowed = Boolean(search.trim()) || filters.action !== "all" || filters.func !== "all";

  const renderDetail = (r: AuditRecord) => (
    <RecordInspector
      record={r}
      records={records}
      onSelect={setSelectedId}
      onClose={closeInspector}
      advanced={advanced}
      onAdvancedChange={setAdvanced}
      now={now}
    />
  );

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

    const { from, to } = resolveRange(filters);
    const range = [from, to].filter(Boolean).join("_to_") || "all";
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
  }, [visible, filters]);

  const viewButtons: [TrailView, string][] = [
    ["runs", "By run"],
    ["rows", "Every row"],
  ];

  return (
    <div className="app">
      <header className="top">
        <div className="top__inner">
          <div className="brand">
            <img className="brand__logo" src="./favicon.svg" alt="" width={30} height={30} />
            <div className="brand__text">
              <h1 className="brand__title">SnipeSync</h1>
              <span className="brand__sub">Audit trail</span>
            </div>
          </div>

          {/* The investigation starts here, so search lives in the top bar. */}
          <label className="search search--top">
            <svg viewBox="0 0 14 14" width="15" height="15" aria-hidden="true">
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
          </label>

          <div className="top__tools">
            <div className="status-pill">
              {/* The freshness label doubles as the manual refresh. */}
              <button
                className="top__status"
                onClick={() => void load()}
                disabled={loading}
                title={
                  lastLoaded
                    ? `Loaded ${formatUtc(lastLoaded.toISOString())} UTC. Click to refresh now.`
                    : "Refresh now"
                }
              >
                <span className={`pulse ${autoRefresh ? "pulse--on" : ""} ${loading ? "pulse--busy" : ""}`} aria-hidden="true" />
                <span className="top__status-text">
                  {loading ? "Loading…" : `Updated ${relativeFromNow(lastLoaded, now)}`}
                </span>
              </button>
              <span className="status-pill__sep" aria-hidden="true" />
              <label className="toggle" title={`Re-check every ${REFRESH_MS / 1000}s`}>
                <input
                  type="checkbox"
                  checked={autoRefresh}
                  onChange={(e) => setAutoRefresh(e.target.checked)}
                />
                <span className="toggle__track" aria-hidden="true" />
                <span className="toggle__label">Auto</span>
                <span className="sr-only">-refresh</span>
              </label>
            </div>
            <ActionHelp />
            <ThemeToggle theme={theme} onChange={setTheme} />
            <SyncButton onFinished={() => void load(true)} />
          </div>
        </div>
      </header>

      <div className="shell">
        <aside className="filters" aria-label="Filters">
          <section className="filter-group">
            <h2 className="filter-group__title">Date range</h2>
            <label className="field">
              <span className="sr-only">Date range</span>
              <select value={filters.range} onChange={(e) => set({ range: e.target.value as RangePreset })}>
                {RANGE_PRESETS.map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
            {filters.range === "custom" && (
              <div className="filter-dates">
                <label className="field">
                  <span className="field-label">From</span>
                  <input type="date" value={filters.from} onChange={(e) => set({ from: e.target.value })} />
                </label>
                <label className="field">
                  <span className="field-label">To</span>
                  <input type="date" value={filters.to} onChange={(e) => set({ to: e.target.value })} />
                </label>
              </div>
            )}
          </section>

          <section className="filter-group">
            <h2 className="filter-group__title">Run type</h2>
            <div className="seg seg--full" role="group" aria-label="Run type">
              {[
                ["all", "All"],
                ["false", "Live"],
                ["true", "Dry run"],
              ].map(([value, label]) => (
                <button
                  key={value}
                  className={`seg__btn ${filters.dryRun === value ? "seg__btn--on" : ""}`}
                  aria-pressed={filters.dryRun === value}
                  onClick={() => set({ dryRun: value })}
                >
                  {label}
                </button>
              ))}
            </div>
          </section>

          <section className="filter-group">
            <h2 className="filter-group__title">Action</h2>
            <ul className="facet">
              <li>
                <button
                  className={`facet__item ${filters.action === "all" ? "facet__item--on" : ""}`}
                  aria-pressed={filters.action === "all"}
                  onClick={() => set({ action: "all" })}
                >
                  <span className="facet__name">All actions</span>
                  <span className="facet__count">{stats?.total ?? "–"}</span>
                </button>
              </li>
              {ACTIONS.filter(
                (a) => !stats || filters.action === a || countOf(stats.byAction, a) !== 0,
              ).map((a) => (
                <li key={a}>
                  <button
                    className={`facet__item ${filters.action === a ? "facet__item--on" : ""}`}
                    aria-pressed={filters.action === a}
                    onClick={() => set({ action: filters.action === a ? "all" : a })}
                    title={a}
                  >
                    <span className={`facet__mark facet__mark--${actionTag(a)}`} aria-hidden="true" />
                    <span className="facet__name">{a}</span>
                    <span className="facet__count">{countOf(stats?.byAction, a)}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>

          <section className="filter-group">
            <h2 className="filter-group__title">Function</h2>
            <ul className="facet">
              <li>
                <button
                  className={`facet__item ${filters.func === "all" ? "facet__item--on" : ""}`}
                  aria-pressed={filters.func === "all"}
                  onClick={() => set({ func: "all" })}
                >
                  <span className="facet__name">All functions</span>
                </button>
              </li>
              {FUNCTIONS.map((f) => (
                <li key={f}>
                  <button
                    className={`facet__item ${filters.func === f ? "facet__item--on" : ""}`}
                    aria-pressed={filters.func === f}
                    onClick={() => set({ func: filters.func === f ? "all" : f })}
                    title={f}
                  >
                    <span className="facet__name">{lineForFunction(f)?.name ?? f}</span>
                    <span className="facet__count">{countOf(stats?.byFunction, f)}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        </aside>

        <main className="workspace">
          <UpdateBanner />

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

          <div className="overview">
            <LastSyncCard run={lastRun} failed={Boolean(error) || (loading && !lastRun)} now={now} />
            <UnmatchedPanel
              records={records}
              approvals={approvals}
              isApproved={isApproved}
              onPick={(user) => {
                setSearch(user);
              }}
              onApprove={(user, email) => void approve(user, email)}
              onUnapprove={(key) => void unapprove(key)}
              busy={approveBusy}
              error={approveError}
              now={now}
              failed={Boolean(error) || (loading && !stats)}
            />
          </div>

          <section className="panel ledger" aria-label="Audit records">
            <div className="ledger__head">
              <h2>Audit trail</h2>
              <span className="ledger__count" aria-live="polite">
                {view === "runs"
                  ? `${runs.length} run${runs.length === 1 ? "" : "s"}`
                  : `${search ? `${visible.length} of ${records.length}` : records.length} row${rowCount === 1 ? "" : "s"}`}
              </span>
              <span className="ledger__spacer" />
              <div className="seg" role="group" aria-label="Group the audit trail">
                {viewButtons.map(([value, label]) => (
                  <button
                    key={value}
                    className={`seg__btn ${view === value ? "seg__btn--on" : ""}`}
                    aria-pressed={view === value}
                    onClick={() => setView(value)}
                  >
                    {label}
                  </button>
                ))}
              </div>
              <button
                className="btn btn--ghost"
                onClick={exportExcel}
                disabled={visible.length === 0}
                title="Download the matching rows as an .xlsx workbook"
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

            {view === "rows" && records.length >= TABLE_LIMIT && (
              <p className="ledger__cap">
                Showing the newest {TABLE_LIMIT} rows. Older rows in this range aren't loaded;
                narrow the dates to reach them.
              </p>
            )}

            {view === "runs" ? (
              <RunsView
                runs={runs}
                loading={loading}
                failed={Boolean(error)}
                matches={runMatches}
                narrowed={runsNarrowed}
                isApproved={isApproved}
                selectedId={selectedId}
                onSelect={setSelectedId}
                renderDetail={renderDetail}
                onShowMore={runs.length >= runLimit ? () => setRunLimit((n) => n + RUN_PAGE) : undefined}
              />
            ) : (
              <AuditTable
                records={visible}
                sort={sort}
                onSortChange={setSort}
                loading={loading}
                searching={search.trim().length > 0}
                selectedId={selected?.id ?? null}
                onSelect={setSelectedId}
                onReset={resetView}
                failed={Boolean(error)}
                isApproved={isApproved}
                renderDetail={renderDetail}
              />
            )}
          </section>
        </main>
      </div>
    </div>
  );
}
