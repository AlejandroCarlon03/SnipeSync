# SnipeSync

Automated employee lifecycle sync between **Microsoft Entra ID** and **Snipe-IT**, built as a .NET 9 isolated-worker Azure Function App.

SnipeSync keeps Snipe-IT aligned with the real state of your organization's Entra ID directory — offboarding departed employees (including reclaiming their hardware, license seats, and accessories), provisioning new ones, and reverting rehires — without manual intervention.

## What it does

### `FormerEmployeeSync`
Runs daily at 2:00 AM. Queries Entra ID for accounts with `accountEnabled = false`, matches each to a Snipe-IT user by email (or full name as a fallback), and:

- Sets their Snipe-IT job title to the configured **former-employee** title (default `"Former Employee"`).
- **Reclaims everything assigned to them** — hardware **assets** (optionally stamping a deprovisioned status label), **license seats**, and **accessories**. When an auto-checkin toggle is off, it still logs what the person holds so IT knows what to physically/manually reclaim. The per-user offboarding logic lives in a shared `IOffboardingService` reused by the reconciliation function below.
- Records each decision to an audit trail and rolls the results into a run summary.

### `OnboardEmployeeSync`
Also runs daily at 2:00 AM. Queries Entra ID for accounts created in the last _N_ days (default 7) that are enabled, and:

- Creates a new Snipe-IT user (random temp password, `ldap_import` enabled) for anyone missing.
- Pushes **department / manager / office** into configured Snipe-IT custom fields (feature 2).
- **Reverts rehires** (feature 3): if a re-enabled user already exists in Snipe-IT with the former-employee title, their title is restored. Set `REHIRE_FULL_SCAN=true` to also scan all enabled accounts (not just recently-created ones) — combine with group scoping to bound the cost.

### `ReconciliationQueueProcessor`
Queue-triggered (drains the `sync-unmatched` queue). When `FormerEmployeeSync` can't match a departed Entra user to a Snipe-IT record, it enqueues them here instead of dropping them. This function retries the match — including **alternate Entra identifiers** (mail, UPN, proxy addresses via a fresh Graph lookup) — and, on a hit, runs the same offboarding action through `IOffboardingService`. Genuine misses are audited (`UnmatchedAfterReconciliation`) and surfaced in the digest; transient write failures bubble up so the runtime retries and eventually moves the message to the **`sync-unmatched-poison`** queue (the real dead-letter). Retry count is set by `extensions.queues.maxDequeueCount` in `host.json`.

All three functions share a single Snipe-IT client (`ISnipeItService` / `SnipeItService`) and the shared `IOffboardingService`.

## Quality-of-life features

| # | Feature | Notes |
|---|---|---|
| 1 | Asset/license/accessory reclaim | Former-employee hardware, license seats, and accessories are auto-reclaimed via `ISnipeItService` + shared `IOffboardingService`. |
| 2 | Dept/manager/office sync | Pulled from Graph, pushed to Snipe-IT user custom fields (configure the db_column names). |
| 3 | Rehire handling | Re-enabled + flagged-former users get their title reverted. |
| 4 | Notifications | Post-run digest to a Teams incoming webhook. No-op (logs only) when unset. |
| 5 | Audit trail | One row per decision to Azure Table Storage. No-op when unset. |
| 6 | Dry-run | `DRY_RUN=true` runs all logic but skips every POST/PATCH, logging what *would* happen. |
| 7 | Config-driven mapping | Titles, matching, field columns, etc. all read from app settings — see below. |
| 8 | Retry / dead-letter | Unmatched users are queued to Azure Storage Queue and drained by `ReconciliationQueueProcessor`; failures land in `sync-unmatched-poison`. |
| 9 | Group scoping | `EMPLOYEE_SECURITY_GROUP_ID` scopes both queries to a security group's transitive members. |

All of the above **degrade gracefully**: if the relevant setting isn't present, that feature is skipped and the core sync keeps working.

## Configuration

Set these in `local.settings.json` (`Values`, git-ignored) locally, or as Application Settings / Key Vault references in Azure.

### Required

| Variable | Purpose |
|---|---|
| `AZURE_TENANT_ID` | Entra ID tenant ID |
| `AZURE_CLIENT_ID` | App Registration client ID |
| `AZURE_CLIENT_SECRET` | App Registration client secret |
| `SNIPEIT_URL` | Base URL of your Snipe-IT instance |
| `SNIPEIT_API_KEY` | Snipe-IT API token (search/read/update/create users; check in assets, license seats, and accessories) |

### Optional (new)

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
| `AUDIT_TABLE_CONNECTION_STRING` | `AzureWebJobsStorage` | Storage account for the audit table. |
| `AUDIT_TABLE_NAME` | `SyncAuditLog` | Audit table name. |
| `RECONCILIATION_QUEUE_CONNECTION_STRING` | `AzureWebJobsStorage` | Storage account for the unmatched-user queue. |
| `RECONCILIATION_QUEUE_NAME` | `sync-unmatched` | Queue name. **Must be present as an app setting** — the `ReconciliationQueueProcessor` binds `%RECONCILIATION_QUEUE_NAME%` at startup and won't load if it's missing. |

> **Finding custom-field db_columns:** in Snipe-IT, `GET /api/v1/fields` lists each custom field's `db_column_name` (the `_snipeit_*` value). Use those for the `SNIPEIT_CF_*` settings. Requires a user fieldset with those fields attached.

## Tech stack

- **.NET 9**, C# — Azure Functions isolated worker (V4)
- **Microsoft Graph SDK** 6.2 for Entra ID queries (client-credentials auth)
- **Snipe-IT REST API** via a typed `HttpClient`
- **Azure.Data.Tables** (audit) and **Azure.Storage.Queues** (reconciliation)
- **OpenTelemetry** → Azure Monitor

## Prerequisites

- .NET 9 SDK, Azure Functions Core Tools
- An **Entra ID App Registration** with a client secret and the `User.Read.All` **application** permission (admin-consented). Group scoping additionally needs `GroupMember.Read.All`.
- A Snipe-IT instance with an API token permitted to search/read/update/create users and check in assets.

## Running locally

```bash
dotnet restore
func start
```

Both functions are timer-triggered (`0 0 2 * * *`). To test on demand, use the Core Tools' manual timer invocation, or temporarily change the cron expression while debugging. Set `DRY_RUN=true` to exercise the full logic against prod Snipe-IT without side effects.

## Deployment

```bash
func azure functionapp publish <your-function-app-name>
```

Make sure the required app settings (and any optional ones you use) are configured before the timers fire. In production, prefer Key Vault references over storing secrets directly.

## Notes / known behavior

- Matching is exact email first, exact full-name fallback; otherwise the user is skipped, queued for reconciliation, and surfaced in the digest rather than guessed at.
- Created users get `ldap_import: 1` and a random temp password, since auth is expected via LDAP/AD.
- Graph queries currently read the first result page only (pre-existing behavior). If your disabled/enabled/created sets exceed a page, add paging via `PageIterator`.
