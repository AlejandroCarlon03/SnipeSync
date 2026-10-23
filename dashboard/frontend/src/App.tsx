import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ACTIONS } from "./actions";
import { api, type AuditRecord, type AuditStats } from "./api";
import { ActionHelp } from "./components/ActionHelp";
import { ActivityChart } from "./components/ActivityChart";
import { AuditTable } from "./components/AuditTable";
import { RecordInspector } from "./components/RecordInspector";
import { SyncButton } from "./components/SyncButton";
import { ThemeToggle } from "./components/ThemeToggle";
import { UnmatchedPanel } from "./components/UnmatchedPanel";
import { UpdateBanner } from "./components/UpdateBanner";
import { formatUtc, relativeFromNow, toUtcDate } from "./format";
import { actionTag, lineForFunction } from "./lines";
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
        const [s, r] = await Promise.all([
          api.stats(common),
          api.records({
            from: common.from,
            to: common.to,
            action: filters.action === "all" ? undefined : filters.action,
            function: common.function,
            limit: TABLE_LIMIT,
          }),
        ]);
        setStats(s);
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

          <div className="top__tools">
            <span
              className="top__status"
              title={lastLoaded ? `${formatUtc(lastLoaded.toISOString())} UTC` : undefined}
            >
              <span className={`pulse ${autoRefresh ? "pulse--on" : ""}`} aria-hidden="true" />
              updated {relativeFromNow(lastLoaded, now)}
            </span>
            <label className="toggle" title={`Re-check every ${REFRESH_MS / 1000}s`}>
              <input
                type="checkbox"
                checked={autoRefresh}
                onChange={(e) => setAutoRefresh(e.target.checked)}
              />
              <span className="toggle__track" aria-hidden="true" />
              Auto-refresh
            </label>
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
              {ACTIONS.map((a) => (
                <li key={a}>
                  <button
                    className={`facet__item ${filters.action === a ? "facet__item--on" : ""}`}
                    aria-pressed={filters.action === a}
                    onClick={() => set({ action: filters.action === a ? "all" : a })}
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
                    className={`facet__item facet__item--two ${filters.func === f ? "facet__item--on" : ""}`}
                    aria-pressed={filters.func === f}
                    onClick={() => set({ func: filters.func === f ? "all" : f })}
                  >
                    <span className="facet__name">
                      {lineForFunction(f)?.name ?? f}
                      <span className="facet__sub">{f}</span>
                    </span>
                    <span className="facet__count">{countOf(stats?.byFunction, f)}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>

          <button className="btn btn--ghost filters__refresh" onClick={() => void load()} disabled={loading}>
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

        {/* The investigation starts here, so search sits right above the trail. */}
        <label className="search search--main">
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

        <div className="desk">
          <section className="panel ledger" aria-label="Audit records">
            <div className="ledger__head">
              <h2>Audit trail</h2>
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
              failed={Boolean(error)}
              renderDetail={(r) => (
                <RecordInspector
                  record={r}
                  records={records}
                  onSelect={setSelectedId}
                  onClose={closeInspector}
                  advanced={advanced}
                  onAdvancedChange={setAdvanced}
                  now={now}
                />
              )}
            />
          </section>

          <aside className="side" aria-label="Overview">
            <UnmatchedPanel records={records} onPick={setSearch} now={now} failed={Boolean(error) || (loading && !stats)} />
            <ActivityChart stats={stats} theme={theme} loading={loading} />
          </aside>
        </div>
      </main>
      </div>
    </div>
  );
}
