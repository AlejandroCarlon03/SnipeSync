import { Fragment, useState, type ReactNode } from "react";
import type { AuditRecord, DeclinedPerson, RunEntry } from "../api";
import { formatLong, formatUtc } from "../format";
import type { ApprovalCheck } from "../lib/approvals";
import { OUTCOME_LABELS, STEP_SHORT, describeStep } from "../lib/syncText";
import { actionTag, lineForFunction } from "../lines";

interface RunsViewProps {
  runs: RunEntry[];
  loading: boolean;
  failed: boolean;
  /** Keeps a person or row on screen; the same predicate the flat table uses. */
  matches: (r: AuditRecord) => boolean;
  /** True while search or an action/function filter narrows the runs. */
  narrowed: boolean;
  isApproved: ApprovalCheck;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  renderDetail: (record: AuditRecord) => ReactNode;
  /** Shown when the host may hold older runs than the ones loaded. */
  onShowMore?: () => void;
}

const CHEVRON = (
  <svg className="run__chevron" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
    <path d="m4.5 3 3 3-3 3" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

/** "Disabled user, no match" for one or both declining actions. */
function declinedWhy(p: DeclinedPerson): string {
  const first = p.actions.includes("SkippedNoMatch");
  const retry = p.actions.includes("UnmatchedAfterReconciliation");
  if (first && retry) return "Skipped at offboard, still unmatched after retry";
  if (retry) return "Still unmatched after retry";
  return "No Snipe-IT match on the first pass";
}

/**
 * The audit trail grouped by sync run: each run lists what it changed in Snipe-IT, then who it
 * declined, folded to one line per person. Approved accounts collapse to a single quiet line.
 */
export function RunsView({
  runs,
  loading,
  failed,
  matches,
  narrowed,
  isApproved,
  selectedId,
  onSelect,
  renderDetail,
  onShowMore,
}: RunsViewProps) {
  // Explicit open/closed per run; a run never touched follows the default (newest open).
  const [open, setOpen] = useState<Record<string, boolean>>({});
  const [showApproved, setShowApproved] = useState<Record<string, boolean>>({});

  const visible = runs
    .map((run) => {
      const changes = run.changes.filter(matches);
      const declined = run.declined
        .filter((p) => matches(p.record))
        .map((p) => ({ ...p, approved: isApproved(p.user, p.email) }));
      return { run, changes, declined };
    })
    .filter((v) => !narrowed || v.changes.length + v.declined.length > 0);

  if (visible.length === 0) {
    return (
      <div className="runs">
        <div className="empty">
          <p className="empty__title">
            {failed
              ? "The run history didn't load."
              : loading
                ? "Loading runs…"
                : narrowed
                  ? "No run has a row that matches."
                  : "No sync runs in this range."}
          </p>
          <p className="empty__body">
            {failed
              ? "These runs are missing, not empty. Use Try again above once the host is back."
              : narrowed
                ? "Clear the search or filters, or switch to Every row."
                : !loading && "Widen the dates, or start one with Sync."}
          </p>
        </div>
      </div>
    );
  }

  const row = (r: AuditRecord, person: ReactNode, what: ReactNode, detail: ReactNode, fn: string, extra = "") => {
    const selected = selectedId === r.id;
    return (
      <Fragment key={r.id}>
        <button
          className={`run-row ${selected ? "run-row--selected" : ""} ${extra}`}
          aria-expanded={selected}
          onClick={() => onSelect(selected ? null : r.id)}
          data-id={r.id}
        >
          {person}
          <span>{what}</span>
          <span className="run-row__detail">{detail}</span>
          <span className="run-row__fn">{fn}</span>
        </button>
        {selected && <div className="run__detail">{renderDetail(r)}</div>}
      </Fragment>
    );
  };

  return (
    <div className="runs">
      {visible.map(({ run, changes, declined }, i) => {
        // Searching opens every run that matched, so the hits are on screen.
        const isOpen = narrowed || (open[run.runId] ?? i === 0);
        const waiting = declined.filter((p) => !p.approved);
        const approved = declined.filter((p) => p.approved);
        const approvedOpen = showApproved[run.runId] ?? false;
        const quiet = changes.length === 0 && waiting.length === 0;

        return (
          <div key={run.runId} className={`run ${isOpen ? "run--open" : ""}`}>
            <button
              className="run__head"
              aria-expanded={isOpen}
              onClick={() => setOpen((o) => ({ ...o, [run.runId]: !isOpen }))}
              title={`${formatUtc(run.startedAt)} UTC`}
            >
              {CHEVRON}
              <span className="run__when">{formatLong(run.startedAt)}</span>
              <span className={`chip ${run.dryRun ? "chip--dry" : ""}`}>{run.dryRun ? "Dry run" : "Live"}</span>
              <span className="run__spacer" />
              <span className="run__sum">
                {run.outcome === "failed" || run.outcome === "completedWithErrors" ? (
                  <span className="run__sum-bad">{OUTCOME_LABELS[run.outcome]}</span>
                ) : null}
                {changes.length > 0 ? (
                  <span className="run__sum-changes">
                    {changes.length} {changes.length === 1 ? "change" : "changes"}
                  </span>
                ) : (
                  <span>No changes</span>
                )}
                {waiting.length > 0 && (
                  <>
                    <span aria-hidden="true">·</span>
                    <span className="run__sum-declined">{waiting.length} declined</span>
                  </>
                )}
                {approved.length > 0 && (
                  <>
                    <span aria-hidden="true">·</span>
                    <span>{approved.length} approved</span>
                  </>
                )}
              </span>
            </button>

            {isOpen && (
              <div className="run__body">
                {run.steps && (
                  <p className="run__steps">
                    {run.steps.map((s) => (
                      <span key={s.step}>
                        <b>{STEP_SHORT[s.step]}</b> {describeStep(s, run.outcome === "failed")}
                      </span>
                    ))}
                  </p>
                )}
                {run.error && <p className="run__error">{run.error}</p>}

                {quiet && approved.length === 0 && (
                  <p className="run__quiet">Nothing changed in Snipe-IT and nobody was declined.</p>
                )}
                {quiet && approved.length > 0 && (
                  <p className="run__quiet">Nothing changed in Snipe-IT. Only approved accounts were skipped.</p>
                )}

                {changes.length > 0 && (
                  <>
                    <div className="run__cols" aria-hidden="true">
                      <span>Person</span>
                      <span>What happened</span>
                      <span>Detail</span>
                      <span>Function</span>
                    </div>
                    {changes.map((r) =>
                      row(
                        r,
                        <span className="person__text">
                          <span className="person__name">{r.user}</span>
                        </span>,
                        <span className={`tag tag--${actionTag(r.action)}`}>{r.action}</span>,
                        r.newValue && r.oldValue ? `${r.oldValue} → ${r.newValue}` : (r.detail ?? r.newValue ?? ""),
                        lineForFunction(r.function)?.name ?? r.function,
                      ),
                    )}
                  </>
                )}

                {waiting.length > 0 && (
                  <>
                    <p className="run__label">
                      Declined, no Snipe-IT record. Add the record, or approve the account under Needs a look.
                    </p>
                    {waiting.map((p) => declinedRow(p))}
                  </>
                )}

                {approved.length > 0 && (
                  <>
                    <button
                      className="run__approved-toggle"
                      aria-expanded={approvedOpen}
                      onClick={() => setShowApproved((o) => ({ ...o, [run.runId]: !approvedOpen }))}
                    >
                      <svg viewBox="0 0 12 12" width="10" height="10" aria-hidden="true">
                        <path d="m4.5 3 3 3-3 3" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
                      </svg>
                      {approved.length} approved {approved.length === 1 ? "account" : "accounts"} skipped as expected
                    </button>
                    {approvedOpen && approved.map((p) => declinedRow(p))}
                  </>
                )}
              </div>
            )}
          </div>
        );
      })}

      {onShowMore && (
        <div className="runs__foot">
          <button className="btn btn--ghost" onClick={onShowMore} disabled={loading}>
            Show older runs
          </button>
        </div>
      )}
    </div>
  );

  function declinedRow(p: DeclinedPerson) {
    return row(
      p.record,
      <span className="person__text">
        <span className="person__name">{p.user}</span>
        <span className="person__mail">{p.email ?? "no email recorded"}</span>
      </span>,
      <span className={`tag ${p.approved ? "tag--approved" : "tag--red"}`}>
        {p.approved ? "Approved, no match" : p.actions.length > 1 ? "No match, 2 tries" : "No match"}
      </span>,
      declinedWhy(p),
      p.functions.map((f) => lineForFunction(f)?.name ?? f).join(", "),
      p.approved ? "run-row--approved" : "",
    );
  }
}
