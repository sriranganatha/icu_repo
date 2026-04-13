namespace GNex.Services.Platform;

/// <summary>Defines the default section list for each BRD type.</summary>
public static class BrdTypeTemplates
{
    public sealed record SectionDef(string Type, string Title, int Order, string Prompt);

    public static IReadOnlyList<SectionDef> GetSections(string brdType) => brdType switch
    {
        "web_application"    => WebApplicationSections,
        "mobile_application" => MobileApplicationSections,
        "api_service"        => ApiServiceSections,
        "data_pipeline"      => DataPipelineSections,
        "integration"        => IntegrationSections,
        _                    => GeneralSections,   // "general" or unknown
    };

    // ══════════════════════════════════════════════════════════════
    // General BRD
    // ══════════════════════════════════════════════════════════════
    public static readonly IReadOnlyList<SectionDef> GeneralSections =
    [
        new("executive_summary",          "Executive Summary",                  1, "Provide a concise overview of the project, its goals, and expected outcomes."),
        new("project_scope",              "Project Scope",                      2, "Define what is in-scope and out-of-scope for this project."),
        new("stakeholders",               "Stakeholders & Roles",              3, "List all stakeholders, their roles, and responsibilities."),
        new("business_objectives",        "Business Objectives",               4, "Describe the measurable business outcomes this project aims to achieve."),
        new("functional_requirements",    "Functional Requirements",           5, "Detail all functional requirements using clear, testable acceptance criteria."),
        new("non_functional_requirements","Non-Functional Requirements",       6, "Specify performance, scalability, security, availability, and other NFRs."),
        new("business_rules",             "Business Rules & Constraints",      7, "Document business rules, constraints, and regulatory requirements."),
        new("data_requirements",          "Data Requirements",                 8, "Describe data models, data flows, storage, and data quality expectations."),
        new("assumptions_dependencies",   "Assumptions & Dependencies",        9, "List assumptions made and external dependencies."),
        new("risks_mitigations",          "Risks & Mitigations",             10, "Identify risks, their impact, likelihood, and mitigation strategies."),
        new("timeline_milestones",        "Timeline & Milestones",           11, "Outline the project timeline, key milestones, and delivery dates."),
        new("success_criteria",           "Success Criteria & KPIs",         12, "Define how success will be measured with specific KPIs."),
        new("glossary",                   "Glossary",                        13, "Define domain-specific terms and abbreviations used in this document."),
    ];

    // ══════════════════════════════════════════════════════════════
    // Web Application BRD — includes extensive UI/UX sections
    // ══════════════════════════════════════════════════════════════
    public static readonly IReadOnlyList<SectionDef> WebApplicationSections =
    [
        new("executive_summary",          "Executive Summary",                  1, "Provide a concise overview of the web application, its goals, and expected outcomes."),
        new("project_scope",              "Project Scope",                      2, "Define what is in-scope and out-of-scope for this web application."),
        new("stakeholders",               "Stakeholders & Roles",              3, "List all stakeholders, their roles, and responsibilities."),
        new("business_objectives",        "Business Objectives",               4, "Describe the measurable business outcomes this web application aims to achieve."),
        new("target_audience",            "Target Audience & User Personas",   5, "Define the target audience segments and create detailed user personas including demographics, goals, pain points, and tech proficiency."),
        new("user_journey_maps",          "User Journey Maps",                 6, "Map out end-to-end user journeys for each persona. Include touchpoints, emotions, pain points, and opportunities at each stage from discovery through task completion."),
        new("information_architecture",   "Information Architecture",          7, "Define the site map, navigation structure, content hierarchy, and labeling system. Include card sorting results if applicable."),
        new("screen_inventory",           "Screen Inventory & Page Descriptions", 8, "List every screen/page in the application with its purpose, key content zones, primary actions, and relationships to other screens."),
        new("wireframe_specifications",   "Wireframe & Layout Specifications", 9, "Describe detailed wireframe specs for each major screen: header, navigation, content areas, sidebars, footers, modals, and drawers. Specify grid system and spacing."),
        new("ui_component_library",       "UI Component Library",             10, "Define the component library: buttons, forms, cards, tables, alerts, badges, tooltips, dropdowns, tabs, accordions, modals, and their variants (primary, secondary, danger, etc.)."),
        new("interaction_patterns",       "Interaction Patterns & Micro-interactions", 11, "Describe interaction patterns: hover states, click feedback, loading states, skeleton screens, transitions, animations, drag-and-drop, infinite scroll, and real-time updates."),
        new("form_design",               "Form Design & Validation",          12, "Specify all forms with field types, validation rules, error messages, inline validation, auto-save behavior, multi-step form flows, and conditional fields."),
        new("responsive_design",          "Responsive Design Strategy",       13, "Define breakpoints, layout changes per viewport (mobile/tablet/desktop/large), touch-friendly targets, and progressive disclosure strategy."),
        new("accessibility",              "Accessibility Requirements (WCAG)",14, "Specify WCAG 2.1 AA compliance: keyboard navigation, screen reader support, color contrast ratios, ARIA labels, focus management, and alt text requirements."),
        new("design_system",              "Design System & Theming",          15, "Define typography scale, color palette (primary, secondary, accent, semantic), spacing system, border radius, shadows, and dark/light mode support."),
        new("navigation_patterns",        "Navigation & Routing",             16, "Specify navigation patterns: top nav, side nav, breadcrumbs, tabs, pagination. Define URL structure, deep linking, and browser history management."),
        new("dashboard_layouts",          "Dashboard & Data Visualization",   17, "Describe dashboard layouts, chart types, KPI cards, data tables with sorting/filtering/pagination, export options, and real-time data refresh."),
        new("notification_system",        "Notification & Messaging System",  18, "Define toast notifications, alert banners, in-app messaging, email notifications, notification preferences, and real-time push notifications."),
        new("search_experience",          "Search & Filtering Experience",    19, "Specify search functionality: auto-complete, faceted search, filters, saved searches, recent searches, and search results layout."),
        new("error_handling_ux",          "Error Handling & Empty States",    20, "Define error pages (404, 500, etc.), inline errors, empty states with calls-to-action, offline states, and retry mechanisms."),
        new("functional_requirements",    "Functional Requirements",          21, "Detail all functional requirements using clear, testable acceptance criteria."),
        new("non_functional_requirements","Non-Functional Requirements",      22, "Specify performance (page load < 2s, TTFB < 200ms), scalability, security, availability, and other NFRs."),
        new("security_requirements",      "Security Requirements",            23, "Define authentication flows, authorization (RBAC), CSRF/XSS protection, CSP headers, rate limiting, session management, and data encryption."),
        new("browser_compatibility",      "Browser & Device Compatibility",   24, "Specify supported browsers, minimum versions, device types, and testing matrix."),
        new("performance_budget",         "Performance Budget",               25, "Define performance budgets: bundle sizes, image optimization, caching strategy, CDN usage, lazy loading, and Core Web Vitals targets."),
        new("data_requirements",          "Data Requirements",                26, "Describe data models, data flows, storage, API integration, and data quality expectations."),
        new("assumptions_dependencies",   "Assumptions & Dependencies",       27, "List assumptions and external dependencies (APIs, CDNs, third-party services)."),
        new("risks_mitigations",          "Risks & Mitigations",             28, "Identify risks, their impact, likelihood, and mitigation strategies."),
        new("timeline_milestones",        "Timeline & Milestones",           29, "Outline the project timeline, key milestones, and delivery dates."),
        new("success_criteria",           "Success Criteria & KPIs",         30, "Define success metrics: user adoption, conversion rates, bounce rates, task completion rates, NPS scores."),
        new("glossary",                   "Glossary",                        31, "Define domain-specific terms and abbreviations used in this document."),
    ];

    // ══════════════════════════════════════════════════════════════
    // Mobile Application BRD — includes extensive UI/UX sections
    // ══════════════════════════════════════════════════════════════
    public static readonly IReadOnlyList<SectionDef> MobileApplicationSections =
    [
        new("executive_summary",          "Executive Summary",                  1, "Provide a concise overview of the mobile application, its goals, and expected outcomes."),
        new("project_scope",              "Project Scope",                      2, "Define what is in-scope and out-of-scope for this mobile application."),
        new("stakeholders",               "Stakeholders & Roles",              3, "List all stakeholders, their roles, and responsibilities."),
        new("business_objectives",        "Business Objectives",               4, "Describe the measurable business outcomes this mobile application aims to achieve."),
        new("platform_strategy",          "Platform Strategy",                  5, "Define target platforms (iOS, Android, cross-platform), minimum OS versions, device support matrix, and platform-specific considerations."),
        new("target_audience",            "Target Audience & User Personas",    6, "Define target audience segments with detailed user personas: demographics, mobile usage patterns, device preferences, connectivity conditions."),
        new("user_journey_maps",          "User Journey Maps",                  7, "Map end-to-end mobile user journeys for each persona. Include touchpoints, gestures, context (on-the-go, seated), and emotional states."),
        new("app_navigation",             "App Navigation & IA",                8, "Define navigation structure: tab bar, drawer, stack navigation, deep linking, universal links, and navigation hierarchy."),
        new("screen_inventory",           "Screen Inventory & Flows",           9, "List every screen with purpose, content zones, primary actions, and screen-to-screen flow diagrams."),
        new("wireframe_specifications",   "Wireframe & Layout Specifications", 10, "Describe detailed wireframe specs per screen: safe areas, status bar, navigation bar, content area, tab bar, gesture zones."),
        new("ui_component_library",       "UI Component Library",              11, "Define mobile components: buttons, cards, lists, bottom sheets, action sheets, snackbars, FABs, pull-to-refresh, swipe actions."),
        new("gesture_interactions",       "Gesture & Touch Interactions",      12, "Specify gestures: tap, long-press, swipe, pinch-to-zoom, drag, 3D Touch/Haptic Touch. Define touch target sizes (min 44x44pt)."),
        new("animation_transitions",      "Animation & Screen Transitions",   13, "Define shared element transitions, hero animations, loading animations, skeleton screens, parallax scrolling, and haptic feedback."),
        new("form_design",               "Form Design & Input Methods",       14, "Specify mobile forms: keyboard types, auto-fill, biometric input, camera/scanner input, voice input, steppers, and validation patterns."),
        new("offline_capability",         "Offline Capability & Data Sync",   15, "Define offline-first strategy: local storage, background sync, conflict resolution, queue management, and offline indicators."),
        new("push_notifications",         "Push Notifications & Alerts",      16, "Specify push notification types, rich notifications, notification channels, quiet hours, deep linking from notifications, and opt-in flows."),
        new("accessibility",              "Accessibility Requirements",        17, "Define VoiceOver/TalkBack support, Dynamic Type, color contrast, haptic patterns, one-handed use, and assistive technology testing."),
        new("design_system",              "Design System & Theming",          18, "Define typography, color system, spacing, elevation, dark mode, platform-adaptive styling (Material/Cupertino), and dynamic theming."),
        new("onboarding_experience",      "Onboarding & First-Run Experience",19, "Describe onboarding flow: welcome screens, permission requests, tutorial walkthrough, account setup, and progressive disclosure."),
        new("functional_requirements",    "Functional Requirements",          20, "Detail all functional requirements with testable acceptance criteria."),
        new("non_functional_requirements","Non-Functional Requirements",      21, "Specify: app launch < 1.5s, ANR tolerance, battery impact, memory usage, storage usage, network efficiency."),
        new("security_requirements",      "Security Requirements",            22, "Define: biometric auth, secure enclave, certificate pinning, jailbreak/root detection, app transport security, data encryption at rest."),
        new("device_compatibility",       "Device & OS Compatibility",        23, "Specify device support matrix, OS version range, screen sizes, notch/dynamic island handling, and foldable support."),
        new("app_store_requirements",     "App Store & Distribution",         24, "Define App Store/Play Store compliance, review guidelines, listing metadata, screenshots, privacy labels, and update strategy."),
        new("data_requirements",          "Data Requirements",                25, "Describe data models, local databases, API integration, background data refresh, and data quality expectations."),
        new("assumptions_dependencies",   "Assumptions & Dependencies",       26, "List assumptions and external dependencies (APIs, SDKs, third-party libraries)."),
        new("risks_mitigations",          "Risks & Mitigations",             27, "Identify risks, their impact, likelihood, and mitigation strategies."),
        new("timeline_milestones",        "Timeline & Milestones",           28, "Outline the project timeline, key milestones, and delivery dates."),
        new("success_criteria",           "Success Criteria & KPIs",         29, "Define: app store rating, DAU/MAU, retention rates, crash-free rate, session duration."),
        new("glossary",                   "Glossary",                        30, "Define domain-specific terms and abbreviations used in this document."),
    ];

    // ══════════════════════════════════════════════════════════════
    // API Service BRD
    // ══════════════════════════════════════════════════════════════
    public static readonly IReadOnlyList<SectionDef> ApiServiceSections =
    [
        new("executive_summary",          "Executive Summary",                  1, "Provide a concise overview of the API service, its goals, and expected outcomes."),
        new("project_scope",              "Project Scope",                      2, "Define what is in-scope and out-of-scope for this API service."),
        new("stakeholders",               "Stakeholders & Roles",              3, "List all stakeholders (API consumers, platform teams, external partners) and their roles."),
        new("business_objectives",        "Business Objectives",               4, "Describe the measurable business outcomes this API service aims to achieve."),
        new("api_design_philosophy",      "API Design Philosophy & Standards",  5, "Define REST/GraphQL/gRPC choice, versioning strategy, naming conventions, and API design guidelines."),
        new("endpoint_catalog",           "Endpoint Catalog",                   6, "List all endpoints/operations with HTTP methods, paths, request/response schemas, status codes, and example payloads."),
        new("data_models",               "Data Models & Schemas",              7, "Define request/response DTOs, entity models, JSON schemas, and field-level validation rules."),
        new("authentication_authorization","Authentication & Authorization",    8, "Specify OAuth2/OIDC flows, API key management, JWT claims, RBAC/ABAC policies, and scopes."),
        new("rate_limiting_throttling",   "Rate Limiting & Throttling",         9, "Define rate limits per tier, burst allowances, quota management, and 429 response handling."),
        new("error_handling",             "Error Handling & Response Codes",   10, "Define error response format, error codes catalog, retry guidance, and problem detail (RFC 7807)."),
        new("pagination_filtering",       "Pagination, Filtering & Sorting",  11, "Specify pagination strategy, filter query syntax, sort parameters, and cursor-based pagination."),
        new("webhooks_events",            "Webhooks & Event Notifications",   12, "Define webhook events, payload formats, retry policies, HMAC signatures, and subscription management."),
        new("sdk_client_generation",      "SDK & Client Generation",          13, "Specify OpenAPI spec generation, SDK targets, auto-generated clients, and developer portal."),
        new("functional_requirements",    "Functional Requirements",          14, "Detail all functional requirements with testable acceptance criteria."),
        new("non_functional_requirements","Non-Functional Requirements",      15, "Specify: latency targets (p50/p95/p99), throughput (RPS), availability (99.9%+), and SLAs."),
        new("security_requirements",      "Security Requirements",            16, "Define OWASP API Top 10 mitigations, input validation, CORS policy, TLS requirements."),
        new("monitoring_observability",   "Monitoring & Observability",       17, "Specify logging, metrics, distributed tracing, health checks, and alerting."),
        new("data_requirements",          "Data Requirements",                18, "Describe data stores, caching strategy, data retention, and GDPR considerations."),
        new("migration_versioning",       "Migration & Versioning Strategy",  19, "Define backward compatibility rules, deprecation timeline, and migration guides."),
        new("assumptions_dependencies",   "Assumptions & Dependencies",       20, "List assumptions and external dependencies."),
        new("risks_mitigations",          "Risks & Mitigations",             21, "Identify risks, their impact, likelihood, and mitigation strategies."),
        new("timeline_milestones",        "Timeline & Milestones",           22, "Outline the project timeline, key milestones, and delivery dates."),
        new("success_criteria",           "Success Criteria & KPIs",         23, "Define: API adoption, error rates, latency, uptime, developer satisfaction."),
        new("glossary",                   "Glossary",                        24, "Define domain-specific terms and abbreviations used in this document."),
    ];

    // ══════════════════════════════════════════════════════════════
    // Data Pipeline BRD
    // ══════════════════════════════════════════════════════════════
    public static readonly IReadOnlyList<SectionDef> DataPipelineSections =
    [
        new("executive_summary",          "Executive Summary",                  1, "Provide a concise overview of the data pipeline, its goals, and expected outcomes."),
        new("project_scope",              "Project Scope",                      2, "Define what is in-scope and out-of-scope for this data pipeline."),
        new("stakeholders",               "Stakeholders & Roles",              3, "List all stakeholders (data engineers, analysts, consumers) and their roles."),
        new("business_objectives",        "Business Objectives",               4, "Describe the measurable business outcomes this data pipeline aims to achieve."),
        new("data_sources",              "Data Sources & Ingestion",            5, "Define all data sources, connection methods, ingestion frequency, and schema expectations."),
        new("etl_elt_flows",             "ETL/ELT Flow Design",                6, "Describe extraction, transformation, and loading steps. Include flow diagrams."),
        new("data_models",               "Data Models & Schema Design",        7, "Define source schemas, staging schemas, dimensional models (star/snowflake), and schema evolution strategy."),
        new("data_quality",              "Data Quality & Validation",           8, "Specify data quality rules, null handling, deduplication, anomaly detection, and data profiling."),
        new("data_lineage",              "Data Lineage & Traceability",         9, "Define end-to-end data lineage tracking, impact analysis, and metadata management."),
        new("scheduling_orchestration",  "Scheduling & Orchestration",         10, "Specify pipeline scheduling, DAG design, dependency management, retries, and backfill strategy."),
        new("monitoring_alerting",       "Monitoring & Alerting",              11, "Define pipeline health metrics, SLA monitoring, failure alerting, and data freshness tracking."),
        new("functional_requirements",   "Functional Requirements",            12, "Detail all functional requirements with testable acceptance criteria."),
        new("non_functional_requirements","Non-Functional Requirements",       13, "Specify: throughput, latency, data freshness SLAs, storage limits, and compute budgets."),
        new("security_requirements",     "Security Requirements",              14, "Define data encryption in transit/at rest, PII handling, masking, RBAC, and audit logging."),
        new("data_retention",            "Data Retention & Archival",          15, "Specify retention policies, archival strategy, purging rules, and regulatory compliance."),
        new("disaster_recovery",         "Disaster Recovery & Replay",         16, "Define backup strategy, replay capability, point-in-time recovery, and RPO/RTO targets."),
        new("assumptions_dependencies",  "Assumptions & Dependencies",         17, "List assumptions and external dependencies."),
        new("risks_mitigations",         "Risks & Mitigations",               18, "Identify risks, their impact, likelihood, and mitigation strategies."),
        new("timeline_milestones",       "Timeline & Milestones",             19, "Outline the project timeline, key milestones, and delivery dates."),
        new("success_criteria",          "Success Criteria & KPIs",           20, "Define: pipeline uptime, data freshness, error rates, throughput, cost per GB."),
        new("glossary",                  "Glossary",                          21, "Define domain-specific terms and abbreviations used in this document."),
    ];

    // ══════════════════════════════════════════════════════════════
    // Integration BRD
    // ══════════════════════════════════════════════════════════════
    public static readonly IReadOnlyList<SectionDef> IntegrationSections =
    [
        new("executive_summary",          "Executive Summary",                  1, "Provide a concise overview of the integration, its goals, and expected outcomes."),
        new("project_scope",              "Project Scope",                      2, "Define what is in-scope and out-of-scope for this integration."),
        new("stakeholders",               "Stakeholders & Roles",              3, "List all stakeholders and their roles."),
        new("business_objectives",        "Business Objectives",               4, "Describe the measurable business outcomes this integration aims to achieve."),
        new("system_landscape",           "System Landscape & Context",         5, "Map all systems involved, their roles, and interaction patterns (sync/async, push/pull)."),
        new("integration_patterns",       "Integration Patterns",               6, "Define patterns used: request-reply, pub-sub, event streaming, batch file transfer, shared database, API gateway."),
        new("message_formats",            "Message Formats & Contracts",        7, "Specify message schemas, payload examples, versioning, and contract testing approach."),
        new("data_mapping",              "Data Mapping & Transformation",       8, "Define field-level mapping rules, transformation logic, and canonical data model."),
        new("error_handling",             "Error Handling & Retry Logic",       9, "Define dead-letter queues, retry policies, circuit breakers, and compensating transactions."),
        new("idempotency_ordering",       "Idempotency & Message Ordering",   10, "Specify idempotency keys, exactly-once semantics, and message ordering guarantees."),
        new("monitoring_observability",   "Monitoring & Observability",        11, "Define distributed tracing, correlation IDs, health endpoints, and alerting."),
        new("functional_requirements",    "Functional Requirements",           12, "Detail all functional requirements with testable acceptance criteria."),
        new("non_functional_requirements","Non-Functional Requirements",       13, "Specify: latency, throughput, message delivery SLAs, and availability targets."),
        new("security_requirements",      "Security Requirements",             14, "Define mTLS, API keys, OAuth scopes, IP whitelisting, and data encryption."),
        new("testing_strategy",           "Integration Testing Strategy",      15, "Define: contract tests, integration tests, chaos testing, and test environments."),
        new("assumptions_dependencies",   "Assumptions & Dependencies",        16, "List assumptions and external dependencies."),
        new("risks_mitigations",          "Risks & Mitigations",              17, "Identify risks, their impact, likelihood, and mitigation strategies."),
        new("timeline_milestones",        "Timeline & Milestones",            18, "Outline the project timeline, key milestones, and delivery dates."),
        new("success_criteria",           "Success Criteria & KPIs",          19, "Define: integration uptime, message delivery rate, latency, error rates."),
        new("glossary",                   "Glossary",                         20, "Define domain-specific terms and abbreviations used in this document."),
    ];

    /// <summary>All valid BRD type codes.</summary>
    public static readonly IReadOnlyList<string> ValidTypes =
        ["general", "web_application", "mobile_application", "api_service", "data_pipeline", "integration"];

    public static bool IsValid(string brdType) => ValidTypes.Contains(brdType);

    public static string DisplayName(string brdType) => brdType switch
    {
        "general"            => "General",
        "web_application"    => "Web Application",
        "mobile_application" => "Mobile Application",
        "api_service"        => "REST / GraphQL API",
        "data_pipeline"      => "Data Pipeline",
        "integration"        => "Integration",
        _                    => brdType,
    };
}
