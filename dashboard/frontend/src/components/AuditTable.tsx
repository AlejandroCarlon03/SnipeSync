import { Fragment, useState } from "react";
import type { AuditRecord } from "../api";
import { formatUtc, relativeFromNow } from "../format";
import { SORTABLE_COLUMNS, type Sort, type SortKey } from "../lib/tableView";

interface AuditTableProps {
  /** Already search-filtered and sorted by applyTableView. */
  records: AuditRecord[];
  sort: Sort;
  onSortChange: (sort: Sort) => void;
  loading: boolean;
  /** True when a search is active but matched nothing, for a better empty state. */
  searching: boolean;
}

export function AuditTable({
  records,
  sort,
  onSortChange,
  loading,
  searching,
}: AuditTableProps) {
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const toggleSort = (key: SortKey) => {
    // First click on a new column sorts descending — for timestamps and counts
    // "most recent / biggest first" is nearly always what's wanted.
    onSortChange(
      sort.key === key
        ? { key, dir: sort.dir === "asc" ? "desc" : "asc" }
        : { key, dir: "desc" },
    );
  };

  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            {SORTABLE_COLUMNS.map((c) => (
              <th key={c.key}>
                <button
                  className={`th-sort ${sort.key === c.key ? "th-sort--on" : ""}`}
                  onClick={() => toggleSort(c.key)}
                  aria-label={`Sort by ${c.label}`}
                >
                  {c.label}
                  <span className="th-sort__arrow" aria-hidden="true">
                    {sort.key === c.key ? (sort.dir === "asc" ? "▲" : "▼") : ""}
                  </span>
                </button>
              </th>
            ))}
            <th>Detail</th>
            <th>Run</th>
          </tr>
        </thead>
        <tbody>
          {records.map((r) => {
            const open = expandedId === r.id;
            return (
              <Fragment key={r.id}>
                <tr
                  className="row-click"
                  onClick={() => setExpandedId(open ? null : r.id)}
                  aria-expanded={open}
                >
                  <td title={formatUtc(r.timestampUtc)}>
                    <span className="row-caret" aria-hidden="true">
                      {open ? "▾" : "▸"}
                    </span>
                    {relativeFromNow(r.timestampUtc)}
                  </td>
                  <td>{r.function}</td>
                  <td>{r.user}</td>
                  <td>
                    <span className="tag">{r.action}</span>
                  </td>
                  <td className="muted">{r.detail ?? r.newValue ?? ""}</td>
                  <td>{r.dryRun ? "dry-run" : "live"}</td>
                </tr>

                {open && (
                  <tr className="row-detail">
                    <td colSpan={6}>
                      {/* oldValue is not shown in the collapsed row at all — on a
                          MarkedFormerEmployee row this is the previous job title. */}
                      <dl className="detail-grid">
                        <Field label="Time (UTC)" value={formatUtc(r.timestampUtc)} />
                        <Field label="Function" value={r.function} />
                        <Field label="User" value={r.user} />
                        <Field label="Action" value={r.action} />
                        <Field label="Old value" value={r.oldValue} />
                        <Field label="New value" value={r.newValue} />
                        <Field label="Detail" value={r.detail} />
                        <Field label="Run" value={r.dryRun ? "dry-run" : "live"} />
                        <Field label="Month partition" value={r.ym} />
                        <Field label="Record id" value={r.id} />
                      </dl>
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}

          {records.length === 0 && !loading && (
            <tr>
              <td colSpan={6} className="muted center">
                {searching
                  ? "No records match that search."
                  : "No records for these filters."}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

function Field({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <div className="detail-grid__item">
      <dt>{label}</dt>
      <dd className={value ? "" : "muted"}>{value || "—"}</dd>
    </div>
  );
}
