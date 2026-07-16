# SnipeSync

Automated employee lifecycle sync between **Microsoft Entra ID** and **Snipe-IT**, built as a .NET 9 isolated-worker Azure Function App.

SnipeSync runs two scheduled functions that keep Snipe-IT aligned with the real state of your organization's Entra ID directory — offboarding departed employees (including reclaiming their assets), provisioning new ones, and reverting rehires — without manual intervention.

## What it does

### `FormerEmployeeSync`
Runs daily at 2:00 AM. Queries Entra ID for accounts with `accountEnabled = false`, matches each to a Snipe-IT user by email (or full name as a fallback), and:

- Sets their Snipe-IT job title to the configured **former-employee** title (default `"Former Employee"`).
- **Checks their assets back in** (feature 1) — this is the real offboarding signal for an asset tracker. Optionally stamps a deprovisioned status label on each reclaimed asset. When auto check-in is disabled, it still logs everything the person has out so IT knows what to physically reclaim.
- Records each decision to an audit trail and rolls the results into a run summary.

### `OnboardEmployeeSync`
Also runs daily at 2:00 AM. Queries Entra ID for accounts created in the last _N_ days (default 7) that are enabled, and:

- Creates a new Snipe-IT user (random temp password, `ldap_import` enabled) for anyone missing.
- Pushes **department / manager / office** into configured Snipe-IT custom fields (feature 2).
- **Reverts rehires** (feature 3): if a re-enabled user already exists in Snipe-IT with the former-employee title, their title is restored. Set `REHIRE_FULL_SCAN=true` to also scan all enabled accounts (not just recently-created ones) — combine with group scoping to bound the cost.

Both functions share a single Snipe-IT client (`ISnipeItService` / `SnipeItService`).

## Quality-of-life features

| # | Feature | Notes |
|---|---|---|
| 1 | Asset check-in + status | `GetUserAssets` / `CheckinAsset` on `ISnipeItService`; former-employee assets auto-checked-in. |
| 2 | Dept/manager/office sync | Pulled from Graph, pushed to Snipe-IT user custom fields (configure the db_column names). |
| 3 | Rehire handling | Re-enabled + flagged-former users get their title reverted. |
| 4 | Notifications | Post-run digest to a Teams incoming webhook. No-op (logs only) when unset. |
| 5 | Audit trail | One row per decision to Azure Table Storage. No-op when unset. |
| 6 | Dry-run | `DRY_RUN=true` runs all logic but skips every POST/PATCH, logging what *would* happen. |
| 7 | Config-driven mapping | Titles, matching, field columns, etc. all read from app settings — see below. |
| 8 | Retry / dead-letter | Unmatched users are queued to Azure Storage Queue for a second-pass reconciliation and surfaced in the digest. |
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
| `SNIPEIT_API_KEY` | Snipe-IT API token (search/read/update/create users, check in assets) |

### Optional (new)

| Variable | Default | Purpose |
|---|---|---|
| `FORMER_EMPLOYEE_TITLE` | `Former Employee` | Title used to flag a departed user. |
| `NEW_EMPLOYEE_TITLE` | `New Employee` | Fallback title for a created user with no Entra title. |
| `REHIRE_TITLE` | _(live Entra title, else `NEW_EMPLOYEE_TITLE`)_ | Title to restore on rehire. |
| `REHIRE_FULL_SCAN` | `false` | Also scan all enabled accounts for rehires, not just recent ones. |
| `AUTO_CHECKIN_ASSETS` | `true` | Auto check-in a former employee's assets. When `false`, only logs them. |
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
| `RECONCILIATION_QUEUE_NAME` | `sync-unmatched` | Queue name. |

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
