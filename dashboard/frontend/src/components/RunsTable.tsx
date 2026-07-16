import type { SyncRun } from "../api";

function when(iso: string): string {
  const then = new Date(iso).getTime();
  const mins = Math.round((Date.now() - then) / 60000);
  if (mins < 60) return `${Math.max(mins, 0)}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

const FUNCTION_LABEL: Record<string, string> = {
  OnboardEmployeeSync: "Onboard",
  FormerEmployeeSync: "Offboard",
};

export function RunsTable({ runs }: { runs: SyncRun[] }) {
  return (
    <section className="card">
      <header className="card__head">
        <div>
          <h2 className="card__title">Recent runs</h2>
          <p className="card__sub">Both functions run daily at 02:00</p>
        </div>
      </header>

      <div className="card__body card__body--flush">
        <div className="table-scroll table-scroll--short">
          <ul className="runs">
            {runs.map((r) => {
              const failed = r.failed > 0;
              const wrote = r.created + r.title_changed;
              return (
                <li key={r.id} className="run">
                  <span
                    className={`run__status run__status--${failed ? "critical" : "good"}`}
                    aria-hidden="true"
                  />

                  <div className="run__main">
                    <div className="run__title">
                      <span className="run__fn">{FUNCTION_LABEL[r.function] ?? r.function}</span>
                      {r.dry_run && <span className="chip chip--dry">Dry run</span>}
                      {failed && <span className="chip chip--fail">{r.failed} failed</span>}
                    </div>
                    <div className="run__meta">
                      {wrote === 0 ? "No changes needed" : `${wrote} change${wrote === 1 ? "" : "s"}`}
                      {" · "}
                      {r.users_scanned} scanned
                      {" · "}
                      {r.duration_seconds.toFixed(0)}s
                    </div>
                  </div>

                  <time className="run__when" dateTime={r.started_at}>
                    {when(r.started_at)}
                  </time>
                </li>
              );
            })}
          </ul>
        </div>
      </div>
    </section>
  );
}
