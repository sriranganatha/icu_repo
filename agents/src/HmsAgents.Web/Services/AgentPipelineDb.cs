using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HmsAgents.Web.Services;

/// <summary>
/// SQLite-backed persistent database for all pipeline run data.
/// Survives server restarts and enables disaster recovery of the full pipeline state.
/// DB file: agents/src/HmsAgents.Web/agent-pipeline.db
/// </summary>
public sealed class AgentPipelineDb : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<AgentPipelineDb> _logger;
    private static readonly JsonSerializerOptions s_json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AgentPipelineDb(ILogger<AgentPipelineDb> logger)
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "agent-pipeline.db");
        dbPath = Path.GetFullPath(dbPath);
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;

        InitializeSchema();
        _logger.LogInformation("AgentPipelineDb initialized at {Path}", dbPath);
    }

    // ════════════════════════════════════════════════════════════════
    //  Schema
    // ════════════════════════════════════════════════════════════════

    private void InitializeSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS PipelineRuns (
                RunId           TEXT PRIMARY KEY,
                Status          TEXT NOT NULL DEFAULT 'Running',
                StartedAt       TEXT NOT NULL,
                CompletedAt     TEXT,
                RequirementCount INTEGER DEFAULT 0,
                ArtifactCount   INTEGER DEFAULT 0,
                FindingCount    INTEGER DEFAULT 0,
                TestDiagCount   INTEGER DEFAULT 0,
                BacklogCount    INTEGER DEFAULT 0,
                DurationMs      REAL DEFAULT 0,
                Instructions    TEXT,
                ConfigJson      TEXT
            );

            CREATE TABLE IF NOT EXISTS AgentEvents (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId       TEXT NOT NULL,
                Agent       TEXT NOT NULL,
                Status      TEXT NOT NULL,
                Message     TEXT,
                ArtifactCount INTEGER DEFAULT 0,
                FindingCount  INTEGER DEFAULT 0,
                ElapsedMs   REAL DEFAULT 0,
                RetryAttempt INTEGER DEFAULT 0,
                Timestamp   TEXT NOT NULL,
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS AgentStatuses (
                RunId       TEXT NOT NULL,
                Agent       TEXT NOT NULL,
                Status      TEXT NOT NULL,
                Message     TEXT,
                ElapsedMs   REAL DEFAULT 0,
                ArtifactCount INTEGER DEFAULT 0,
                FindingCount  INTEGER DEFAULT 0,
                RetryAttempt INTEGER DEFAULT 0,
                UpdatedAt   TEXT NOT NULL,
                PRIMARY KEY (RunId, Agent),
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS Requirements (
                Id                  TEXT NOT NULL,
                RunId               TEXT NOT NULL,
                SourceFile          TEXT,
                Section             TEXT,
                HeadingLevel        INTEGER DEFAULT 0,
                Title               TEXT NOT NULL,
                Description         TEXT,
                Module              TEXT,
                Tags                TEXT,
                AcceptanceCriteria  TEXT,
                DependsOn           TEXT,
                CreatedAt           TEXT NOT NULL,
                PRIMARY KEY (Id, RunId),
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS BacklogItems (
                Id                  TEXT NOT NULL,
                RunId               TEXT NOT NULL,
                ParentId            TEXT,
                SourceRequirementId TEXT,
                ItemType            TEXT NOT NULL,
                Status              TEXT NOT NULL DEFAULT 'New',
                Title               TEXT NOT NULL,
                Description         TEXT,
                Module              TEXT,
                Priority            INTEGER DEFAULT 0,
                Iteration           INTEGER DEFAULT 0,
                AcceptanceCriteria  TEXT,
                DependsOn           TEXT,
                Tags                TEXT,
                CreatedAt           TEXT NOT NULL,
                StartedAt           TEXT,
                CompletedAt         TEXT,
                AssignedAgent       TEXT DEFAULT '',
                PRIMARY KEY (Id, RunId),
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS Findings (
                Id                  TEXT NOT NULL,
                RunId               TEXT NOT NULL,
                ArtifactId          TEXT,
                FilePath            TEXT,
                LineNumber          INTEGER,
                Severity            TEXT NOT NULL,
                Category            TEXT,
                Message             TEXT,
                Suggestion          TEXT,
                TracedRequirementId TEXT,
                PRIMARY KEY (Id, RunId),
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS Artifacts (
                Id              TEXT NOT NULL,
                RunId           TEXT NOT NULL,
                Layer           TEXT NOT NULL,
                RelativePath    TEXT,
                FileName        TEXT,
                Namespace       TEXT,
                ProducedBy      TEXT,
                ContentLength   INTEGER DEFAULT 0,
                TracedReqIds    TEXT,
                GeneratedAt     TEXT NOT NULL,
                PRIMARY KEY (Id, RunId),
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS TestDiagnostics (
                Id              TEXT NOT NULL,
                RunId           TEXT NOT NULL,
                TestName        TEXT,
                AgentUnderTest  TEXT,
                Outcome         TEXT NOT NULL,
                Diagnostic      TEXT,
                Remediation     TEXT,
                Category        TEXT,
                DurationMs      REAL DEFAULT 0,
                AttemptNumber   INTEGER DEFAULT 1,
                Timestamp       TEXT NOT NULL,
                PRIMARY KEY (Id, RunId),
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE INDEX IF NOT EXISTS IX_AgentEvents_RunId ON AgentEvents(RunId);
            CREATE INDEX IF NOT EXISTS IX_AgentStatuses_RunId ON AgentStatuses(RunId);
            CREATE INDEX IF NOT EXISTS IX_Requirements_RunId ON Requirements(RunId);
            CREATE INDEX IF NOT EXISTS IX_BacklogItems_RunId ON BacklogItems(RunId);
            CREATE INDEX IF NOT EXISTS IX_Findings_RunId ON Findings(RunId);
            CREATE INDEX IF NOT EXISTS IX_Artifacts_RunId ON Artifacts(RunId);
            CREATE INDEX IF NOT EXISTS IX_TestDiagnostics_RunId ON TestDiagnostics(RunId);

            CREATE TABLE IF NOT EXISTS AuditLog (
                Id              TEXT NOT NULL,
                RunId           TEXT NOT NULL,
                Sequence        INTEGER NOT NULL,
                Agent           TEXT NOT NULL,
                Action          TEXT NOT NULL,
                Severity        TEXT NOT NULL DEFAULT 'Info',
                Description     TEXT NOT NULL,
                Details         TEXT,
                InputHash       TEXT,
                OutputHash      TEXT,
                Timestamp       TEXT NOT NULL,
                PreviousHash    TEXT NOT NULL DEFAULT '',
                EntryHash       TEXT NOT NULL,
                PRIMARY KEY (Id),
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS HumanDecisions (
                Id              TEXT NOT NULL PRIMARY KEY,
                RunId           TEXT NOT NULL,
                RequestingAgent TEXT NOT NULL,
                Category        TEXT NOT NULL,
                Title           TEXT NOT NULL,
                Description     TEXT NOT NULL,
                Details         TEXT,
                Decision        TEXT NOT NULL DEFAULT 'Pending',
                DecisionReason  TEXT,
                RequestedAt     TEXT NOT NULL,
                DecidedAt       TEXT,
                TimeoutMinutes  REAL NOT NULL DEFAULT 30,
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE TABLE IF NOT EXISTS OrchestratorInstructions (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId           TEXT,
                Instruction     TEXT NOT NULL,
                Source          TEXT NOT NULL DEFAULT 'Manual',
                CreatedAt       TEXT NOT NULL,
                FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
            );

            CREATE INDEX IF NOT EXISTS IX_AuditLog_RunId ON AuditLog(RunId);
            CREATE INDEX IF NOT EXISTS IX_AuditLog_RunId_Sequence ON AuditLog(RunId, Sequence);
            CREATE INDEX IF NOT EXISTS IX_HumanDecisions_RunId ON HumanDecisions(RunId);
            CREATE INDEX IF NOT EXISTS IX_HumanDecisions_Pending ON HumanDecisions(Decision) WHERE Decision = 'Pending';
            CREATE INDEX IF NOT EXISTS IX_OrchestratorInstructions_RunId ON OrchestratorInstructions(RunId);
        """;
        cmd.ExecuteNonQuery();
    }

    // ════════════════════════════════════════════════════════════════
    //  Pipeline Run lifecycle
    // ════════════════════════════════════════════════════════════════

    // ════════════════════════════════════════════════════════════════
    //  Orchestrator Instructions
    // ════════════════════════════════════════════════════════════════

    public void SaveInstruction(string? runId, string instruction, string source = "Manual")
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO OrchestratorInstructions (RunId, Instruction, Source, CreatedAt)
            VALUES (@runId, @instruction, @source, @now)
        """;
        cmd.Parameters.AddWithValue("@runId", (object?)runId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@instruction", instruction);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<InstructionRow> GetInstructionHistory(int limit = 50)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, RunId, Instruction, Source, CreatedAt
            FROM OrchestratorInstructions
            ORDER BY CreatedAt DESC
            LIMIT @limit
        """;
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        var rows = new List<InstructionRow>();
        while (reader.Read())
        {
            rows.Add(new InstructionRow
            {
                Id = reader.GetInt32(0),
                RunId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Instruction = reader.GetString(2),
                Source = reader.GetString(3),
                CreatedAt = reader.GetString(4)
            });
        }
        return rows;
    }

    public sealed class InstructionRow
    {
        public int Id { get; set; }
        public string? RunId { get; set; }
        public string Instruction { get; set; } = "";
        public string Source { get; set; } = "Manual";
        public string CreatedAt { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════════
    //  Pipeline Run lifecycle
    // ════════════════════════════════════════════════════════════════

    public void StartRun(string runId, string? instructions, string? configJson)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PipelineRuns (RunId, Status, StartedAt, Instructions, ConfigJson)
            VALUES (@runId, 'Running', @now, @instructions, @configJson)
        """;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@instructions", (object?)instructions ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@configJson", (object?)configJson ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void CompleteRun(string runId, int requirementCount, int artifactCount,
        int findingCount, int testDiagCount, int backlogCount, double durationMs)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE PipelineRuns SET
                Status = 'Completed', CompletedAt = @now,
                RequirementCount = @reqs, ArtifactCount = @arts,
                FindingCount = @finds, TestDiagCount = @tests,
                BacklogCount = @backlog, DurationMs = @dur
            WHERE RunId = @runId
        """;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@reqs", requirementCount);
        cmd.Parameters.AddWithValue("@arts", artifactCount);
        cmd.Parameters.AddWithValue("@finds", findingCount);
        cmd.Parameters.AddWithValue("@tests", testDiagCount);
        cmd.Parameters.AddWithValue("@backlog", backlogCount);
        cmd.Parameters.AddWithValue("@dur", durationMs);
        cmd.ExecuteNonQuery();
    }

    public void FailRun(string runId, string error)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE PipelineRuns SET Status = 'Failed', CompletedAt = @now
            WHERE RunId = @runId
        """;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    // ════════════════════════════════════════════════════════════════
    //  Agent Events (append-only timeline)
    // ════════════════════════════════════════════════════════════════

    public void RecordAgentEvent(string runId, string agent, string status, string message,
        int artifactCount, int findingCount, double elapsedMs, int retryAttempt)
    {
        using var conn = Open();

        // Append to event log
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO AgentEvents (RunId, Agent, Status, Message, ArtifactCount, FindingCount, ElapsedMs, RetryAttempt, Timestamp)
                VALUES (@runId, @agent, @status, @msg, @arts, @finds, @elapsed, @retry, @now)
            """;
            cmd.Parameters.AddWithValue("@runId", runId);
            cmd.Parameters.AddWithValue("@agent", agent);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@msg", message ?? "");
            cmd.Parameters.AddWithValue("@arts", artifactCount);
            cmd.Parameters.AddWithValue("@finds", findingCount);
            cmd.Parameters.AddWithValue("@elapsed", elapsedMs);
            cmd.Parameters.AddWithValue("@retry", retryAttempt);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        // Upsert latest status
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO AgentStatuses (RunId, Agent, Status, Message, ElapsedMs, ArtifactCount, FindingCount, RetryAttempt, UpdatedAt)
                VALUES (@runId, @agent, @status, @msg, @elapsed, @arts, @finds, @retry, @now)
                ON CONFLICT(RunId, Agent) DO UPDATE SET
                    Status = @status, Message = @msg, ElapsedMs = @elapsed,
                    ArtifactCount = CASE WHEN @arts > 0 THEN @arts ELSE AgentStatuses.ArtifactCount END,
                    FindingCount = CASE WHEN @finds > 0 THEN @finds ELSE AgentStatuses.FindingCount END,
                    RetryAttempt = CASE WHEN @retry > 0 THEN @retry ELSE AgentStatuses.RetryAttempt END,
                    UpdatedAt = @now
            """;
            cmd.Parameters.AddWithValue("@runId", runId);
            cmd.Parameters.AddWithValue("@agent", agent);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@msg", message ?? "");
            cmd.Parameters.AddWithValue("@elapsed", elapsedMs);
            cmd.Parameters.AddWithValue("@arts", artifactCount);
            cmd.Parameters.AddWithValue("@finds", findingCount);
            cmd.Parameters.AddWithValue("@retry", retryAttempt);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Bulk persist at pipeline completion
    // ════════════════════════════════════════════════════════════════

    public void SaveRequirements(string runId, IEnumerable<RequirementRow> items)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM Requirements WHERE RunId = @runId";
                del.Parameters.AddWithValue("@runId", runId);
                del.ExecuteNonQuery();
            }

            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO Requirements (Id, RunId, SourceFile, Section, HeadingLevel, Title, Description,
                        Module, Tags, AcceptanceCriteria, DependsOn, CreatedAt)
                    VALUES (@id, @runId, @src, @section, @heading, @title, @desc,
                        @module, @tags, @ac, @deps, @created)
                """;
                cmd.Parameters.AddWithValue("@id", item.Id);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@src", item.SourceFile ?? "");
                cmd.Parameters.AddWithValue("@section", item.Section ?? "");
                cmd.Parameters.AddWithValue("@heading", item.HeadingLevel);
                cmd.Parameters.AddWithValue("@title", item.Title);
                cmd.Parameters.AddWithValue("@desc", item.Description ?? "");
                cmd.Parameters.AddWithValue("@module", item.Module ?? "");
                cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(item.Tags ?? [], s_json));
                cmd.Parameters.AddWithValue("@ac", JsonSerializer.Serialize(item.AcceptanceCriteria ?? [], s_json));
                cmd.Parameters.AddWithValue("@deps", JsonSerializer.Serialize(item.DependsOn ?? [], s_json));
                cmd.Parameters.AddWithValue("@created", item.CreatedAt.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void SaveBacklogItems(string runId, IEnumerable<BacklogItemRow> items)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // Clear old items for this run
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM BacklogItems WHERE RunId = @runId";
                del.Parameters.AddWithValue("@runId", runId);
                del.ExecuteNonQuery();
            }

            foreach (var item in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO BacklogItems (Id, RunId, ParentId, SourceRequirementId, ItemType, Status,
                        Title, Description, Module, Priority, Iteration, AcceptanceCriteria, DependsOn, Tags,
                        CreatedAt, StartedAt, CompletedAt, AssignedAgent)
                    VALUES (@id, @runId, @parentId, @srcReqId, @type, @status,
                        @title, @desc, @module, @priority, @iteration, @ac, @deps, @tags,
                        @created, @started, @completed, @assignedAgent)
                """;
                cmd.Parameters.AddWithValue("@id", item.Id);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@parentId", item.ParentId ?? "");
                cmd.Parameters.AddWithValue("@srcReqId", item.SourceRequirementId ?? "");
                cmd.Parameters.AddWithValue("@type", item.ItemType);
                cmd.Parameters.AddWithValue("@status", item.Status);
                cmd.Parameters.AddWithValue("@title", item.Title);
                cmd.Parameters.AddWithValue("@desc", item.Description ?? "");
                cmd.Parameters.AddWithValue("@module", item.Module ?? "");
                cmd.Parameters.AddWithValue("@priority", item.Priority);
                cmd.Parameters.AddWithValue("@iteration", item.Iteration);
                cmd.Parameters.AddWithValue("@ac", JsonSerializer.Serialize(item.AcceptanceCriteria ?? [], s_json));
                cmd.Parameters.AddWithValue("@deps", JsonSerializer.Serialize(item.DependsOn ?? [], s_json));
                cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(item.Tags ?? [], s_json));
                cmd.Parameters.AddWithValue("@created", item.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@started", (object?)item.StartedAt?.ToString("o") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@completed", (object?)item.CompletedAt?.ToString("o") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@assignedAgent", item.AssignedAgent ?? "");
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void SaveFindings(string runId, IEnumerable<FindingRow> items)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM Findings WHERE RunId = @runId";
                del.Parameters.AddWithValue("@runId", runId);
                del.ExecuteNonQuery();
            }

            foreach (var f in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO Findings (Id, RunId, ArtifactId, FilePath, LineNumber, Severity, Category, Message, Suggestion, TracedRequirementId)
                    VALUES (@id, @runId, @artId, @path, @line, @sev, @cat, @msg, @sug, @reqId)
                """;
                cmd.Parameters.AddWithValue("@id", f.Id);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@artId", f.ArtifactId ?? "");
                cmd.Parameters.AddWithValue("@path", f.FilePath ?? "");
                cmd.Parameters.AddWithValue("@line", (object?)f.LineNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sev", f.Severity);
                cmd.Parameters.AddWithValue("@cat", f.Category ?? "");
                cmd.Parameters.AddWithValue("@msg", f.Message ?? "");
                cmd.Parameters.AddWithValue("@sug", f.Suggestion ?? "");
                cmd.Parameters.AddWithValue("@reqId", (object?)f.TracedRequirementId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public void SaveArtifacts(string runId, IEnumerable<ArtifactRow> items)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM Artifacts WHERE RunId = @runId";
                del.Parameters.AddWithValue("@runId", runId);
                del.ExecuteNonQuery();
            }

            foreach (var a in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO Artifacts (Id, RunId, Layer, RelativePath, FileName, Namespace, ProducedBy, ContentLength, TracedReqIds, GeneratedAt)
                    VALUES (@id, @runId, @layer, @path, @file, @ns, @by, @len, @reqs, @gen)
                """;
                cmd.Parameters.AddWithValue("@id", a.Id);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@layer", a.Layer);
                cmd.Parameters.AddWithValue("@path", a.RelativePath ?? "");
                cmd.Parameters.AddWithValue("@file", a.FileName ?? "");
                cmd.Parameters.AddWithValue("@ns", a.Namespace ?? "");
                cmd.Parameters.AddWithValue("@by", a.ProducedBy);
                cmd.Parameters.AddWithValue("@len", a.ContentLength);
                cmd.Parameters.AddWithValue("@reqs", JsonSerializer.Serialize(a.TracedReqIds ?? [], s_json));
                cmd.Parameters.AddWithValue("@gen", a.GeneratedAt.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public void SaveTestDiagnostics(string runId, IEnumerable<TestDiagRow> items)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            using (var del = conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM TestDiagnostics WHERE RunId = @runId";
                del.Parameters.AddWithValue("@runId", runId);
                del.ExecuteNonQuery();
            }

            foreach (var d in items)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO TestDiagnostics (Id, RunId, TestName, AgentUnderTest, Outcome, Diagnostic, Remediation, Category, DurationMs, AttemptNumber, Timestamp)
                    VALUES (@id, @runId, @name, @agent, @outcome, @diag, @rem, @cat, @dur, @attempt, @ts)
                """;
                cmd.Parameters.AddWithValue("@id", d.Id);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@name", d.TestName ?? "");
                cmd.Parameters.AddWithValue("@agent", d.AgentUnderTest ?? "");
                cmd.Parameters.AddWithValue("@outcome", d.Outcome);
                cmd.Parameters.AddWithValue("@diag", d.Diagnostic ?? "");
                cmd.Parameters.AddWithValue("@rem", d.Remediation ?? "");
                cmd.Parameters.AddWithValue("@cat", d.Category ?? "");
                cmd.Parameters.AddWithValue("@dur", d.DurationMs);
                cmd.Parameters.AddWithValue("@attempt", d.AttemptNumber);
                cmd.Parameters.AddWithValue("@ts", d.Timestamp.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // ════════════════════════════════════════════════════════════════
    //  Queries — for dashboard restore
    // ════════════════════════════════════════════════════════════════

    public PipelineRunRow? GetLatestRun()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM PipelineRuns ORDER BY StartedAt DESC LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PipelineRunRow
        {
            RunId = r.GetString(r.GetOrdinal("RunId")),
            Status = r.GetString(r.GetOrdinal("Status")),
            StartedAt = r.GetString(r.GetOrdinal("StartedAt")),
            CompletedAt = r.IsDBNull(r.GetOrdinal("CompletedAt")) ? null : r.GetString(r.GetOrdinal("CompletedAt")),
            RequirementCount = r.GetInt32(r.GetOrdinal("RequirementCount")),
            ArtifactCount = r.GetInt32(r.GetOrdinal("ArtifactCount")),
            FindingCount = r.GetInt32(r.GetOrdinal("FindingCount")),
            TestDiagCount = r.GetInt32(r.GetOrdinal("TestDiagCount")),
            BacklogCount = r.GetInt32(r.GetOrdinal("BacklogCount")),
            DurationMs = r.GetDouble(r.GetOrdinal("DurationMs"))
        };
    }

    public List<PipelineRunRow> GetAllRuns()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM PipelineRuns ORDER BY StartedAt DESC";
        using var r = cmd.ExecuteReader();
        var rows = new List<PipelineRunRow>();
        while (r.Read())
        {
            rows.Add(new PipelineRunRow
            {
                RunId = r.GetString(r.GetOrdinal("RunId")),
                Status = r.GetString(r.GetOrdinal("Status")),
                StartedAt = r.GetString(r.GetOrdinal("StartedAt")),
                CompletedAt = r.IsDBNull(r.GetOrdinal("CompletedAt")) ? null : r.GetString(r.GetOrdinal("CompletedAt")),
                RequirementCount = r.GetInt32(r.GetOrdinal("RequirementCount")),
                ArtifactCount = r.GetInt32(r.GetOrdinal("ArtifactCount")),
                FindingCount = r.GetInt32(r.GetOrdinal("FindingCount")),
                TestDiagCount = r.GetInt32(r.GetOrdinal("TestDiagCount")),
                BacklogCount = r.GetInt32(r.GetOrdinal("BacklogCount")),
                DurationMs = r.GetDouble(r.GetOrdinal("DurationMs"))
            });
        }
        return rows;
    }

    public Dictionary<string, AgentStatusRow> GetAgentStatuses(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AgentStatuses WHERE RunId = @runId";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<string, AgentStatusRow>();
        while (r.Read())
        {
            var agent = r.GetString(r.GetOrdinal("Agent"));
            dict[agent] = new AgentStatusRow
            {
                Agent = agent,
                Status = r.GetString(r.GetOrdinal("Status")),
                Message = r.IsDBNull(r.GetOrdinal("Message")) ? "" : r.GetString(r.GetOrdinal("Message")),
                ElapsedMs = r.GetDouble(r.GetOrdinal("ElapsedMs")),
                ArtifactCount = r.GetInt32(r.GetOrdinal("ArtifactCount")),
                FindingCount = r.GetInt32(r.GetOrdinal("FindingCount")),
                RetryAttempt = r.GetInt32(r.GetOrdinal("RetryAttempt"))
            };
        }
        return dict;
    }

    public List<BacklogItemRow> GetBacklogItems(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM BacklogItems WHERE RunId = @runId ORDER BY Priority, ItemType";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var r = cmd.ExecuteReader();
        var items = new List<BacklogItemRow>();
        while (r.Read())
        {
            items.Add(new BacklogItemRow
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                ParentId = GetNullableString(r, "ParentId"),
                SourceRequirementId = GetNullableString(r, "SourceRequirementId"),
                ItemType = r.GetString(r.GetOrdinal("ItemType")),
                Status = r.GetString(r.GetOrdinal("Status")),
                Title = r.GetString(r.GetOrdinal("Title")),
                Description = GetNullableString(r, "Description"),
                Module = GetNullableString(r, "Module"),
                Priority = r.GetInt32(r.GetOrdinal("Priority")),
                Iteration = r.GetInt32(r.GetOrdinal("Iteration")),
                AcceptanceCriteria = DeserializeList(GetNullableString(r, "AcceptanceCriteria")),
                DependsOn = DeserializeList(GetNullableString(r, "DependsOn")),
                Tags = DeserializeList(GetNullableString(r, "Tags")),
                CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt"))),
                StartedAt = ParseNullableDateTimeOffset(GetNullableString(r, "StartedAt")),
                CompletedAt = ParseNullableDateTimeOffset(GetNullableString(r, "CompletedAt")),
                AssignedAgent = GetNullableString(r, "AssignedAgent") ?? ""
            });
        }
        return items;
    }

    /// <summary>Update priority for a single backlog item.</summary>
    public bool UpdateBacklogPriority(string runId, string itemId, int priority)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE BacklogItems SET Priority = @priority WHERE RunId = @runId AND Id = @id";
        cmd.Parameters.AddWithValue("@priority", priority);
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@id", itemId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Update status for a single backlog item.</summary>
    public bool UpdateBacklogStatus(string runId, string itemId, string status)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE BacklogItems SET Status = @status WHERE RunId = @runId AND Id = @id";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@id", itemId);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Update multiple fields on a single backlog item.</summary>
    public bool UpdateBacklogItem(string runId, string itemId, int? priority, string? status, string? assignedAgent, int? iteration)
    {
        var setClauses = new List<string>();
        var parameters = new List<(string name, object value)>();

        if (priority.HasValue) { setClauses.Add("Priority = @priority"); parameters.Add(("@priority", priority.Value)); }
        if (status is not null) { setClauses.Add("Status = @status"); parameters.Add(("@status", status)); }
        if (assignedAgent is not null) { setClauses.Add("AssignedAgent = @agent"); parameters.Add(("@agent", assignedAgent)); }
        if (iteration.HasValue) { setClauses.Add("Iteration = @iter"); parameters.Add(("@iter", iteration.Value)); }
        if (setClauses.Count == 0) return false;

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE BacklogItems SET {string.Join(", ", setClauses)} WHERE RunId = @runId AND Id = @id";
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@id", itemId);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<RequirementRow> GetRequirements(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Requirements WHERE RunId = @runId ORDER BY HeadingLevel, Title";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var r = cmd.ExecuteReader();
        var items = new List<RequirementRow>();
        while (r.Read())
        {
            items.Add(new RequirementRow
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                SourceFile = GetNullableString(r, "SourceFile"),
                Section = GetNullableString(r, "Section"),
                HeadingLevel = r.GetInt32(r.GetOrdinal("HeadingLevel")),
                Title = r.GetString(r.GetOrdinal("Title")),
                Description = GetNullableString(r, "Description"),
                Module = GetNullableString(r, "Module"),
                Tags = DeserializeList(GetNullableString(r, "Tags")),
                AcceptanceCriteria = DeserializeList(GetNullableString(r, "AcceptanceCriteria")),
                DependsOn = DeserializeList(GetNullableString(r, "DependsOn")),
                CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("CreatedAt")))
            });
        }
        return items;
    }

    public List<FindingRow> GetFindings(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Findings WHERE RunId = @runId";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var r = cmd.ExecuteReader();
        var items = new List<FindingRow>();
        while (r.Read())
        {
            items.Add(new FindingRow
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                ArtifactId = GetNullableString(r, "ArtifactId"),
                FilePath = GetNullableString(r, "FilePath"),
                LineNumber = r.IsDBNull(r.GetOrdinal("LineNumber")) ? null : r.GetInt32(r.GetOrdinal("LineNumber")),
                Severity = r.GetString(r.GetOrdinal("Severity")),
                Category = GetNullableString(r, "Category"),
                Message = GetNullableString(r, "Message"),
                Suggestion = GetNullableString(r, "Suggestion"),
                TracedRequirementId = GetNullableString(r, "TracedRequirementId")
            });
        }
        return items;
    }

    public List<ArtifactRow> GetArtifacts(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Artifacts WHERE RunId = @runId";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var r = cmd.ExecuteReader();
        var items = new List<ArtifactRow>();
        while (r.Read())
        {
            items.Add(new ArtifactRow
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                Layer = r.GetString(r.GetOrdinal("Layer")),
                RelativePath = GetNullableString(r, "RelativePath"),
                FileName = GetNullableString(r, "FileName"),
                Namespace = GetNullableString(r, "Namespace"),
                ProducedBy = r.GetString(r.GetOrdinal("ProducedBy")),
                ContentLength = r.GetInt32(r.GetOrdinal("ContentLength")),
                TracedReqIds = DeserializeList(GetNullableString(r, "TracedReqIds")),
                GeneratedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("GeneratedAt")))
            });
        }
        return items;
    }

    public List<TestDiagRow> GetTestDiagnostics(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM TestDiagnostics WHERE RunId = @runId";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var r = cmd.ExecuteReader();
        var items = new List<TestDiagRow>();
        while (r.Read())
        {
            items.Add(new TestDiagRow
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                TestName = GetNullableString(r, "TestName"),
                AgentUnderTest = GetNullableString(r, "AgentUnderTest"),
                Outcome = r.GetString(r.GetOrdinal("Outcome")),
                Diagnostic = GetNullableString(r, "Diagnostic"),
                Remediation = GetNullableString(r, "Remediation"),
                Category = GetNullableString(r, "Category"),
                DurationMs = r.GetDouble(r.GetOrdinal("DurationMs")),
                AttemptNumber = r.GetInt32(r.GetOrdinal("AttemptNumber")),
                Timestamp = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("Timestamp")))
            });
        }
        return items;
    }

    public List<AgentEventRow> GetAgentEvents(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM AgentEvents WHERE RunId = @runId ORDER BY Id ASC";
        cmd.Parameters.AddWithValue("@runId", runId);
        using var r = cmd.ExecuteReader();
        var items = new List<AgentEventRow>();
        while (r.Read())
        {
            items.Add(new AgentEventRow
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                Agent = r.GetString(r.GetOrdinal("Agent")),
                Status = r.GetString(r.GetOrdinal("Status")),
                Message = GetNullableString(r, "Message"),
                ArtifactCount = r.GetInt32(r.GetOrdinal("ArtifactCount")),
                FindingCount = r.GetInt32(r.GetOrdinal("FindingCount")),
                ElapsedMs = r.GetDouble(r.GetOrdinal("ElapsedMs")),
                RetryAttempt = r.GetInt32(r.GetOrdinal("RetryAttempt")),
                Timestamp = r.GetString(r.GetOrdinal("Timestamp"))
            });
        }
        return items;
    }

    // ════════════════════════════════════════════════════════════════
    //  Audit Log — hash-chained, tamper-evident
    // ════════════════════════════════════════════════════════════════

    private readonly object _auditLock = new();

    public (int Sequence, string EntryHash) AppendAuditEntry(
        string id, string runId, string agent, string action, string severity,
        string description, string? details, string? inputHash, string? outputHash,
        DateTimeOffset timestamp)
    {
        lock (_auditLock)
        {
            using var conn = Open();

            // Get current chain head
            int nextSeq = 1;
            string prevHash = "";
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Sequence, EntryHash FROM AuditLog WHERE RunId = @runId ORDER BY Sequence DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@runId", runId);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    nextSeq = r.GetInt32(0) + 1;
                    prevHash = r.GetString(1);
                }
            }

            // Compute hash: SHA256(seq + runId + agent + action + description + details + inputHash + outputHash + timestamp + prevHash)
            var payload = $"{nextSeq}|{runId}|{agent}|{action}|{description}|{details}|{inputHash}|{outputHash}|{timestamp:o}|{prevHash}";
            var entryHash = ComputeSha256(payload);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO AuditLog (Id, RunId, Sequence, Agent, Action, Severity, Description, Details, InputHash, OutputHash, Timestamp, PreviousHash, EntryHash)
                    VALUES (@id, @runId, @seq, @agent, @action, @severity, @desc, @details, @inHash, @outHash, @ts, @prevHash, @entryHash)
                """;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@runId", runId);
                cmd.Parameters.AddWithValue("@seq", nextSeq);
                cmd.Parameters.AddWithValue("@agent", agent);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@severity", severity);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@inHash", (object?)inputHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@outHash", (object?)outputHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ts", timestamp.ToString("o"));
                cmd.Parameters.AddWithValue("@prevHash", prevHash);
                cmd.Parameters.AddWithValue("@entryHash", entryHash);
                cmd.ExecuteNonQuery();
            }

            return (nextSeq, entryHash);
        }
    }

    public List<AuditLogRow> GetAuditLog(string runId, int? limit = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = limit.HasValue
            ? "SELECT * FROM AuditLog WHERE RunId = @runId ORDER BY Sequence ASC LIMIT @limit"
            : "SELECT * FROM AuditLog WHERE RunId = @runId ORDER BY Sequence ASC";
        cmd.Parameters.AddWithValue("@runId", runId);
        if (limit.HasValue) cmd.Parameters.AddWithValue("@limit", limit.Value);
        using var r = cmd.ExecuteReader();
        var items = new List<AuditLogRow>();
        while (r.Read())
        {
            items.Add(new AuditLogRow
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                RunId = r.GetString(r.GetOrdinal("RunId")),
                Sequence = r.GetInt32(r.GetOrdinal("Sequence")),
                Agent = r.GetString(r.GetOrdinal("Agent")),
                Action = r.GetString(r.GetOrdinal("Action")),
                Severity = r.GetString(r.GetOrdinal("Severity")),
                Description = r.GetString(r.GetOrdinal("Description")),
                Details = GetNullableString(r, "Details"),
                InputHash = GetNullableString(r, "InputHash"),
                OutputHash = GetNullableString(r, "OutputHash"),
                Timestamp = r.GetString(r.GetOrdinal("Timestamp")),
                PreviousHash = r.GetString(r.GetOrdinal("PreviousHash")),
                EntryHash = r.GetString(r.GetOrdinal("EntryHash"))
            });
        }
        return items;
    }

    public (bool IsValid, int? BrokenAtSequence) VerifyAuditChain(string runId)
    {
        var entries = GetAuditLog(runId);
        string prevHash = "";
        foreach (var e in entries)
        {
            if (e.PreviousHash != prevHash)
                return (false, e.Sequence);

            var payload = $"{e.Sequence}|{e.RunId}|{e.Agent}|{e.Action}|{e.Description}|{e.Details}|{e.InputHash}|{e.OutputHash}|{e.Timestamp}|{e.PreviousHash}";
            var expectedHash = ComputeSha256(payload);
            if (e.EntryHash != expectedHash)
                return (false, e.Sequence);

            prevHash = e.EntryHash;
        }
        return (true, null);
    }

    // ════════════════════════════════════════════════════════════════
    //  Human-in-the-Loop Decisions
    // ════════════════════════════════════════════════════════════════

    public void InsertHumanDecision(string id, string runId, string agent, string category,
        string title, string description, string? details, double timeoutMinutes)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO HumanDecisions (Id, RunId, RequestingAgent, Category, Title, Description, Details, Decision, RequestedAt, TimeoutMinutes)
            VALUES (@id, @runId, @agent, @cat, @title, @desc, @details, 'Pending', @now, @timeout)
        """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@agent", agent);
        cmd.Parameters.AddWithValue("@cat", category);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@desc", description);
        cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@timeout", timeoutMinutes);
        cmd.ExecuteNonQuery();
    }

    public void UpdateHumanDecision(string id, string decision, string? reason)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE HumanDecisions SET Decision = @decision, DecisionReason = @reason, DecidedAt = @now
            WHERE Id = @id AND Decision = 'Pending'
        """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@decision", decision);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<HumanDecisionRow> GetPendingDecisions()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM HumanDecisions WHERE Decision = 'Pending' ORDER BY RequestedAt ASC";
        return ReadDecisionRows(cmd);
    }

    public List<HumanDecisionRow> GetDecisionHistory(string runId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM HumanDecisions WHERE RunId = @runId ORDER BY RequestedAt ASC";
        cmd.Parameters.AddWithValue("@runId", runId);
        return ReadDecisionRows(cmd);
    }

    public HumanDecisionRow? GetDecision(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM HumanDecisions WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = ReadDecisionRows(cmd);
        return rows.Count > 0 ? rows[0] : null;
    }

    private static List<HumanDecisionRow> ReadDecisionRows(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var items = new List<HumanDecisionRow>();
        while (r.Read())
        {
            items.Add(new HumanDecisionRow
            {
                Id = r.GetString(r.GetOrdinal("Id")),
                RunId = r.GetString(r.GetOrdinal("RunId")),
                RequestingAgent = r.GetString(r.GetOrdinal("RequestingAgent")),
                Category = r.GetString(r.GetOrdinal("Category")),
                Title = r.GetString(r.GetOrdinal("Title")),
                Description = r.GetString(r.GetOrdinal("Description")),
                Details = GetNullableString(r, "Details"),
                Decision = r.GetString(r.GetOrdinal("Decision")),
                DecisionReason = GetNullableString(r, "DecisionReason"),
                RequestedAt = r.GetString(r.GetOrdinal("RequestedAt")),
                DecidedAt = GetNullableString(r, "DecidedAt"),
                TimeoutMinutes = r.GetDouble(r.GetOrdinal("TimeoutMinutes"))
            });
        }
        return items;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // Enable WAL mode for better concurrent read performance
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static string? GetNullableString(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, s_json) ?? []; }
        catch { return []; }
    }

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? s)
        => string.IsNullOrEmpty(s) ? null : DateTimeOffset.Parse(s);

    /// <summary>Deletes all data from every table — full project reset.</summary>
    public void PurgeAllData()
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        try
        {
            var tables = new[]
            {
                "AgentEvents", "AgentStatuses", "Requirements", "BacklogItems",
                "Findings", "Artifacts", "TestDiagnostics", "AuditLog",
                "HumanDecisions", "OrchestratorInstructions", "PipelineRuns"
            };
            foreach (var table in tables)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DELETE FROM {table}";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public void Dispose() { /* SQLite connections are opened/closed per operation */ }
}

// ════════════════════════════════════════════════════════════════════
//  Row DTOs
// ════════════════════════════════════════════════════════════════════

public sealed class PipelineRunRow
{
    public string RunId { get; set; } = "";
    public string Status { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
    public int RequirementCount { get; set; }
    public int ArtifactCount { get; set; }
    public int FindingCount { get; set; }
    public int TestDiagCount { get; set; }
    public int BacklogCount { get; set; }
    public double DurationMs { get; set; }
}

public sealed class AgentStatusRow
{
    public string Agent { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public double ElapsedMs { get; set; }
    public int ArtifactCount { get; set; }
    public int FindingCount { get; set; }
    public int RetryAttempt { get; set; }
}

public sealed class BacklogItemRow
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string? SourceRequirementId { get; set; }
    public string ItemType { get; set; } = "";
    public string Status { get; set; } = "New";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Module { get; set; }
    public int Priority { get; set; }
    public int Iteration { get; set; }
    public List<string>? AcceptanceCriteria { get; set; }
    public List<string>? DependsOn { get; set; }
    public List<string>? Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string AssignedAgent { get; set; } = "";
    // Gap-analysis & detail fields
    public string? DetailedSpec { get; set; }
    public List<string>? AffectedServices { get; set; }
    public List<string>? IdentifiedGaps { get; set; }
    public List<string>? MatchingArtifactPaths { get; set; }
    public string Coverage { get; set; } = "NotAssessed";
    public string ProducedBy { get; set; } = "";
}

public sealed class RequirementRow
{
    public string Id { get; set; } = "";
    public string? SourceFile { get; set; }
    public string? Section { get; set; }
    public int HeadingLevel { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Module { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? AcceptanceCriteria { get; set; }
    public List<string>? DependsOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FindingRow
{
    public string Id { get; set; } = "";
    public string? ArtifactId { get; set; }
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
    public string Severity { get; set; } = "";
    public string? Category { get; set; }
    public string? Message { get; set; }
    public string? Suggestion { get; set; }
    public string? TracedRequirementId { get; set; }
}

public sealed class ArtifactRow
{
    public string Id { get; set; } = "";
    public string Layer { get; set; } = "";
    public string? RelativePath { get; set; }
    public string? FileName { get; set; }
    public string? Namespace { get; set; }
    public string ProducedBy { get; set; } = "";
    public int ContentLength { get; set; }
    public List<string>? TracedReqIds { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}

public sealed class TestDiagRow
{
    public string Id { get; set; } = "";
    public string? TestName { get; set; }
    public string? AgentUnderTest { get; set; }
    public string Outcome { get; set; } = "";
    public string? Diagnostic { get; set; }
    public string? Remediation { get; set; }
    public string? Category { get; set; }
    public double DurationMs { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public sealed class AgentEventRow
{
    public int Id { get; set; }
    public string Agent { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public int ArtifactCount { get; set; }
    public int FindingCount { get; set; }
    public double ElapsedMs { get; set; }
    public int RetryAttempt { get; set; }
    public string Timestamp { get; set; } = "";
}

public sealed class AuditLogRow
{
    public string Id { get; set; } = "";
    public string RunId { get; set; } = "";
    public int Sequence { get; set; }
    public string Agent { get; set; } = "";
    public string Action { get; set; } = "";
    public string Severity { get; set; } = "Info";
    public string Description { get; set; } = "";
    public string? Details { get; set; }
    public string? InputHash { get; set; }
    public string? OutputHash { get; set; }
    public string Timestamp { get; set; } = "";
    public string PreviousHash { get; set; } = "";
    public string EntryHash { get; set; } = "";
}

public sealed class HumanDecisionRow
{
    public string Id { get; set; } = "";
    public string RunId { get; set; } = "";
    public string RequestingAgent { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Details { get; set; }
    public string Decision { get; set; } = "Pending";
    public string? DecisionReason { get; set; }
    public string RequestedAt { get; set; } = "";
    public string? DecidedAt { get; set; }
    public double TimeoutMinutes { get; set; }
}
