# Implementation TODO from reqs.md

This backlog maps the requirement set in `reqs.md` to concrete implementation tasks for the current HMS agent platform.

## Already Covered (do not duplicate)

- Agent registry and dependency-aware orchestration.
- Task-to-agent matching based on task description, DOD, tags, and acceptance criteria.
- Broad specialist agent pool: architecture, database, service, application, integration, testing, review, security, compliance, observability, deployment.
- Backlog lifecycle with queue/in-progress/completed states.
- DOD verification by dedicated `DodVerificationAgent`.
- Unit tests for assignment and core backlog execution flow.

## P0 Tasks (Start Here)

### 1) Requirement Intake and Enrichment

- [ ] Implement multimodal requirement intake (PDF, DOCX, image OCR, voice transcript).
  - AC: Uploading any supported format creates normalized `Requirement` objects with source provenance.
- [x] Implement ambiguity detection with clarifying questions.
  - AC: Ambiguous requirements produce explicit clarification questions and warning findings.
- [ ] Add requirement version history with semantic diff and rollback.
  - AC: Requirement edits are versioned and queryable with before/after diff.
- [ ] Add contradiction and duplicate detection across requirement set.
  - AC: Conflicting/duplicate requirements are flagged with merge guidance.

### 2) BRD Generator

- [ ] Add dedicated BRD generation workflow and section templates.
  - AC: BRD output includes summary, scope, stakeholders, FR/NFR, constraints, integrations.
- [ ] Add BRD diagram generation (Mermaid/PlantUML).
  - AC: Context, flow, and sequence diagrams are generated for approved requirements.
- [ ] Add BRD review/approval loop with status and audit trail.
  - AC: BRD can move Draft -> InReview -> Approved/Rejected with comments.
- [ ] Add BRD export targets (Markdown and PDF first).
  - AC: Approved BRD can be exported as versioned artifacts.

### 3) Orchestrator Hardening

- [ ] Add deterministic replay and checkpoint resume.
  - AC: Failed runs can restart from checkpoint with deterministic ordering.
- [ ] Add conflict resolution pass for overlapping artifact writes.
  - AC: Path/content conflicts are detected and resolved with audit logs.
- [ ] Add policy-driven SLA/retry/escalation budgets per agent.
  - AC: Repeated failures escalate automatically per configured policy.

### 4) Quality and Delivery Gates

- [ ] Enforce requirement-to-artifact-to-test traceability gate.
  - AC: Release is blocked when shipped artifacts lack requirement links.
- [ ] Enforce acceptance criteria coverage gate.
  - AC: Release is blocked when acceptance criteria are uncovered.
- [ ] Create release evidence package for compliance sign-off.
  - AC: Build/test/security/compliance reports are bundled per run.

## P1 Tasks

### 5) Backlog Decomposer Improvements

- [ ] Add capacity-aware sprint planning suggestions.
  - AC: Stories are grouped into suggested sprints based on dependencies and capacity.
- [ ] Add historical estimation calibration.
  - AC: Story points include confidence based on prior throughput.
- [ ] Add dependency critical-path and risk scoring.
  - AC: Backlog ranking reflects blockers, risk, and business value.

### 6) Agent Pool Extensions

- [ ] Add BRD-specific synthesis agent (or BRD coordinator role).
  - AC: BRD narrative and structure can be produced without manual assembly.
- [ ] Add multimodal extraction/validation agent pair.
  - AC: Extracted requirement fields carry confidence and validation status.
- [ ] Add merge-resolution agent for semantic code conflicts.
  - AC: Conflicts unresolved by static rules are routed to dedicated agent.

## P2 Tasks

### 7) Learning Loop and Optimization

- [ ] Persist post-run learning outcomes by task type and agent route.
  - AC: Future routing can use historical success/failure patterns.
- [ ] Add simulation mode for orchestration planning.
  - AC: Dry-run predicts execution waves, bottlenecks, and likely escalations.

## Current Sprint Start

1. Requirement ambiguity detection and clarification questions (done in this pass).
2. Requirement version history model and persistence.
3. BRD generation baseline with markdown template.
4. Traceability + acceptance coverage quality gates.
