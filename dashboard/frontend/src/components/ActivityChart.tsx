import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { Bar, BarChart, CartesianGrid, Cell, Tooltip, XAxis, YAxis } from "recharts";
import type { AuditStats } from "../api";

const MONTH_HEIGHT = 300;
const BAR_SIZE = 22;

// Tone per action, aligned with the summary-tile colours. Unmapped actions use the accent.
const ACTION_TONES: Record<string, string> = {
  MarkedFormerEmployee: "#e06c4f",
  AssetCheckedIn: "#e0955f",
  LicenseReclaimed: "#d0b24a",
  AccessoryReclaimed: "#b5c24a",
  SkippedNoMatch: "#c9a227",
  UnmatchedAfterReconciliation: "#c97f27",
  ReconciledMatch: "#4f9de0",
  Created: "#4fb06a",
  Rehired: "#6a9de0",
};

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

export function ActivityChart({ stats }: { stats: AuditStats | null }) {
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

  const grid = token("--border", "#e2e2e2");
  const axis = token("--muted", "#6b7280");
  const accent = token("--accent", "#3b6cf6");

  const actionHeight = Math.max(160, data.length * (BAR_SIZE + 14) + 40);

  return (
    <section className="panel">
      <div className="panel__head">
        <h2>{mode === "action" ? "Actions breakdown" : "Monthly trend"}</h2>
        <div className="seg">
          <button
            className={`seg__btn ${mode === "action" ? "seg__btn--on" : ""}`}
            onClick={() => setMode("action")}
          >
            By action
          </button>
          <button
            className={`seg__btn ${mode === "month" ? "seg__btn--on" : ""}`}
            onClick={() => setMode("month")}
          >
            By month
          </button>
        </div>
      </div>

      <div ref={ref} style={{ padding: 16 }}>
        {data.length === 0 ? (
          <div className="muted center" style={{ padding: 24 }}>
            No data for these filters.
          </div>
        ) : width > 0 ? (
          mode === "action" ? (
            <BarChart
              width={width}
              height={actionHeight}
              data={data}
              layout="vertical"
              margin={{ top: 4, right: 24, bottom: 4, left: 8 }}
            >
              <CartesianGrid horizontal={false} stroke={grid} />
              <XAxis type="number" allowDecimals={false} stroke={axis} fontSize={12} />
              <YAxis type="category" dataKey="label" width={200} stroke={axis} fontSize={12} />
              <Tooltip cursor={{ fill: "transparent" }} />
              <Bar dataKey="count" barSize={BAR_SIZE} radius={[0, 4, 4, 0]}>
                {data.map((d) => (
                  <Cell key={d.label} fill={ACTION_TONES[d.label] ?? accent} />
                ))}
              </Bar>
            </BarChart>
          ) : (
            <BarChart
              width={width}
              height={MONTH_HEIGHT}
              data={data}
              margin={{ top: 4, right: 16, bottom: 4, left: 0 }}
            >
              <CartesianGrid vertical={false} stroke={grid} />
              <XAxis dataKey="label" stroke={axis} fontSize={12} />
              <YAxis allowDecimals={false} stroke={axis} fontSize={12} />
              <Tooltip cursor={{ fill: "transparent" }} />
              <Bar dataKey="count" fill={accent} radius={[4, 4, 0, 0]} />
            </BarChart>
          )
        ) : null}
      </div>
    </section>
  );
}
