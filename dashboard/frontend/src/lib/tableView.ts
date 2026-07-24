/**
 * Search + sort for the audit table. Kept out of the component file so App can
 * derive the visible rows (the Excel export needs them) without importing a
 * component module for its non-component exports.
 */
import type { AuditRecord } from "../api";

export type SortKey = "timestampUtc" | "function" | "user" | "action";

export interface Sort {
  key: SortKey;
  dir: "asc" | "desc";
}

export const DEFAULT_SORT: Sort = { key: "timestampUtc", dir: "desc" };

export const SORTABLE_COLUMNS: { key: SortKey; label: string }[] = [
  { key: "timestampUtc", label: "Time (UTC)" },
  { key: "function", label: "Function" },
  { key: "user", label: "User" },
  { key: "action", label: "Action" },
];

/** The fields free-text search looks at — everything a human might type. */
const SEARCHABLE: (keyof AuditRecord)[] = [
  "user",
  "action",
  "function",
  "detail",
  "oldValue",
  "newValue",
];

export function applyTableView(
  records: AuditRecord[],
  search: string,
  sort: Sort,
): AuditRecord[] {
  const needle = search.trim().toLowerCase();

  const filtered = needle
    ? records.filter((r) =>
        SEARCHABLE.some((f) => String(r[f] ?? "").toLowerCase().includes(needle)),
      )
    : records;

  // Copy before sorting — the caller's array is React state.
  return [...filtered].sort((a, b) => {
    const av = String(a[sort.key] ?? "");
    const bv = String(b[sort.key] ?? "");
    // Cosmos timestamps are ISO, so lexicographic order is chronological order.
    const cmp = av.localeCompare(bv);
    return sort.dir === "asc" ? cmp : -cmp;
  });
}
