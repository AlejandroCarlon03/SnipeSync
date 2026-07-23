namespace SnipeITSyncFormerEmployees;

/// <summary>
/// Central, config-driven settings for the sync jobs. Everything here is read from
/// environment variables (local.settings.json Values locally, Application Settings /
/// Key Vault references in Azure) so behavior can be tuned without a code change.
/// </summary>
public class SyncOptions
{
    // --- Feature 7: config-driven field mapping ---------------------------------
    /// <summary>Job title used to flag a departed user in Snipe-IT.</summary>
    public string FormerEmployeeTitle { get; }

    /// <summary>Fallback job title for a newly-created Snipe-IT user with no Entra title.</summary>
    public string NewEmployeeTitle { get; }

    /// <summary>
    /// Title to restore when a former employee is re-hired. When null we fall back to the
    /// live Entra job title, then to <see cref="NewEmployeeTitle"/>.
    /// </summary>
    public string? RehireTitle { get; }

    /// <summary>
    /// When true, OnboardEmployeeSync additionally scans <em>all</em> enabled users (not just
    /// recently-created ones) to catch re-enabled long-standing accounts flagged as former
    /// employees. Off by default because it costs one Snipe-IT lookup per enabled user;
    /// combine with EMPLOYEE_SECURITY_GROUP_ID to bound the cost.
    /// </summary>
    public bool RehireFullScan { get; }

    // --- Feature 1: asset check-in / status -------------------------------------
    /// <summary>When true, automatically check in assets still assigned to a departed user.</summary>
    public bool AutoCheckinAssets { get; }

    /// <summary>
    /// Optional Snipe-IT status label id to stamp on an asset as it is checked in
    /// (e.g. a "Pending deprovision" / "Suspended" label). Null leaves the status unchanged.
    /// </summary>
    public int? DeprovisionedStatusId { get; }

    /// <summary>When true, reclaim (check in) license seats assigned to a departed user.</summary>
    public bool AutoCheckinLicenses { get; }

    /// <summary>When true, reclaim (check in) accessories assigned to a departed user.</summary>
    public bool AutoCheckinAccessories { get; }

    // --- Feature 2: department / manager / office custom fields ------------------
    /// <summary>Snipe-IT user custom-field db_column for department (e.g. _snipeit_department_5). Null skips.</summary>
    public string? DepartmentFieldColumn { get; }
    public string? ManagerFieldColumn { get; }
    public string? OfficeFieldColumn { get; }

    public bool SyncUserFields => DepartmentFieldColumn is not null
                                  || ManagerFieldColumn is not null
                                  || OfficeFieldColumn is not null;

    // --- Feature 6: dry-run -----------------------------------------------------
    /// <summary>When true, all mutating Snipe-IT calls are skipped and only logged.</summary>
    public bool DryRun { get; }

    // --- Feature 9: group scoping -----------------------------------------------
    /// <summary>Optional Entra security-group object id to scope the sync to (e.g. "DKB Employees").</summary>
    public string? EmployeeSecurityGroupId { get; }

    /// <summary>Look-back window (days) for the onboarding query.</summary>
    public int OnboardLookbackDays { get; }

    // --- Feature 4: notifications -----------------------------------------------
    public string? TeamsWebhookUrl { get; }

    // --- Feature 5: audit trail -------------------------------------------------
    public string? AuditTableConnectionString { get; }
    public string AuditTableName { get; }

    // --- Feature 5 (Cosmos backend): queryable audit history --------------------
    /// <summary>
    /// When set, audit decisions are written to Cosmos DB (queryable via GET /api/audit) instead
    /// of Azure Table Storage. Null falls back to <see cref="TableAuditService"/>.
    /// </summary>
    public string? CosmosConnectionString { get; }
    public string CosmosDatabaseName { get; }
    public string CosmosAuditContainer { get; }

    /// <summary>True when a Cosmos connection is configured — selects the Cosmos audit backend.</summary>
    public bool UseCosmosAudit => CosmosConnectionString is not null;

    // --- Feature 8: reconciliation / dead-letter queue --------------------------
    /// <summary>
    /// Always the function app's own storage (AzureWebJobsStorage). The ReconciliationQueueProcessor
    /// trigger binds to that same connection, and a QueueTrigger cannot express the "custom setting,
    /// else fall back" logic this class uses elsewhere — so the producer is pinned to it deliberately.
    /// Splitting the queue onto a second account would make writes land where nothing is listening.
    /// </summary>
    public string? ReconciliationQueueConnectionString { get; }
    public string ReconciliationQueueName { get; }

    public SyncOptions()
    {
        FormerEmployeeTitle = Get("FORMER_EMPLOYEE_TITLE") ?? "Former Employee";
        NewEmployeeTitle = Get("NEW_EMPLOYEE_TITLE") ?? "New Employee";
        RehireTitle = Get("REHIRE_TITLE");
        RehireFullScan = GetBool("REHIRE_FULL_SCAN", defaultValue: false);

        AutoCheckinAssets = GetBool("AUTO_CHECKIN_ASSETS", defaultValue: true);
        DeprovisionedStatusId = GetInt("DEPROVISIONED_STATUS_ID");
        AutoCheckinLicenses = GetBool("AUTO_CHECKIN_LICENSES", defaultValue: true);
        AutoCheckinAccessories = GetBool("AUTO_CHECKIN_ACCESSORIES", defaultValue: true);

        DepartmentFieldColumn = Get("SNIPEIT_CF_DEPARTMENT");
        ManagerFieldColumn = Get("SNIPEIT_CF_MANAGER");
        OfficeFieldColumn = Get("SNIPEIT_CF_OFFICE");

        DryRun = GetBool("DRY_RUN", defaultValue: false);

        EmployeeSecurityGroupId = Get("EMPLOYEE_SECURITY_GROUP_ID");
        OnboardLookbackDays = GetInt("ONBOARD_LOOKBACK_DAYS") ?? 7;

        TeamsWebhookUrl = Get("TEAMS_WEBHOOK_URL");

        // Audit + queue default to the function app's own storage account.
        var webJobsStorage = Get("AzureWebJobsStorage");
        AuditTableConnectionString = Get("AUDIT_TABLE_CONNECTION_STRING") ?? webJobsStorage;
        AuditTableName = Get("AUDIT_TABLE_NAME") ?? "SyncAuditLog";

        CosmosConnectionString = Get("COSMOS_CONNECTION_STRING");
        CosmosDatabaseName = Get("COSMOS_DATABASE_NAME") ?? "SnipeSync";
        CosmosAuditContainer = Get("COSMOS_AUDIT_CONTAINER") ?? "AuditLog";

        ReconciliationQueueConnectionString = webJobsStorage;
        ReconciliationQueueName = Get("RECONCILIATION_QUEUE_NAME") ?? "sync-unmatched";
    }

    private static string? Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool GetBool(string name, bool defaultValue)
    {
        var raw = Get(name);
        if (raw is null) return defaultValue;
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
    }

    private static int? GetInt(string name)
    {
        var raw = Get(name);
        return int.TryParse(raw, out var value) ? value : null;
    }
}
