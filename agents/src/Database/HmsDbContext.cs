using Microsoft.EntityFrameworkCore;
using Hms.Database.Entities.Mpi;
using Hms.Database.Entities.Clinical;
using Hms.Database.Entities.Inpatient;
using Hms.Database.Entities.Emergency;
using Hms.Database.Entities.Diagnostics;
using Hms.Database.Entities.Revenue;
using Hms.Database.Entities.Governance;
using Hms.Database.Entities.Ai;
using Hms.Database.Entities.Platform.Technology;
using Hms.Database.Entities.Platform.AgentRegistry;
using Hms.Database.Entities.Platform.Standards;
using Hms.Database.Entities.Platform.LlmConfig;
using Hms.Database.Entities.Platform.Workflows;
using Hms.Database.Entities.Platform.Projects;

namespace Hms.Database;

public class HmsDbContext : DbContext
{
    private readonly string _tenantId;

    /// <summary>Exposes the tenant ID for repositories that need to auto-stamp entities.</summary>
    public string CurrentTenantId => _tenantId;

    public HmsDbContext(DbContextOptions<HmsDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantId = tenantProvider.TenantId;
    }

    // MPI
    public DbSet<PatientProfile> PatientProfiles => Set<PatientProfile>();
    public DbSet<PatientIdentifier> PatientIdentifiers => Set<PatientIdentifier>();

    // Clinical
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();

    // Inpatient
    public DbSet<Admission> Admissions => Set<Admission>();
    public DbSet<AdmissionEligibility> AdmissionEligibilities => Set<AdmissionEligibility>();

    // Emergency
    public DbSet<EmergencyArrival> EmergencyArrivals => Set<EmergencyArrival>();
    public DbSet<TriageAssessment> TriageAssessments => Set<TriageAssessment>();

    // Diagnostics
    public DbSet<ResultRecord> ResultRecords => Set<ResultRecord>();

    // Revenue
    public DbSet<Claim> Claims => Set<Claim>();

    // Governance
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // AI
    public DbSet<AiInteraction> AiInteractions => Set<AiInteraction>();

    // ── Platform: Technology Registry ──────────────────────────
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<Framework> Frameworks => Set<Framework>();
    public DbSet<DatabaseTechnology> DatabaseTechnologies => Set<DatabaseTechnology>();
    public DbSet<CloudProvider> CloudProviders => Set<CloudProvider>();
    public DbSet<DevOpsTool> DevOpsTools => Set<DevOpsTool>();
    public DbSet<PackageRegistry> PackageRegistries => Set<PackageRegistry>();
    public DbSet<ApiProtocol> ApiProtocols => Set<ApiProtocol>();

    // ── Platform: Agent Registry ───────────────────────────────
    public DbSet<AgentTypeDefinition> AgentTypeDefinitions => Set<AgentTypeDefinition>();
    public DbSet<AgentModelMapping> AgentModelMappings => Set<AgentModelMapping>();
    public DbSet<AgentToolDefinition> AgentToolDefinitions => Set<AgentToolDefinition>();
    public DbSet<AgentPromptTemplate> AgentPromptTemplates => Set<AgentPromptTemplate>();
    public DbSet<AgentConstraint> AgentConstraints => Set<AgentConstraint>();

    // ── Platform: Template Library ─────────────────────────────
    public DbSet<BrdTemplate> BrdTemplates => Set<BrdTemplate>();
    public DbSet<ArchitectureTemplate> ArchitectureTemplates => Set<ArchitectureTemplate>();
    public DbSet<CodeTemplate> CodeTemplates => Set<CodeTemplate>();
    public DbSet<FileStructureTemplate> FileStructureTemplates => Set<FileStructureTemplate>();
    public DbSet<CiCdTemplate> CiCdTemplates => Set<CiCdTemplate>();
    public DbSet<DockerTemplate> DockerTemplates => Set<DockerTemplate>();
    public DbSet<TestTemplate> TestTemplates => Set<TestTemplate>();
    public DbSet<IaCTemplate> IaCTemplates => Set<IaCTemplate>();
    public DbSet<DocumentationTemplate> DocumentationTemplates => Set<DocumentationTemplate>();

    // ── Platform: Standards ────────────────────────────────────
    public DbSet<CodingStandard> CodingStandards => Set<CodingStandard>();
    public DbSet<NamingConvention> NamingConventions => Set<NamingConvention>();
    public DbSet<SecurityPolicy> SecurityPolicies => Set<SecurityPolicy>();
    public DbSet<ReviewChecklist> ReviewChecklists => Set<ReviewChecklist>();
    public DbSet<QualityGate> QualityGates => Set<QualityGate>();

    // ── Platform: LLM Config ──────────────────────────────────
    public DbSet<LlmProviderConfig> LlmProviderConfigs => Set<LlmProviderConfig>();
    public DbSet<LlmModelConfig> LlmModelConfigs => Set<LlmModelConfig>();
    public DbSet<LlmRoutingRule> LlmRoutingRules => Set<LlmRoutingRule>();
    public DbSet<TokenBudget> TokenBudgets => Set<TokenBudget>();

    // ── Platform: Configuration ───────────────────────────────
    public DbSet<ConfigSnapshot> ConfigSnapshots => Set<ConfigSnapshot>();
    public DbSet<CompatibilityRule> CompatibilityRules => Set<CompatibilityRule>();
    public DbSet<StarterKit> StarterKits => Set<StarterKit>();
    public DbSet<TemplateVariable> TemplateVariables => Set<TemplateVariable>();
    public DbSet<AgentPluginManifest> AgentPluginManifests => Set<AgentPluginManifest>();

    // ── Platform: Workflows ───────────────────────────────────
    public DbSet<SdlcWorkflow> SdlcWorkflows => Set<SdlcWorkflow>();
    public DbSet<StageDefinition> StageDefinitions => Set<StageDefinition>();
    public DbSet<ApprovalGateConfig> ApprovalGateConfigs => Set<ApprovalGateConfig>();
    public DbSet<TransitionRule> TransitionRules => Set<TransitionRule>();

    // ── Platform: Projects ────────────────────────────────────
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectSettings> ProjectSettings => Set<ProjectSettings>();
    public DbSet<ProjectTeamMember> ProjectTeamMembers => Set<ProjectTeamMember>();
    public DbSet<ProjectTechStack> ProjectTechStacks => Set<ProjectTechStack>();
    public DbSet<ProjectDependency> ProjectDependencies => Set<ProjectDependency>();
    public DbSet<ProjectIntegration> ProjectIntegrations => Set<ProjectIntegration>();
    public DbSet<EnvironmentConfig> EnvironmentConfigs => Set<EnvironmentConfig>();
    public DbSet<ProjectArchitecture> ProjectArchitectures => Set<ProjectArchitecture>();
    public DbSet<ModuleDefinition> ModuleDefinitions => Set<ModuleDefinition>();
    public DbSet<ApiContract> ApiContracts => Set<ApiContract>();
    public DbSet<DataModelDefinition> DataModelDefinitions => Set<DataModelDefinition>();
    public DbSet<ArchitectureDecisionRecord> ArchitectureDecisionRecords => Set<ArchitectureDecisionRecord>();
    public DbSet<RawRequirement> RawRequirements => Set<RawRequirement>();
    public DbSet<EnrichedRequirement> EnrichedRequirements => Set<EnrichedRequirement>();
    public DbSet<BrdSectionRecord> BrdSectionRecords => Set<BrdSectionRecord>();
    public DbSet<BrdFeedbackRecord> BrdFeedbackRecords => Set<BrdFeedbackRecord>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<AgentAssignment> AgentAssignments => Set<AgentAssignment>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentArtifactRecord> AgentArtifactRecords => Set<AgentArtifactRecord>();
    public DbSet<AgentConversation> AgentConversations => Set<AgentConversation>();
    public DbSet<ReviewResult> ReviewResults => Set<ReviewResult>();
    public DbSet<QualityReport> QualityReports => Set<QualityReport>();
    public DbSet<TraceabilityRecord> TraceabilityRecords => Set<TraceabilityRecord>();
    public DbSet<ProjectMetric> ProjectMetrics => Set<ProjectMetric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema separation by bounded context
        modelBuilder.Entity<PatientProfile>().ToTable("patient_profile", "cl_mpi");
        modelBuilder.Entity<PatientIdentifier>().ToTable("patient_identifier", "cl_mpi");
        modelBuilder.Entity<Encounter>().ToTable("encounter", "cl_encounter");
        modelBuilder.Entity<ClinicalNote>().ToTable("clinical_note", "cl_encounter");
        modelBuilder.Entity<Admission>().ToTable("admission", "cl_inpatient");
        modelBuilder.Entity<AdmissionEligibility>().ToTable("admission_eligibility", "cl_inpatient");
        modelBuilder.Entity<EmergencyArrival>().ToTable("emergency_arrival", "cl_emergency");
        modelBuilder.Entity<TriageAssessment>().ToTable("triage_assessment", "cl_emergency");
        modelBuilder.Entity<ResultRecord>().ToTable("result_record", "cl_diagnostics");
        modelBuilder.Entity<Claim>().ToTable("claim", "op_revenue");
        modelBuilder.Entity<AuditEvent>().ToTable("audit_event", "gov_audit");
        modelBuilder.Entity<AiInteraction>().ToTable("ai_interaction", "gov_ai");

        // ── Platform: Technology Registry → plt_meta ──────────
        modelBuilder.Entity<Language>().ToTable("language", "plt_meta");
        modelBuilder.Entity<Framework>().ToTable("framework", "plt_meta");
        modelBuilder.Entity<DatabaseTechnology>().ToTable("database_technology", "plt_meta");
        modelBuilder.Entity<CloudProvider>().ToTable("cloud_provider", "plt_meta");
        modelBuilder.Entity<DevOpsTool>().ToTable("devops_tool", "plt_meta");
        modelBuilder.Entity<PackageRegistry>().ToTable("package_registry", "plt_meta");
        modelBuilder.Entity<ApiProtocol>().ToTable("api_protocol", "plt_meta");

        // ── Platform: Agent Registry → plt_meta ───────────────
        modelBuilder.Entity<AgentTypeDefinition>().ToTable("agent_type_definition", "plt_meta");
        modelBuilder.Entity<AgentModelMapping>().ToTable("agent_model_mapping", "plt_meta");
        modelBuilder.Entity<AgentToolDefinition>().ToTable("agent_tool_definition", "plt_meta");
        modelBuilder.Entity<AgentPromptTemplate>().ToTable("agent_prompt_template", "plt_meta");
        modelBuilder.Entity<AgentConstraint>().ToTable("agent_constraint", "plt_meta");

        // ── Platform: Template Library → plt_meta ─────────────
        modelBuilder.Entity<BrdTemplate>().ToTable("brd_template", "plt_meta");
        modelBuilder.Entity<ArchitectureTemplate>().ToTable("architecture_template", "plt_meta");
        modelBuilder.Entity<CodeTemplate>().ToTable("code_template", "plt_meta");
        modelBuilder.Entity<FileStructureTemplate>().ToTable("file_structure_template", "plt_meta");
        modelBuilder.Entity<CiCdTemplate>().ToTable("cicd_template", "plt_meta");
        modelBuilder.Entity<DockerTemplate>().ToTable("docker_template", "plt_meta");
        modelBuilder.Entity<TestTemplate>().ToTable("test_template", "plt_meta");
        modelBuilder.Entity<IaCTemplate>().ToTable("iac_template", "plt_meta");
        modelBuilder.Entity<DocumentationTemplate>().ToTable("documentation_template", "plt_meta");

        // ── Platform: Standards → plt_meta ────────────────────
        modelBuilder.Entity<CodingStandard>().ToTable("coding_standard", "plt_meta");
        modelBuilder.Entity<NamingConvention>().ToTable("naming_convention", "plt_meta");
        modelBuilder.Entity<SecurityPolicy>().ToTable("security_policy", "plt_meta");
        modelBuilder.Entity<ReviewChecklist>().ToTable("review_checklist", "plt_meta");
        modelBuilder.Entity<QualityGate>().ToTable("quality_gate", "plt_meta");

        // ── Platform: LLM Config → plt_meta ──────────────────
        modelBuilder.Entity<LlmProviderConfig>().ToTable("llm_provider_config", "plt_meta");
        modelBuilder.Entity<LlmModelConfig>().ToTable("llm_model_config", "plt_meta");
        modelBuilder.Entity<LlmRoutingRule>().ToTable("llm_routing_rule", "plt_meta");
        modelBuilder.Entity<TokenBudget>().ToTable("token_budget", "plt_meta");

        // ── Platform: Configuration → plt_meta ────────────────
        modelBuilder.Entity<ConfigSnapshot>().ToTable("config_snapshot", "plt_meta");
        modelBuilder.Entity<CompatibilityRule>().ToTable("compatibility_rule", "plt_meta");
        modelBuilder.Entity<StarterKit>().ToTable("starter_kit", "plt_meta");
        modelBuilder.Entity<TemplateVariable>().ToTable("template_variable", "plt_meta");
        modelBuilder.Entity<AgentPluginManifest>().ToTable("agent_plugin_manifest", "plt_meta");

        // ── Platform: Workflows → plt_meta ────────────────────
        modelBuilder.Entity<SdlcWorkflow>().ToTable("sdlc_workflow", "plt_meta");
        modelBuilder.Entity<StageDefinition>().ToTable("stage_definition", "plt_meta");
        modelBuilder.Entity<ApprovalGateConfig>().ToTable("approval_gate_config", "plt_meta");
        modelBuilder.Entity<TransitionRule>().ToTable("transition_rule", "plt_meta");

        // ── Platform: Projects → plt_project ──────────────────
        modelBuilder.Entity<Project>().ToTable("project", "plt_project");
        modelBuilder.Entity<ProjectSettings>().ToTable("project_settings", "plt_project");
        modelBuilder.Entity<ProjectTeamMember>().ToTable("project_team_member", "plt_project");
        modelBuilder.Entity<ProjectTechStack>().ToTable("project_tech_stack", "plt_project");
        modelBuilder.Entity<ProjectDependency>().ToTable("project_dependency", "plt_project");
        modelBuilder.Entity<ProjectIntegration>().ToTable("project_integration", "plt_project");
        modelBuilder.Entity<EnvironmentConfig>().ToTable("environment_config", "plt_project");
        modelBuilder.Entity<ProjectArchitecture>().ToTable("project_architecture", "plt_project");
        modelBuilder.Entity<ModuleDefinition>().ToTable("module_definition", "plt_project");
        modelBuilder.Entity<ApiContract>().ToTable("api_contract", "plt_project");
        modelBuilder.Entity<DataModelDefinition>().ToTable("data_model_definition", "plt_project");
        modelBuilder.Entity<ArchitectureDecisionRecord>().ToTable("architecture_decision_record", "plt_project");
        modelBuilder.Entity<RawRequirement>().ToTable("raw_requirement", "plt_project");
        modelBuilder.Entity<EnrichedRequirement>().ToTable("enriched_requirement", "plt_project");
        modelBuilder.Entity<BrdSectionRecord>().ToTable("brd_section_record", "plt_project");
        modelBuilder.Entity<BrdFeedbackRecord>().ToTable("brd_feedback_record", "plt_project");
        modelBuilder.Entity<Epic>().ToTable("epic", "plt_project");
        modelBuilder.Entity<Story>().ToTable("story", "plt_project");
        modelBuilder.Entity<TaskItem>().ToTable("task_item", "plt_project");
        modelBuilder.Entity<Sprint>().ToTable("sprint", "plt_project");
        modelBuilder.Entity<TaskDependency>().ToTable("task_dependency", "plt_project");

        // ── Platform: Execution → plt_audit ───────────────────
        modelBuilder.Entity<AgentAssignment>().ToTable("agent_assignment", "plt_audit");
        modelBuilder.Entity<AgentRun>().ToTable("agent_run", "plt_audit");
        modelBuilder.Entity<AgentArtifactRecord>().ToTable("agent_artifact_record", "plt_audit");
        modelBuilder.Entity<AgentConversation>().ToTable("agent_conversation", "plt_audit");
        modelBuilder.Entity<ReviewResult>().ToTable("review_result", "plt_audit");

        // ── Platform: Metrics → plt_audit ─────────────────────
        modelBuilder.Entity<QualityReport>().ToTable("quality_report", "plt_audit");
        modelBuilder.Entity<TraceabilityRecord>().ToTable("traceability_record", "plt_audit");
        modelBuilder.Entity<ProjectMetric>().ToTable("project_metric", "plt_audit");

        // Tenant-scoped global query filters
        modelBuilder.Entity<PatientProfile>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Encounter>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Admission>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<EmergencyArrival>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<ResultRecord>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Claim>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<AuditEvent>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<AiInteraction>().HasQueryFilter(e => e.TenantId == _tenantId);

        // Platform tenant-scoped query filters
        modelBuilder.Entity<Project>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<AgentTypeDefinition>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Language>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<SdlcWorkflow>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<LlmProviderConfig>().HasQueryFilter(e => e.TenantId == _tenantId);

        // ── Relationship configurations ───────────────────────
        // TaskDependency: two FKs to TaskItem
        modelBuilder.Entity<TaskDependency>()
            .HasOne(d => d.Task)
            .WithMany(t => t.DependenciesFrom)
            .HasForeignKey(d => d.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaskDependency>()
            .HasOne(d => d.DependsOnTask)
            .WithMany(t => t.DependenciesTo)
            .HasForeignKey(d => d.DependsOnTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        // TransitionRule: two FKs to StageDefinition
        modelBuilder.Entity<TransitionRule>()
            .HasOne(t => t.FromStage)
            .WithMany(s => s.TransitionsFrom)
            .HasForeignKey(t => t.FromStageId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TransitionRule>()
            .HasOne(t => t.ToStage)
            .WithMany(s => s.TransitionsTo)
            .HasForeignKey(t => t.ToStageId)
            .OnDelete(DeleteBehavior.Restrict);

        // Project → Slug unique per tenant
        modelBuilder.Entity<Project>()
            .HasIndex(e => new { e.TenantId, e.Slug }).IsUnique();

        // Unique constraints include tenant_id
        modelBuilder.Entity<PatientProfile>()
            .HasIndex(e => new { e.TenantId, e.EnterprisePersonKey }).IsUnique();
        modelBuilder.Entity<PatientIdentifier>()
            .HasIndex(e => new { e.TenantId, e.IdentifierType, e.IdentifierValueHash }).IsUnique();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<PlatformEntityBase>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = _tenantId;
        }
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public interface ITenantProvider
{
    string TenantId { get; }
}