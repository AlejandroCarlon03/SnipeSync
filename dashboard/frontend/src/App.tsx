import { useCallback, useEffect, useState } from "react";
import { api } from "./api";
import type { ActivityPoint, Summary, SyncEvent, SyncRun } from "./api";
import { ActivityChart } from "./components/ActivityChart";
import { EventsTable } from "./components/EventsTable";
import { Header } from "./components/Header";
import { RunsTable } from "./components/RunsTable";
import { StatTile } from "./components/StatTile";
import "./app.css";

type Theme = "light" | "dark";

function initialTheme(): Theme {
  const saved = localStorage.getItem("snipesync.theme");
  if (saved === "light" || saved === "dark") return saved;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/**
 * Writes the theme to the DOM *before* React re-renders.
 *
 * The chart reads its colours from CSS custom properties at render time. If the
 * attribute were set in an effect instead, that effect would run after the
 * chart had already rendered, so the bars would paint with the outgoing theme's
 * colours and stay one toggle behind.
 */
function applyTheme(theme: Theme) {
  document.documentElement.setAttribute("data-theme", theme);
  try {
    localStorage.setItem("snipesync.theme", theme);
  } catch {
    /* Private mode or storage disabled: the theme just won't persist. */
  }
}

export default function App() {
  const [theme, setTheme] = useState<Theme>(initialTheme);
  const [days, setDays] = useState(30);

  const [summary, setSummary] = useState<Summary | null>(null);
  const [activity, setActivity] = useState<ActivityPoint[]>([]);
  const [runs, setRuns] = useState<SyncRun[]>([]);
  const [events, setEvents] = useState<SyncEvent[]>([]);

  const [action, setAction] = useState("all");
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");

  const [loading, setLoading] = useState(true);
  const [eventsLoading, setEventsLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // index.html sets the attribute pre-paint; this keeps it in step on mount.
  useEffect(() => {
    applyTheme(theme);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const toggleTheme = () => {
    const next: Theme = theme === "dark" ? "light" : "dark";
    applyTheme(next);
    setTheme(next);
  };

  // Keeps the app from firing a request on every keystroke.
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(t);
  }, [search]);

  const loadCore = useCallback(async () => {
    setError(null);
    try {
      const [s, a, r] = await Promise.all([api.summary(days), api.activity(days), api.runs(12)]);
      setSummary(s);
      setActivity(a);
      setRuns(r);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not reach the SnipeSync service.");
    } finally {
      setLoading(false);
    }
  }, [days]);

  useEffect(() => {
    void loadCore();
  }, [loadCore]);

  useEffect(() => {
    let cancelled = false;
    setEventsLoading(true);
    api
      .events({ action, search: debouncedSearch, limit: 100 })
      .then((e) => {
        if (!cancelled) setEvents(e);
      })
      .catch(() => {
        if (!cancelled) setEvents([]);
      })
      .finally(() => {
        if (!cancelled) setEventsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [action, debouncedSearch]);

  const refresh = async () => {
    setRefreshing(true);
    await loadCore();
    setEvents(await api.events({ action, search: debouncedSearch, limit: 100 }).catch(() => []));
    setRefreshing(false);
  };

  if (loading) {
    return (
      <div className="boot">
        <div className="boot__spinner" aria-hidden="true" />
        <p>Loading sync data…</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="boot">
        <h2 className="boot__title">Can’t load sync data</h2>
        <p className="boot__msg">{error}</p>
        <button type="button" className="btn" onClick={() => void loadCore()}>
          Try again
        </button>
      </div>
    );
  }

  return (
    <div className="app">
      <Header
        summary={summary}
        days={days}
        onDaysChange={setDays}
        theme={theme}
        onThemeToggle={toggleTheme}
        onRefresh={() => void refresh()}
        refreshing={refreshing}
      />

      <main className="content">
        {summary?.is_sample_data && (
          <div className="notice" role="status">
            <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
              <circle cx="8" cy="8" r="6.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
              <path d="M8 7.2 L8 11.5" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
              <circle cx="8" cy="4.8" r="0.9" fill="currentColor" />
            </svg>
            <span>
              <strong>Sample data.</strong> The sync functions don’t record what they do yet, so these
              figures are generated for design purposes — not real Snipe-IT activity.
            </span>
          </div>
        )}

        {summary && (
          <section className="tiles" aria-label="Key metrics">
            <StatTile
              label="Users created"
              value={summary.users_created.value}
              previous={summary.users_created.previous}
              tone="var(--series-created)"
              hint={`vs previous ${days}d`}
            />
            <StatTile
              label="Titles changed"
              value={summary.titles_changed.value}
              previous={summary.titles_changed.previous}
              tone="var(--series-titles)"
              hint={`vs previous ${days}d`}
            />
            <StatTile
              label="Failed writes"
              value={summary.failures.value}
              previous={summary.failures.previous}
              tone="var(--series-failed)"
              invertDelta
              hint={`vs previous ${days}d`}
            />
            <StatTile
              label="Sync runs"
              value={summary.sync_runs.value}
              previous={summary.sync_runs.previous}
              tone="var(--ink-muted)"
              hint={`${summary.users_scanned.toLocaleString()} users scanned`}
            />
          </section>
        )}

        <ActivityChart data={activity} themeKey={theme} />

        <div className="split">
          <EventsTable
            events={events}
            action={action}
            onActionChange={setAction}
            search={search}
            onSearchChange={setSearch}
            loading={eventsLoading}
          />
          <RunsTable runs={runs} />
        </div>
      </main>
    </div>
  );
}
