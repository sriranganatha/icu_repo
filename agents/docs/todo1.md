# Multi-Application Platform — Implementation TODO

> Gap analysis of `reqs1.md` against the current codebase.
> Items are grouped into phased chunks. Each item is scoped to a single deliverable.

---

## Phase 1 — Master Metadata Domain Models & DB Schema

The platform currently has **no** global metadata entities. All config is hardcoded
(agents in DI, LLM in `SmartLlmRouter`, templates in agent code). This phase
introduces the persistence layer for platform-wide registries.

### 1.1 Technology Registry Entities

- [ ] **1.1.1** Create `Language` entity (`id`, `name`, `version`, `status`, `icon`, `file_extensions[]`)
- [ ] **1.1.2** Create `Framework` entity (`id`, `name`, `language_id`, `version`, `category`, `docs_url`)
- [ ] **1.1.3** Create `DatabaseTechnology` entity (`id`, `name`, `db_type`, `default_port`, `connection_template`)
- [ ] **1.1.4** Create `CloudProvider` entity (`id`, `name`, `regions[]`, `services[]`)
- [ ] **1.1.5** Create `DevOpsTool` entity (`id`, `name`, `category`, `config_template`)
- [ ] **1.1.6** Create `PackageRegistry` entity (`id`, `name`, `language_id`, `url`, `auth_type`)
- [ ] **1.1.7** Create `ApiProtocol` entity (`id`, `name`, `spec_format`)
- [ ] **1.1.8** Add `DbSet<>` registrations for all 7 entities in a new `PlatformDbContext`
- [ ] **1.1.9** Seed migration with initial data (C#, Python, TypeScript, Java, Go; PostgreSQL, MongoDB, Redis; AWS, Azure, GCP; REST, GraphQL, gRPC)

### 1.2 Agent Registry Entities

> Currently agents are registered as `IAgent` singletons in `Program.cs` with no DB backing.

- [ ] **1.2.1** Create `AgentTypeDefinition` entity (`id`, `name`, `description`, `capabilities[]`, `input_schema`, `output_schema`, `default_model`)
- [ ] **1.2.2** Create `AgentModelMapping` entity (`id`, `agent_type_id`, `llm_provider`, `model_id`, `token_limit`, `cost_per_1k_tokens`)
- [ ] **1.2.3** Create `AgentToolDefinition` entity (`id`, `agent_type_id`, `tool_name`, `tool_config`, `is_required`)
- [ ] **1.2.4** Create `AgentPromptTemplate` entity (`id`, `agent_type_id`, `prompt_type`, `prompt_template`, `version`, `is_active`)
- [ ] **1.2.5** Create `AgentConstraint` entity (`id`, `agent_type_id`, `max_tokens`, `max_retries`, `timeout_seconds`, `sandbox_config`)
- [ ] **1.2.6** Seed migration mapping current 40+ `AgentType` enum values to `AgentTypeDefinition` rows

### 1.3 Template Library Entities

> No templates exist in the DB. Agents embed boilerplate inline.

- [ ] **1.3.1** Create `BrdTemplate` entity (`id`, `name`, `project_type`, `sections_json`, `is_default`)
- [ ] **1.3.2** Create `ArchitectureTemplate` entity (`id`, `name`, `pattern`, `diagram_template`)
- [ ] **1.3.3** Create `CodeTemplate` entity (`id`, `name`, `language_id`, `framework_id`, `template_type`, `content`, `variables[]`)
- [ ] **1.3.4** Create `FileStructureTemplate` entity (`id`, `name`, `framework_id`, `tree_json`)
- [ ] **1.3.5** Create `CiCdTemplate` entity (`id`, `name`, `provider`, `language_id`, `pipeline_yaml`)
- [ ] **1.3.6** Create `DockerTemplate` entity (`id`, `name`, `language_id`, `framework_id`, `dockerfile_content`, `compose_content`)
- [ ] **1.3.7** Create `TestTemplate` entity (`id`, `name`, `test_type`, `framework_id`, `test_framework`, `template_content`)
- [ ] **1.3.8** Create `IaCTemplate` entity (`id`, `name`, `cloud_provider_id`, `tool`, `template_content`)
- [ ] **1.3.9** Create `DocumentationTemplate` entity (`id`, `name`, `doc_type`, `template_content`)

### 1.4 Standards & Rules Entities

- [ ] **1.4.1** Create `CodingStandard` entity (`id`, `name`, `language_id`, `rules_json`, `linter_config`)
- [ ] **1.4.2** Create `NamingConvention` entity (`id`, `scope`, `pattern`, `examples`)
- [ ] **1.4.3** Create `SecurityPolicy` entity (`id`, `name`, `category`, `rules[]`, `severity`)
- [ ] **1.4.4** Create `ReviewChecklist` entity (`id`, `name`, `scope`, `checklist_items[]`)
- [ ] **1.4.5** Create `QualityGate` entity (`id`, `name`, `gate_type`, `threshold_config`)

### 1.5 LLM Provider Configuration Entities

> `ILlmProvider` interface and `SmartLlmRouter` exist but configuration is hardcoded.

- [ ] **1.5.1** Create `LlmProviderConfig` entity (`id`, `name`, `api_base_url`, `auth_type`, `rate_limits`)
- [ ] **1.5.2** Create `LlmModelConfig` entity (`id`, `provider_id`, `model_name`, `context_window`, `cost_input`, `cost_output`, `capabilities[]`)
- [ ] **1.5.3** Create `LlmRoutingRule` entity (`id`, `task_type`, `primary_model_id`, `fallback_model_id`, `conditions_json`)
- [ ] **1.5.4** Create `TokenBudget` entity (`id`, `scope`, `budget_tokens`, `alert_threshold`)
- [ ] **1.5.5** Refactor `SmartLlmRouter` to read routing rules from DB instead of hardcoded constructor logic

### 1.6 Workflow Definition Entities

- [ ] **1.6.1** Create `SdlcWorkflow` entity (`id`, `name`, `description`, `stages_json`, `is_default`)
- [ ] **1.6.2** Create `StageDefinition` entity (`id`, `workflow_id`, `name`, `order`, `entry_criteria`, `exit_criteria`, `agents_involved[]`)
- [ ] **1.6.3** Create `ApprovalGate` entity (`id`, `stage_id`, `gate_type`, `approvers_config`, `timeout_hours`)
- [ ] **1.6.4** Create `TransitionRule` entity (`id`, `from_stage_id`, `to_stage_id`, `conditions[]`, `auto_transition`)

---

## Phase 2 — Project-Specific Domain Models & Multi-Project Support

The platform is currently **single-project** (HMS/ICU). `PipelineConfig` hardcodes
DB name, ports, and namespace. This phase introduces multi-project capability.

### 2.1 Project Core Entities

> Currently there is no `Project` entity. `PipelineConfig` serves as the only project-level record.

- [ ] **2.1.1** Create `Project` entity (`id`, `name`, `slug`, `description`, `project_type`, `status`, `created_by`, `created_at`, `org_id`)
- [ ] **2.1.2** Create `ProjectType` enum/entity (`web_app`, `api`, `mobile_app`, `data_pipeline`, `ml_model`, `cli_tool`, `library`, `full_stack`)
- [ ] **2.1.3** Create `ProjectSettings` entity (`project_id`, `git_repo_url`, `default_branch`, `artifact_storage_path`, `notification_config`)
- [ ] **2.1.4** Create `ProjectTeamMember` entity (`project_id`, `user_id`, `role`)
- [ ] **2.1.5** Refactor `PipelineConfig` to hold a `ProjectId` reference and inherit project-level settings
- [ ] **2.1.6** Refactor `AgentContext` to carry a `Project` reference instead of bare `RequirementsBasePath` / `OutputBasePath`

### 2.2 Tech Stack Selection (Per-Project)

- [ ] **2.2.1** Create `ProjectTechStack` entity (`id`, `project_id`, `layer`, `technology_id`, `version`, `config_overrides`)
- [ ] **2.2.2** Create `ProjectDependency` entity (`id`, `project_id`, `package_name`, `version_constraint`, `scope`, `reason`)
- [ ] **2.2.3** Create `ProjectIntegration` entity (`id`, `project_id`, `integration_type`, `provider`, `config_json`)
- [ ] **2.2.4** Create `EnvironmentConfig` entity (`id`, `project_id`, `env_name`, `variables_json_encrypted`, `infra_config`)
- [ ] **2.2.5** Build compatibility matrix validation — prevent invalid tech stack combos (e.g. React frontend + Python-only code agent)

### 2.3 Architecture Decisions (Per-Project)

- [ ] **2.3.1** Create `ProjectArchitecture` entity (`project_id`, `pattern_id`, `customizations_json`)
- [ ] **2.3.2** Create `ModuleDefinition` entity (`id`, `project_id`, `name`, `description`, `responsibilities`, `dependencies[]`)
- [ ] **2.3.3** Create `ApiContract` entity (`id`, `project_id`, `module_id`, `endpoint`, `method`, `request_schema`, `response_schema`, `auth_required`)
- [ ] **2.3.4** Create `DataModelDefinition` entity (`id`, `project_id`, `entity_name`, `fields_json`, `relationships_json`, `indexes_json`)
- [ ] **2.3.5** Create `ArchitectureDecisionRecord` entity (`id`, `project_id`, `title`, `context`, `decision`, `consequences`, `status`, `decided_at`)

### 2.4 Requirements & BRD Persistence

> `Requirement`, `ExpandedRequirement`, `BrdDocument` exist as in-memory models.
> They are **not** persisted to the database between runs.

- [ ] **2.4.1** Create `RawRequirement` DB entity (`id`, `project_id`, `input_text`, `input_type`, `submitted_by`, `submitted_at`)
- [ ] **2.4.2** Create `EnrichedRequirement` DB entity (`id`, `raw_requirement_id`, `enriched_json`, `clarification_questions[]`, `user_responses[]`, `version`)
- [ ] **2.4.3** Create `BrdSection` DB entity (`id`, `brd_id`, `section_type`, `order`, `content`, `diagrams[]`)
- [ ] **2.4.4** Create `BrdFeedback` DB entity (`id`, `brd_id`, `section_id`, `feedback_text`, `resolved`, `resolved_in_version`)
- [ ] **2.4.5** Add repository layer to persist `BrdDocument` to PostgreSQL (currently only in-memory on `AgentContext`)
- [ ] **2.4.6** Add repository layer to persist `Requirement` / `ExpandedRequirement` to PostgreSQL

### 2.5 Backlog Persistence (Per-Project)

> `ExpandedRequirement` has `WorkItemType` (Epic, Story, Task, Bug) but items are
> held in-memory `SynchronizedList` on `AgentContext`. No DB persistence.

- [ ] **2.5.1** Create `Epic` DB entity with FK to `Project` and `BrdSection`
- [ ] **2.5.2** Create `Story` DB entity with FK to `Epic`, sprint assignment, story points
- [ ] **2.5.3** Create `TaskItem` DB entity with FK to `Story`, assigned agent type, estimated tokens
- [ ] **2.5.4** Create `Sprint` DB entity (`id`, `project_id`, `name`, `goal`, `order`, `status`)
- [ ] **2.5.5** Create `TaskDependency` join entity (`task_id`, `depends_on_task_id`, `dependency_type`)
- [ ] **2.5.6** Build mapper between in-memory `ExpandedRequirement` ↔ DB backlog entities

### 2.6 Agent Execution Audit Trail

> `AgentResult` is returned but not persisted. No execution history survives a restart.

- [ ] **2.6.1** Create `AgentAssignment` DB entity (`id`, `task_id`, `agent_type_id`, `status`, `assigned_at`, `started_at`, `completed_at`)
- [ ] **2.6.2** Create `AgentRun` DB entity (`id`, `assignment_id`, `run_number`, `input_context_json`, `output_artifacts[]`, `tokens_used`, `duration_ms`, `status`, `error_log`)
- [ ] **2.6.3** Create `AgentArtifact` DB entity (`id`, `run_id`, `artifact_type`, `file_path`, `content_hash`, `review_status`)
- [ ] **2.6.4** Create `AgentConversation` DB entity (`id`, `run_id`, `messages_json`) — full LLM conversation log
- [ ] **2.6.5** Create `ReviewResult` DB entity (`id`, `artifact_id`, `reviewer_agent_id`, `verdict`, `comments_json`, `severity_counts`)
- [ ] **2.6.6** Add middleware in orchestrator to persist `AgentResult` → `AgentRun` after each agent execution

### 2.7 Project Quality & Metrics Persistence

> `ReleaseEvidence` and `TraceabilityEntry` exist in-memory but are not stored.

- [ ] **2.7.1** Create `QualityReport` DB entity (`id`, `project_id`, `sprint_id`, `test_coverage`, `lint_score`, `complexity_score`, `security_score`, `generated_at`)
- [ ] **2.7.2** Create `TraceabilityRecord` DB entity (`requirement_id`, `story_id`, `task_id`, `artifact_id`, `test_id`)
- [ ] **2.7.3** Create `ProjectMetric` DB entity (`id`, `project_id`, `metric_type`, `value`, `recorded_at`)
- [ ] **2.7.4** Add metrics collection hooks in orchestrator to emit `ProjectMetric` rows (tokens, cost, cycle time, human interventions)

---

## Phase 3 — Platform Admin UI (Configuration Screens)

These are **new Razor Pages** under `Pages/Admin/` for managing global platform metadata.
All CRUD, no pipeline logic. Bootstrap 5.3 + Bootstrap Icons + DataTables.

### 3.1 Technology Manager

- [ ] **3.1.1** `Pages/Admin/Technologies/Index.cshtml` — Tabbed grid (Languages, Frameworks, Databases, Cloud, DevOps, Registries, Protocols) with search, status filter, bulk actions
- [ ] **3.1.2** `Pages/Admin/Technologies/Edit.cshtml` — Create/Edit form with version management, status toggle (active/deprecated), icon upload
- [ ] **3.1.3** API controller `Api/TechnologiesController.cs` — CRUD endpoints for all 7 technology entities

### 3.2 Agent Manager

- [ ] **3.2.1** `Pages/Admin/Agents/Index.cshtml` — Agent type cards with capabilities badges, model assignment, status indicators
- [ ] **3.2.2** `Pages/Admin/Agents/Configure.cshtml` — Per-agent configuration: model selection, system prompt editor (Monaco/CodeMirror), tool toggles, constraint sliders
- [ ] **3.2.3** `Pages/Admin/Agents/Prompts.cshtml` — Versioned prompt template editor with diff view, A/B test toggle, preview panel
- [ ] **3.2.4** `Pages/Admin/Agents/TestBench.cshtml` — Send test prompts to an agent and see raw LLM output (sandbox mode)

### 3.3 Template Manager

- [ ] **3.3.1** `Pages/Admin/Templates/Index.cshtml` — Template library grid filtered by type (BRD, Architecture, Code, CI/CD, Docker, Test, IaC, Docs)
- [ ] **3.3.2** `Pages/Admin/Templates/Edit.cshtml` — Template editor with `{{variable}}` syntax highlighting, live preview panel, variable registry sidebar
- [ ] **3.3.3** Import/Export buttons — Upload JSON/YAML bundles, download as portable template pack

### 3.4 Standards & Rules Manager

- [ ] **3.4.1** `Pages/Admin/Standards/Index.cshtml` — Tabbed view: Coding Standards, Naming Conventions, Security Policies, Review Checklists, Quality Gates
- [ ] **3.4.2** `Pages/Admin/Standards/CodingRules.cshtml` — Per-language rule editor with linter config preview (ESLint, Ruff, StyleCop)
- [ ] **3.4.3** `Pages/Admin/Standards/QualityGates.cshtml` — Threshold config panel: coverage %, complexity max, duplication %, review score minimum

### 3.5 Workflow Designer

- [ ] **3.5.1** `Pages/Admin/Workflows/Index.cshtml` — List of SDLC workflow definitions with clone/archive actions
- [ ] **3.5.2** `Pages/Admin/Workflows/Designer.cshtml` — Visual drag-and-drop stage builder (uses JS library like Drawflow or custom SVG). Define stages, assign agents, set entry/exit criteria, configure approval gates and transition rules
- [ ] **3.5.3** `Pages/Admin/Workflows/Preview.cshtml` — Read-only Mermaid diagram rendering of a workflow with stage details expandable

### 3.6 LLM Configuration

- [ ] **3.6.1** `Pages/Admin/Llm/Providers.cshtml` — Provider cards (OpenAI, Gemini, Anthropic, local Ollama) with connection status indicator, API key management (masked), rate limit display
- [ ] **3.6.2** `Pages/Admin/Llm/Models.cshtml` — Model inventory table: context window, costs, capabilities tags; enable/disable per model
- [ ] **3.6.3** `Pages/Admin/Llm/Routing.cshtml` — Routing rule builder: task_type → primary model → fallback model, with drag-reorder priority
- [ ] **3.6.4** `Pages/Admin/Llm/Budgets.cshtml` — Token budget configuration per scope (task/story/project) with alert threshold sliders and burn-down chart

### 3.7 Security & Compliance Policies

- [ ] **3.7.1** `Pages/Admin/Security/Policies.cshtml` — Policy list by category (Auth, Data, Network, Compliance) with severity badges, enable/disable toggle
- [ ] **3.7.2** `Pages/Admin/Security/ComplianceMatrix.cshtml` — HIPAA / SOC2 / OWASP checklist grid showing which rules are enforced and which agents check them

### 3.8 User & Organization Management

- [ ] **3.8.1** Create `Organization` and `User` entities with roles (`admin`, `developer`, `reviewer`, `viewer`)
- [ ] **3.8.2** `Pages/Admin/Users/Index.cshtml` — User list with role badges, last active, project count
- [ ] **3.8.3** `Pages/Admin/Users/Permissions.cshtml` — Role-based permission matrix (CRUD per entity type)
- [ ] **3.8.4** `Pages/Admin/AuditLog.cshtml` — Searchable audit log of all admin actions with timestamp, user, action, entity, diff

---

## Phase 4 — Project-Level UI Screens

New and enhanced Razor Pages under `Pages/Projects/` for the per-project experience.
Existing pages (BRD, Sprints, Traceability, Release, Agents, Review) to be
extended with project context.

### 4.1 Project Wizard

- [ ] **4.1.1** `Pages/Projects/Create.cshtml` — Multi-step wizard: (1) Name & Type → (2) Tech Stack picker (with compatibility validation) → (3) Architecture pattern selector → (4) Workflow assignment → (5) Review & Create
- [ ] **4.1.2** Preset/Starter Kit selector — "SaaS API Starter", "Full Stack Web", "Data Pipeline", "ML Model", "CLI Tool" bundles that pre-fill wizard steps
- [ ] **4.1.3** `Pages/Projects/Index.cshtml` — Project listing with cards (name, type icon, status badge, team avatars, last activity)
- [ ] **4.1.4** `Pages/Projects/Settings.cshtml` — Project settings editor: tech stack overrides, agent config overrides, environment variables, Git settings

### 4.2 Requirement Input & Enrichment

- [ ] **4.2.1** `Pages/Projects/Requirements/Input.cshtml` — Rich text editor (Quill/TipTap) for requirements input, file upload (MD, DOCX, PDF), voice-to-text placeholder
- [ ] **4.2.2** Real-time AI enrichment side panel — as user types, show extracted entities, auto-generated acceptance criteria, suggested clarification questions
- [ ] **4.2.3** `Pages/Projects/Requirements/Manage.cshtml` — Requirements list with version history, diff view, status indicators, bulk approve/reject

### 4.3 BRD Viewer Enhancement

> `Pages/Brd/Index.cshtml` exists with status tabs, card grid, detail modal.

- [ ] **4.3.1** Add section-by-section BRD view with inline comment threads (not just modal)
- [ ] **4.3.2** Add version diff viewer — side-by-side comparison of BRD versions
- [ ] **4.3.3** Add approval workflow panel — reviewers listed, approval status per reviewer, one-click approve/reject with comments

### 4.4 Backlog Board

- [ ] **4.4.1** `Pages/Projects/Backlog/Board.cshtml` — Kanban board with columns: To Do, In Queue, In Progress, Review, Done. Drag-and-drop to re-prioritize. Filter by epic, agent, status, sprint
- [ ] **4.4.2** `Pages/Projects/Backlog/List.cshtml` — Table/list view with sorting, grouping by epic, bulk status change
- [ ] **4.4.3** Dependency graph visualization — DAG of task dependencies. Highlight critical path. Click node to see task detail. Uses D3.js or Mermaid

### 4.5 Agent Dashboard Enhancement

> `Pages/Agents/` exists with basic agent status view.

- [ ] **4.5.1** Add live execution log streaming per agent (SignalR-powered terminal view)
- [ ] **4.5.2** Add token usage sparklines per agent (last 10 runs)
- [ ] **4.5.3** Add artifact production timeline — what each agent produced and when
- [ ] **4.5.4** Add error drill-down — click a failed agent to see error log, retry button, manual override

### 4.6 Code Browser

- [ ] **4.6.1** `Pages/Projects/Code/Browser.cshtml` — File tree of generated artifacts with syntax-highlighted code viewer
- [ ] **4.6.2** Inline requirement annotations — hover over code sections to see which requirement they trace to
- [ ] **4.6.3** Review finding overlays — show review findings inline on the code (error/warning markers)

### 4.7 Test Results Dashboard

- [ ] **4.7.1** `Pages/Projects/Testing/Results.cshtml` — Test run summary cards (pass/fail/skip counts), test tree with expandable suites
- [ ] **4.7.2** Coverage heatmap — file-level coverage visualization using color-coded bars
- [ ] **4.7.3** Failing test detail — show error message, stack trace, suggested fix from AI, one-click re-run

### 4.8 Architecture Viewer

- [ ] **4.8.1** `Pages/Projects/Architecture/View.cshtml` — Auto-generated system diagrams from generated artifacts: module boundaries, data flow (Mermaid/D3)
- [ ] **4.8.2** API surface view — endpoints table with method, path, auth, request/response schemas
- [ ] **4.8.3** ADR timeline — Architecture Decision Records displayed as a chronological timeline

### 4.9 Delivery Dashboard

- [ ] **4.9.1** `Pages/Projects/Delivery/Index.cshtml` — Build status, deployment readiness checklist, quality gate pass/fail summary, final package download button
- [ ] **4.9.2** Deployment pipeline visualization — stages (build → test → security scan → deploy) with status indicators
- [ ] **4.9.3** Release notes auto-generation — compile from completed stories, BRD sections, and ADRs

---

## Phase 5 — Monitoring & Analytics Screens

### 5.1 Cost Dashboard

- [ ] **5.1.1** `Pages/Analytics/Cost.cshtml` — Token usage breakdown by project, agent, task type. Bar chart + table
- [ ] **5.1.2** Budget burn-down chart — current spend vs budget per project/org with projected exhaustion date
- [ ] **5.1.3** Cost alert configuration — threshold-based alerts (email/SignalR notification) when budget hits 80%, 90%, 100%

### 5.2 Performance Analytics

- [ ] **5.2.1** `Pages/Analytics/Performance.cshtml` — Cycle time per task type (box plot), agent success/failure rates (bar chart), human intervention frequency (trend line)
- [ ] **5.2.2** Agent leaderboard — rank agents by success rate, average duration, token efficiency
- [ ] **5.2.3** Pipeline throughput — items completed per day/week, rolling average

### 5.3 Quality Trends

- [ ] **5.3.1** `Pages/Analytics/Quality.cshtml` — Test coverage over time (line chart), defect density per sprint (bar chart), review pass rates (gauge)
- [ ] **5.3.2** Security vulnerability trend — CVE count over time, severity distribution
- [ ] **5.3.3** Technical debt tracker — complexity score trend, duplication %, deprecated dependency count

### 5.4 Audit Log Viewer

- [ ] **5.4.1** `Pages/Analytics/AuditLog.cshtml` — Unified log of all actions (user + agent). Columns: timestamp, actor, action, entity, details. Filterable by date range, actor type, action type
- [ ] **5.4.2** Export to CSV/JSON for compliance reporting
- [ ] **5.4.3** Anomaly highlighting — flag unusual patterns (e.g., agent running 10x longer than average, repeated failures)

---

## Phase 6 — Configuration Patterns & Platform Infrastructure

Cross-cutting concerns that enable platform flexibility and scalability.

### 6.1 Layered Configuration Inheritance

> Currently `PipelineConfig` is flat. No concept of master → org → project override chain.

- [ ] **6.1.1** Implement `ConfigResolver` service with 3-tier resolution: Master Default → Organization Override → Project Override
- [ ] **6.1.2** Add `org_id` column to all master metadata entities for org-scoped overrides
- [ ] **6.1.3** Add `project_id` column (nullable) to standards, quality gates, and coding rules for project-scoped overrides
- [ ] **6.1.4** Build config merge logic — deep-merge JSON configs with explicit `null` to clear an inherited value

### 6.2 Configuration Versioning & Snapshots

- [ ] **6.2.1** Create `ConfigSnapshot` entity — captures full resolved config at pipeline run start
- [ ] **6.2.2** Store snapshot ID on each `AgentRun` for full reproducibility
- [ ] **6.2.3** `Pages/Admin/Config/History.cshtml` — Config version timeline with diff viewer between snapshots

### 6.3 Tech Stack Compatibility Matrix

- [ ] **6.3.1** Create `CompatibilityRule` entity (`source_tech_id`, `target_tech_id`, `compatibility`: required/recommended/incompatible/neutral)
- [ ] **6.3.2** Build validation service that checks `ProjectTechStack` against compatibility rules before project creation
- [ ] **6.3.3** Show warnings/errors in Project Wizard step 2 when incompatible selections are made

### 6.4 Preset / Starter Kits

- [ ] **6.4.1** Create `StarterKit` entity (`id`, `name`, `description`, `icon`, `tech_stack_json`, `architecture_pattern_id`, `workflow_id`, `templates_json`)
- [ ] **6.4.2** Seed 5+ starter kits: SaaS API (FastAPI + PostgreSQL + Redis + Docker + GitHub Actions), Full Stack Web (Next.js + Node + PostgreSQL + Vercel), Data Pipeline (Python + Airflow + Snowflake + dbt), .NET Enterprise (ASP.NET + SQL Server + Azure + Terraform), Mobile (Flutter + Firebase + GCP)
- [ ] **6.4.3** `Pages/Projects/StarterKits.cshtml` — Visual picker with preview cards showing included tech, architecture diagram thumbnail, and "Use This" button

### 6.5 Template Variable System

- [ ] **6.5.1** Create `TemplateVariable` registry entity (`name`, `scope`, `description`, `example_value`, `resolver_type`)
- [ ] **6.5.2** Build `TemplateEngine` service that resolves `{{project.name}}`, `{{project.tech_stack.backend.framework}}`, `{{project.database.connection_string}}` etc. from project context
- [ ] **6.5.3** Add variable autocomplete to template editor UI (Phase 3.3.2)
- [ ] **6.5.4** Add validation — detect undefined variables in templates before save

### 6.6 Soft Delete Infrastructure

- [ ] **6.6.1** Add `is_active` and `archived_at` columns to all master metadata entities
- [ ] **6.6.2** Add global query filter (`is_active = true`) in `PlatformDbContext`
- [ ] **6.6.3** Add "Show Archived" toggle in all admin list pages
- [ ] **6.6.4** Implement cascade: archiving a Language also archives its Frameworks and CodeTemplates

### 6.7 Multi-Tenancy for Platform Metadata

> DB already has `ITenantProvider` and `TenantId` on clinical entities. Extend to platform metadata.

- [ ] **6.7.1** Add `org_id` tenant column to Technology, Template, Standard entities
- [ ] **6.7.2** Add global query filters for `org_id` in `PlatformDbContext`
- [ ] **6.7.3** Build `OrgScopedRepository<T>` base class that auto-filters by current org
- [ ] **6.7.4** Org switcher in top navbar — switch between organizations

### 6.8 Import / Export (Project Recipes)

- [ ] **6.8.1** Build `ProjectRecipeExporter` service — export entire project config as a JSON/YAML bundle (tech stack, templates, standards, workflow, presets)
- [ ] **6.8.2** Build `ProjectRecipeImporter` service — import a recipe to create a new project with all config pre-filled
- [ ] **6.8.3** `Pages/Projects/Recipe.cshtml` — Export/Import UI with preview of what's included
- [ ] **6.8.4** Recipe marketplace placeholder — list of shared recipes from the team/org

### 6.9 Plugin Architecture for Agents

> Agents are currently code-defined classes implementing `IAgent`. Adding a new agent requires a code change + rebuild.

- [ ] **6.9.1** Define `AgentPluginManifest` schema — system prompt + tools + constraints + I/O schema in JSON
- [ ] **6.9.2** Build `DynamicAgent` class that reads its behavior from `AgentTypeDefinition` in DB instead of hardcoded logic
- [ ] **6.9.3** `Pages/Admin/Agents/PluginBuilder.cshtml` — UI to define a new agent type purely via configuration (no code)
- [ ] **6.9.4** Hot-reload — register new `DynamicAgent` instances in DI without app restart (use `IServiceCollection` factory pattern)

### 6.10 Feedback-Driven Auto-Tuning

> `LearningLoopAgent` exists but only produces reports. No closed-loop improvement.

- [ ] **6.10.1** Build `AutoTuner` service that reads `AgentLearningRecord` data and adjusts agent routing (primary ↔ fallback model swap when failure rate exceeds threshold)
- [ ] **6.10.2** Auto-tune prompt templates — track prompt version → success rate correlation, auto-promote winning prompts
- [ ] **6.10.3** Auto-tune estimation — compare `SprintPlan` predicted points vs actual cycle time, adjust `EstimatePoints()` formula
- [ ] **6.10.4** `Pages/Analytics/Tuning.cshtml` — Dashboard showing auto-tuning decisions, before/after metrics, manual override controls

---

## Phase 7 — Database Infrastructure & Migrations

### 7.1 New PlatformDbContext

> Current `GNexDbContext` covers clinical/HMS entities only. Platform metadata needs its own context.

- [ ] **7.1.1** Create `PlatformDbContext` with DbSets for all Phase 1 & Phase 2 entities (separate from clinical `GNexDbContext`)
- [ ] **7.1.2** Configure schema separation: `platform_meta` for master metadata, `platform_project` for project entities, `platform_audit` for execution/metrics
- [ ] **7.1.3** Add EF Core migrations for initial schema creation
- [ ] **7.1.4** Add seed data migration: default languages, frameworks, databases, starter kits, SDLC workflows
- [ ] **7.1.5** Add `PlatformDbContext` registration in `Program.cs` alongside existing `GNexDbContext`
- [ ] **7.1.6** Configure connection string resolution (same PostgreSQL instance, separate schemas — or separate DB via config)

### 7.2 Repository Layer

- [ ] **7.2.1** Create generic `IPlatformRepository<T>` interface with CRUD + query + soft-delete + pagination
- [ ] **7.2.2** Implement `PlatformRepository<T>` with EF Core, org-scoped, soft-delete-aware
- [ ] **7.2.3** Create specialized repositories: `IProjectRepository`, `ITemplateRepository`, `IAgentRegistryRepository`
- [ ] **7.2.4** Add unit of work pattern (`IPlatformUnitOfWork`) for transactional multi-entity operations

---

## Phase 8 — Service Layer & API Controllers

### 8.1 Platform Services

- [ ] **8.1.1** `ITechnologyService` + `TechnologyService` — CRUD for all 7 technology entities, with validation and caching
- [ ] **8.1.2** `IAgentRegistryService` + `AgentRegistryService` — CRUD for agent definitions, model mappings, prompts, tools, constraints
- [ ] **8.1.3** `ITemplateService` + `TemplateService` — CRUD for all 9 template types, variable resolution, import/export
- [ ] **8.1.4** `IStandardsService` + `StandardsService` — CRUD for coding standards, naming conventions, security policies, review checklists, quality gates
- [ ] **8.1.5** `IWorkflowService` + `WorkflowService` — CRUD for workflows, stages, gates, transitions; validation of stage ordering and circular refs
- [ ] **8.1.6** `ILlmConfigService` + `LlmConfigService` — CRUD for providers, models, routing rules, budgets; health-check for provider connectivity

### 8.2 Project Services

- [ ] **8.2.1** `IProjectService` (refactored) — Create/update/archive projects, apply starter kits, validate tech stack compatibility
- [ ] **8.2.2** `IBacklogService` + `BacklogService` — CRUD for epics/stories/tasks, sprint management, dependency resolution, priority rebalancing
- [ ] **8.2.3** `IRequirementPersistenceService` — Persist raw/enriched requirements, manage versions, link to BRD sections
- [ ] **8.2.4** `IAgentExecutionService` — Record agent assignments, runs, artifacts, conversations; query execution history
- [ ] **8.2.5** `IMetricsService` + `MetricsService` — Record and query project metrics, aggregate for analytics dashboards

### 8.3 API Controllers

- [ ] **8.3.1** `Api/TechnologiesController.cs` — RESTful CRUD for languages, frameworks, databases, etc.
- [ ] **8.3.2** `Api/AgentRegistryController.cs` — RESTful CRUD for agent definitions and configuration
- [ ] **8.3.3** `Api/TemplatesController.cs` — RESTful CRUD + import/export endpoints
- [ ] **8.3.4** `Api/ProjectsController.cs` — Project CRUD, tech stack selection, recipe import/export
- [ ] **8.3.5** `Api/BacklogController.cs` — Backlog item CRUD, sprint management, kanban state changes
- [ ] **8.3.6** `Api/AnalyticsController.cs` — Metrics aggregation endpoints for dashboard charts
- [ ] **8.3.7** Add Swagger/OpenAPI documentation for all API controllers

---

## Phase 9 — Orchestrator Refactoring for Multi-Project

### 9.1 Project-Scoped Pipeline

> Currently the orchestrator runs a single pipeline with a flat `AgentContext`.

- [ ] **9.1.1** Refactor `AgentOrchestrator.ExecuteAsync` to accept a `Project` and resolve its tech stack, workflow, and agent assignments from DB
- [ ] **9.1.2** Make `AgentContext` project-scoped — carry `ProjectId`, resolved tech stack, and project-specific agent config
- [ ] **9.1.3** Support concurrent pipeline runs for different projects (isolated `AgentContext` per run)

### 9.2 Workflow-Driven Execution

> Currently the orchestrator uses a hardcoded dependency DAG (`s_dependencies`).

- [ ] **9.2.1** Load `SdlcWorkflow` and `StageDefinition` from DB instead of using `s_dependencies` dict
- [ ] **9.2.2** Map `StageDefinition.agents_involved` to actual `IAgent` instances dynamically
- [ ] **9.2.3** Implement entry/exit criteria checks from `StageDefinition` before stage transitions
- [ ] **9.2.4** Implement approval gates — pause pipeline at `ApprovalGate` stages, notify reviewers, resume on approval

### 9.3 Dynamic Agent Resolution

- [ ] **9.3.1** Replace hardcoded `IAgent` singletons with factory pattern that resolves agents from `AgentTypeDefinition` + `DynamicAgent`
- [ ] **9.3.2** Per-project agent config overrides — a project can use a different LLM model or system prompt for a specific agent
- [ ] **9.3.3** Agent capability matching — given a task, find the best available agent based on capabilities[] and current load

---

## Phase 10 — Unit Tests & Integration Tests

### 10.1 Domain Model Tests

- [ ] **10.1.1** Tests for all Phase 1 entities — validation, defaults, relationships
- [ ] **10.1.2** Tests for all Phase 2 entities — project creation, tech stack assignment, backlog hierarchy
- [ ] **10.1.3** Tests for `ConfigResolver` — 3-tier inheritance, merge, null override

### 10.2 Service Layer Tests

- [ ] **10.2.1** Tests for `TechnologyService` — CRUD, validation, caching
- [ ] **10.2.2** Tests for `AgentRegistryService` — agent definition lifecycle, prompt versioning
- [ ] **10.2.3** Tests for `TemplateService` — variable resolution, import/export round-trip
- [ ] **10.2.4** Tests for `WorkflowService` — stage ordering, circular ref detection, gate configuration
- [ ] **10.2.5** Tests for `ProjectService` — wizard flow, starter kit application, tech stack compatibility validation
- [ ] **10.2.6** Tests for `BacklogService` — CRUD, dependency resolution, sprint allocation
- [ ] **10.2.7** Tests for `MetricsService` — recording, aggregation, time-range queries

### 10.3 Repository Tests

- [ ] **10.3.1** `PlatformRepository<T>` tests with in-memory SQLite — CRUD, soft delete, pagination, org scoping
- [ ] **10.3.2** `ProjectRepository` tests — project with full tech stack, backlog hierarchy
- [ ] **10.3.3** `TemplateRepository` tests — template CRUD, variable registry

### 10.4 Integration Tests

- [ ] **10.4.1** Project Wizard end-to-end — create project via API, verify DB state, verify default config resolution
- [ ] **10.4.2** Pipeline execution with project context — verify `AgentContext` carries project tech stack and workflow
- [ ] **10.4.3** Config snapshot — run pipeline, verify `ConfigSnapshot` created, verify reproducibility
- [ ] **10.4.4** Recipe import/export round-trip — export project A config, import as project B, verify identical config

### 10.5 UI Page Tests

- [ ] **10.5.1** Razor Page model tests for all admin pages — verify `OnGetAsync` / `OnPostAsync` return correct data
- [ ] **10.5.2** Project wizard page model tests — verify step navigation, validation, tech stack compatibility feedback

---

## Summary — Item Counts Per Phase

| Phase | Description                          | Items |
|-------|--------------------------------------|-------|
| 1     | Master Metadata Domain Models        | 39    |
| 2     | Project-Specific Models              | 30    |
| 3     | Platform Admin UI                    | 22    |
| 4     | Project-Level UI                     | 24    |
| 5     | Monitoring & Analytics               | 11    |
| 6     | Configuration Patterns               | 29    |
| 7     | Database Infrastructure              | 10    |
| 8     | Services & API Controllers           | 17    |
| 9     | Orchestrator Refactoring             | 10    |
| 10    | Unit & Integration Tests             | 16    |
| **Total** |                                  | **208** |

### Recommended Execution Order

```
Phase 1 (models) → Phase 7 (DB) → Phase 8 (services) → Phase 2 (project models)
   → Phase 6 (config patterns) → Phase 9 (orchestrator refactor)
   → Phase 3 (admin UI) → Phase 4 (project UI) → Phase 5 (analytics)
   → Phase 10 (tests — continuously alongside each phase)
```

