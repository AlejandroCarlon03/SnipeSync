import { useLayoutEffect, useMemo, useRef, useState } from "react";
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { AuditStats } from "../api";
import { actionToken, isUnmatched } from "../lines";

const MONTH_HEIGHT = 220;
const BAR_SIZE = 14;

/**
 * Measures the container width so the chart gets explicit dimensions. Recharts'
 * ResponsiveContainer relies solely on ResizeObserver and renders nothing where
 * it never fires; a synchronous first read plus resize/observer updates always draws.
 * (Salvaged from the old dashboard branch.)
 */
function useMeasuredWidth<T extends HTMLElement>() {
  const ref = useRef<T | null>(null);
  const [width, setWidth] = useState(0);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
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

/** Reads a CSS custom property off :root — Recharts needs real values, not var(). */
function token(name: string, fallback: string): string {
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v || fallback;
}

type Mode = "action" | "month";

/**
 * `theme` isn't read directly — it's the signal that the CSS custom properties
 * under it have changed. Recharts needs literal colour values, so `token()`
 * resolves them during render; without a prop tying this component to the theme,
 * a toggle would leave the chart painted in the previous palette.
 */
export function ActivityChart({
  stats,
  theme,
  loading,
}: {
  stats: AuditStats | null;
  theme: string;
  loading?: boolean;
}) {
  const [mode, setMode] = useState<Mode>("action");
  const [ref, width] = useMeasuredWidth<HTMLDivElement>();

  const data = useMemo(() => {
    if (!stats) return [];
    if (mode === "action") {
      // Already largest-first from the API.
      return stats.byAction.map((b) => ({ label: b.key ?? "(none)", count: b.count }));
    }
    // Chronological for the month trend.
    return [...stats.byMonth]
      .map((b) => ({ label: b.key ?? "(none)", count: b.count }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }, [stats, mode]);

  // Re-resolved whenever the theme flips. `theme` is the cache key, not an input: the
  // values live in CSS custom properties read off the DOM, so nothing inside this memo
  // can reference it.
  const colors = useMemo(
    () => ({
      grid: token("--rule", "#dde3ee"),
      axis: token("--ink-muted", "#56647f"),
      ink: token("--ink", "#0a1733"),
      surface: token("--surface-2", "#f2f5fa"),
      rule: token("--rule-strong", "#c4cddd"),
      accent: token("--accent", "#0a7491"),
      tones: Object.fromEntries(
        (stats?.byAction ?? []).map((b) => [b.key ?? "", token(actionToken(b.key ?? ""), "#0a7491")]),
      ) as Record<string, string>,
    }),
    // oxlint-disable-next-line react-hooks/exhaustive-deps
    [theme, stats],
  );

  const actionHeight = Math.max(140, data.length * (BAR_SIZE + 16) + 32);
  const total = data.reduce((acc, d) => acc + d.count, 0);

  const tooltip = {
    cursor: { fill: colors.grid },
    contentStyle: {
      background: colors.surface,
      border: `1px solid ${colors.rule}`,
      borderRadius: 4,
      color: colors.ink,
      fontSize: 12,
      padding: "6px 10px",
    },
    labelStyle: { color: colors.ink, fontWeight: 600 },
    itemStyle: { color: colors.ink, padding: 0 },
  };

  return (
    <section className="panel">
      <div className="panel__head">
        <div>
          <h2>{mode === "action" ? "Decisions by action" : "Decisions by month"}</h2>
          <p className="panel__sub">{stats ? `${total.toLocaleString()} in range` : "not loaded"}</p>
        </div>
        <div className="seg" role="group" aria-label="Chart grouping">
          <button
            className={`seg__btn ${mode === "action" ? "seg__btn--on" : ""}`}
            aria-pressed={mode === "action"}
            onClick={() => setMode("action")}
          >
            By action
          </button>
          <button
            className={`seg__btn ${mode === "month" ? "seg__btn--on" : ""}`}
            aria-pressed={mode === "month"}
            onClick={() => setMode("month")}
          >
            By month
          </button>
        </div>
      </div>

      <div className="chart">
        {/* Measured without the padding, so the chart is exactly as wide as its box. */}
        <div ref={ref}>
        {loading && data.length === 0 ? (
          <div className="chart__loading" aria-label="Loading chart">
            {[80, 55, 40, 24].map((w) => (
              <span key={w} className="skeleton" style={{ width: `${w}%` }} />
            ))}
          </div>
        ) : data.length === 0 ? (
          <p className="chart__empty">
            {stats ? "No decisions in this range." : "Unknown until the audit trail loads."}
          </p>
        ) : width > 0 ? (
          mode === "action" ? (
            <BarChart
              width={width}
              height={actionHeight}
              data={data}
              layout="vertical"
              margin={{ top: 0, right: 12, bottom: 0, left: 0 }}
            >
              <CartesianGrid horizontal={false} stroke={colors.grid} />
              <XAxis
                type="number"
                allowDecimals={false}
                stroke={colors.axis}
                tickLine={false}
                axisLine={false}
                fontSize={11}
              />
              <YAxis
                type="category"
                dataKey="label"
                width={Math.min(190, width * 0.48)}
                stroke={colors.axis}
                tick={{ fill: colors.ink }}
                tickLine={false}
                axisLine={false}
                fontSize={11.5}
              />
              <Tooltip {...tooltip} />
              <Bar dataKey="count" name="Decisions" barSize={BAR_SIZE} radius={[0, 3, 3, 0]}>
                {data.map((d) => (
                  // Declined actions draw like their tags, a tinted bar with a brick edge:
                  // only actions are filled solid with brick.
                  <Cell
                    key={d.label}
                    fill={colors.tones[d.label] ?? colors.accent}
                    fillOpacity={isUnmatched(d.label) ? 0.28 : 1}
                    stroke={isUnmatched(d.label) ? colors.tones[d.label] : undefined}
                  />
                ))}
              </Bar>
            </BarChart>
          ) : (
            <AreaChart
              width={width}
              height={MONTH_HEIGHT}
              data={data}
              margin={{ top: 8, right: 8, bottom: 0, left: -18 }}
            >
              <defs>
                <linearGradient id="month-fill" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={colors.accent} stopOpacity={0.35} />
                  <stop offset="100%" stopColor={colors.accent} stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid vertical={false} stroke={colors.grid} />
              <XAxis dataKey="label" stroke={colors.axis} tickLine={false} axisLine={false} fontSize={11} />
              <YAxis allowDecimals={false} stroke={colors.axis} tickLine={false} axisLine={false} fontSize={11} />
              <Tooltip {...tooltip} cursor={{ stroke: colors.rule }} />
              <Area
                type="monotone"
                dataKey="count"
                name="Decisions"
                stroke={colors.accent}
                strokeWidth={2.25}
                fill="url(#month-fill)"
                dot={{ r: 3.5, fill: colors.surface, stroke: colors.accent, strokeWidth: 2 }}
                activeDot={{ r: 5, fill: colors.accent, stroke: colors.surface, strokeWidth: 2 }}
                className="chart__glow"
              />
            </AreaChart>
          )
        ) : null}
        </div>
      </div>
    </section>
  );
}
