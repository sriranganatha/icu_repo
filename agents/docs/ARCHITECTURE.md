# HMS Multi-Agent Pipeline — Architecture, Design & Flow

## Table of Contents

- [1. System Overview](#1-system-overview)
- [2. Agent Catalog (35 Agents)](#2-agent-catalog-35-agents)
- [3. Agent Categories](#3-agent-categories)
- [4. Dependency Graph](#4-dependency-graph)
- [5. Pipeline Execution Flow](#5-pipeline-execution-flow)
- [6. Backlog Lifecycle](#6-backlog-lifecycle)
- [7. Iterative Feedback Loops](#7-iterative-feedback-loops)
- [8. Finding → Remediation Dispatch](#8-finding--remediation-dispatch)
- [9. Human-in-the-Loop (HITL) Gates](#9-human-in-the-loop-hitl-gates)
- [10. Self-Healing & Retry](#10-self-healing--retry)
- [11. WIP Limits & Adaptive Throttling](#11-wip-limits--adaptive-throttling)
- [12. Inter-Agent Communication](#12-inter-agent-communication)
- [13. Technology Stack](#13-technology-stack)

---

## 1. System Overview

The HMS (Hospital Management System) pipeline is an **autonomous multi-agent orchestration system** that takes natural language requirements and produces a fully built, tested, reviewed, and deployed healthcare microservice platform.

```
┌──────────────────────────────────────────────────────────────┐
│                     ORCHESTRATOR (Daemon Loop)                │
│                                                              │
│  Requirements ──► Expand ──► Backlog ──► Code-Gen ──► Review │
│       │               ▲          │          │           │    │
│       │               │          │          ▼           │    │
│       ▼               │          │      Build/Deploy    │    │
│   Architect ───►      │          │          │           │    │
│   Platform    ────────┘          │          ▼           │    │
│                                  │       Monitor        │    │
│                                  │          │           │    │
│                                  ◄──────────┘◄──────────┘    │
│                              (Feedback Loop)                 │
│                                                              │
│  Enrichment Agents (Security, HIPAA, SOC2, etc.) ──► parallel│
│                                                              │
│                     ──► Supervisor (Final Report) ──►        │
└──────────────────────────────────────────────────────────────┘
```

**Key Traits:**
- Dependency-driven parallel dispatch (agents run as soon as deps are met)
- Iterative backlog-driven development (code-gen agents re-dispatch while work items exist)
- Feedback loops (Review, Build, Deploy, Monitor feed findings back through Expander → Backlog)
- Self-healing retries with BugFix → Performance → Review cycles
- Human-in-the-Loop gates for DDL execution and critical security/compliance findings
- Adaptive WIP limits to prevent pipeline overload

---

## 2. Agent Catalog (35 Agents)

### Foundation Agents

| Agent | Type | Description |
|-------|------|-------------|
| **Requirements Reader** | `RequirementsReader` | Scans `icu/docs` and extracts structured requirements from markdown files |
| **Requirements Expander** | `RequirementsExpander` | Expands high-level requirements into Epics → User Stories → Use Cases → Tasks with dependency chains |
| **Requirement Analyzer** | `RequirementAnalyzer` | Gap-analysis engine — compares requirements to artifacts and generates stories to close gaps |
| **Architect** | `Architect` | Derives bounded-context architecture guidance and target services from requirements |
| **Platform Builder** | `PlatformBuilder` | Defines delivery, observability, runtime, and quality gates for downstream agents |
| **Planning** | `Planning` | Analyzes requirements holistically and creates structured implementation plans for code-gen agents |

### Backlog Management

| Agent | Type | Description |
|-------|------|-------------|
| **Backlog Manager** | `Backlog` | Tracks work items, manages backlog status, coordinates iterative development |

### Code Generation Agents

| Agent | Type | Description |
|-------|------|-------------|
| **Database** | `Database` | Generates per-microservice EF Core entities, DbContext, repositories, migrations, and Docker PostgreSQL |
| **Service Layer** | `ServiceLayer` | Generates DTOs, service interfaces, implementations, and Kafka integration events |
| **Application** | `Application` | Generates API Gateway + per-microservice minimal API endpoints, middleware, health checks |
| **Integration** | `Integration` | Generates Kafka event bus, FHIR/HL7 adapters, outbox pattern, consumers, and DLQ handlers |
| **UI/UX** | `UiUx` | WCAG 2.1 AA accessibility validation, responsive design, reusable UI component scaffolding |

### Quality & Analysis Agents

| Agent | Type | Description |
|-------|------|-------------|
| **Testing** | `Testing` | Generates real unit tests for services, repositories, tenant isolation driven by domain model |
| **Review** | `Review` | Reviews code against requirements for correctness, security, compliance, feature/NFR coverage |
| **Code Reasoning** | `CodeReasoning` | Detects cross-service inconsistencies, contract mismatches, wiring gaps before Review |
| **Code Quality** | `CodeQuality` | Static analysis of cyclomatic complexity, duplication, naming, standards |
| **Performance** | `Performance` | Scans and optimizes for async, pagination, N+1 queries, caching, CancellationToken propagation |
| **Gap Analysis** | `GapAnalysis` | Identifies missing implementations and feeds gap items back to RequirementsExpander |

### Remediation Agents

| Agent | Type | Description |
|-------|------|-------------|
| **Bug Fix** | `BugFix` | Repairs code based on ReviewAgent findings — removes TODOs, fills missing fields |
| **Refactoring** | `Refactoring` | AI-driven refactoring — dead code removal, pattern application, SOLID enforcement |

### Security & Compliance Agents

| Agent | Type | Description |
|-------|------|-------------|
| **Security** | `Security` | OWASP Top 10 analysis, input validation, auth patterns, encryption, middleware |
| **HIPAA Compliance** | `HipaaCompliance` | Enforces HIPAA §164.312: PHI encryption, audit trails, minimum necessary access, breach notification |
| **SOC 2 Compliance** | `Soc2Compliance` | Generates SOC 2 Type II controls: change gates, access reviews, incident response, backup/DR |
| **Access Control** | `AccessControl` | Generates healthcare RBAC roles, permission matrices, authz policies, emergency access, consent |

### Infrastructure & Operations Agents

| Agent | Type | Description |
|-------|------|-------------|
| **Infrastructure** | `Infrastructure` | Generates Dockerfiles, K8s manifests, Helm charts, docker-compose, CI/CD, DB runners |
| **Configuration** | `Configuration` | Generates per-env configs, secrets, feature flags, CORS, rate limiting, health checks |
| **Observability** | `Observability` | Generates OpenTelemetry, Prometheus metrics, structured logging, Grafana dashboards, alerts |
| **API Documentation** | `ApiDocumentation` | Generates OpenAPI 3.1, FHIR annotations, PHI docs, Swagger UI config |
| **Dependency Audit** | `DependencyAudit` | NuGet vulnerability scanning, license compliance, version pinning verification |
| **Migration** | `Migration` | Generates EF Core migrations, seed data scripts, and rollback scripts |

### Build, Deploy & Monitor Agents

| Agent | Type | Description |
|-------|------|-------------|
| **Build** | `Build` | Restores, compiles, validates solution — reports errors with AI-suggested fixes |
| **Deploy** | `Deploy` | Builds, publishes, deploys HMS solution from pipeline output |
| **Monitor** | `Monitor` | Monitors containers/services — health checks, log inspection, issue detection |
| **Load Test** | `LoadTest` | Generates k6/JMeter load test scripts with realistic healthcare workloads |

### Supervisor

| Agent | Type | Description |
|-------|------|-------------|
| **Supervisor** | `Supervisor` | Final gate — monitors agents, runs diagnostics, triggers remediation, ensures pipeline health |

---

## 3. Agent Categories

The orchestrator classifies agents into behavioral groups:

| Category | Agents | Behavior |
|----------|--------|----------|
| **Meta Agents** | Backlog, Supervisor, Review, RequirementsExpander, RequirementAnalyzer, CodeReasoning, Planning | Track/review items but don't produce deliverables — skip lifecycle Claim/Start/Complete |
| **Backlog-Driven** | Database, ServiceLayer, Application, Integration, UiUx, Testing | Iteratively re-dispatched while InQueue work items remain for them to claim |
| **Feedback Agents** | Review, Build, Deploy, Monitor, BugFix | When they produce new findings → trigger RequirementsExpander → Backlog expansion |
| **Heal-Cycle Skip** | BugFix, Performance, Review, Supervisor, Backlog, RequirementsExpander, Deploy, Build, Monitor, Planning, CodeReasoning | Skip the BugFix → Performance → Review self-healing loop (to avoid recursion) |
| **Post-Backlog Gates** | Build, Deploy, Monitor, LoadTest | Only dispatched after all actionable backlog items are Completed |

---

## 4. Dependency Graph

```
                         RequirementsReader
                        /        |         \
                       /         |          \
              Architect    RequirementsExpander    Security, HIPAA, SOC2,
                 |               |                AccessControl, Observability,
            PlatformBuilder    Backlog             Infrastructure, ApiDocs,
            /   |   \    \      |                  Performance
           /    |    \    \     |
     Database  Service Application Integration    DependencyAudit  Configuration
         |     Layer      |       |                     |               |
         |       |        |       |                     |               |
      Migration  |      UiUx     |                     |               |
         |       |        |       |                     |               |
         +-+-----+--------+------+                     |               |
           |                                           |               |
        Testing                                        |               |
           |                                           |               |
    CodeReasoning    CodeQuality                       |               |
           |              |                            |               |
           +------+-------+                            |               |
                  |                                    |               |
               Review                                  |               |
              /      \                                 |               |
         BugFix    Refactoring                         |               |
            |                                          |               |
    RequirementAnalyzer ──► GapAnalysis                |               |
            |                                          |               |
         Planning (RequirementsExpander + Architect)    |               |
                                                       |               |
    ═══════════════════════════════════════════════════════════════════
    POST-BACKLOG GATE: All actionable items must be Completed
    ═══════════════════════════════════════════════════════════════════
                                    |
                   Build (all code-gen + Review + BugFix)
                                    |
                   Deploy (Review + Testing + Infrastructure)
                                    |
                   Monitor (Deploy)
                                    |
                   LoadTest (Deploy)
                                    |
                              Supervisor (final)
```

### Dependency Table (Complete)

| Agent | Depends On |
|-------|-----------|
| RequirementsReader | *(none — entry point)* |
| Architect | RequirementsReader |
| PlatformBuilder | Architect |
| RequirementsExpander | RequirementsReader |
| Backlog | RequirementsExpander |
| Planning | RequirementsExpander, Architect |
| Database | PlatformBuilder, Backlog |
| ServiceLayer | PlatformBuilder, Backlog |
| Application | PlatformBuilder, Backlog |
| Integration | PlatformBuilder, Backlog |
| Testing | Database, ServiceLayer, PlatformBuilder |
| UiUx | Application |
| CodeReasoning | Database, ServiceLayer, Application, Integration |
| CodeQuality | Database, ServiceLayer, Application, Integration |
| Migration | Database |
| Review | Database, ServiceLayer, Application, Integration, Testing |
| BugFix | Review |
| Refactoring | Review |
| RequirementAnalyzer | Database, ServiceLayer, Application, Integration, Review |
| GapAnalysis | RequirementAnalyzer, Integration, Review |
| Security | RequirementsReader |
| HipaaCompliance | RequirementsReader |
| Soc2Compliance | RequirementsReader |
| AccessControl | RequirementsReader |
| Observability | RequirementsReader |
| Infrastructure | RequirementsReader |
| ApiDocumentation | RequirementsReader |
| Performance | RequirementsReader |
| DependencyAudit | PlatformBuilder |
| Configuration | PlatformBuilder |
| Build | Database, ServiceLayer, Application, Integration, Testing, Review, BugFix |
| Deploy | Review, Testing, Infrastructure |
| Monitor | Deploy |
| LoadTest | Deploy |
| Supervisor | *(special — runs as final gate after everything)* |

---

## 5. Pipeline Execution Flow

### Phase 1: Foundation (Dependency-Driven, Parallel)

```
Step 1:  RequirementsReader starts (no deps)
            │
Step 2:  RequirementsReader completes
            ├──► Architect dispatches
            ├──► RequirementsExpander dispatches
            └──► Enrichment agents dispatch in parallel:
                  Security, HIPAA, SOC2, AccessControl,
                  Observability, Infrastructure, ApiDocs, Performance
            │
Step 3:  Architect completes
            └──► PlatformBuilder dispatches
         RequirementsExpander completes
            └──► Backlog dispatches (creates initial work items)
            │
Step 4:  PlatformBuilder completes
            ├──► DependencyAudit, Configuration dispatch
            └──► TRIGGERS: re-queue RequirementsExpander → Backlog
                 (PlatformBuilder output → Expander creates platform-
                 derived technical requirements → Backlog creates
                 work items with deps and priorities)
```

### Phase 2: Backlog-Driven Development (Iterative)

Code-gen agents **never receive work directly from PlatformBuilder**.
All work flows through the Backlog:

```
Step 5:  Backlog + PlatformBuilder both complete
            └──► Code-gen agents dispatch and CLAIM InQueue items
                 from Backlog:
                  Database, ServiceLayer, Application, Integration
            │
Step 6:  Code-gen agents claim items → InProgress → Completed
            │
Step 7:  ITERATIVE CHECK: Still InQueue items?
            ├── YES → re-dispatch code-gen agents (another batch)
            └── NO  → proceed to quality phase
            │
Step 8:  Post-code-gen agents dispatch:
            ├── Testing (after Database + ServiceLayer)
            ├── CodeReasoning, CodeQuality (after all code-gen)
            ├── Migration (after Database)
            └── UiUx (after Application)
```

### Phase 3: Review & Remediation (Iterative Cycles)

```
Step 9:   Review dispatches (after all code-gen + Testing)
            │
Step 10:  Review produces findings
            ├──► Remediation dispatch:
            │     BugFix, Security, Performance, Testing, etc.
            │     (based on finding categories)
            ├──► RequirementsExpander → Backlog re-queued
            │     (findings become new work items)
            └──► Code-gen agents re-dispatch if new InQueue items
            │
Step 11:  BugFix/Remediation agents complete with new findings
            └──► Review re-queued (review the fixes)
            │
Step 12:  CONVERGES when Review produces 0 new findings
```

### Phase 4: Post-Backlog Gates

```
Step 13:  ALL actionable backlog items Completed?
            ├── NO  → log skip, wait for convergence
            └── YES → Build agent dispatches
            │
Step 14:  Build → Fix → Rebuild cycle (max 3 iterations):
            ├── Build succeeds → proceed to Deploy
            └── Build fails → BugFix → rebuild
            │
Step 15:  Deploy dispatches (after Build passes)
            │
Step 16:  Monitor dispatches (after Deploy)
            ├── Monitor → Fix → Redeploy cycle
            └── LoadTest dispatches (after Deploy)
            │
Step 17:  Supervisor generates final report
```

### Phase 5: Pipeline Complete

```
Step 18:  Write all artifacts to disk
          Publish completion event
          Audit log: pipeline completed
```

---

## 6. Backlog Lifecycle

Work items follow a strict lifecycle managed by `WorkItemLifecyclePolicy`:

```
    ┌─────┐    ReevaluateBlocked    ┌─────────┐    Claim()     ┌──────────┐
    │ New │ ──────────────────────► │ InQueue  │ ────────────► │ Received │
    └─────┘   (deps satisfied,     └─────────┘  (agent takes  └──────────┘
     ▲  ▲      queue has room)        ▲   ▲       ownership)      │
     │  │                             │   │                       │ Start()
     │  │    Fail() exhausted         │   │                       ▼
     │  └─────────────────────────────┘   │                  ┌────────────┐
     │       Fail() retriable             │                  │ InProgress │
     │                                    │                  └────────────┘
     │                                    │                       │
     │                                    └───────────────────────┘
     │                                        Fail() retry          │ Complete()
     │                                                              ▼
     │                                                         ┌───────────┐
     └─────────────────────────────────────────────────────────│ Completed │
                          (parent rollup)                      └───────────┘
```

### Work Item Types

| Type | Description | Claiming |
|------|-------------|----------|
| **Epic** | Large feature group | Not claimed (parent container) |
| **UserStory** | Business capability | Not claimed (parent container) |
| **UseCase** | Implementation scenario | Claimed by matching agent |
| **Task** | Multi-agent technical task | Claimed by ALL relevant agents |
| **Bug** | Defect fix | Claimed by BugFix agent |

### WIP Limits

| Limit | Default | Purpose |
|-------|---------|---------|
| `MaxQueueItems` | 10 | Max items in `InQueue` (Received) state |
| `MaxInDevItems` | 10 | Max items in `InProgress` state |
| `BatchSize` | 50 | Max items an agent can claim per dispatch |

**Effective claim limit** = `min(BatchSize, availableQueueSlots, availableDevSlots)`

---

## 7. Iterative Feedback Loops

### 7.1 Feedback-Driven Expansion

When a feedback agent (Review, Build, Deploy, Monitor, BugFix) completes with **new findings**:

```
Feedback Agent ──► new findings detected
                      │
                      ├──► RequirementsExpander re-queued
                      │       (findings → expanded work items)
                      │
                      ├──► Backlog re-queued
                      │       (work items → InQueue)
                      │
                      └──► Review re-queued (if non-Review agent)
                              (review the new/fixed code)
```

### 7.2 Backlog-Driven Re-Dispatch

Every daemon loop tick, the orchestrator checks:

```
For each backlog-driven agent (Database, ServiceLayer, Application, Integration, UiUx, Testing):
    IF agent already completed
    AND there are InQueue items the agent can claim
    THEN re-dispatch the agent for another batch
```

This ensures work items created by feedback loops get processed.

### 7.3 Review → Remediation Cycle

Uses a **generation counter** (not a one-shot flag) so cycles repeat:

```
Review (findings: 5) ──► BugFix, Security, Performance dispatched
                              │
BugFix completes ────────────► Review re-queued
                              │
Review (findings: 2) ──► BugFix dispatched again
                              │
BugFix completes ────────────► Review re-queued
                              │
Review (findings: 0) ──► CONVERGED, proceed to Build
```

### 7.4 PlatformBuilder → Expander → Backlog Expansion

One-time trigger when PlatformBuilder completes. Its output (architecture,
platform conventions, service boundaries) feeds back through the Expander
so that platform-derived technical requirements become tracked backlog items:

```
PlatformBuilder done ──► RequirementsExpander re-queued
                              (architecture output → expanded work items
                               with deps, priorities, agent assignments)
                         ──► Backlog re-queued
                              (process expanded items into InQueue)
                         ──► Code-gen agents wait for Backlog
                              (claim work from InQueue, NOT directly
                               from PlatformBuilder)
```

### 7.5 Build-Fix Cycle (max 3 iterations)

```
Build ──► compile errors? ──► BugFix ──► rebuild
  │                                         │
  └── passes ──► Deploy                     └── still fails? ──► skip Deploy
```

### 7.6 Monitor-Fix Cycle

```
Deploy ──► Monitor ──► issues? ──► BugFix ──► redeploy ──► re-monitor
             │
             └── healthy ──► done
```

---

## 8. Finding → Remediation Dispatch

When Review (or other feedback agents) report findings, they are categorized and routed:

| Finding Category | Remediation Agent(s) |
|-----------------|---------------------|
| `NFR-CODE-01`, `NFR-CODE-02`, `NFR-TEST-01` | BugFix |
| `Implementation` | BugFix, ServiceLayer |
| `MultiTenant` | BugFix, Database |
| `Audit`, `Traceability`, `Conventions` | BugFix |
| `Security` | BugFix, Security |
| `Coverage`, `FeatureCoverage` | BugFix |
| `TestCoverage` | BugFix, Testing |
| `Performance`, `Performance-N+1`, `Performance-EF` | Performance |
| `OWASP-A01`, `OWASP-A02`, `OWASP-A03` | Security |
| `HIPAA-164.312(a)`, `HIPAA-164.312(b)` | HipaaCompliance |
| `SOC2-CC6`, `SOC2-CC7`, `SOC2-CC8` | Soc2Compliance |
| `Build`, `Deployment`, `Runtime`, `Database` | BugFix |

---

## 9. Human-in-the-Loop (HITL) Gates

Two types of mandatory human approval:

### 9.1 Database DDL Execution

```
DatabaseAgent starts + ExecuteDdl=true + first attempt
    │
    └──► HITL prompt: "Database DDL execution requires approval"
           Timeout: 15 minutes
           │
           ├── Approved → DDL executes, flag set for run
           ├── Rejected → DDL disabled, artifacts-only mode
           └── Timed out → DDL disabled for safety
```

### 9.2 Critical Findings

```
Any agent produces SecurityViolation or ComplianceViolation findings
    │
    └──► HITL prompt: "N critical finding(s) require review"
           Timeout: 30 minutes
           │
           ├── Approved → pipeline continues
           └── Rejected → treat as agent failure
```

---

## 10. Self-Healing & Retry

Each agent runs through `RunAgentWithHealingAsync` with up to 3 attempts:

```
Attempt 1: Execute normally
    │
    ├── Success → Completed
    └── Failure → classify error
                    │
                    ├── Non-recoverable → fail immediately
                    └── Recoverable → retry with delay
                          │
Attempt 2: Execute with exponential backoff
    │
    ├── Success → "Self-healed on attempt 2"
    └── Failure → retry again
                    │
Attempt 3: Final attempt
    │
    ├── Success → "Self-healed on attempt 3"
    └── Failure → exhausted, mark failed
```

**Work Item Lifecycle on Failure:**
- RetryCount < MaxItemRetries → item set back to `InQueue` (re-claimable)
- RetryCount >= MaxItemRetries → item returned to `New` (backlogged)

---

## 11. WIP Limits & Adaptive Throttling

The orchestrator dynamically adjusts WIP limits based on pipeline health:

| Blocked Ratio | Queue Adjustment | InDev Adjustment |
|--------------|-----------------|-----------------|
| < 55% | base (10) | base (10) |
| 55% - 74% | base + 5 | base + 5 |
| >= 75% | base + 10 | base + 10 |

**Max adaptive cap:** 50 (prevents runaway expansion)

**Backpressure:** When queue/dev limits are full, agents skip claiming and log:
```
[Lifecycle] {agent} skipped claiming — WIP limits reached (queue=10/10, inDev=10/10)
```

---

## 12. Inter-Agent Communication

### 12.1 Directive Queue

Agents communicate via `AgentContext.DirectiveQueue`:

```csharp
context.DirectiveQueue.Enqueue(new AgentDirective
{
    From = AgentType.GapAnalysis,
    To = AgentType.RequirementsExpander,
    Action = "EXPAND_GAPS",
    Details = "Expand 5 implementation gaps into backlog items"
});
```

**Supported Actions:**
| Action | Effect |
|--------|--------|
| `RE_RUN` | Re-queue target agent for a fresh run |
| `EXPAND_NEW` | Schedule RequirementsExpander to expand new requirements |
| `EXPAND_GAPS` | Schedule RequirementsExpander to expand gap items |
| `REFRESH_BACKLOG` | Schedule Backlog to reprocess work items |

### 12.2 Progress Reporting

Agents report progress via `context.ReportProgress`:

```csharp
await context.ReportProgress(AgentType.Database, "Generated 12 entity classes...");
```

Events are published via SignalR to the UI in real-time.

### 12.3 Shared Context

All agents share the `AgentContext` which contains:
- `Requirements` — raw requirements from docs
- `ExpandedRequirements` — backlog work items (Epics, Stories, Tasks, Bugs)
- `Artifacts` — generated code files
- `Findings` — Review/quality findings
- `AgentStatuses` — live status of each agent
- `ImplementationPlan` — structured plan from Planning agent
- `DomainModel` — architecture from Architect agent
- `FailureRecords` — structured agent failure records

---

## 13. Technology Stack

| Component | Technology |
|-----------|------------|
| Runtime | .NET 10.0 / ASP.NET Core |
| Real-time | SignalR Hub |
| Pipeline State | SQLite |
| Target Database | PostgreSQL (multi-tenant) |
| Containerization | Docker + docker-compose |
| Event Bus | Kafka (generated) |
| Healthcare Standards | FHIR R4, HL7 v2 |
| Compliance | HIPAA §164.312, SOC 2 Type II, OWASP Top 10 |
| Observability | OpenTelemetry, Prometheus, Grafana |
| Load Testing | k6, JMeter |
| API Docs | OpenAPI 3.1, Swagger |
| Web UI | ASP.NET Razor Pages |

---

*Generated from orchestrator source code — AgentOrchestrator.cs*


If any task are stoped in mid and they were in dev, those task will be restarting when app is started.
