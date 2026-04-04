# Hospital Management System Multi-Tenant Strategy

## 1. Purpose

This document defines the supported tenancy models, isolation patterns, control-plane responsibilities, data-plane options, and decision criteria for deploying the Hospital Management System across multiple hospitals, groups, regions, and regulatory environments.

## 2. Multi-Tenant Objectives

- Support SaaS efficiency for smaller organizations.
- Support stronger isolation for larger or more regulated hospital groups.
- Preserve a consistent product and API model across tenancy flavors.
- Enable tenant migration between isolation models as size or regulation changes.
- Enforce regional data residency and per-tenant governance.

## 3. Tenancy Layers

### Control Plane

Shared services that may remain centralized if permitted by policy:

- Tenant registry.
- Facility and organizational hierarchy.
- Feature flags.
- Role catalog templates.
- Terminology and reference data distribution.
- Release management metadata.
- Fleet observability metadata.

### Data Plane

Tenant-serving runtime and data services for:

- Clinical data.
- Financial data.
- audit and evidence data.
- AI interaction and governance data.
- operational read models.

### Experience Plane

- Staff web and mobile applications.
- Patient portal and kiosks.
- Partner integration endpoints.

## 4. Supported Tenancy Flavors

### Flavor 1: Shared SaaS Tenant Partitioning

Characteristics:

- Shared application clusters.
- Shared databases or stores with tenant partition keys.
- Lower cost and faster onboarding.

Best for:

- Single clinics.
- Small provider groups.
- Pilot environments.

Risks to manage:

- Stronger need for tenant-safe query enforcement.
- More careful noisy-neighbor and bulk operation controls.

### Flavor 2: Shared Runtime with Dedicated Clinical Data Stores

Characteristics:

- Shared application services.
- Dedicated database, schema, or storage accounts for clinical or audit domains.
- Shared lower-risk control-plane metadata.

Best for:

- Mid-sized hospitals.
- Tenants with stricter residency or isolation needs for PHI.

### Flavor 3: Dedicated Tenant Deployment

Characteristics:

- Dedicated runtime, integration workers, and data stores.
- Optional shared control plane for metadata and release orchestration.

Best for:

- Large health systems.
- National or public-sector deployments.
- Tenants requiring custom integrations or strong contractual isolation.

### Flavor 4: Federated Enterprise Tenancy

Characteristics:

- One hospital group may operate multiple subsidiaries, facilities, or legal entities under a tenant family.
- Shared identity and reference data may coexist with segmented clinical data.

Best for:

- Enterprise hospital networks.
- Merged health systems with staged harmonization.

## 5. Tenancy Selection Criteria

- Regulatory residency requirements.
- PHI volume and sensitivity.
- Contractual isolation commitments.
- integration customization needs.
- performance and latency profile.
- disaster recovery expectations.
- operational cost tolerance.
- need for tenant-specific release cadence.

## 6. Isolation Dimensions

### Compute Isolation

- shared pods or workers
- dedicated worker pools
- dedicated clusters

### Storage Isolation

- row-level partitioning
- schema-per-tenant
- database-per-tenant
- storage account or bucket isolation

### Network Isolation

- logical network segmentation
- dedicated private connectivity
- partner-network isolation for tenant-specific interfaces

### Cryptographic Isolation

- shared KMS hierarchy with tenant-wrapped keys
- dedicated tenant keys
- regional key domains

## 7. Tenant Context Propagation

Every request, event, job, and audit record should propagate:

- tenant_id
- region_id
- facility_id where relevant
- correlation_id
- actor identity
- channel or integration source

This rule applies even in dedicated deployments so control-plane governance, observability, and migration remain consistent.

## 8. Data Residency Model

- Tenants are assigned to one or more approved residency domains.
- Clinical, audit, ROI, and AI evidence data should default to the tenant residency domain.
- Cross-region replication must be policy governed and visible to administrators.
- Global metadata in a control plane must avoid storing unnecessary PHI.

## 9. Tenant Customization Model

Tenant-level customization may include:

- facility structure
- forms and templates
- specialty order sets
- pricing and payer rules
- notification policies
- AI enablement flags
- retention overrides within legal bounds

Customization should be metadata-driven and version-controlled rather than implemented as tenant-specific code forks where possible.

## 10. Tenant Lifecycle

### Provisioning

- create tenant record
- assign tenancy flavor
- assign region and policy packs
- provision core data stores and secrets
- load baseline reference data and roles

### Expansion

- add facilities or departments
- enable new service modules
- provision partner integrations

### Promotion

- migrate from shared partitioning to dedicated schema, store, or deployment
- maintain identifiers and audit continuity

### Offboarding

- disable access
- export or archive records according to legal obligations
- decommission compute and secrets
- verify destruction where allowed

## 11. Observability by Tenancy Flavor

- tenant-scoped dashboards for service health, event lag, AI usage, and integration backlog
- region-scoped monitoring for residency and disaster recovery
- noisy-neighbor detection in shared environments
- tenant-scoped audit evidence exports

## 12. Backup and Recovery by Tenancy Flavor

### Shared Partitioned Tenants

- support full-environment backup with tenant-aware recovery tooling where feasible

### Dedicated Store Tenants

- support tenant-scoped restore and regional failover according to policy

### Dedicated Deployments

- support complete tenant environment recovery with tenant-specific RPO and RTO where contracted

## 13. AI and Tenancy

- AI retrieval must never cross tenant boundaries.
- Shared model serving is acceptable only when input, output, logs, and caches remain tenant isolated and contractually acceptable.
- High-sensitivity tenants may require dedicated model endpoints or dedicated inference environments.
- Prompt templates and approved models may be globally governed while enablement remains tenant scoped.

## 14. Recommended Defaults

### Small Tenants

- Flavor 1 with strict tenant partitioning, strong ABAC, and shared runtime.

### Mid-Sized Hospitals

- Flavor 2 with shared application runtime and dedicated clinical or audit stores.

### Large Health Systems

- Flavor 3 or Flavor 4 with dedicated clinical plane and optional federated enterprise control plane.

## 15. Architecture Requirements Derived from Tenancy Strategy

- control plane and data plane separation
- tenant-aware API gateway
- tenant-aware event bus metadata
- tenant-portable identifiers
- policy-based routing to dedicated or shared stores
- tenant-scoped backup, retention, and legal hold management
- tenant-level kill switches for AI and integrations