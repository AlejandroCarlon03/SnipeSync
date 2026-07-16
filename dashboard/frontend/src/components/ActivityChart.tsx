import { useLayoutEffect, useRef, useState } from "react";
import { Bar, BarChart, CartesianGrid, Tooltip, XAxis, YAxis } from "recharts";
import type { ActivityPoint } from "../api";

const CHART_HEIGHT = 280;

/**
 * Measures an element's width so the chart can be given explicit dimensions.
 *
 * Recharts' ResponsiveContainer would normally do this, but it depends solely on
 * ResizeObserver -- and where ResizeObserver is unavailable or never fires, it
 * measures 0 and renders nothing at all. Measuring here takes a width from the
 * initial synchronous read and then keeps it current from either ResizeObserver
 * or the window resize event, so the chart still draws if one of them is absent.
 */
function useMeasuredWidth<T extends HTMLElement>() {
  const ref = useRef<T | null>(null);
  const [width, setWidth] = useState(0);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;

    // Synchronous first read: never depends on an observer to draw at all.
    const update = () => setWidth(el.clientWidth);
    update();

    window.addEventListener("resize", update);

    let observer: ResizeObserver | undefined;
    if (typeof ResizeObserver !== "undefined") {
      observer = new ResizeObserver(update);
      observer.observe(el);
    }

    return () => {
      window.removeEventListener("resize", update);
      observer?.disconnect();
    };
  }, []);

  return [ref, width] as const;
}

/** Reads a token off :root so Recharts (which needs real values, not var()) can use it. */
function token(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

const SERIES = [
  { key: "users_created", label: "Users created", token: "--series-created" },
  { key: "titles_changed", label: "Titles changed", token: "--series-titles" },
  { key: "failures", label: "Failed writes", token: "--series-failed" },
] as const;

function shortDate(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

function ChartTooltip({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;

  const total = payload.reduce((sum: number, p: any) => sum + (p.value ?? 0), 0);

  return (
    <div className="tooltip">
      <div className="tooltip__title">{shortDate(label)}</div>
      {total === 0 ? (
        <div className="tooltip__empty">No sync activity</div>
      ) : (
        <ul className="tooltip__list">
          {payload
            .slice()
            .reverse()
            .map((p: any) => (
              <li key={p.dataKey} className="tooltip__row">
                <span className="tooltip__swatch" style={{ background: p.color }} />
                <span className="tooltip__label">
                  {SERIES.find((s) => s.key === p.dataKey)?.label ?? p.dataKey}
                </span>
                <span className="tooltip__value">{p.value}</span>
              </li>
            ))}
        </ul>
      )}
    </div>
  );
}

/**
 * Rounds the data-end of the stack rather than of each segment.
 *
 * Recharts applies `radius` per Bar, so on a day with no failures the top-most
 * drawn segment would be a different series and would render flat. This picks
 * the last series with a non-zero value in the row and rounds only that one.
 */
function StackedBar(props: any) {
  const { fill, x, y, width, height, stroke, strokeWidth, payload, dataKey } = props;
  if (!height || height <= 0) return null;

  const topMost = [...SERIES].reverse().find((s) => (payload?.[s.key] ?? 0) > 0);
  const isTop = topMost?.key === dataKey;
  const r = isTop ? Math.min(4, width / 2, height) : 0;

  // Path: bottom-left, up, optional rounded top corners, down, close.
  const d = `
    M ${x} ${y + height}
    L ${x} ${y + r}
    ${r ? `Q ${x} ${y} ${x + r} ${y}` : ""}
    L ${x + width - r} ${y}
    ${r ? `Q ${x + width} ${y} ${x + width} ${y + r}` : ""}
    L ${x + width} ${y + height}
    Z
  `;

  return <path d={d} fill={fill} stroke={stroke} strokeWidth={strokeWidth} />;
}

interface ActivityChartProps {
  data: ActivityPoint[];
  /** Bumped on theme change so the chart re-reads its colour tokens. */
  themeKey: string;
}

export function ActivityChart({ data, themeKey }: ActivityChartProps) {
  const [wrapRef, width] = useMeasuredWidth<HTMLDivElement>();

  // themeKey is read so the colours below are re-resolved when the theme flips.
  void themeKey;
  const colors = SERIES.map((s) => token(s.token));
  const grid = token("--gridline");
  const axis = token("--ink-muted");
  const surface = token("--surface");

  const isEmpty = data.every(
    (d) => d.users_created + d.titles_changed + d.failures === 0
  );

  return (
    <section className="card card--chart">
      <header className="card__head">
        <div>
          <h2 className="card__title">Sync activity</h2>
          <p className="card__sub">Actions written to Snipe-IT per day</p>
        </div>

        {/* Legend is always present: with three series, colour alone must not
            be the only channel carrying identity. */}
        <ul className="legend">
          {SERIES.map((s, i) => (
            <li key={s.key} className="legend__item">
              <span className="legend__swatch" style={{ background: colors[i] }} aria-hidden="true" />
              {s.label}
            </li>
          ))}
        </ul>
      </header>

      <div className="card__body" ref={wrapRef}>
        {isEmpty ? (
          <div className="empty">No sync activity in this period.</div>
        ) : width === 0 ? (
          // First paint, before measurement: reserve the height so nothing jumps.
          <div style={{ height: CHART_HEIGHT }} />
        ) : (
          <BarChart
            width={width}
            height={CHART_HEIGHT}
            data={data}
            margin={{ top: 8, right: 8, bottom: 0, left: -18 }}
            barCategoryGap="22%"
          >
              <CartesianGrid stroke={grid} strokeDasharray="0" vertical={false} />
              <XAxis
                dataKey="date"
                tickFormatter={shortDate}
                stroke={grid}
                tick={{ fill: axis, fontSize: 11 }}
                tickLine={false}
                axisLine={{ stroke: grid }}
                minTickGap={28}
              />
              <YAxis
                stroke={grid}
                tick={{ fill: axis, fontSize: 11 }}
                tickLine={false}
                axisLine={false}
                allowDecimals={false}
                width={44}
              />
              <Tooltip
                content={<ChartTooltip />}
                cursor={{ fill: "var(--accent-wash)" }}
              />
              {SERIES.map((s, i) => (
                <Bar
                  key={s.key}
                  dataKey={s.key}
                  stackId="a"
                  fill={colors[i]}
                  // A hairline of the surface colour separates stacked fills.
                  stroke={surface}
                  strokeWidth={1}
                  shape={<StackedBar />}
                  isAnimationActive={false}
                />
              ))}
          </BarChart>
        )}
      </div>
    </section>
  );
}
