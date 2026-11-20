/**
 * Approved accounts: people signed off in the app as fine without a Snipe-IT record (shared
 * mailboxes and the like). The host keys an approval by email when it has one, else by name,
 * and matches a person on either; this mirrors ApprovalMatcher in Approvals.cs so the screen
 * agrees with the host before the next refetch.
 */
import type { Approval } from "../api";

export type ApprovalCheck = (user: string, email?: string | null) => boolean;

const norm = (s: string) => s.trim().toLowerCase();

export function approvalChecker(approvals: Approval[]): ApprovalCheck {
  const keys = new Set<string>();
  for (const a of approvals) {
    keys.add(norm(a.key));
    keys.add(norm(a.user));
    if (a.email) keys.add(norm(a.email));
  }
  return (user, email) => keys.has(norm(user)) || (email ? keys.has(norm(email)) : false);
}

export function looksLikeEmail(value: string | null | undefined): value is string {
  return Boolean(value && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value));
}
