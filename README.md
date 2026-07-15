# SnipeSync

Automated employee lifecycle sync between **Microsoft Entra ID** and **Snipe-IT**, built as a .NET 9 isolated-worker Azure Function App.

SnipeSync runs two scheduled functions that keep Snipe-IT user records aligned with the real state of your organization's Entra ID directory — flagging departed employees and provisioning new ones — without any manual intervention.

## What it does

### `FormerEmployeeSync`
Runs daily at 2:00 AM. Queries Entra ID for accounts with `accountEnabled = false`, matches each one to a Snipe-IT user by email (or full name as a fallback), and updates their Snipe-IT job title to `"Former Employee"` if it isn't already set. Accounts without a display name or mail address are skipped and logged.

### `OnboardEmployeeSync`
Also runs daily at 2:00 AM. Queries Entra ID for accounts created in the last 7 days, checks whether each one already exists in Snipe-IT, and creates a new Snipe-IT user (with a randomly generated temporary password and `ldap_import` enabled) for anyone missing. Job title falls back to `"New Employee"` if Entra ID doesn't have one set.

Both functions share a single Snipe-IT client (`ISnipeItService` / `SnipeItService`) so lookup, update, and create logic stays consistent between the two workflows.

## Tech stack

- **.NET 9**, C# — Azure Functions isolated worker model (V4)
- **Microsoft Graph SDK** (`Microsoft.Graph` 6.2) for Entra ID queries, authenticated via `ClientSecretCredential`
- **Snipe-IT REST API** via a typed `HttpClient`
- **OpenTelemetry** (`Microsoft.Azure.Functions.Worker.OpenTelemetry` + `Azure.Monitor.OpenTelemetry.Exporter`) for telemetry/logging export to Azure Monitor

## Project structure

```
SnipeITSyncFormerEmployees/
├── Functions/
│   ├── FormerEmployeeSync.cs      # Timer trigger: flags disabled Entra accounts as former employees
│   └── OnboardEmployeeSync.cs     # Timer trigger: creates Snipe-IT users for new Entra accounts
├── Services/
│   ├── ISnipeItService.cs         # Snipe-IT client contract
│   └── SnipeItService.cs          # Snipe-IT REST API implementation (find/update/create user)
├── Models/
│   └── SnipeItModels.cs           # SnipeItUser, SnipeItSearchResponse, SnipeItStatus records
├── Properties/
│   └── launchSettings.json
├── Program.cs                     # DI setup: HttpClient, GraphServiceClient (client-credentials auth)
├── host.json
└── SnipeITSyncFormerEmployees.csproj
```

## Prerequisites

- .NET 9 SDK
- Azure Functions Core Tools (for local runs)
- An **Entra ID App Registration** with:
  - A client secret
  - The `User.Read.All` **application** permission, with admin consent granted (needed to read `accountEnabled` and `createdDateTime` across all users, including disabled ones)
- A Snipe-IT instance with an API token that has permission to search, read, update, and create users

## Configuration

The app reads the following environment variables (set these in `local.settings.json` for local development — it's git-ignored — or as Application Settings on the Function App in Azure):

| Variable | Purpose |
|---|---|
| `AZURE_TENANT_ID` | Entra ID tenant ID |
| `AZURE_CLIENT_ID` | App Registration client ID |
| `AZURE_CLIENT_SECRET` | App Registration client secret |
| `SNIPEIT_URL` | Base URL of your Snipe-IT instance (e.g. `https://snipeit.example.com`) |
| `SNIPEIT_API_KEY` | Snipe-IT personal/API access token |

Example `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AZURE_TENANT_ID": "<entra-tenant-id>",
    "AZURE_CLIENT_ID": "<app-registration-client-id>",
    "AZURE_CLIENT_SECRET": "<app-registration-client-secret>",
    "SNIPEIT_URL": "https://your-snipeit-instance.com",
    "SNIPEIT_API_KEY": "<snipe-it-api-token>"
  }
}
```

In production, prefer pulling these from Azure Key Vault via app setting references rather than storing secrets directly on the Function App.

## Running locally

```bash
dotnet restore
func start
```

Both functions are timer-triggered (`0 0 2 * * *` — daily at 2:00 AM). To test on demand without waiting for the schedule, use the Azure Functions Core Tools' manual timer invocation, or temporarily change the cron expression while debugging.

## Deployment

```bash
func azure functionapp publish <your-function-app-name>
```

Or wire this up through your CI/CD pipeline of choice (GitHub Actions, Azure DevOps, etc.). Make sure the target Function App has `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `SNIPEIT_URL`, and `SNIPEIT_API_KEY` configured as Application Settings before the timers fire.

## Notes / known behavior

- User matching between Entra ID and Snipe-IT is done by exact email match first, falling back to exact full-name match — if neither is unique or present, the user is skipped and logged rather than guessed at.
- `OnboardEmployeeSync` sets `ldap_import: 1` on created users and assigns a random 18-byte base64 temporary password, since actual authentication is expected to happen via LDAP/AD rather than the Snipe-IT local password.

## License

Not currently specified — add one (e.g. MIT) if this repo will be public, or mark it as internal/proprietary if it's tied to a specific employer.
