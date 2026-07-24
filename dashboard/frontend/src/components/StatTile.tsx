interface StatTileProps {
  label: string;
  value: number;
  previous: number;
  /** The swatch beside the label ties the tile to its series in the chart. */
  tone: string;
  /** For metrics where a fall is the good outcome (failures). */
  invertDelta?: boolean;
  hint: string;
}

function formatDelta(value: number, previous: number): { text: string; pct: number } | null {
  if (previous === 0) {
    // No baseline to compare against -- a "+100%" here would be an invention.
    return value === 0 ? null : { text: "new", pct: 0 };
  }
  const pct = ((value - previous) / previous) * 100;
  const rounded = Math.round(pct);
  if (rounded === 0) return { text: "no change", pct: 0 };
  return { text: `${rounded > 0 ? "+" : ""}${rounded}%`, pct };
}

export function StatTile({ label, value, previous, tone, invertDelta, hint }: StatTileProps) {
  const delta = formatDelta(value, previous);

  // Direction is the raw movement; sentiment is whether that movement is good.
  const rising = delta ? delta.pct > 0 : false;
  const flat = !delta || delta.pct === 0;
  const good = invertDelta ? !rising : rising;

  const sentiment = flat ? "flat" : good ? "good" : "bad";

  return (
    <article className="tile">
      <div className="tile__head">
        <span className="tile__swatch" style={{ background: tone }} aria-hidden="true" />
        <h3 className="tile__label">{label}</h3>
      </div>

      <div className="tile__value">{value.toLocaleString()}</div>

      <div className="tile__foot">
        {delta && (
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
                  strokeWidth="1.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
            )}
            {delta.text}
          </span>
        )}
        <span className="tile__hint">{hint}</span>
      </div>
    </article>
  );
}
