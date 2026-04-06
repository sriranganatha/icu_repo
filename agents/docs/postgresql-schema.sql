-- HMS Agents pipeline schema for PostgreSQL
-- Source: migrated from the previous SQLite schema in AgentPipelineDb

CREATE TABLE IF NOT EXISTS PipelineRuns (
    RunId             TEXT PRIMARY KEY,
    Status            TEXT NOT NULL DEFAULT 'Running',
    StartedAt         TEXT NOT NULL,
    CompletedAt       TEXT,
    RequirementCount  INTEGER DEFAULT 0,
    ArtifactCount     INTEGER DEFAULT 0,
    FindingCount      INTEGER DEFAULT 0,
    TestDiagCount     INTEGER DEFAULT 0,
    BacklogCount      INTEGER DEFAULT 0,
    DurationMs        DOUBLE PRECISION DEFAULT 0,
    Instructions      TEXT,
    ConfigJson        TEXT
);

CREATE TABLE IF NOT EXISTS AgentEvents (
    Id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    RunId             TEXT NOT NULL,
    Agent             TEXT NOT NULL,
    Status            TEXT NOT NULL,
    Message           TEXT,
    ArtifactCount     INTEGER DEFAULT 0,
    FindingCount      INTEGER DEFAULT 0,
    ElapsedMs         DOUBLE PRECISION DEFAULT 0,
    RetryAttempt      INTEGER DEFAULT 0,
    Timestamp         TEXT NOT NULL,
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS AgentStatuses (
    RunId             TEXT NOT NULL,
    Agent             TEXT NOT NULL,
    Status            TEXT NOT NULL,
    Message           TEXT,
    ElapsedMs         DOUBLE PRECISION DEFAULT 0,
    ArtifactCount     INTEGER DEFAULT 0,
    FindingCount      INTEGER DEFAULT 0,
    RetryAttempt      INTEGER DEFAULT 0,
    UpdatedAt         TEXT NOT NULL,
    PRIMARY KEY (RunId, Agent),
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS Requirements (
    Id                TEXT NOT NULL,
    RunId             TEXT NOT NULL,
    SourceFile        TEXT,
    Section           TEXT,
    HeadingLevel      INTEGER DEFAULT 0,
    Title             TEXT NOT NULL,
    Description       TEXT,
    Module            TEXT,
    Tags              TEXT,
    AcceptanceCriteria TEXT,
    DependsOn         TEXT,
    CreatedAt         TEXT NOT NULL,
    PRIMARY KEY (Id, RunId),
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS BacklogItems (
    Id                TEXT NOT NULL,
    RunId             TEXT NOT NULL,
    ParentId          TEXT,
    SourceRequirementId TEXT,
    ItemType          TEXT NOT NULL,
    Status            TEXT NOT NULL DEFAULT 'New',
    Title             TEXT NOT NULL,
    Description       TEXT,
    Module            TEXT,
    Priority          INTEGER DEFAULT 0,
    Iteration         INTEGER DEFAULT 0,
    AcceptanceCriteria TEXT,
    DependsOn         TEXT,
    Tags              TEXT,
    TechnicalNotes    TEXT,
    DefinitionOfDone  TEXT,
    DetailedSpec      TEXT,
    CreatedAt         TEXT NOT NULL,
    StartedAt         TEXT,
    CompletedAt       TEXT,
    AssignedAgent     TEXT DEFAULT '',
    PRIMARY KEY (Id, RunId),
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS Findings (
    Id                TEXT NOT NULL,
    RunId             TEXT NOT NULL,
    ArtifactId        TEXT,
    FilePath          TEXT,
    LineNumber        INTEGER,
    Severity          TEXT NOT NULL,
    Category          TEXT,
    Message           TEXT,
    Suggestion        TEXT,
    TracedRequirementId TEXT,
    PRIMARY KEY (Id, RunId),
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS Artifacts (
    Id                TEXT NOT NULL,
    RunId             TEXT NOT NULL,
    Layer             TEXT NOT NULL,
    RelativePath      TEXT,
    FileName          TEXT,
    Namespace         TEXT,
    ProducedBy        TEXT,
    ContentLength     INTEGER DEFAULT 0,
    TracedReqIds      TEXT,
    GeneratedAt       TEXT NOT NULL,
    PRIMARY KEY (Id, RunId),
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS TestDiagnostics (
    Id                TEXT NOT NULL,
    RunId             TEXT NOT NULL,
    TestName          TEXT,
    AgentUnderTest    TEXT,
    Outcome           TEXT NOT NULL,
    Diagnostic        TEXT,
    Remediation       TEXT,
    Category          TEXT,
    DurationMs        DOUBLE PRECISION DEFAULT 0,
    AttemptNumber     INTEGER DEFAULT 1,
    Timestamp         TEXT NOT NULL,
    PRIMARY KEY (Id, RunId),
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS AuditLog (
    Id                TEXT NOT NULL,
    RunId             TEXT NOT NULL,
    Sequence          INTEGER NOT NULL,
    Agent             TEXT NOT NULL,
    Action            TEXT NOT NULL,
    Severity          TEXT NOT NULL DEFAULT 'Info',
    Description       TEXT NOT NULL,
    Details           TEXT,
    InputHash         TEXT,
    OutputHash        TEXT,
    Timestamp         TEXT NOT NULL,
    PreviousHash      TEXT NOT NULL DEFAULT '',
    EntryHash         TEXT NOT NULL,
    PRIMARY KEY (Id),
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS HumanDecisions (
    Id                TEXT NOT NULL PRIMARY KEY,
    RunId             TEXT NOT NULL,
    RequestingAgent   TEXT NOT NULL,
    Category          TEXT NOT NULL,
    Title             TEXT NOT NULL,
    Description       TEXT NOT NULL,
    Details           TEXT,
    Decision          TEXT NOT NULL DEFAULT 'Pending',
    DecisionReason    TEXT,
    RequestedAt       TEXT NOT NULL,
    DecidedAt         TEXT,
    TimeoutMinutes    DOUBLE PRECISION NOT NULL DEFAULT 30,
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS OrchestratorInstructions (
    Id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    RunId             TEXT,
    Instruction       TEXT NOT NULL,
    Source            TEXT NOT NULL DEFAULT 'Manual',
    CreatedAt         TEXT NOT NULL,
    FOREIGN KEY (RunId) REFERENCES PipelineRuns(RunId)
);

CREATE TABLE IF NOT EXISTS WorkItemTemplates (
    TemplateKey       TEXT PRIMARY KEY,
    ItemType          TEXT NOT NULL,
    TemplateName      TEXT NOT NULL,
    Purpose           TEXT NOT NULL,
    TemplateFormat    TEXT NOT NULL,
    ExampleContent    TEXT,
    Version           INTEGER NOT NULL DEFAULT 1,
    IsActive          BOOLEAN NOT NULL DEFAULT TRUE,
    UpdatedAt         TEXT NOT NULL
);

INSERT INTO WorkItemTemplates (TemplateKey, ItemType, TemplateName, Purpose, TemplateFormat, ExampleContent, Version, IsActive, UpdatedAt)
VALUES
(
    'epic.v1',
    'Epic',
    'Epic Template',
    'High-level strategic outcome with business value and measurable success criteria.',
    'EPIC|<id>|<title>|<summary>|<business_value>|<success_criteria_semicolon_sep>|<scope>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>',
    '[E-AUTH-001] Password Recovery Modernization',
    1,
    TRUE,
    NOW()::TEXT
),
(
    'story.v1',
    'UserStory',
    'User Story Template',
    'Feature from end-user perspective using As a / I want / so that format.',
    'STORY|<id>|<parent_id>|As a [Type of User], I want to [Action] so that [Value/Benefit].|<given_when_then_acceptance_criteria_semicolon_sep>|<story_points 1,2,3,5,8>|<labels_csv>|<priority 1-3>|<services_csv>|<depends_on_ids_csv>|<detailed_spec>',
    'As a Registered User, I want to reset my forgotten password so that I can regain account access securely.',
    1,
    TRUE,
    NOW()::TEXT
),
(
    'usecase.v1',
    'UseCase',
    'Use Case Template',
    'Detailed actor-system flow with preconditions, numbered main flow, and postconditions.',
    'USECASE|<id>|<parent_id>|<use_case_name>|<actor>|<preconditions>|<main_flow_steps_semicolon_sep>|<alt_flows>|<postconditions>|<services_csv>',
    'Reset Forgotten Password | Actor: Registered User | Preconditions: User is on login page with internet connection.',
    1,
    TRUE,
    NOW()::TEXT
),
(
    'task.v1',
    'Task',
    'Task Template',
    'Technical to-do item to fulfill a story.',
    'TASK|<id>|<parent_id>|[T-101] <technical_title>|<description>|<technical_notes>|<definition_of_done_semicolon_sep>|<tags_csv>|<priority 1-3>|<services_csv>|<detailed_spec>',
    '[T-101] Create POST /auth/reset endpoint | DoD: [ ] Unit tests passed.; [ ] Documentation updated in Swagger.; [ ] Code reviewed by peer.',
    1,
    TRUE,
    NOW()::TEXT
),
(
    'bug.v1',
    'Bug',
    'Bug Report Template',
    'Clear, reproducible failure documentation.',
    'BUG|<id>|<parent_id>|[BUG] <title>|<severity Blocker/Critical/Major/Minor>|<environment>|<steps_to_reproduce_semicolon_sep>|<expected_result>|<actual_result>|<services_csv>',
    '[BUG] Login button non-responsive on Safari (Mobile)',
    1,
    TRUE,
    NOW()::TEXT
)
ON CONFLICT (TemplateKey) DO UPDATE SET
    ItemType = EXCLUDED.ItemType,
    TemplateName = EXCLUDED.TemplateName,
    Purpose = EXCLUDED.Purpose,
    TemplateFormat = EXCLUDED.TemplateFormat,
    ExampleContent = EXCLUDED.ExampleContent,
    Version = EXCLUDED.Version,
    IsActive = EXCLUDED.IsActive,
    UpdatedAt = EXCLUDED.UpdatedAt;

CREATE INDEX IF NOT EXISTS IX_AgentEvents_RunId ON AgentEvents(RunId);
CREATE INDEX IF NOT EXISTS IX_AgentStatuses_RunId ON AgentStatuses(RunId);
CREATE INDEX IF NOT EXISTS IX_Requirements_RunId ON Requirements(RunId);
CREATE INDEX IF NOT EXISTS IX_BacklogItems_RunId ON BacklogItems(RunId);
CREATE INDEX IF NOT EXISTS IX_Findings_RunId ON Findings(RunId);
CREATE INDEX IF NOT EXISTS IX_Artifacts_RunId ON Artifacts(RunId);
CREATE INDEX IF NOT EXISTS IX_TestDiagnostics_RunId ON TestDiagnostics(RunId);
CREATE INDEX IF NOT EXISTS IX_AuditLog_RunId ON AuditLog(RunId);
CREATE INDEX IF NOT EXISTS IX_AuditLog_RunId_Sequence ON AuditLog(RunId, Sequence);
CREATE INDEX IF NOT EXISTS IX_HumanDecisions_RunId ON HumanDecisions(RunId);
CREATE INDEX IF NOT EXISTS IX_HumanDecisions_Decision ON HumanDecisions(Decision);
CREATE INDEX IF NOT EXISTS IX_OrchestratorInstructions_RunId ON OrchestratorInstructions(RunId);
CREATE INDEX IF NOT EXISTS IX_WorkItemTemplates_ItemType ON WorkItemTemplates(ItemType);
