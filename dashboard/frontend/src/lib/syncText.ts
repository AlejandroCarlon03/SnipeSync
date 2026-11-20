/** Wording shared by the Sync popover, the Last sync card and the run history. */
import type { SyncRun, SyncStep, SyncStepCounts } from "../api";

export const STEP_LABELS: Record<SyncStep["step"], string> = {
  offboard: "Offboard former employees",
  reconcile: "Retry unmatched users",
  onboard: "Onboard new hires and rehires",
  licenses: "Check out M365 license seats",
};

/** Short names for a run's one-line step summary. */
export const STEP_SHORT: Record<SyncStep["step"], string> = {
  offboard: "Offboard",
  reconcile: "Retry",
  onboard: "Onboard",
  licenses: "Licenses",
};

export const OUTCOME_LABELS: Record<SyncRun["outcome"], string> = {
  running: "Running",
  succeeded: "Finished",
  completedWithErrors: "Finished with errors",
  failed: "Did not run",
};

/** "2 offboarded · 3 assets in" — only the counters that moved, so a quiet step reads "no changes". */
export function describeCounts(c: SyncStepCounts | null): string {
  if (!c) return "";
  const parts: [number, string, string][] = [
    [c.offboarded, "offboarded", "offboarded"],
    [c.onboarded, "created", "created"],
    [c.rehired, "rehired", "rehired"],
    [c.assetsCheckedIn, "asset in", "assets in"],
    [c.licensesReclaimed, "seat reclaimed", "seats reclaimed"],
    [c.accessoriesReclaimed, "accessory in", "accessories in"],
    [c.licensesCheckedOut, "seat checked out", "seats checked out"],
    [c.unmatched.length, "unmatched", "unmatched"],
    [c.failed, "failed", "failed"],
  ];
  const moved = parts
    .filter(([n]) => n > 0)
    .map(([n, one, many]) => `${n} ${n === 1 ? one : many}`);
  const checked = c.processed > 0 ? `${c.processed} checked` : "";
  return [checked, ...(moved.length ? moved : ["no changes"])].filter(Boolean).join(" · ");
}

/** What one step did, in a phrase: its counts, its error, or why it didn't run. */
export function describeStep(s: SyncStep, runFailed: boolean): string {
  if (s.error) return s.error;
  if (s.state === "skipped") return runFailed ? "didn't run" : "skipped (turned off in settings)";
  if (s.state === "pending") return "waiting";
  if (s.state === "running") return "running…";
  return describeCounts(s.counts);
}
