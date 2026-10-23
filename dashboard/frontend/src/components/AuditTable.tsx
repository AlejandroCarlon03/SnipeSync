import { Fragment, type KeyboardEvent, type ReactNode } from "react";
import type { AuditRecord } from "../api";
import { formatLong, formatUtc } from "../format";
import { actionTag, isUnmatched, lineForFunction } from "../lines";
import { SORTABLE_COLUMNS, type Sort, type SortKey } from "../lib/tableView";

interface AuditTableProps {
  /** Already search-filtered and sorted by applyTableView. */
  records: AuditRecord[];
  sort: Sort;
  onSortChange: (sort: Sort) => void;
  loading: boolean;
  /** True when a search is active but matched nothing, for a better empty state. */
  searching: boolean;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  /** Clears search and filters from the empty state. */
  onReset: () => void;
  /** The last load failed: the table is empty because nothing arrived, not because nothing happened. */
  failed?: boolean;
  /** The selected row's details, drawn in a full-width row right under it. */
  renderDetail: (record: AuditRecord) => ReactNode;
}

const SKELETON_ROWS = 7;

export function AuditTable({
  records,
  sort,
  onSortChange,
  loading,
  searching,
  selectedId,
  onSelect,
  onReset,
  failed,
  renderDetail,
}: AuditTableProps) {
  const toggleSort = (key: SortKey) => {
    // First click on a new column sorts descending — for timestamps and counts
    // "most recent / biggest first" is nearly always what's wanted.
    onSortChange(
      sort.key === key
        ? { key, dir: sort.dir === "asc" ? "desc" : "asc" }
        : { key, dir: "desc" },
    );
  };

  // Rows are a roving list: Enter/Space opens, arrows walk, Escape closes the inspector.
  const onRowKey = (e: KeyboardEvent<HTMLTableRowElement>, id: string) => {
    if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      onSelect(selectedId === id ? null : id);
    } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      // Step over the open detail row: only record rows carry a data-id.
      const step = (el: Element | null) =>
        e.key === "ArrowDown" ? el?.nextElementSibling : el?.previousElementSibling;
      let sibling = step(e.currentTarget);
      while (sibling instanceof HTMLElement && !sibling.dataset.id) sibling = step(sibling);
      if (sibling instanceof HTMLElement) {
        sibling.focus();
        // Follow focus only when the inspector is already open, so arrowing isn't noisy.
        if (selectedId && sibling.dataset.id) onSelect(sibling.dataset.id);
      }
    } else if (e.key === "Escape" && selectedId) {
      onSelect(null);
    }
  };

  const showSkeleton = loading && records.length === 0;

  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            {SORTABLE_COLUMNS.map((c) => {
              const on = sort.key === c.key;
              return (
                <th
                  key={c.key}
                  aria-sort={on ? (sort.dir === "asc" ? "ascending" : "descending") : "none"}
                >
                  <button
                    className={`th-sort ${on ? "th-sort--on" : ""}`}
                    onClick={() => toggleSort(c.key)}
                  >
                    {c.label}
                    <svg
                      className={`th-sort__arrow ${on && sort.dir === "asc" ? "th-sort__arrow--up" : ""}`}
                      viewBox="0 0 10 10"
                      width="9"
                      height="9"
                      aria-hidden="true"
                    >
                      <path d="M2 3.5 5 6.5 8 3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  </button>
                </th>
              );
            })}
            <th>Detail</th>
          </tr>
        </thead>
        <tbody>
          {showSkeleton &&
            Array.from({ length: SKELETON_ROWS }, (_, i) => (
              <tr key={`sk-${i}`} className="row-skeleton" aria-hidden="true">
                {[56, 70, 90, 110, 160].map((w, j) => (
                  <td key={j}>
                    <span className="skeleton" style={{ width: w }} />
                  </td>
                ))}
              </tr>
            ))}

          {records.map((r) => {
            const selected = selectedId === r.id;
            const line = lineForFunction(r.function);
            const unmatched = isUnmatched(r.action);
            return (
              <Fragment key={r.id}>
                <tr
                  data-id={r.id}
                  className={`row ${selected ? "row--selected" : ""} ${unmatched ? "row--anomaly" : ""}`}
                  tabIndex={0}
                  aria-selected={selected}
                  aria-expanded={selected}
                  aria-label={`${r.action} for ${r.user}. ${selected ? "Hide details" : "Show details"}`}
                  onClick={() => onSelect(selected ? null : r.id)}
                  onKeyDown={(e) => onRowKey(e, r.id)}
                >
                  <td className="cell-time" title={`${formatUtc(r.timestampUtc)} UTC`}>
                    {formatLong(r.timestampUtc)}
                    {/* Live is the default, so only the exception is marked. */}
                    {r.dryRun && <span className="chip chip--dry">dry run</span>}
                  </td>
                  <td title={r.function}>
                    <span className="line-badge">{line?.name ?? r.function}</span>
                  </td>
                  <td className="cell-user">{r.user}</td>
                  <td>
                    <span className={`tag tag--${actionTag(r.action)}`}>
                      {r.action}
                    </span>
                  </td>
                  <td className="cell-detail">{r.detail ?? r.newValue ?? ""}</td>
                </tr>
                {selected && (
                  <tr className="row-detail">
                    <td colSpan={5}>{renderDetail(r)}</td>
                  </tr>
                )}
              </Fragment>
            );
          })}

          {records.length === 0 && !loading && failed && (
            <tr className="row-empty">
              <td colSpan={5}>
                <div className="empty">
                  <p className="empty__title">The audit trail didn't load.</p>
                  <p className="empty__body">
                    These rows are missing, not empty. Use Try again above once the host is back.
                  </p>
                </div>
              </td>
            </tr>
          )}

          {records.length === 0 && !loading && !failed && (
            <tr className="row-empty">
              <td colSpan={5}>
                <div className="empty">
                  <svg viewBox="0 0 64 20" width="64" height="20" aria-hidden="true">
                    <path d="M2 10h22M40 10h22" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
                    <circle cx="32" cy="10" r="6" fill="none" stroke="currentColor" strokeWidth="2" />
                  </svg>
                  <p className="empty__title">
                    {searching ? "No records match that search." : "No records for these filters."}
                  </p>
                  <p className="empty__body">
                    {searching
                      ? "Search looks at user, action, function, detail, and old and new values."
                      : "Nothing was audited in this range. Widen the dates or clear the filters."}
                  </p>
                  <button className="btn btn--ghost" onClick={onReset}>
                    {searching ? "Clear search" : "Reset filters"}
                  </button>
                </div>
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
