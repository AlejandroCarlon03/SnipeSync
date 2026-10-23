import { useMemo } from "react";
import type { AuditRecord } from "../api";
import { formatUtc, relativeFromNow } from "../format";
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
 */

interface Row {
  user: string;
  email: string | null;
  attempts: number;
  firstSeen: string;
  lastSeen: string;
}

function looksLikeEmail(value: string | null | undefined): boolean {
  return Boolean(value && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value));
}

export function UnmatchedPanel({
  records,
  onPick,
  now,
  compact,
  failed,
}: {
  records: AuditRecord[];
  /** Narrows the audit table to this person. */
  onPick: (user: string) => void;
  now: number;
  /** Beside the inspector: the list only, without the explanatory note. */
  compact?: boolean;
  /** Nothing has loaded (still loading, or the load failed), so an empty list proves nothing. */
  failed?: boolean;
}) {
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
      // Rows arrive newest-first from the API, but don't rely on it.
      if (r.timestampUtc < existing.firstSeen) existing.firstSeen = r.timestampUtc;
      if (r.timestampUtc > existing.lastSeen) existing.lastSeen = r.timestampUtc;
      existing.email ??= looksLikeEmail(r.detail) ? r.detail : null;
    }

    return [...byUser.values()].sort((a, b) => b.attempts - a.attempts);
  }, [records]);

  // Standard competition ranking: 1 + how many people have strictly more attempts.
  const rankOf = (r: Row) => 1 + rows.filter((o) => o.attempts > r.attempts).length;

  if (rows.length === 0 && failed) {
    return (
      <section className="panel">
        <div className="panel__head">
          <h2>Needs a look</h2>
        </div>
        <p className="panel__clear">Unknown until the audit trail loads.</p>
      </section>
    );
  }

  if (rows.length === 0) {
    return (
      <section className="panel">
        <div className="panel__head">
          <h2>Needs a look</h2>
        </div>
        <p className="panel__clear">
          <span className="clear-mark" aria-hidden="true" />
          Every disabled Entra user in this range matched a Snipe-IT record.
        </p>
      </section>
    );
  }

  return (
    <section className="panel panel--anomaly">
      <div className="panel__head">
        <div>
          <h2>Needs a look</h2>
          <p className="panel__sub">
            {rows.length} {rows.length === 1 ? "person" : "people"} with no Snipe-IT record ·{" "}
            {rows.reduce((n, r) => n + r.attempts, 0)} no-match decisions
          </p>
        </div>
      </div>

      {!compact && (
      <p className="panel__note">
        These Entra accounts have <strong>no matching Snipe-IT user</strong>, so the sync had
        nothing to offboard. Each is a gap left by the original bulk import, not a sync
        failure. Create the Snipe-IT record (or confirm the person never needed one) to clear
        them from this list.
      </p>
      )}

      <ul className="unmatched">
        {rows.map((r) => (
          <li key={r.user}>
            <button
              className="unmatched__row"
              onClick={() => onPick(r.user)}
              title={`Show ${r.user}'s records in the audit trail`}
            >
              {/* Ranked by attempts; people tied on attempts share a rank. */}
              <span className="rank" aria-label={`Rank ${rankOf(r)}`}>
                {rankOf(r)}
              </span>
              <span className="person__text">
                <span className="person__name">{r.user}</span>
                <span className="person__mail">{r.email ?? "no email recorded"}</span>
              </span>
              <span className="unmatched__stats">
                <span className="unmatched__attempts">
                  {r.attempts} <span className="muted">attempt{r.attempts === 1 ? "" : "s"}</span>
                </span>
                <span
                  className="unmatched__seen"
                  title={`First seen ${formatUtc(r.firstSeen)} UTC, last seen ${formatUtc(r.lastSeen)} UTC`}
                >
                  first {relativeFromNow(r.firstSeen, now)} · last {relativeFromNow(r.lastSeen, now)}
                </span>
              </span>
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}
