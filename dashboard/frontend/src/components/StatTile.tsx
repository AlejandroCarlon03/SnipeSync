import type { CSSProperties } from "react";

interface StatTileProps {
  label: string;
  value: number;
  previous: number;
  /** The line ink this count belongs to; ties the tile to its series in the chart. */
  tone: string;
  /** For metrics where a fall is the good outcome (failures). */
  invertDelta?: boolean;
  hint: string;
  /** Raised to the anomaly treatment when the value is non-zero. */
  anomaly?: boolean;
  /** First load in flight: show a placeholder rather than a misleading 0. */
  pending?: boolean;
  /** The board states "no prior period" once, so tiles skip that delta. */
  hideNoBaseline?: boolean;
  /** The load failed: show "–", not a placeholder that implies it is still coming. */
  unknown?: boolean;
}

function formatDelta(value: number, previous: number): { text: string; pct: number } | null {
  if (previous === 0) {
    // No baseline to compare against -- a "+100%" here would be an invention.
    return value === 0 ? null : { text: "none in prior period", pct: 0 };
  }
  const pct = ((value - previous) / previous) * 100;
  const rounded = Math.round(pct);
  if (rounded === 0) return { text: "same as prior period", pct: 0 };
  return { text: `${rounded > 0 ? "+" : ""}${rounded}% vs prior`, pct };
}

export function StatTile({
  label,
  value,
  previous,
  tone,
  invertDelta,
  hint,
  anomaly,
  pending,
  hideNoBaseline,
  unknown,
}: StatTileProps) {
  const delta = hideNoBaseline && previous === 0 ? null : formatDelta(value, previous);

  // Direction is the raw movement; sentiment is whether that movement is good.
  const rising = delta ? delta.pct > 0 : false;
  const flat = !delta || delta.pct === 0;
  const good = invertDelta ? !rising : rising;

  const sentiment = flat ? "flat" : good ? "good" : "bad";
  const alert = Boolean(anomaly && value > 0 && !pending);

  return (
    <article
      className={`tile ${alert ? "tile--alert" : ""}`}
      style={{ "--tone": tone } as CSSProperties}
    >
      <div className="tile__head">
        <span className="tile__station" aria-hidden="true" />
        <h3 className="tile__label">{label}</h3>
      </div>

      <div className="tile__row">
        <div className="tile__value" aria-busy={pending || undefined}>
          {unknown ? "–" : pending ? <span className="skeleton skeleton--num" /> : value.toLocaleString()}
        </div>
        <p className="tile__hint">{hint}</p>
      </div>

      <div className="tile__foot">
        {delta && !pending && !unknown && (
          <span className={`tile__delta tile__delta--${sentiment}`}>
            {!flat && (
              <svg
                className={`tile__arrow ${rising ? "" : "tile__arrow--down"}`}
                viewBox="0 0 12 12"
                width="12"
                height="12"
                aria-hidden="true"
              >
                <path
                  d="M6 2.5 L6 9.5 M6 2.5 L3 5.5 M6 2.5 L9 5.5"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.6"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
            )}
            {delta.text}
          </span>
        )}
      </div>
    </article>
  );
}
