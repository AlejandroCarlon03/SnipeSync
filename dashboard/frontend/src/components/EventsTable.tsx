import type { ActionName, SyncEvent } from "../api";

const ACTION_META: Record<ActionName, { label: string; tone: string }> = {
  user_created: { label: "Created", tone: "var(--series-created)" },
  title_changed: { label: "Title changed", tone: "var(--series-titles)" },
  failed: { label: "Failed", tone: "var(--series-failed)" },
  skipped: { label: "Skipped", tone: "var(--ink-muted)" },
};

const FILTERS: { value: string; label: string }[] = [
  { value: "all", label: "All" },
  { value: "user_created", label: "Created" },
  { value: "title_changed", label: "Titles" },
  { value: "failed", label: "Failed" },
];

function timestamp(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function initials(name: string): string {
  return name
    .split(" ")
    .map((p) => p[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

interface EventsTableProps {
  events: SyncEvent[];
  action: string;
  onActionChange: (a: string) => void;
  search: string;
  onSearchChange: (s: string) => void;
  loading: boolean;
}

export function EventsTable({
  events,
  action,
  onActionChange,
  search,
  onSearchChange,
  loading,
}: EventsTableProps) {
  return (
    <section className="card">
      <header className="card__head">
        <div>
          <h2 className="card__title">Activity log</h2>
          <p className="card__sub">Every user the sync touched, newest first</p>
        </div>

        <div className="controls">
          <div className="search">
            <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
              <circle cx="7" cy="7" r="4.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
              <path d="M10.5 10.5 L14 14" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
            </svg>
            <input
              type="search"
              placeholder="Search name, email, detail"
              value={search}
              onChange={(e) => onSearchChange(e.target.value)}
              aria-label="Search activity log"
            />
          </div>

          <div className="segmented" role="group" aria-label="Filter by action">
            {FILTERS.map((f) => (
              <button
                key={f.value}
                type="button"
                className={`segmented__btn ${action === f.value ? "is-active" : ""}`}
                aria-pressed={action === f.value}
                onClick={() => onActionChange(f.value)}
              >
                {f.label}
              </button>
            ))}
          </div>
        </div>
      </header>

      <div className="card__body card__body--flush">
        {loading ? (
          <div className="empty">Loading…</div>
        ) : events.length === 0 ? (
          <div className="empty">
            No matching activity{search ? ` for “${search}”` : ""}.
          </div>
        ) : (
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">User</th>
                  <th scope="col">Action</th>
                  <th scope="col">Detail</th>
                  <th scope="col" className="table__num">When</th>
                </tr>
              </thead>
              <tbody>
                {events.map((e) => {
                  const meta = ACTION_META[e.action];
                  return (
                    <tr key={e.id}>
                      <td>
                        <div className="person">
                          <span className="avatar" aria-hidden="true">{initials(e.display_name)}</span>
                          <div className="person__text">
                            <span className="person__name">{e.display_name}</span>
                            <span className="person__mail">{e.email}</span>
                          </div>
                        </div>
                      </td>
                      <td>
                        {/* Swatch + text: the action never rides on colour alone. */}
                        <span className="badge">
                          <span className="badge__dot" style={{ background: meta.tone }} aria-hidden="true" />
                          {meta.label}
                        </span>
                      </td>
                      <td className="table__detail">{e.detail}</td>
                      <td className="table__num table__when">{timestamp(e.timestamp)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}
