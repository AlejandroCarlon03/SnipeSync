# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

**Primary: the maintainer (IT operations), diagnosing.** Opens the audit dashboard when
something looks wrong — chasing why a departing employee was never matched to a Snipe-IT
record, or confirming a nightly run did what it was supposed to. This is investigation:
arriving with a specific question and needing the answer out of the audit trail.

**Same person, routine check.** A regular glance to confirm the 2 AM and 3 AM syncs
behaved. Scanning for anomalies while expecting to find none — so anomalies must be
findable without being hunted for.

**Wider IT team.** Other IT staff open it to see what happened to a departing employee's
hardware, license seats, and accessories. They do not know the sync's internals and should
not need to.

The product is built and run for a single organization's own IT operation. The README's
generic "your organization" phrasing is habit, not an intent to distribute.

## Product Purpose

SnipeSync keeps Snipe-IT aligned with the real state of the organization's Entra ID
directory without manual intervention: offboarding departed employees and reclaiming their
hardware, license seats, and accessories; provisioning new hires; and reverting rehires.

The dashboard exists to make the sync's decisions inspectable. Every decision is written to
an audit trail, and the dashboard is the surface where a human reads that trail back.
Success is that a person who leaves the company has their equipment reclaimed in Snipe-IT
without anyone remembering to do it — and that when the sync *doesn't* act on someone, the
reason is visible rather than silent.

## Positioning

The sync refuses to guess. When two Snipe-IT users share a name, or when an email match
misses, it does not pick one — it queues the user for reconciliation, retries with alternate
Entra identifiers (mail, UPN, proxy addresses), and audits a genuine miss as
`UnmatchedAfterReconciliation`. A throttled lookup is surfaced as a lookup failure, never
mistaken for "user not found," so rate limiting cannot silently skip a real employee or
create a duplicate.

Every feature degrades gracefully: if a setting is absent, that feature is skipped and the
core sync keeps working.

## Operating Context

The Azure Function App is out of service. The sync now runs from the dashboard itself: the
**Sync** button runs offboard → retry unmatched → onboard/rehires → license seats in one
pass (optionally as a dry run), and an optional nightly scheduled task runs the same thing
headless. Only one sync runs at a time. The audit trail is a local SQLite file.

The dashboard is an installed Windows app (Inno Setup, per-user) whose exe serves a React +
Vite SPA on loopback and opens it in an Edge app window, because WebView2 black-screens on
the maintainer's machine. It is launched on demand, not left open — syncs are occasional, so
the SPA polls at 60s (2s only while a sync it started is running) and this is explicitly not
a live feed.

## Capabilities and Constraints

**Confirmed constraints that all future work must preserve:**

- **Local-only, never hosted.** The dashboard displays employee PII and is deliberately a
  local desktop app. No design decision may assume a public URL or multi-tenant auth.
- **One deliberate write path.** The dashboard reads the audit trail, and its only
  state-changing action is the Sync button, which runs the same sync the timers used to.
  Every change it makes is audited. Nothing else in the UI writes to Snipe-IT or Entra.
- **Must render correctly in a plain browser**, because the Edge app window is the shipping
  launch mode, not the Photino shell.

**Current dashboard capabilities:** date-range / action / function / run-type filters; four
period-over-period delta tiles; a Recharts by-action and by-month toggle; a "No Snipe-IT
record" panel; an audit table with free-text search, click-to-sort headers, and
click-to-expand rows; Excel export of the rows currently on screen; an action glossary
popover; 60s silent auto-refresh; relative timestamps with absolute UTC tooltips; and a
dark/light toggle.

**Theme constraint.** `index.html` sets `data-theme` before first paint from
`localStorage["snipesync.theme"]`, and `ThemeToggle` sets the attribute *synchronously*
rather than in an effect — `ActivityChart` resolves its colors from computed styles during
render and would otherwise paint one render behind in the old palette. Any theming change
must keep this property.

**Known structural limitation:** the audit log records successes only. There is no `error`
field and no `LookupFailed` / `CheckinFailed` / `TitleUpdateFailed` / `AlreadyFormer`
actions, so the dashboard currently cannot distinguish "genuinely absent" from "throttled"
from "ambiguous name." Adding them is the highest-value item on the deferred backlog and
requires a Function App change.

**Query gaps that shape the UI:** `/api/audit` has no `dryRun` filter, so the SPA filters
client-side *after* a 500-row cap and can under-report. There is no paging past
`MaxLimit = 1000`, and filters are exact-equality only. `/api/audit/stats` cannot group by
`user` or filter by `action`.

**Terminology:** *former employee* (a disabled Entra account flagged in Snipe-IT),
*unmatched* (a disabled Entra user with no Snipe-IT counterpart), *reclaim* (checking in
assets, license seats, and accessories), *dry run* (all logic, no writes).

**Undecided:** whether the deferred backlog items — failure auditing, run tracking with a
`runId`, `GET /api/health`, an unmatched-user resolver — will be built. `app.css` already
carries unused `.runs` / `.run` styles anticipating run tracking.

## Evidence on Hand

**Real audit data exists** and is readable locally through both the Cosmos and Table Storage
backends.

**The headline finding is verified, not assumed:** the audit log reads ~100% "unmatched"
because the original Snipe-IT bulk import silently missed some Entra users. Those people
have since left, so `FormerEmployeeSync` correctly finds a disabled Entra account with no
Snipe-IT counterpart and writes `SkippedNoMatch`. This was confirmed by read-only Snipe-IT
API calls reproducing `FindSnipeItUser` exactly. It is a data-import gap, not a bug — the
sync is behaving correctly. Design work must not present these rows as sync failures.

**A known data-quality artifact:** at least one shared mailbox ("Gilbert Store (Not the
Sales Staff)") is being processed as if it were a person.

**Absent — do not fabricate:** there are no testimonials, customers, benchmarks, pricing,
adoption numbers, or uptime figures. There is no logo or brand identity beyond the product
name *SnipeSync* and the existing `favicon.svg`.

## Brand Commitments

The product is named **SnipeSync**. A `favicon.svg` exists. No other identity, palette,
voice, or asset commitment has been established.

## Product Principles

1. **Never guess about a person.** Ambiguity is surfaced and queued, not resolved by
   picking. The UI must preserve this: show the ambiguity rather than smoothing it away.
2. **Silence is the enemy.** The product's value is that decisions which would otherwise be
   invisible get recorded and read back. Anything the sync chose not to do is as important
   to show as what it did.
3. **Distinguish "didn't happen" from "couldn't happen."** A missing match, a throttled
   lookup, and a genuine absence are different facts with different remedies — even where
   the current audit schema cannot yet tell them apart.
4. **Degrade gracefully.** An absent configuration disables one feature; it never breaks the
   core. The interface should hold to the same rule when data is missing or an endpoint is
   unavailable.
5. **Built for someone who already knows the domain.** The primary reader understands Entra,
   Snipe-IT, and this sync. Optimize for fast answers over explanation — while keeping the
   wider IT team's question ("what happened to this person's laptop?") answerable without
   that background.

## Accessibility & Inclusion

No formal standard has been established for this project. Recorded as undecided rather than
assumed — future work should not claim a WCAG conformance level that was never set.
