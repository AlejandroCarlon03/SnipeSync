import { useMemo } from "react";
import type { AuditRecord } from "../api";
import { formatUtc, initials, relativeFromNow } from "../format";

/**
 * The two actions that mean "this Entra user has no Snipe-IT counterpart".
 * Verified 2026-07-24 against the live Snipe-IT instance: the users landing here
 * are genuinely absent from Snipe-IT (0 hits on both the email and the name
 * search, and on a loose surname search), not mis-matched. So this panel is a
 * reconciliation list for the original Entra->Snipe-IT import, not an error log.
 *
 * Caveat worth remembering: FindSnipeItUser also returns null when a name matches
 * *more than one* Snipe-IT user ("not guessing"), and that case is indistinguishable
 * from absence in the stored audit data. Someone appearing here with a common name
 * may be ambiguous rather than missing.
 */
const UNMATCHED_ACTIONS = ["SkippedNoMatch", "UnmatchedAfterReconciliation"];

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

export function UnmatchedPanel({ records }: { records: AuditRecord[] }) {
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

  if (rows.length === 0) return null;

  return (
    <section className="panel">
      <div className="panel__head">
        <h2>No Snipe-IT record</h2>
        <span className="muted">
          {rows.length} user{rows.length === 1 ? "" : "s"}
        </span>
      </div>

      <div className="panel__note">
        <div className="notice">
          <span aria-hidden="true">ⓘ</span>
          <span>
            These Entra accounts have <strong>no matching Snipe-IT user</strong>, so the
            sync had nothing to offboard. Each is a gap left by the original bulk import
            — the sync is behaving correctly. Create the Snipe-IT record (or confirm the
            person never needed one) to clear them from this list.
          </span>
        </div>
      </div>

      <div className="table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th>User</th>
              <th>Attempts</th>
              <th>First seen</th>
              <th>Last seen</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.user}>
                <td>
                  <span className="person">
                    <span className="avatar" aria-hidden="true">
                      {initials(r.user)}
                    </span>
                    <span className="person__text">
                      <span className="person__name">{r.user}</span>
                      <span className="person__mail">{r.email ?? "no email recorded"}</span>
                    </span>
                  </span>
                </td>
                <td>{r.attempts}</td>
                <td title={formatUtc(r.firstSeen)}>{relativeFromNow(r.firstSeen)}</td>
                <td title={formatUtc(r.lastSeen)}>{relativeFromNow(r.lastSeen)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
