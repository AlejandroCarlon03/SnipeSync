/**
 * The sync drawn as a network: each function is a line, and each audited action is the
 * station a person reached on it. Used for colour (table, chart, tiles) and for the route
 * diagram in the record inspector.
 *
 * Routes are read off the action vocabulary in actions.ts and PRODUCT.md, not from extra
 * audit data: the audit log stores one action per row, so a route shows the stops that
 * action implies, never steps that were separately recorded.
 */

export type LineId = "offboard" | "reconcile" | "onboard" | "licenses";

export interface LineInfo {
  id: LineId;
  name: string;
  /** CSS custom property holding the line's ink. */
  color: string;
}

export const LINES: Record<LineId, LineInfo> = {
  offboard: { id: "offboard", name: "Offboard", color: "var(--line-offboard)" },
  reconcile: { id: "reconcile", name: "Reconcile", color: "var(--line-reconcile)" },
  onboard: { id: "onboard", name: "Onboard", color: "var(--line-onboard)" },
  licenses: { id: "licenses", name: "Licenses", color: "var(--line-licenses)" },
};

const FUNCTION_LINES: Record<string, LineId> = {
  FormerEmployeeSync: "offboard",
  OffboardingService: "offboard",
  ReconciliationQueueProcessor: "reconcile",
  OnboardEmployeeSync: "onboard",
  LicenseSync: "licenses",
};

/** The two actions that mean "this Entra user has no Snipe-IT counterpart". */
export const UNMATCHED_ACTIONS = ["SkippedNoMatch", "UnmatchedAfterReconciliation"];

export function isUnmatched(action: string): boolean {
  return UNMATCHED_ACTIONS.includes(action);
}

export function lineForFunction(fn: string): LineInfo | null {
  const id = FUNCTION_LINES[fn];
  return id ? LINES[id] : null;
}

/** The colour an action is drawn in: red for anything unmatched, else its line's ink. */
export function actionColor(action: string, fn?: string): string {
  if (isUnmatched(action)) return "var(--anomaly)";
  const line = ROUTES[action]?.line ?? (fn ? FUNCTION_LINES[fn] : undefined);
  return line ? LINES[line].color : "var(--ink)";
}

/** Same as actionColor, keyed by the token name so Recharts can resolve a literal value. */
export function actionToken(action: string): string {
  if (isUnmatched(action)) return "--anomaly";
  const line = ROUTES[action]?.line;
  return line ? `--line-${line}` : "--ink";
}

/**
 * Tag style for an action. Red solid: the sync declined. Black solid: it changed Snipe-IT
 * (offboarding and reclaiming). Outline: everything else it recorded.
 */
export type ActionTag = "red" | "black" | "outline";

export function actionTag(action: string): ActionTag {
  if (isUnmatched(action)) return "red";
  return ROUTES[action]?.line === "offboard" ? "black" : "outline";
}

/**
 * passed: implied by this action. here / stopped: the stop this action records.
 * later: reached by another loaded record for the same person. not-reached: no loaded
 * record shows it. transfer: a change onto another line that always follows this action.
 */
export type StopState = "passed" | "here" | "stopped" | "later" | "not-reached" | "transfer";

export interface Stop {
  label: string;
  state: StopState;
  /** Ink for a stop on another line (a transfer). */
  color?: string;
  /** The loaded record that proves a later or transfer stop, so the stop can link to it. */
  recordId?: string;
}

interface NextStop {
  label: string;
  /** Actions on the same person that prove this stop was reached. */
  reachedBy?: string[];
  /** A line change that always happens after this action (PRODUCT.md: misses are queued). */
  transferTo?: LineId;
}

interface RouteDef {
  line: LineId;
  /** Stops up to and including the one this action records. */
  path: string[];
  /** Where the line goes next. */
  next?: NextStop;
  /** True when the action is the sync declining to act. */
  stopped?: boolean;
}

/** The minimum of an audit record the route needs from the person's other rows. */
interface TrailRecord {
  id: string;
  action: string;
  timestampUtc: string;
}

const ROUTES: Record<string, RouteDef> = {
  SkippedNoMatch: {
    line: "offboard",
    path: ["Disabled in Entra", "Looked up in Snipe-IT", "No match"],
    next: {
      label: "Queued for retry",
      reachedBy: ["UnmatchedAfterReconciliation", "ReconciledMatch"],
      transferTo: "reconcile",
    },
    stopped: true,
  },
  UnmatchedAfterReconciliation: {
    line: "reconcile",
    path: ["Queued after first pass", "Retried mail, UPN, proxies", "Still no match"],
    next: { label: "Offboard" },
    stopped: true,
  },
  ReconciledMatch: {
    line: "reconcile",
    path: ["Queued after first pass", "Retried mail, UPN, proxies", "Matched"],
    next: { label: "Offboard", reachedBy: ["MarkedFormerEmployee"] },
  },
  MarkedFormerEmployee: {
    line: "offboard",
    path: ["Disabled in Entra", "Matched in Snipe-IT", "Marked former employee"],
    next: {
      label: "Reclaim equipment",
      reachedBy: ["AssetCheckedIn", "LicenseReclaimed", "AccessoryReclaimed"],
    },
  },
  AssetCheckedIn: {
    line: "offboard",
    path: ["Disabled in Entra", "Marked former employee", "Asset checked in"],
  },
  LicenseReclaimed: {
    line: "offboard",
    path: ["Disabled in Entra", "Marked former employee", "Seat reclaimed"],
  },
  AccessoryReclaimed: {
    line: "offboard",
    path: ["Disabled in Entra", "Marked former employee", "Accessory reclaimed"],
  },
  Created: {
    line: "onboard",
    path: ["New in Entra", "No Snipe-IT user yet", "Created in Snipe-IT"],
  },
  Rehired: {
    line: "onboard",
    path: ["Back in Entra", "Former record found", "Reactivated"],
  },
  LicenseCheckedOut: {
    line: "licenses",
    path: ["Licensed in Microsoft 365", "Matched in Snipe-IT", "Seat checked out"],
  },
};

export interface Route {
  line: LineInfo;
  stops: Stop[];
  stopped: boolean;
}

export function routeFor(action: string, fn: string, trail: TrailRecord[] = []): Route {
  const def = ROUTES[action];
  if (!def) {
    // An action this build doesn't know yet: show it on its function's line, honestly short.
    const line = lineForFunction(fn) ?? LINES.offboard;
    return {
      line,
      stops: [
        { label: fn, state: "passed" },
        { label: action, state: "here" },
      ],
      stopped: false,
    };
  }

  const stops: Stop[] = def.path.map((label, i) => ({
    label,
    state: i < def.path.length - 1 ? "passed" : def.stopped ? "stopped" : "here",
  }));

  if (def.next) {
    const { label, reachedBy, transferTo } = def.next;
    // Newest proof first, so the link lands on the latest record that reached the stop.
    const proof = reachedBy
      ? [...trail]
          .filter((t) => reachedBy.includes(t.action))
          .sort((a, b) => b.timestampUtc.localeCompare(a.timestampUtc))[0]
      : undefined;
    if (transferTo) {
      stops.push({ label, state: "transfer", color: LINES[transferTo].color, recordId: proof?.id });
    } else {
      stops.push({ label, state: proof ? "later" : "not-reached", recordId: proof?.id });
    }
  }
  return { line: LINES[def.line], stops, stopped: Boolean(def.stopped) };
}
