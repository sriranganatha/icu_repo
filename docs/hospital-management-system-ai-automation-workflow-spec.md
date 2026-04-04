# Hospital Management System AI Automation Workflow Specification

## 1. Purpose

This document defines how AI copilots and workflow automation operate end to end across diagnosis support, treatment recommendation, care planning, and operational task orchestration.

It focuses on states, approval checkpoints, execution boundaries, reversibility, and tenant-safe automation behavior.

## 2. Automation Principles

- AI may propose, draft, prioritize, route, and prepare actions.
- AI may not finalize diagnosis, prescribe, place unrestricted orders, or execute irreversible legal record changes without approved human checkpoints.
- Workflow automation must remain observable, auditable, reversible where possible, and tenant-aware.
- Clinical and hospital automation must fail safely to manual workflow.

## 3. AI Action Classes

### Class A: Advisory Only

Examples:

- differential support
- treatment suggestion
- next-step diagnostic suggestion
- staffing prioritization suggestion

Execution rule:

- no direct system mutation beyond audit record generation

### Class B: Draft Generation

Examples:

- note drafts
- discharge instructions
- care-plan drafts
- referral letters

Execution rule:

- draft objects may be created, but destination records are unchanged until accepted

### Class C: Approval-Gated Automation

Examples:

- task creation across nursing, pharmacy, case management, and billing queues
- patient reminder generation
- documentation completion routing
- pre-auth packet preparation
- discharge workflow task orchestration

Execution rule:

- automation proposal may create pending actions, but execution requires policy-defined approval when risk threshold is met

### Class D: Prohibited Autonomous Execution

Examples:

- diagnosis finalization
- prescription finalization
- autonomous treatment-plan commitment
- destructive legal disclosure action

Execution rule:

- prohibited unless separate governance and legal approval exists, which is not assumed here

## 4. Clinical Copilot Workflow

### 4.1 Diagnostic Support Flow

1. clinician initiates request or workflow triggers governed assistance
2. retrieval layer assembles approved patient context
3. differential support service produces diagnosis candidates and evidence summary
4. response is labeled advisory and stored in AI audit records
5. clinician accepts, edits, ignores, or rejects suggestions

### 4.2 Treatment Recommendation Flow

1. clinician requests treatment support or selects diagnosis context
2. safety filters evaluate contraindications, allergies, formulary, renal function, and protected conditions where applicable
3. recommendation service returns treatment, monitoring, and follow-up options with rationale
4. clinician may accept, edit, reject, or convert into downstream tasks or draft care-plan content
5. downstream actions require explicit human confirmation before mutation of clinical record or orders

### 4.3 Care-Plan Draft Flow

1. copilot synthesizes diagnosis, diagnostics, medications, nursing tasks, and case-management context
2. draft care plan is generated with tasks, follow-up actions, and patient education suggestions
3. multidisciplinary reviewers may approve or modify sections
4. accepted sections flow into governed destination workflows

## 5. Automation Proposal Workflow

### 5.1 Automation States

- proposed
- pending_approval
- approved
- executing
- partially_executed
- completed
- rejected
- cancelled
- failed
- rolled_back

### 5.2 Approval Policy Inputs

- workflow type
- clinical risk class
- financial materiality
- privacy sensitivity
- legal sensitivity
- tenant policy
- department policy
- user role and authority

### 5.3 Automation Decision Rules

- low-risk administrative automations may auto-execute under policy
- patient-care-affecting automations require clinician or designated reviewer approval
- high-sensitivity or cross-functional automations may require dual approval
- prohibited automations are blocked before proposal creation

## 6. Example Automation Use Cases

### 6.1 Care Coordination Automation

- trigger follow-up tasks after discharge
- notify pharmacy about medication fulfillment blockers
- create home-health referral preparation tasks

### 6.2 Diagnostic Escalation Automation

- create urgent review task when critical biomarker threshold is reached
- route result review to responsible clinician and escalation backup

### 6.3 Revenue-Cycle Automation

- prepare authorization packet
- route missing documentation queue
- prioritize denials for likely overturn or high-value claims

### 6.4 Hospital Operations Automation

- raise bed turnover preparation task
- escalate ED boarding risk
- notify staffing coordinator of acuity imbalance

## 7. Reversibility Rules

- task creation should be cancelable or closable with audit reason
- notification dispatch should preserve send log and suppression logic
- queue transitions should be reversible when no downstream irreversible action has occurred
- irreversible actions must be approval-gated and explicitly marked as such in proposal metadata

## 8. Multi-Tenant Automation Constraints

- automation may not create actions outside the originating tenant
- federated enterprise tenants may route across facilities only when tenant policy allows
- dedicated-tenant deployments still store tenant and facility context for approvals and audit
- shared automation engines must isolate proposal queues, execution workers, and caches by tenant

## 9. Audit Requirements

Every automation proposal or copilot-driven workflow must retain:

- tenant_id
- facility_id
- workflow_type
- trigger source
- input evidence references
- model and prompt version
- policy decision
- approver identity where applicable
- execution result
- rollback or cancellation result where applicable

## 10. Monitoring Requirements

- proposal volume by workflow and tenant
- approval rate by workflow class
- rejection and override rate
- failure and rollback rate
- downstream outcome correlation where feasible
- latency by copilot and automation workflow

## 11. Required API and Event Hooks

- automation proposal create and approve APIs
- treatment recommendation accept and reject APIs
- ai.interaction-recorded.v1
- ai.output-accepted.v1
- ai.output-rejected.v1
- ai.output-overridden.v1
- workflow-specific execution events

## 12. Implementation Notes

- use workflow engine plus policy engine, not ad hoc service branching
- represent automation state transitions explicitly
- never allow silent auto-application of clinically consequential AI output