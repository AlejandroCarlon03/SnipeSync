# SnipeSync

Automated employee lifecycle sync between **Microsoft Entra ID** and **Snipe-IT**.

SnipeSync keeps Snipe-IT aligned with the real state of your organization's Entra ID directory — offboarding departed employees (including reclaiming their hardware, license seats, and accessories), provisioning new ones, and reverting rehires — without manual intervention.

It runs as a **local Windows app**: a dashboard that shows the audit trail and has a **Sync** button
that runs the whole sync on demand, plus an optional nightly scheduled task. It was originally an
Azure Function App; that host is still in the repo (and still builds) but is no longer deployed.

## Install and run

1. Build the installer (needs the .NET 9 SDK, Node 22, and Inno Setup 6 —
   `winget install JRSoftware.InnoSetup`):

   ```
   installer\build.cmd 1.0.0
   ```

2. Run `installer\output\SnipeSync-Setup-1.0.0.exe`. It installs per-user (no admin) to
   `%LOCALAPPDATA%\Programs\SnipeSync`, adds Start-menu and desktop shortcuts, and can optionally
   schedule a nightly sync at 2:00 AM. It also removes the old hand-published
   `%LOCALAPPDATA%\SnipeSyncDashboard` copy and its shortcuts.
3. Open **SnipeSync**. The first time, click **Sync** → **Import settings file…** and pick the old
   Functions `local.settings.json`; Azure-only keys are dropped. Or edit
   `%LOCALAPPDATA%\SnipeSync\settings.json` by hand (created from a template on first launch).
4. Click **Sync**. Tick **Dry run** to see what would change without touching Snipe-IT.

Settings and the audit database (`audit.db`, SQLite) live in `%LOCALAPPDATA%\SnipeSync`, outside
the install folder, so upgrades and uninstalls keep them. The nightly task runs
`SnipeSync.Dashboard.exe --sync` and writes its output to `last-sync.log` in the same folder.

### Shipping an update

Every installed copy checks this repo's latest GitHub release when the dashboard opens (and every few
hours while it stays open). When the release is newer than what's installed, the dashboard shows
**SnipeSync x.y.z is available** with an **Update now** button: it downloads the release's installer,
checks it against GitHub's SHA-256, runs it silently (per-user, no admin prompt), and reopens the
dashboard on the new version. Settings, the audit database and the nightly task are kept.

Merging to master doesn't publish anything. To publish a release, merge, then tag the merge on master
and push the tag:

```
git tag v1.0.3
git push origin v1.0.3
```

The **Release** workflow (`.github/workflows/release.yml`) builds the installer on a Windows runner and
creates release `v1.0.3` with `SnipeSync-Setup-1.0.3.exe` attached. It refuses a tag that isn't
`v<major>.<minor>.<patch>` or that points at a commit not on master.

To build and publish from your own machine instead (needs the GitHub CLI signed in: `gh auth login`):

```
installer\release.cmd 1.0.3
installer\release.cmd 1.0.3 "What changed, in one line"
```

That runs `build.cmd`, tags the commit you're on and creates the same release. Use one path or the
other for a given version, not both. Keep
those names: the dashboard looks for exactly that tag and asset. Versions only go up; drafts and
pre-releases are ignored, so a pre-release can be tried by hand first. The repo the dashboard checks
is `SnipeSync:UpdateRepository` in `appsettings.json` (empty turns the check off).

### One sync, four steps

A sync runs these in order (spaced so they never hit Snipe-IT's rate limit at once). Only one sync
runs at a time, across the dashboard and the nightly task.

1. **Offboard** — `FormerEmployeeSync` below.
2. **Retry unmatched** — the reconciliation pass (`ReconciliationQueueProcessor` below) over the
   users step 1 couldn't match, straight away instead of via a storage queue.
3. **Onboard / rehires** — `OnboardEmployeeSync` below.
4. **License seats** — `LicenseSync` below; skipped unless `LICENSE_SYNC_ENABLED=true`.

A failure in one step is reported and the next step still runs. Every decision lands in the audit
trail tagged with the run's id.

## What it does

### `FormerEmployeeSync`
Step 1 of a sync (on Azure: daily at 2:00 AM). Queries Entra ID for accounts with `accountEnabled = false` (paging through every Graph result page), matches each to a Snipe-IT user by email — case-insensitively, falling back to a full-name match when the email search misses — and:

- Sets their Snipe-IT job title to the configured **former-employee** title (default `"Former Employee"`).
- **Reclaims everything assigned to them** — hardware **assets** (optionally stamping a deprovisioned status label), **license seats**, and **accessories**. When an auto-checkin toggle is off, it still logs what the person holds so IT knows what to physically/manually reclaim. The per-user offboarding logic lives in a shared `IOffboardingService` reused by the reconciliation function below.
- Records each decision to an audit trail and rolls the results into a run summary.

> **Steady-state cost:** the Entra `accountEnabled = false` set grows as people leave, but a user who is *already* marked former was reclaimed on the run that first flagged them — so the offboarding logic skips the asset/license/accessory lookups for them and only does a single match check. That keeps the nightly Snipe-IT call volume (and its rate-limiting) roughly flat instead of climbing with every past departure.

### `OnboardEmployeeSync`
Runs after `FormerEmployeeSync` (on Azure: daily at 3:00 AM, an hour later, so the two don't hit Snipe-IT's rate limit simultaneously). Queries Entra ID for accounts created in the last _N_ days (default 7) that are enabled, and:

- Creates a new Snipe-IT user (random temp password, `ldap_import` enabled) for anyone missing.
- Pushes **department / manager / office** into configured Snipe-IT custom fields (feature 2).
- **Reverts rehires** (feature 3): if a re-enabled user already exists in Snipe-IT with the former-employee title, their title is restored. Set `REHIRE_FULL_SCAN=true` to also scan all enabled accounts (not just recently-created ones) — combine with group scoping to bound the cost.

### `LicenseSync`
Mirrors each active Entra user's paid M365 licenses into Snipe-IT seats: Entra SKU part numbers
(e.g. `SPE_E3`) are mapped to Snipe-IT license names through `LICENSE_SKU_MAP`, and a free seat is
checked out for anyone entitled to one who doesn't hold it yet. Additive only (reclaiming is the
offboarding path's job) and idempotent. A mapped license that doesn't exist in Snipe-IT is created,
sized to the tenant's owned seats, when `LICENSE_AUTO_CREATE` is on. Unmapped SKUs and exhausted
seats are listed in the run digest.

### `LicenseSavingsReport` (Azure only)
Weekly Teams digest of seats reclaimed on offboarding, priced through `LICENSE_COST_MAP`. It reads the
Cosmos audit trail, so it only runs on the Azure host.

### `AuditQuery` (HTTP)
`GET /api/audit` — function-key protected (the response contains employee PII). Runs a
parameterized Cosmos SQL query over the audit history and returns JSON. Only active when the
Cosmos audit backend is configured; returns `503` otherwise. Optional query params: `user`,
`action`, `function`, `from` (ISO date), `to` (ISO date), `limit` (default 100, max 1000). The
Cosmos request-unit charge for the query is returned in the `x-cosmos-ru` response header.

```
GET /api/audit?user=Jane%20Doe&from=2026-07-01&limit=50&code=<function-key>
```

### `ReconciliationQueueProcessor`
Queue-triggered (drains the `sync-unmatched` queue). When `FormerEmployeeSync` can't match a departed Entra user to a Snipe-IT record, it enqueues them here instead of dropping them. This function retries the match — including **alternate Entra identifiers** (mail, UPN, proxy addresses via a fresh Graph lookup) — and, on a hit, runs the same offboarding action through `IOffboardingService`. Genuine misses are audited (`UnmatchedAfterReconciliation`) and surfaced in the digest; transient write failures bubble up so the runtime retries and eventually moves the message to the **`sync-unmatched-poison`** queue (the real dead-letter). Retry count is set by `extensions.queues.maxDequeueCount` in `host.json`.

The sync logic itself lives in `SnipeSync.Core` as job classes (`FormerEmployeeSyncJob`,
`OnboardEmployeeSyncJob`, `ReconciliationJob`, `LicenseSyncJob`); the Azure functions and the local
dashboard are both thin hosts over them. All of them share a single Snipe-IT client (`ISnipeItService` / `SnipeItService`) and the shared `IOffboardingService`.

## Quality-of-life features

| # | Feature | Notes |
|---|---|---|
| 1 | Asset/license/accessory reclaim | Former-employee hardware, license seats, and accessories are auto-reclaimed via `ISnipeItService` + shared `IOffboardingService`. |
| 2 | Dept/manager/office sync | Pulled from Graph, pushed to Snipe-IT user custom fields (configure the db_column names). |
| 3 | Rehire handling | Re-enabled + flagged-former users get their title reverted. |
| 4 | Notifications | Post-run digest to a Teams incoming webhook. No-op (logs only) when unset. |
| 5 | Audit trail | One record per decision. Local app: **SQLite** (`%LOCALAPPDATA%\SnipeSync\audit.db`). Azure host: **Cosmos DB** when `COSMOS_CONNECTION_STRING` is set, otherwise **Table Storage**. Both serve the same `GET /api/audit` and `/api/audit/stats`. |
| 6 | Dry-run | `DRY_RUN=true` runs all logic but skips every POST/PATCH, logging what *would* happen. |
| 7 | Config-driven mapping | Titles, matching, field columns, etc. all read from app settings — see below. |
| 8 | Retry / dead-letter | Unmatched users get a second-pass match with alternate Entra identifiers — in the same run locally, via an Azure Storage Queue (with `sync-unmatched-poison` as the dead-letter) on Azure. |
| 9 | Group scoping | `EMPLOYEE_SECURITY_GROUP_ID` scopes both queries to a security group's transitive members. |
| 10 | Throttle-aware Snipe-IT client | The Snipe-IT `HttpClient` retries `429`/`5xx`/transient errors with backoff (honoring `Retry-After`) via a Polly policy. A lookup that still fails is surfaced as a lookup failure — never mistaken for "user not found" — so throttling can't silently skip a real employee or create a duplicate. The reconciliation queue drains one message at a time (`batchSize: 1`) to avoid stampeding Snipe-IT. |

All of the above **degrade gracefully**: if the relevant setting isn't present, that feature is skipped and the core sync keeps working.

## Configuration

Local app: the `Values` block of `%LOCALAPPDATA%\SnipeSync\settings.json` (same shape as the Functions
`local.settings.json`, so one can be imported into the other). Azure host: `local.settings.json` when
running under `func`, or Application Settings / Key Vault references in Azure. Settings are re-read at
the start of every sync, so edits apply on the next run.

### Required

| Variable | Purpose |
|---|---|
| `AZURE_TENANT_ID` | Entra ID tenant ID |
| `AZURE_CLIENT_ID` | App Registration client ID |
| `AZURE_CLIENT_SECRET` | App Registration client secret |
| `SNIPEIT_URL` | Base URL of your Snipe-IT instance |
| `SNIPEIT_API_KEY` | Snipe-IT API token (search/read/update/create users; check in assets, license seats, and accessories) |

### Optional

| Variable | Default | Purpose |
|---|---|---|
| `FORMER_EMPLOYEE_TITLE` | `Former Employee` | Title used to flag a departed user. |
| `NEW_EMPLOYEE_TITLE` | `New Employee` | Fallback title for a created user with no Entra title. |
| `REHIRE_TITLE` | _(live Entra title, else `NEW_EMPLOYEE_TITLE`)_ | Title to restore on rehire. |
| `REHIRE_FULL_SCAN` | `false` | Also scan all enabled accounts for rehires, not just recent ones. |
| `AUTO_CHECKIN_ASSETS` | `true` | Auto check-in a former employee's assets. When `false`, only logs them. |
| `AUTO_CHECKIN_LICENSES` | `true` | Auto-reclaim a former employee's license seats. When `false`, only logs them. |
| `AUTO_CHECKIN_ACCESSORIES` | `true` | Auto-reclaim a former employee's accessories. When `false`, only logs them. |
| `DEPROVISIONED_STATUS_ID` | _(unset)_ | Snipe-IT status label id to stamp on assets as they're checked in. |
| `SNIPEIT_CF_DEPARTMENT` | _(unset)_ | User custom-field db_column for department (e.g. `_snipeit_department_5`). |
| `SNIPEIT_CF_MANAGER` | _(unset)_ | User custom-field db_column for manager. |
| `SNIPEIT_CF_OFFICE` | _(unset)_ | User custom-field db_column for office location. |
| `DRY_RUN` | `false` | Skip all writes; log intended actions only. |
| `EMPLOYEE_SECURITY_GROUP_ID` | _(unset)_ | Scope both queries to this security group's transitive members. |
| `ONBOARD_LOOKBACK_DAYS` | `7` | Look-back window for the onboarding query. |
| `TEAMS_WEBHOOK_URL` | _(unset)_ | Teams incoming-webhook URL for the run digest. |
| `LICENSE_SYNC_ENABLED` | `false` | Turns on the license-seat step. |
| `LICENSE_SKU_MAP` | _(empty)_ | JSON map of Entra SKU part number → Snipe-IT license name, e.g. `{"SPE_E3":"Microsoft 365 E3"}`. |
| `LICENSE_AUTO_CREATE` | `true` | Create a mapped license in Snipe-IT when it doesn't exist yet. |
| `LICENSE_CATEGORY_ID` | _(unset)_ | Snipe-IT category id for auto-created licenses. |
| `LICENSE_COST_MAP` | _(empty)_ | JSON map of Snipe-IT license name → monthly seat cost (Azure savings report only). |
| _Azure host only:_ | | |
| `AUDIT_TABLE_CONNECTION_STRING` | `AzureWebJobsStorage` | Storage account for the audit table (Table Storage backend). |
| `AUDIT_TABLE_NAME` | `SyncAuditLog` | Audit table name (Table Storage backend). |
| `COSMOS_CONNECTION_STRING` | _(unset)_ | When set, audit records go to **Cosmos DB** instead of Table Storage, and `GET /api/audit` becomes active. |
| `COSMOS_DATABASE_NAME` | `SnipeSync` | Cosmos database (created if missing). |
| `COSMOS_AUDIT_CONTAINER` | `AuditLog` | Cosmos container for audit records (created if missing, partition key `/ym`). |
| `RECONCILIATION_QUEUE_NAME` | `sync-unmatched` | Queue name. **Must be present as an app setting** — the `ReconciliationQueueProcessor` binds `%RECONCILIATION_QUEUE_NAME%` at startup and won't load if it's missing. |

> The unmatched-user queue always lives in `AzureWebJobsStorage` — the same connection the
> `ReconciliationQueueProcessor` trigger binds to. It is deliberately not configurable: a queue trigger
> can't express the "custom setting, else fall back" default this app uses elsewhere, so pointing the
> writer at a second storage account would send messages where nothing is listening.

> **Finding custom-field db_columns:** in Snipe-IT, `GET /api/v1/fields` lists each custom field's `db_column_name` (the `_snipeit_*` value). Use those for the `SNIPEIT_CF_*` settings. Requires a user fieldset with those fields attached.

## Tech stack

- **.NET 9**, C#. `SnipeSync.Core` (the sync engine), `dashboard/SnipeSync.Dashboard` (local host:
  ASP.NET Core on loopback + SQLite, opened in an Edge app window), and `SnipeITSyncFormerEmployees`
  (the Azure Functions isolated-worker host, out of service)
- **Microsoft Graph SDK** 6.2 for Entra ID queries (client-credentials auth)
- **Snipe-IT REST API** via a typed `HttpClient` with a Polly retry policy
- **React + Vite + TypeScript** dashboard (`dashboard/frontend`), built into the host by MSBuild
- **Inno Setup 6** installer (`installer/`)
- Azure host only: **Cosmos DB** / **Table Storage** audit, **Storage Queues**, **OpenTelemetry** → Azure Monitor

## Prerequisites

- .NET 9 SDK and Node 22 to build; Inno Setup 6 to build the installer
- An **Entra ID App Registration** with a client secret and the `User.Read.All` **application** permission (admin-consented). Group scoping additionally needs `GroupMember.Read.All`; license sync reads the tenant's subscribed SKUs, which needs `Organization.Read.All`.
- A Snipe-IT instance with an API token permitted to search/read/update/create users, check in assets, license seats and accessories, and check out license seats.

## Developing

```bash
dotnet build SnipeITSyncFormerEmployees.sln

# Dashboard dev loop (two terminals)
cd dashboard/SnipeSync.Dashboard && dotnet run -- --headless   # host on http://127.0.0.1:5177
cd dashboard/frontend && npm run dev                            # Vite on :5173, proxies /api to :5177

# One sync from the command line, no UI
cd dashboard/SnipeSync.Dashboard && dotnet run -- --sync --dry-run

# Frontend gates
cd dashboard/frontend && npm run build && npm run lint

# Regenerate favicon / .ico / installer bitmap after editing dashboard/assets/snipesync.svg
cd dashboard/frontend && npm run icons
```

Launch modes of `SnipeSync.Dashboard.exe`: no arguments opens the dashboard in an Edge app window
(a second launch just reopens the window); `--headless` serves only; `--sync [--dry-run]` runs one
sync and exits (0 = clean, 1 = errors); `--window` uses the old Photino window, which renders black
under WebView2 on our machines.

## Azure host (out of service)

The Function App runs the same jobs on timers (`0 0 2 * * *`, `0 0 3 * * *`, `0 0 4 * * *`) plus
the queue-triggered reconciliation and the Monday savings report. To bring it back:

```bash
cd SnipeITSyncFormerEmployees
func start                                              # local, with Azurite
func azure functionapp publish <your-function-app-name>  # deploy
```

`RECONCILIATION_QUEUE_NAME` must be set as an app setting (the queue trigger binds to it at startup).
Prefer Key Vault references for secrets, and managed identity over a Cosmos connection string.

## Notes / known behavior

- **Matching order:** case-insensitive email first; if that misses (blank or stale email on the Snipe-IT record), a case-insensitive full-name match. Trailing parentheticals in the Entra display name — e.g. `"Jane Doe (Former Employee)"` from offboarding renames — are stripped before comparing. If two Snipe-IT users share the name, the sync refuses to guess: the user is queued for reconciliation and surfaced in the digest instead.
- Created users get `ldap_import: 1` and a random temp password, since auth is expected via LDAP/AD.
- Graph queries walk every result page via `PageIterator`, so large disabled/enabled/created sets are handled in full.
- **Dry run:** `DRY_RUN=true` in settings forces every run dry; the dashboard's checkbox can only make a run *more* cautious, never override that.
- **Testing from the Azure portal:** in Test/Run, click **Run** once and then just switch to the output/log view — clicking Run again on the Output tab fires a *second* invocation, which doubles the Snipe-IT call volume and makes the logs look duplicated.
