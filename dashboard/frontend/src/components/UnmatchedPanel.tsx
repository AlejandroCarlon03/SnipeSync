import { useMemo, useState } from "react";
import type { Approval, AuditRecord } from "../api";
import { formatUtc, relativeFromNow } from "../format";
import { looksLikeEmail, type ApprovalCheck } from "../lib/approvals";
import { UNMATCHED_ACTIONS } from "../lines";

/*
 * UNMATCHED_ACTIONS are the two actions that mean "this Entra user has no Snipe-IT
 * counterpart". Verified 2026-07-24 against the live Snipe-IT instance: the users landing
 * here are genuinely absent from Snipe-IT (0 hits on both the email and the name search,
 * and on a loose surname search), not mis-matched. So this panel is a reconciliation list
 * for the original Entra->Snipe-IT import, not an error log.
 *
 * Caveat worth remembering: FindSnipeItUser also returns null when a name matches
 * *more than one* Snipe-IT user ("not guessing"), and that case is indistinguishable
 * from absence in the stored audit data. Someone appearing here with a common name
 * may be ambiguous rather than missing.
 *
 * Approved accounts (shared mailboxes and the like, signed off in the app) drop out of the
 * list. The sync still audits them; they just stop asking for attention.
 */

interface Row {
  user: string;
  email: string | null;
  attempts: number;
  firstSeen: string;
  lastSeen: string;
}

export function UnmatchedPanel({
  records,
  approvals,
  isApproved,
  onPick,
  onApprove,
  onUnapprove,
  busy,
  error,
  now,
  failed,
}: {
  records: AuditRecord[];
  approvals: Approval[];
  isApproved: ApprovalCheck;
  /** Narrows the audit trail to this person. */
  onPick: (user: string) => void;
  onApprove: (user: string, email: string | null) => void;
  onUnapprove: (key: string) => void;
  /** An approve or undo is in flight; its key (or user) disables that row's button. */
  busy: string | null;
  error: string | null;
  now: number;
  /** Nothing has loaded (still loading, or the load failed), so an empty list proves nothing. */
  failed?: boolean;
}) {
  const [showApproved, setShowApproved] = useState(false);

  const rows = useMemo<Row[]>(() => {
    const byUser = new Map<string, Row>();

    for (const r of records) {
      if (!UNMATCHED_ACTIONS.includes(r.action)) continue;

      const existing = byUser.get(r.user);
      if (!existing) {
        byUser.set(r.user, {
          user: r.user,
          email: looksLikeEmail(r.detail) ? r.detail : null,
          attempts: 1,
          firstSeen: r.timestampUtc,
          lastSeen: r.timestampUtc,
        });
        continue;
      }

      existing.attempts += 1;
      if (r.timestampUtc < existing.firstSeen) existing.firstSeen = r.timestampUtc;
      if (r.timestampUtc > existing.lastSeen) existing.lastSeen = r.timestampUtc;
      existing.email ??= looksLikeEmail(r.detail) ? r.detail : null;
    }

    return [...byUser.values()]
      .filter((r) => !isApproved(r.user, r.email))
      .sort((a, b) => b.attempts - a.attempts || a.user.localeCompare(b.user));
  }, [records, isApproved]);

  const approvedList = approvals.length > 0 && (
    <div className="approved">
      <button
        className="approved__toggle"
        aria-expanded={showApproved}
        onClick={() => setShowApproved((v) => !v)}
      >
        <svg viewBox="0 0 12 12" width="10" height="10" aria-hidden="true">
          <path d="m4.5 3 3 3-3 3" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        {approvals.length} approved {approvals.length === 1 ? "account" : "accounts"}, not flagged
      </button>
      {showApproved && (
        <ul className="approved__list">
          {approvals.map((a) => (
            <li className="approved__item" key={a.key}>
              <span className="person__text">
                <span className="person__name">{a.user}</span>
                <span className="person__mail">
                  {a.email ?? "no email recorded"} · approved {relativeFromNow(a.approvedAt, now)}
                </span>
                {a.note && <span className="approved__note">{a.note}</span>}
              </span>
              <button
                className="btn btn--ghost btn--small"
                disabled={busy === a.key}
                onClick={() => onUnapprove(a.key)}
                title="Flag this account again when the sync can't match it"
              >
                Undo
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );

  const head = (
    <div className="panel__head">
      <h2>Needs a look</h2>
      {rows.length > 0 && (
        <p className="panel__sub">
          {rows.length} {rows.length === 1 ? "person" : "people"} · no Snipe-IT record
        </p>
      )}
    </div>
  );

  if (rows.length === 0) {
    return (
      <section className="panel" aria-label="Needs a look">
        {head}
        <p className="panel__clear">
          {failed ? (
            "Unknown until the audit trail loads."
          ) : (
            <>
              <span className="clear-mark" aria-hidden="true" />
              {approvals.length > 0
                ? "Nobody new. Every unmatched account in this range is approved."
                : "Every disabled Entra user in this range matched a Snipe-IT record."}
            </>
          )}
        </p>
        {error && <p className="approve-error">{error}</p>}
        {approvedList}
      </section>
    );
  }

  return (
    <section className="panel panel--anomaly" aria-label="Needs a look">
      {head}
      <p className="panel__note">
        Gaps from the original Snipe-IT import, not sync failures. Add the Snipe-IT record, or
        approve an account that never needs one.
      </p>

      <ul className="unmatched">
        {rows.map((r) => (
          <li key={r.user} className="unmatched__item">
            <button
              className="unmatched__row"
              onClick={() => onPick(r.user)}
              title={`Show ${r.user}'s records. First seen ${relativeFromNow(r.firstSeen, now)} (${formatUtc(r.firstSeen)} UTC), last seen ${relativeFromNow(r.lastSeen, now)} (${formatUtc(r.lastSeen)} UTC).`}
            >
              <span className="person__text">
                <span className="person__name">{r.user}</span>
                <span className="person__mail">{r.email ?? "no email recorded"}</span>
              </span>
              <span className="unmatched__attempts">
                <strong>{r.attempts}</strong> attempt{r.attempts === 1 ? "" : "s"}
              </span>
            </button>
            <button
              className="btn btn--ghost btn--small unmatched__approve"
              disabled={busy === r.user}
              onClick={() => onApprove(r.user, r.email)}
              title="Mark this account as fine without a Snipe-IT record, so it stops being flagged"
            >
              Approve
            </button>
          </li>
        ))}
      </ul>
      {error && <p className="approve-error">{error}</p>}
      {approvedList}
    </section>
  );
}
