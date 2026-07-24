/**
 * The action vocabulary written by CosmosAuditService.RecordAsync, with a
 * plain-English gloss for each. Single source of truth for both the Action
 * filter dropdown and the help panel, so the two can't drift apart.
 *
 * Keep in step with the call sites in FormerEmployeeSync /
 * ReconciliationQueueProcessor / OffboardingService / OnboardEmployeeSync.
 */

export interface ActionInfo {
  action: string;
  description: string;
  /** Which function emits it — shown as context in the help panel. */
  source: string;
}

export const ACTION_INFO: ActionInfo[] = [
  {
    action: "SkippedNoMatch",
    description: "Disabled user, no Snipe-IT match (first pass).",
    source: "FormerEmployeeSync",
  },
  {
    action: "UnmatchedAfterReconciliation",
    description: "Still no match after retry.",
    source: "ReconciliationQueueProcessor",
  },
  {
    action: "MarkedFormerEmployee",
    description: "User matched, title flipped to former-employee.",
    source: "OffboardingService",
  },
  {
    action: "AssetCheckedIn",
    description: "A checked-out asset was reclaimed.",
    source: "OffboardingService",
  },
  {
    action: "LicenseReclaimed",
    description: "A license seat was reclaimed.",
    source: "OffboardingService",
  },
  {
    action: "AccessoryReclaimed",
    description: "An accessory was reclaimed.",
    source: "OffboardingService",
  },
  {
    action: "ReconciledMatch",
    description: "Retry found a match on a second try.",
    source: "ReconciliationQueueProcessor",
  },
  {
    action: "Created",
    description: "A new hire was created in Snipe-IT.",
    source: "OnboardEmployeeSync",
  },
  {
    action: "Rehired",
    description: "A returning employee's existing record was reactivated.",
    source: "OnboardEmployeeSync",
  },
];

/** Action names in help-panel order — also the order of the filter dropdown. */
export const ACTIONS = ACTION_INFO.map((a) => a.action);
