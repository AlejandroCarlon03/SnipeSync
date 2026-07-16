import type { HealthState, Summary } from "../api";

const RANGES = [7, 30, 90];

const HEALTH_TONE: Record<HealthState, string> = {
  good: "var(--status-good)",
  warning: "var(--status-warning)",
  critical: "var(--status-critical)",
  unknown: "var(--ink-muted)",
};

function HealthIcon({ state }: { state: HealthState }) {
  // Icon + label, so state never depends on colour alone.
  if (state === "good") {
    return (
      <svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true">
        <path d="M3.5 8.5 L6.5 11.5 L12.5 4.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }
  if (state === "unknown") {
    return (
      <svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true">
        <circle cx="8" cy="8" r="5.5" fill="none" stroke="currentColor" strokeWidth="2" />
      </svg>
    );
  }
  return (
    <svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true">
      <path d="M8 2.5 L14.5 13.5 L1.5 13.5 Z" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinejoin="round" />
      <path d="M8 6.5 L8 9.5" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" />
      <circle cx="8" cy="11.5" r="0.9" fill="currentColor" />
    </svg>
  );
}

interface HeaderProps {
  summary: Summary | null;
  days: number;
  onDaysChange: (d: number) => void;
  theme: "light" | "dark";
  onThemeToggle: () => void;
  onRefresh: () => void;
  refreshing: boolean;
}

export function Header({
  summary,
  days,
  onDaysChange,
  theme,
  onThemeToggle,
  onRefresh,
  refreshing,
}: HeaderProps) {
  const health = summary?.health;

  return (
    <header className="topbar">
      <div className="topbar__inner">
        <div className="brand">
          <span className="brand__mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="18" height="18">
              <path
                d="M4 7.5 C4 5.5 7.6 4 12 4 C16.4 4 20 5.5 20 7.5 L20 16.5 C20 18.5 16.4 20 12 20 C7.6 20 4 18.5 4 16.5 Z"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
              />
              <path d="M4 7.5 C4 9.5 7.6 11 12 11 C16.4 11 20 9.5 20 7.5" fill="none" stroke="currentColor" strokeWidth="1.8" />
              <path d="M4 12 C4 14 7.6 15.5 12 15.5 C16.4 15.5 20 14 20 12" fill="none" stroke="currentColor" strokeWidth="1.8" />
            </svg>
          </span>
          <div className="brand__text">
            <h1 className="brand__title">SnipeSync</h1>
            <p className="brand__sub">Entra ID → Snipe-IT</p>
          </div>
        </div>

        {health && (
          <div className="health" style={{ color: HEALTH_TONE[health.state] }}>
            <HealthIcon state={health.state} />
            <span className="health__label">{health.label}</span>
          </div>
        )}

        <div className="topbar__actions">
          <div className="segmented" role="group" aria-label="Time range">
            {RANGES.map((r) => (
              <button
                key={r}
                type="button"
                className={`segmented__btn ${days === r ? "is-active" : ""}`}
                aria-pressed={days === r}
                onClick={() => onDaysChange(r)}
              >
                {r}d
              </button>
            ))}
          </div>

          <button
            type="button"
            className="icon-btn"
            onClick={onRefresh}
            disabled={refreshing}
            aria-label="Refresh data"
            title="Refresh"
          >
            <svg className={refreshing ? "spin" : ""} viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
              <path
                d="M13.5 8 A5.5 5.5 0 1 1 11.6 3.9"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.6"
                strokeLinecap="round"
              />
              <path d="M13.8 1.6 L13.8 4.6 L10.8 4.6" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
          </button>

          <button
            type="button"
            className="icon-btn"
            onClick={onThemeToggle}
            aria-label={`Switch to ${theme === "dark" ? "light" : "dark"} theme`}
            title={`Switch to ${theme === "dark" ? "light" : "dark"} theme`}
          >
            {theme === "dark" ? (
              <svg viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
                <circle cx="8" cy="8" r="3.2" fill="none" stroke="currentColor" strokeWidth="1.6" />
                <path
                  d="M8 1.5 L8 3 M8 13 L8 14.5 M1.5 8 L3 8 M13 8 L14.5 8 M3.4 3.4 L4.5 4.5 M11.5 11.5 L12.6 12.6 M12.6 3.4 L11.5 4.5 M4.5 11.5 L3.4 12.6"
                  stroke="currentColor"
                  strokeWidth="1.6"
                  strokeLinecap="round"
                />
              </svg>
            ) : (
              <svg viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
                <path
                  d="M13.5 9.8 A6 6 0 1 1 6.2 2.5 A4.8 4.8 0 0 0 13.5 9.8 Z"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.6"
                  strokeLinejoin="round"
                />
              </svg>
            )}
          </button>
        </div>
      </div>
    </header>
  );
}
