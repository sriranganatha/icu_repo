using System.Diagnostics;
using System.Text;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HmsAgents.Agents.Database;

/// <summary>
/// Provisions a PostgreSQL Docker container and executes DDL to create schemas, tables, stored procedures.
/// </summary>
public sealed class DockerDbProvisioner
{
    private readonly ILogger _logger;

    public DockerDbProvisioner(ILogger logger) => _logger = logger;

    /// <summary>
    /// Ensures a PostgreSQL Docker container is running. Creates one if it doesn't exist.
    /// </summary>
    public async Task<bool> EnsureContainerAsync(PipelineConfig config, CancellationToken ct = default)
    {
        _logger.LogInformation("Checking for Docker container '{Container}'...", config.DockerContainerName);

        // Check if container exists
        var (exitCode, output) = await RunProcessAsync("docker",
            $"inspect --format \"{{{{.State.Running}}}}\" {config.DockerContainerName}", ct);

        if (exitCode == 0 && output.Trim().Contains("true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Container '{Container}' is already running.", config.DockerContainerName);
            return true;
        }

        // Check if container exists but stopped
        if (exitCode == 0)
        {
            _logger.LogInformation("Starting existing container '{Container}'...", config.DockerContainerName);
            var (startExit, _) = await RunProcessAsync("docker", $"start {config.DockerContainerName}", ct);
            if (startExit == 0)
            {
                await WaitForPostgresReady(config, ct);
                return true;
            }
        }

        // Create new container
        _logger.LogInformation("Creating PostgreSQL container '{Container}' on port {Port}...",
            config.DockerContainerName, config.DbPort);

        var args = new StringBuilder();
        args.Append("run -d ");
        args.Append($"--name {config.DockerContainerName} ");
        args.Append($"-p {config.DbPort}:5432 ");
        args.Append($"-e POSTGRES_USER={config.DbUser} ");
        args.Append($"-e POSTGRES_PASSWORD={config.DbPassword} ");
        args.Append($"-e POSTGRES_DB={config.DbName} ");
        args.Append($"-v {config.DockerContainerName}_data:/var/lib/postgresql/data ");
        args.Append("postgres:16-alpine");

        var (runExit, runOutput) = await RunProcessAsync("docker", args.ToString(), ct);
        if (runExit != 0)
        {
            _logger.LogError("Failed to create Docker container: {Output}", runOutput);
            return false;
        }

        _logger.LogInformation("Container created. Waiting for PostgreSQL to be ready...");
        await WaitForPostgresReady(config, ct);
        return true;
    }

    /// <summary>
    /// Executes DDL against the running PostgreSQL instance — creates schemas, tables, indexes, stored procedures, and RLS policies.
    /// </summary>
    public async Task<(bool success, int objectsCreated, List<string> errors)> ExecuteDdlAsync(
        PipelineConfig config, CancellationToken ct = default)
    {
        var connStr = $"Host={config.DbHost};Port={config.DbPort};Database={config.DbName};Username={config.DbUser};Password={config.DbPassword}";
        var errors = new List<string>();
        int objectsCreated = 0;

        await using var conn = new NpgsqlConnection(connStr);
        try
        {
            await conn.OpenAsync(ct);
            _logger.LogInformation("Connected to PostgreSQL at localhost:{Port}/{Db}", config.DbPort, config.DbName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to PostgreSQL");
            return (false, 0, [ex.Message]);
        }

        // 1. Create schemas
        var schemas = new[] { "cl_mpi", "cl_encounter", "cl_inpatient", "cl_emergency", "cl_diagnostics", "op_revenue", "gov_audit", "gov_ai" };
        foreach (var schema in schemas)
        {
            try
            {
                await ExecuteSqlAsync(conn, $"CREATE SCHEMA IF NOT EXISTS {schema};", ct);
                objectsCreated++;
                _logger.LogInformation("Schema '{Schema}' ensured.", schema);
            }
            catch (Exception ex) { errors.Add($"Schema {schema}: {ex.Message}"); }
        }

        // 2. Create tables
        var tableDdl = GenerateTableDdl();
        foreach (var (tableName, ddl) in tableDdl)
        {
            try
            {
                await ExecuteSqlAsync(conn, ddl, ct);
                objectsCreated++;
                _logger.LogInformation("Table '{Table}' created.", tableName);
            }
            catch (Exception ex) { errors.Add($"Table {tableName}: {ex.Message}"); }
        }

        // 3. Create indexes
        var indexDdl = GenerateIndexDdl();
        foreach (var (indexName, ddl) in indexDdl)
        {
            try
            {
                await ExecuteSqlAsync(conn, ddl, ct);
                objectsCreated++;
            }
            catch (Exception ex) { errors.Add($"Index {indexName}: {ex.Message}"); }
        }
        _logger.LogInformation("{Count} indexes created.", indexDdl.Count);

        // 4. Create stored procedures
        var procDdl = GenerateStoredProcedures();
        foreach (var (procName, ddl) in procDdl)
        {
            try
            {
                await ExecuteSqlAsync(conn, ddl, ct);
                objectsCreated++;
                _logger.LogInformation("Procedure '{Proc}' created.", procName);
            }
            catch (Exception ex) { errors.Add($"Procedure {procName}: {ex.Message}"); }
        }

        // 5. Enable RLS policies
        var rlsDdl = GenerateRlsPolicies();
        foreach (var (policyName, ddl) in rlsDdl)
        {
            try
            {
                await ExecuteSqlAsync(conn, ddl, ct);
                objectsCreated++;
            }
            catch (Exception ex) { errors.Add($"RLS {policyName}: {ex.Message}"); }
        }
        _logger.LogInformation("RLS policies applied. Total objects: {Count}, Errors: {Errors}",
            objectsCreated, errors.Count);

        return (errors.Count == 0, objectsCreated, errors);
    }

    /// <summary>Returns the container status summary as a string.</summary>
    public async Task<string> GetContainerStatusAsync(PipelineConfig config, CancellationToken ct = default)
    {
        var (exitCode, output) = await RunProcessAsync("docker",
            $"inspect --format \"Name={{{{.Name}}}} State={{{{.State.Status}}}} Ports={{{{.NetworkSettings.Ports}}}}\" {config.DockerContainerName}", ct);
        return exitCode == 0 ? output.Trim() : "Container not found";
    }

    #region DDL Generation

    private static List<(string name, string ddl)> GenerateTableDdl() =>
    [
        ("cl_mpi.patient_profile", """
            CREATE TABLE IF NOT EXISTS cl_mpi.patient_profile (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64),
                enterprise_person_key   VARCHAR(128) NOT NULL,
                legal_given_name        VARCHAR(256) NOT NULL,
                legal_family_name       VARCHAR(256) NOT NULL,
                preferred_name          VARCHAR(256),
                date_of_birth           DATE NOT NULL,
                sex_at_birth            VARCHAR(16),
                primary_language        VARCHAR(16),
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                source_system           VARCHAR(64),
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            """),

        ("cl_mpi.patient_identifier", """
            CREATE TABLE IF NOT EXISTS cl_mpi.patient_identifier (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL REFERENCES cl_mpi.patient_profile(id),
                identifier_type         VARCHAR(64) NOT NULL,
                identifier_value_hash   VARCHAR(256) NOT NULL,
                issuer                  VARCHAR(128),
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """),

        ("cl_encounter.encounter", """
            CREATE TABLE IF NOT EXISTS cl_encounter.encounter (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_type          VARCHAR(64) NOT NULL,
                source_pathway          VARCHAR(64),
                attending_provider_ref  VARCHAR(128),
                start_at                TIMESTAMPTZ NOT NULL,
                end_at                  TIMESTAMPTZ,
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            """),

        ("cl_encounter.clinical_note", """
            CREATE TABLE IF NOT EXISTS cl_encounter.clinical_note (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                encounter_id            VARCHAR(32) NOT NULL REFERENCES cl_encounter.encounter(id),
                patient_id              VARCHAR(32) NOT NULL,
                note_type               VARCHAR(64) NOT NULL,
                note_classification_code VARCHAR(64),
                content_json            JSONB NOT NULL DEFAULT '{}',
                ai_interaction_id       VARCHAR(32),
                authored_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
                authored_by             VARCHAR(128) NOT NULL,
                amended_from_note_id    VARCHAR(32),
                version_no              INTEGER NOT NULL DEFAULT 1,
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """),

        ("cl_inpatient.admission", """
            CREATE TABLE IF NOT EXISTS cl_inpatient.admission (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_id            VARCHAR(32) NOT NULL,
                admit_class             VARCHAR(64) NOT NULL,
                admit_source            VARCHAR(64),
                status_code             VARCHAR(32) NOT NULL DEFAULT 'active',
                expected_discharge_at   TIMESTAMPTZ,
                utilization_status_code VARCHAR(32),
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            """),

        ("cl_inpatient.admission_eligibility", """
            CREATE TABLE IF NOT EXISTS cl_inpatient.admission_eligibility (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_id            VARCHAR(32) NOT NULL,
                candidate_class         VARCHAR(64),
                decision_code           VARCHAR(32) NOT NULL,
                rationale_json          JSONB,
                payer_authorization_status VARCHAR(32),
                override_flag           BOOLEAN NOT NULL DEFAULT FALSE,
                approved_by             VARCHAR(128),
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL
            );
            """),

        ("cl_emergency.emergency_arrival", """
            CREATE TABLE IF NOT EXISTS cl_emergency.emergency_arrival (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32),
                temporary_identity_alias VARCHAR(128),
                arrival_mode            VARCHAR(64),
                chief_complaint         TEXT,
                handoff_source          VARCHAR(128),
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL
            );
            """),

        ("cl_emergency.triage_assessment", """
            CREATE TABLE IF NOT EXISTS cl_emergency.triage_assessment (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                arrival_id              VARCHAR(32) NOT NULL REFERENCES cl_emergency.emergency_arrival(id),
                patient_id              VARCHAR(32),
                acuity_level            VARCHAR(16) NOT NULL,
                chief_complaint         TEXT,
                vital_snapshot_json     JSONB NOT NULL DEFAULT '{}',
                re_triage_flag          BOOLEAN NOT NULL DEFAULT FALSE,
                pathway_recommendation  VARCHAR(64),
                performed_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
                performed_by            VARCHAR(128) NOT NULL,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """),

        ("cl_diagnostics.result_record", """
            CREATE TABLE IF NOT EXISTS cl_diagnostics.result_record (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                order_id                VARCHAR(32) NOT NULL,
                analyte_code            VARCHAR(64) NOT NULL,
                measured_value          VARCHAR(256),
                unit_code               VARCHAR(32),
                abnormal_flag           VARCHAR(16),
                critical_flag           BOOLEAN NOT NULL DEFAULT FALSE,
                result_at               TIMESTAMPTZ NOT NULL,
                recorded_by             VARCHAR(128) NOT NULL,
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'clinical_restricted',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            """),

        ("op_revenue.claim", """
            CREATE TABLE IF NOT EXISTS op_revenue.claim (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64) NOT NULL,
                patient_id              VARCHAR(32) NOT NULL,
                encounter_ref           VARCHAR(32) NOT NULL,
                payer_ref               VARCHAR(128) NOT NULL,
                claim_status            VARCHAR(32) NOT NULL,
                billed_amount           NUMERIC(14,2) NOT NULL DEFAULT 0,
                allowed_amount          NUMERIC(14,2),
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'financial_sensitive',
                legal_hold_flag         BOOLEAN NOT NULL DEFAULT FALSE,
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL,
                updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                updated_by              VARCHAR(128) NOT NULL,
                version_no              INTEGER NOT NULL DEFAULT 1
            );
            """),

        ("gov_audit.audit_event", """
            CREATE TABLE IF NOT EXISTS gov_audit.audit_event (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64),
                event_type              VARCHAR(64) NOT NULL,
                entity_type             VARCHAR(64) NOT NULL,
                entity_id               VARCHAR(128) NOT NULL,
                actor_type              VARCHAR(32) NOT NULL,
                actor_id                VARCHAR(128) NOT NULL,
                correlation_id          VARCHAR(128) NOT NULL,
                classification_code     VARCHAR(64) NOT NULL,
                occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
                payload_json            JSONB NOT NULL DEFAULT '{}',
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """),

        ("gov_ai.ai_interaction", """
            CREATE TABLE IF NOT EXISTS gov_ai.ai_interaction (
                id                      VARCHAR(32) PRIMARY KEY DEFAULT replace(gen_random_uuid()::text, '-', ''),
                tenant_id               VARCHAR(64) NOT NULL,
                region_id               VARCHAR(64) NOT NULL,
                facility_id             VARCHAR(64),
                interaction_type        VARCHAR(64) NOT NULL,
                encounter_id            VARCHAR(32),
                patient_id              VARCHAR(32),
                model_version           VARCHAR(64) NOT NULL,
                prompt_version          VARCHAR(64) NOT NULL,
                input_summary_json      JSONB,
                output_summary_json     JSONB,
                outcome_code            VARCHAR(32) NOT NULL,
                accepted_by             VARCHAR(128),
                rejected_by             VARCHAR(128),
                override_reason         TEXT,
                classification_code     VARCHAR(64) NOT NULL DEFAULT 'ai_evidence',
                created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
                created_by              VARCHAR(128) NOT NULL
            );
            """)
    ];

    private static List<(string name, string ddl)> GenerateIndexDdl() =>
    [
        ("idx_patient_tenant_epk", "CREATE UNIQUE INDEX IF NOT EXISTS idx_patient_tenant_epk ON cl_mpi.patient_profile (tenant_id, enterprise_person_key);"),
        ("idx_patient_id_tenant_type", "CREATE UNIQUE INDEX IF NOT EXISTS idx_patient_id_tenant_type ON cl_mpi.patient_identifier (tenant_id, identifier_type, identifier_value_hash);"),
        ("idx_encounter_tenant_patient", "CREATE INDEX IF NOT EXISTS idx_encounter_tenant_patient ON cl_encounter.encounter (tenant_id, patient_id);"),
        ("idx_encounter_tenant_status", "CREATE INDEX IF NOT EXISTS idx_encounter_tenant_status ON cl_encounter.encounter (tenant_id, status_code);"),
        ("idx_note_encounter", "CREATE INDEX IF NOT EXISTS idx_note_encounter ON cl_encounter.clinical_note (tenant_id, encounter_id);"),
        ("idx_admission_tenant_patient", "CREATE INDEX IF NOT EXISTS idx_admission_tenant_patient ON cl_inpatient.admission (tenant_id, patient_id);"),
        ("idx_emergency_tenant_facility", "CREATE INDEX IF NOT EXISTS idx_emergency_tenant_facility ON cl_emergency.emergency_arrival (tenant_id, facility_id);"),
        ("idx_result_tenant_patient", "CREATE INDEX IF NOT EXISTS idx_result_tenant_patient ON cl_diagnostics.result_record (tenant_id, patient_id);"),
        ("idx_result_tenant_order", "CREATE INDEX IF NOT EXISTS idx_result_tenant_order ON cl_diagnostics.result_record (tenant_id, order_id);"),
        ("idx_claim_tenant_patient", "CREATE INDEX IF NOT EXISTS idx_claim_tenant_patient ON op_revenue.claim (tenant_id, patient_id);"),
        ("idx_audit_tenant_entity", "CREATE INDEX IF NOT EXISTS idx_audit_tenant_entity ON gov_audit.audit_event (tenant_id, entity_type, entity_id);"),
        ("idx_audit_correlation", "CREATE INDEX IF NOT EXISTS idx_audit_correlation ON gov_audit.audit_event (correlation_id);"),
        ("idx_ai_tenant_encounter", "CREATE INDEX IF NOT EXISTS idx_ai_tenant_encounter ON gov_ai.ai_interaction (tenant_id, encounter_id);")
    ];

    private static List<(string name, string ddl)> GenerateStoredProcedures() =>
    [
        ("sp_set_tenant_context", """
            CREATE OR REPLACE PROCEDURE sp_set_tenant_context(p_tenant_id TEXT)
            LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM set_config('app.current_tenant_id', p_tenant_id, true);
            END;
            $$;
            """),

        ("sp_register_patient", """
            CREATE OR REPLACE PROCEDURE sp_register_patient(
                p_tenant_id TEXT, p_region_id TEXT, p_facility_id TEXT,
                p_epk TEXT, p_given TEXT, p_family TEXT, p_dob DATE,
                p_created_by TEXT,
                INOUT p_patient_id TEXT DEFAULT NULL
            )
            LANGUAGE plpgsql AS $$
            BEGIN
                INSERT INTO cl_mpi.patient_profile
                    (tenant_id, region_id, facility_id, enterprise_person_key,
                     legal_given_name, legal_family_name, date_of_birth, created_by, updated_by)
                VALUES
                    (p_tenant_id, p_region_id, p_facility_id, p_epk,
                     p_given, p_family, p_dob, p_created_by, p_created_by)
                RETURNING id INTO p_patient_id;
            END;
            $$;
            """),

        ("sp_create_encounter", """
            CREATE OR REPLACE PROCEDURE sp_create_encounter(
                p_tenant_id TEXT, p_region_id TEXT, p_facility_id TEXT,
                p_patient_id TEXT, p_encounter_type TEXT, p_created_by TEXT,
                INOUT p_encounter_id TEXT DEFAULT NULL
            )
            LANGUAGE plpgsql AS $$
            BEGIN
                INSERT INTO cl_encounter.encounter
                    (tenant_id, region_id, facility_id, patient_id,
                     encounter_type, start_at, created_by, updated_by)
                VALUES
                    (p_tenant_id, p_region_id, p_facility_id, p_patient_id,
                     p_encounter_type, now(), p_created_by, p_created_by)
                RETURNING id INTO p_encounter_id;
            END;
            $$;
            """),

        ("sp_admit_patient", """
            CREATE OR REPLACE PROCEDURE sp_admit_patient(
                p_tenant_id TEXT, p_region_id TEXT, p_facility_id TEXT,
                p_patient_id TEXT, p_encounter_id TEXT, p_admit_class TEXT,
                p_created_by TEXT,
                INOUT p_admission_id TEXT DEFAULT NULL
            )
            LANGUAGE plpgsql AS $$
            BEGIN
                INSERT INTO cl_inpatient.admission
                    (tenant_id, region_id, facility_id, patient_id, encounter_id,
                     admit_class, created_by, updated_by)
                VALUES
                    (p_tenant_id, p_region_id, p_facility_id, p_patient_id, p_encounter_id,
                     p_admit_class, p_created_by, p_created_by)
                RETURNING id INTO p_admission_id;
            END;
            $$;
            """),

        ("sp_log_audit_event", """
            CREATE OR REPLACE PROCEDURE sp_log_audit_event(
                p_tenant_id TEXT, p_region_id TEXT, p_event_type TEXT,
                p_entity_type TEXT, p_entity_id TEXT,
                p_actor_type TEXT, p_actor_id TEXT,
                p_correlation_id TEXT, p_classification TEXT,
                p_payload JSONB DEFAULT '{}'
            )
            LANGUAGE plpgsql AS $$
            BEGIN
                INSERT INTO gov_audit.audit_event
                    (tenant_id, region_id, event_type, entity_type, entity_id,
                     actor_type, actor_id, correlation_id, classification_code, payload_json)
                VALUES
                    (p_tenant_id, p_region_id, p_event_type, p_entity_type, p_entity_id,
                     p_actor_type, p_actor_id, p_correlation_id, p_classification, p_payload);
            END;
            $$;
            """),

        ("fn_patient_count_by_tenant", """
            CREATE OR REPLACE FUNCTION fn_patient_count_by_tenant(p_tenant_id TEXT)
            RETURNS BIGINT
            LANGUAGE sql STABLE AS $$
                SELECT count(*) FROM cl_mpi.patient_profile WHERE tenant_id = p_tenant_id AND status_code = 'active';
            $$;
            """)
    ];

    private static List<(string name, string ddl)> GenerateRlsPolicies()
    {
        var tables = new[]
        {
            ("cl_mpi.patient_profile", "tenant_isolation_patient"),
            ("cl_encounter.encounter", "tenant_isolation_encounter"),
            ("cl_inpatient.admission", "tenant_isolation_admission"),
            ("cl_emergency.emergency_arrival", "tenant_isolation_emergency"),
            ("cl_diagnostics.result_record", "tenant_isolation_result"),
            ("op_revenue.claim", "tenant_isolation_claim"),
            ("gov_audit.audit_event", "tenant_isolation_audit"),
            ("gov_ai.ai_interaction", "tenant_isolation_ai")
        };

        var policies = new List<(string, string)>();
        foreach (var (table, policy) in tables)
        {
            policies.Add((policy, $"""
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS {policy} ON {table};
                CREATE POLICY {policy} ON {table}
                    USING (tenant_id = current_setting('app.current_tenant_id', true));
                """));
        }
        return policies;
    }

    #endregion

    #region Helpers

    private async Task WaitForPostgresReady(PipelineConfig config, CancellationToken ct)
    {
        for (int i = 0; i < 30; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct);
            var (exit, _) = await RunProcessAsync("docker",
                $"exec {config.DockerContainerName} pg_isready -U {config.DbUser}", ct);
            if (exit == 0)
            {
                _logger.LogInformation("PostgreSQL is ready.");
                return;
            }
        }
        _logger.LogWarning("Timed out waiting for PostgreSQL to become ready.");
    }

    private static async Task ExecuteSqlAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 30;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(int exitCode, string output)> RunProcessAsync(
        string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var sb = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, sb.ToString());
    }

    #endregion
}
