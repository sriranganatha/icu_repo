using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GNex.Database.Migrations
{
    /// <inheritdoc />
    public partial class PlatformSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "plt_audit");

            migrationBuilder.EnsureSchema(
                name: "plt_meta");

            migrationBuilder.EnsureSchema(
                name: "plt_project");

            migrationBuilder.CreateTable(
                name: "agent_plugin_manifest",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentTypeCode = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    ToolsJson = table.Column<string>(type: "text", nullable: false),
                    ConstraintsJson = table.Column<string>(type: "text", nullable: false),
                    InputSchemaJson = table.Column<string>(type: "text", nullable: false),
                    OutputSchemaJson = table.Column<string>(type: "text", nullable: false),
                    FallbackAgentTypeCode = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_plugin_manifest", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_type_definition",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "text", nullable: false),
                    InputSchemaJson = table.Column<string>(type: "text", nullable: true),
                    OutputSchemaJson = table.Column<string>(type: "text", nullable: true),
                    DefaultModelId = table.Column<string>(type: "text", nullable: true),
                    AgentTypeCode = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_type_definition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "api_protocol",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SpecFormat = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_protocol", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "architecture_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Pattern = table.Column<string>(type: "text", nullable: false),
                    DiagramTemplate = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architecture_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "brd_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ProjectType = table.Column<string>(type: "text", nullable: false),
                    SectionsJson = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brd_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cloud_provider",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RegionsJson = table.Column<string>(type: "text", nullable: false),
                    ServicesJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cloud_provider", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "compatibility_rule",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    SourceTechnologyId = table.Column<string>(type: "text", nullable: false),
                    SourceTechnologyCategory = table.Column<string>(type: "text", nullable: false),
                    TargetTechnologyId = table.Column<string>(type: "text", nullable: false),
                    TargetTechnologyCategory = table.Column<string>(type: "text", nullable: false),
                    Compatibility = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    VersionConstraint = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compatibility_rule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "config_snapshot",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: true),
                    SnapshotType = table.Column<string>(type: "text", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    TriggerReason = table.Column<string>(type: "text", nullable: true),
                    AgentRunId = table.Column<string>(type: "text", nullable: true),
                    PreviousSnapshotId = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_snapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "database_technology",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DbType = table.Column<string>(type: "text", nullable: false),
                    DefaultPort = table.Column<int>(type: "integer", nullable: false),
                    ConnectionTemplate = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_database_technology", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "devops_tool",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    ConfigTemplate = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devops_tool", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documentation_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DocType = table.Column<string>(type: "text", nullable: false),
                    TemplateContent = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documentation_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "language",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    FileExtensionsJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_language", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "llm_provider_config",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "text", nullable: false),
                    AuthType = table.Column<string>(type: "text", nullable: false),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_provider_config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "llm_routing_rule",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TaskType = table.Column<string>(type: "text", nullable: false),
                    PrimaryModelId = table.Column<string>(type: "text", nullable: false),
                    FallbackModelId = table.Column<string>(type: "text", nullable: true),
                    ConditionsJson = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_routing_rule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "naming_convention",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Pattern = table.Column<string>(type: "text", nullable: false),
                    ExamplesJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_naming_convention", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "project",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ProjectType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quality_gate",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    GateType = table.Column<string>(type: "text", nullable: false),
                    ThresholdConfigJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_gate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "review_checklist",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    ChecklistItemsJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_checklist", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sdlc_workflow",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sdlc_workflow", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "security_policy",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    RulesJson = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_policy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "starter_kit",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    TechStackJson = table.Column<string>(type: "text", nullable: false),
                    ArchitecturePattern = table.Column<string>(type: "text", nullable: false),
                    WorkflowId = table.Column<string>(type: "text", nullable: true),
                    TemplatesJson = table.Column<string>(type: "text", nullable: false),
                    PreviewImageUrl = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_starter_kit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "template_variable",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ExampleValue = table.Column<string>(type: "text", nullable: true),
                    ResolverType = table.Column<string>(type: "text", nullable: false),
                    ResolverExpression = table.Column<string>(type: "text", nullable: true),
                    DataType = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_variable", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "token_budget",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    BudgetTokens = table.Column<long>(type: "bigint", nullable: false),
                    AlertThreshold = table.Column<double>(type: "double precision", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_budget", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "agent_constraint",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentTypeDefinitionId = table.Column<string>(type: "text", nullable: false),
                    MaxTokens = table.Column<int>(type: "integer", nullable: false),
                    MaxRetries = table.Column<int>(type: "integer", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    SandboxConfigJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_constraint", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_constraint_agent_type_definition_AgentTypeDefinitionId",
                        column: x => x.AgentTypeDefinitionId,
                        principalSchema: "plt_meta",
                        principalTable: "agent_type_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_model_mapping",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentTypeDefinitionId = table.Column<string>(type: "text", nullable: false),
                    LlmProvider = table.Column<string>(type: "text", nullable: false),
                    ModelId = table.Column<string>(type: "text", nullable: false),
                    TokenLimit = table.Column<int>(type: "integer", nullable: false),
                    CostPer1kTokens = table.Column<decimal>(type: "numeric", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_model_mapping", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_model_mapping_agent_type_definition_AgentTypeDefiniti~",
                        column: x => x.AgentTypeDefinitionId,
                        principalSchema: "plt_meta",
                        principalTable: "agent_type_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_prompt_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentTypeDefinitionId = table.Column<string>(type: "text", nullable: false),
                    PromptType = table.Column<string>(type: "text", nullable: false),
                    PromptTemplateText = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_prompt_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_prompt_template_agent_type_definition_AgentTypeDefini~",
                        column: x => x.AgentTypeDefinitionId,
                        principalSchema: "plt_meta",
                        principalTable: "agent_type_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_tool_definition",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentTypeDefinitionId = table.Column<string>(type: "text", nullable: false),
                    ToolName = table.Column<string>(type: "text", nullable: false),
                    ToolConfigJson = table.Column<string>(type: "text", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_tool_definition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_tool_definition_agent_type_definition_AgentTypeDefini~",
                        column: x => x.AgentTypeDefinitionId,
                        principalSchema: "plt_meta",
                        principalTable: "agent_type_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "iac_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CloudProviderId = table.Column<string>(type: "text", nullable: true),
                    Tool = table.Column<string>(type: "text", nullable: false),
                    TemplateContent = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_iac_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_iac_template_cloud_provider_CloudProviderId",
                        column: x => x.CloudProviderId,
                        principalSchema: "plt_meta",
                        principalTable: "cloud_provider",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "cicd_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    LanguageId = table.Column<string>(type: "text", nullable: true),
                    PipelineYaml = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cicd_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cicd_template_language_LanguageId",
                        column: x => x.LanguageId,
                        principalSchema: "plt_meta",
                        principalTable: "language",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "coding_standard",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LanguageId = table.Column<string>(type: "text", nullable: true),
                    RulesJson = table.Column<string>(type: "text", nullable: false),
                    LinterConfig = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_standard", x => x.Id);
                    table.ForeignKey(
                        name: "FK_coding_standard_language_LanguageId",
                        column: x => x.LanguageId,
                        principalSchema: "plt_meta",
                        principalTable: "language",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "framework",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LanguageId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    DocsUrl = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_framework", x => x.Id);
                    table.ForeignKey(
                        name: "FK_framework_language_LanguageId",
                        column: x => x.LanguageId,
                        principalSchema: "plt_meta",
                        principalTable: "language",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_registry",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LanguageId = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    AuthType = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_registry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_package_registry_language_LanguageId",
                        column: x => x.LanguageId,
                        principalSchema: "plt_meta",
                        principalTable: "language",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "llm_model_config",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: false),
                    ModelName = table.Column<string>(type: "text", nullable: false),
                    ContextWindow = table.Column<int>(type: "integer", nullable: false),
                    CostInputPer1kTokens = table.Column<decimal>(type: "numeric", nullable: false),
                    CostOutputPer1kTokens = table.Column<decimal>(type: "numeric", nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_model_config", x => x.Id);
                    table.ForeignKey(
                        name: "FK_llm_model_config_llm_provider_config_ProviderId",
                        column: x => x.ProviderId,
                        principalSchema: "plt_meta",
                        principalTable: "llm_provider_config",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "architecture_decision_record",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Context = table.Column<string>(type: "text", nullable: false),
                    Decision = table.Column<string>(type: "text", nullable: false),
                    Consequences = table.Column<string>(type: "text", nullable: true),
                    AdrStatus = table.Column<string>(type: "text", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architecture_decision_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_architecture_decision_record_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "brd_section_record",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BrdId = table.Column<string>(type: "text", nullable: false),
                    SectionType = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    DiagramsJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brd_section_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brd_section_record_project_BrdId",
                        column: x => x.BrdId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_model_definition",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    EntityName = table.Column<string>(type: "text", nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    RelationshipsJson = table.Column<string>(type: "text", nullable: true),
                    IndexesJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_model_definition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_data_model_definition_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "environment_config",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    EnvName = table.Column<string>(type: "text", nullable: false),
                    VariablesJson = table.Column<string>(type: "text", nullable: false),
                    InfraConfigJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_environment_config", x => x.Id);
                    table.ForeignKey(
                        name: "FK_environment_config_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_definition",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Responsibilities = table.Column<string>(type: "text", nullable: true),
                    DependenciesJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_definition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_module_definition_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_architecture",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    PatternId = table.Column<string>(type: "text", nullable: false),
                    CustomizationsJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_architecture", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_architecture_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_dependency",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    PackageName = table.Column<string>(type: "text", nullable: false),
                    VersionConstraint = table.Column<string>(type: "text", nullable: true),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_dependency", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_dependency_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_integration",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    IntegrationType = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_integration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_integration_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_metric",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    MetricType = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    DimensionsJson = table.Column<string>(type: "text", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_metric", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_metric_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_settings",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    GitRepoUrl = table.Column<string>(type: "text", nullable: true),
                    DefaultBranch = table.Column<string>(type: "text", nullable: false),
                    ArtifactStoragePath = table.Column<string>(type: "text", nullable: true),
                    NotificationConfigJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_settings_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_team_member",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_team_member", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_team_member_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_tech_stack",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    Layer = table.Column<string>(type: "text", nullable: false),
                    TechnologyId = table.Column<string>(type: "text", nullable: false),
                    TechnologyType = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: true),
                    ConfigOverridesJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_tech_stack", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_tech_stack_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_requirement",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    InputText = table.Column<string>(type: "text", nullable: false),
                    InputType = table.Column<string>(type: "text", nullable: false),
                    SubmittedBy = table.Column<string>(type: "text", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_requirement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_raw_requirement_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sprint",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Goal = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sprint", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sprint_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "traceability_record",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    RequirementId = table.Column<string>(type: "text", nullable: true),
                    StoryId = table.Column<string>(type: "text", nullable: true),
                    TaskId = table.Column<string>(type: "text", nullable: true),
                    ArtifactId = table.Column<string>(type: "text", nullable: true),
                    TestId = table.Column<string>(type: "text", nullable: true),
                    LinkType = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_traceability_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_traceability_record_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stage_definition",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkflowId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    EntryCriteria = table.Column<string>(type: "text", nullable: true),
                    ExitCriteria = table.Column<string>(type: "text", nullable: true),
                    AgentsInvolvedJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stage_definition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stage_definition_sdlc_workflow_WorkflowId",
                        column: x => x.WorkflowId,
                        principalSchema: "plt_meta",
                        principalTable: "sdlc_workflow",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "code_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LanguageId = table.Column<string>(type: "text", nullable: true),
                    FrameworkId = table.Column<string>(type: "text", nullable: true),
                    TemplateType = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    VariablesJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_code_template_framework_FrameworkId",
                        column: x => x.FrameworkId,
                        principalSchema: "plt_meta",
                        principalTable: "framework",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_code_template_language_LanguageId",
                        column: x => x.LanguageId,
                        principalSchema: "plt_meta",
                        principalTable: "language",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "docker_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LanguageId = table.Column<string>(type: "text", nullable: true),
                    FrameworkId = table.Column<string>(type: "text", nullable: true),
                    DockerfileContent = table.Column<string>(type: "text", nullable: false),
                    ComposeContent = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_docker_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_docker_template_framework_FrameworkId",
                        column: x => x.FrameworkId,
                        principalSchema: "plt_meta",
                        principalTable: "framework",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_docker_template_language_LanguageId",
                        column: x => x.LanguageId,
                        principalSchema: "plt_meta",
                        principalTable: "language",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "file_structure_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FrameworkId = table.Column<string>(type: "text", nullable: true),
                    TreeJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_structure_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_file_structure_template_framework_FrameworkId",
                        column: x => x.FrameworkId,
                        principalSchema: "plt_meta",
                        principalTable: "framework",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "test_template",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TestType = table.Column<string>(type: "text", nullable: false),
                    FrameworkId = table.Column<string>(type: "text", nullable: true),
                    TestFramework = table.Column<string>(type: "text", nullable: false),
                    TemplateContent = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_test_template_framework_FrameworkId",
                        column: x => x.FrameworkId,
                        principalSchema: "plt_meta",
                        principalTable: "framework",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "brd_feedback_record",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BrdId = table.Column<string>(type: "text", nullable: false),
                    SectionId = table.Column<string>(type: "text", nullable: true),
                    FeedbackText = table.Column<string>(type: "text", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedInVersion = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brd_feedback_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brd_feedback_record_brd_section_record_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "plt_project",
                        principalTable: "brd_section_record",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "epic",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    BrdSectionId = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epic", x => x.Id);
                    table.ForeignKey(
                        name: "FK_epic_brd_section_record_BrdSectionId",
                        column: x => x.BrdSectionId,
                        principalSchema: "plt_project",
                        principalTable: "brd_section_record",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_epic_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_contract",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    ModuleId = table.Column<string>(type: "text", nullable: true),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    Method = table.Column<string>(type: "text", nullable: false),
                    RequestSchemaJson = table.Column<string>(type: "text", nullable: true),
                    ResponseSchemaJson = table.Column<string>(type: "text", nullable: true),
                    AuthRequired = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_contract", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_contract_module_definition_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "plt_project",
                        principalTable: "module_definition",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_api_contract_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "enriched_requirement",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RawRequirementId = table.Column<string>(type: "text", nullable: false),
                    EnrichedJson = table.Column<string>(type: "text", nullable: false),
                    ClarificationQuestionsJson = table.Column<string>(type: "text", nullable: false),
                    UserResponsesJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enriched_requirement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_enriched_requirement_raw_requirement_RawRequirementId",
                        column: x => x.RawRequirementId,
                        principalSchema: "plt_project",
                        principalTable: "raw_requirement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quality_report",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    SprintId = table.Column<string>(type: "text", nullable: true),
                    CoveragePercent = table.Column<decimal>(type: "numeric", nullable: true),
                    LintErrors = table.Column<int>(type: "integer", nullable: true),
                    LintWarnings = table.Column<int>(type: "integer", nullable: true),
                    ComplexityScore = table.Column<decimal>(type: "numeric", nullable: true),
                    SecurityVulnerabilities = table.Column<int>(type: "integer", nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_report", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quality_report_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_quality_report_sprint_SprintId",
                        column: x => x.SprintId,
                        principalSchema: "plt_project",
                        principalTable: "sprint",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "approval_gate_config",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StageId = table.Column<string>(type: "text", nullable: false),
                    GateType = table.Column<string>(type: "text", nullable: false),
                    ApproversConfigJson = table.Column<string>(type: "text", nullable: true),
                    TimeoutHours = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_gate_config", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_gate_config_stage_definition_StageId",
                        column: x => x.StageId,
                        principalSchema: "plt_meta",
                        principalTable: "stage_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transition_rule",
                schema: "plt_meta",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FromStageId = table.Column<string>(type: "text", nullable: false),
                    ToStageId = table.Column<string>(type: "text", nullable: false),
                    ConditionsJson = table.Column<string>(type: "text", nullable: true),
                    AutoTransition = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transition_rule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transition_rule_stage_definition_FromStageId",
                        column: x => x.FromStageId,
                        principalSchema: "plt_meta",
                        principalTable: "stage_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transition_rule_stage_definition_ToStageId",
                        column: x => x.ToStageId,
                        principalSchema: "plt_meta",
                        principalTable: "stage_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    EpicId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    AcceptanceCriteriaJson = table.Column<string>(type: "text", nullable: false),
                    StoryPoints = table.Column<int>(type: "integer", nullable: true),
                    SprintId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_epic_EpicId",
                        column: x => x.EpicId,
                        principalSchema: "plt_project",
                        principalTable: "epic",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_story_sprint_SprintId",
                        column: x => x.SprintId,
                        principalSchema: "plt_project",
                        principalTable: "sprint",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "task_item",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoryId = table.Column<string>(type: "text", nullable: false),
                    TaskType = table.Column<string>(type: "text", nullable: false),
                    AssignedAgentType = table.Column<string>(type: "text", nullable: true),
                    DependsOnJson = table.Column<string>(type: "text", nullable: false),
                    EstimatedTokens = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_item_story_StoryId",
                        column: x => x.StoryId,
                        principalSchema: "plt_project",
                        principalTable: "story",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_assignment",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TaskId = table.Column<string>(type: "text", nullable: false),
                    AgentTypeDefinitionId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_assignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_assignment_project_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "plt_project",
                        principalTable: "project",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_agent_assignment_task_item_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "plt_project",
                        principalTable: "task_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_dependency",
                schema: "plt_project",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TaskId = table.Column<string>(type: "text", nullable: false),
                    DependsOnTaskId = table.Column<string>(type: "text", nullable: false),
                    DependencyType = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_dependency", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_dependency_task_item_DependsOnTaskId",
                        column: x => x.DependsOnTaskId,
                        principalSchema: "plt_project",
                        principalTable: "task_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_task_dependency_task_item_TaskId",
                        column: x => x.TaskId,
                        principalSchema: "plt_project",
                        principalTable: "task_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_run",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AssignmentId = table.Column<string>(type: "text", nullable: false),
                    RunNumber = table.Column<int>(type: "integer", nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: true),
                    OutputJson = table.Column<string>(type: "text", nullable: true),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_run", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_run_agent_assignment_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "plt_audit",
                        principalTable: "agent_assignment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_artifact_record",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RunId = table.Column<string>(type: "text", nullable: false),
                    ArtifactType = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: true),
                    ReviewStatus = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_artifact_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_artifact_record_agent_run_RunId",
                        column: x => x.RunId,
                        principalSchema: "plt_audit",
                        principalTable: "agent_run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_conversation",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RunId = table.Column<string>(type: "text", nullable: false),
                    MessagesJson = table.Column<string>(type: "text", nullable: false),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_conversation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_conversation_agent_run_RunId",
                        column: x => x.RunId,
                        principalSchema: "plt_audit",
                        principalTable: "agent_run",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_result",
                schema: "plt_audit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ArtifactId = table.Column<string>(type: "text", nullable: false),
                    ReviewerAgentType = table.Column<string>(type: "text", nullable: false),
                    Verdict = table.Column<string>(type: "text", nullable: false),
                    CommentsJson = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_result", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_result_agent_artifact_record_ArtifactId",
                        column: x => x.ArtifactId,
                        principalSchema: "plt_audit",
                        principalTable: "agent_artifact_record",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_artifact_record_RunId",
                schema: "plt_audit",
                table: "agent_artifact_record",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_assignment_ProjectId",
                schema: "plt_audit",
                table: "agent_assignment",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_assignment_TaskId",
                schema: "plt_audit",
                table: "agent_assignment",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_constraint_AgentTypeDefinitionId",
                schema: "plt_meta",
                table: "agent_constraint",
                column: "AgentTypeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_conversation_RunId",
                schema: "plt_audit",
                table: "agent_conversation",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_model_mapping_AgentTypeDefinitionId",
                schema: "plt_meta",
                table: "agent_model_mapping",
                column: "AgentTypeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_prompt_template_AgentTypeDefinitionId",
                schema: "plt_meta",
                table: "agent_prompt_template",
                column: "AgentTypeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_run_AssignmentId",
                schema: "plt_audit",
                table: "agent_run",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_tool_definition_AgentTypeDefinitionId",
                schema: "plt_meta",
                table: "agent_tool_definition",
                column: "AgentTypeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_api_contract_ModuleId",
                schema: "plt_project",
                table: "api_contract",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_api_contract_ProjectId",
                schema: "plt_project",
                table: "api_contract",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_approval_gate_config_StageId",
                schema: "plt_meta",
                table: "approval_gate_config",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_architecture_decision_record_ProjectId",
                schema: "plt_project",
                table: "architecture_decision_record",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_brd_feedback_record_SectionId",
                schema: "plt_project",
                table: "brd_feedback_record",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_brd_section_record_BrdId",
                schema: "plt_project",
                table: "brd_section_record",
                column: "BrdId");

            migrationBuilder.CreateIndex(
                name: "IX_cicd_template_LanguageId",
                schema: "plt_meta",
                table: "cicd_template",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_code_template_FrameworkId",
                schema: "plt_meta",
                table: "code_template",
                column: "FrameworkId");

            migrationBuilder.CreateIndex(
                name: "IX_code_template_LanguageId",
                schema: "plt_meta",
                table: "code_template",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_coding_standard_LanguageId",
                schema: "plt_meta",
                table: "coding_standard",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_data_model_definition_ProjectId",
                schema: "plt_project",
                table: "data_model_definition",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_docker_template_FrameworkId",
                schema: "plt_meta",
                table: "docker_template",
                column: "FrameworkId");

            migrationBuilder.CreateIndex(
                name: "IX_docker_template_LanguageId",
                schema: "plt_meta",
                table: "docker_template",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_enriched_requirement_RawRequirementId",
                schema: "plt_project",
                table: "enriched_requirement",
                column: "RawRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_environment_config_ProjectId",
                schema: "plt_project",
                table: "environment_config",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_epic_BrdSectionId",
                schema: "plt_project",
                table: "epic",
                column: "BrdSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_epic_ProjectId",
                schema: "plt_project",
                table: "epic",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_file_structure_template_FrameworkId",
                schema: "plt_meta",
                table: "file_structure_template",
                column: "FrameworkId");

            migrationBuilder.CreateIndex(
                name: "IX_framework_LanguageId",
                schema: "plt_meta",
                table: "framework",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_iac_template_CloudProviderId",
                schema: "plt_meta",
                table: "iac_template",
                column: "CloudProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_llm_model_config_ProviderId",
                schema: "plt_meta",
                table: "llm_model_config",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_module_definition_ProjectId",
                schema: "plt_project",
                table: "module_definition",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_package_registry_LanguageId",
                schema: "plt_meta",
                table: "package_registry",
                column: "LanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_project_TenantId_Slug",
                schema: "plt_project",
                table: "project",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_architecture_ProjectId",
                schema: "plt_project",
                table: "project_architecture",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_dependency_ProjectId",
                schema: "plt_project",
                table: "project_dependency",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_integration_ProjectId",
                schema: "plt_project",
                table: "project_integration",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_metric_ProjectId",
                schema: "plt_audit",
                table: "project_metric",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_settings_ProjectId",
                schema: "plt_project",
                table: "project_settings",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_team_member_ProjectId",
                schema: "plt_project",
                table: "project_team_member",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_tech_stack_ProjectId",
                schema: "plt_project",
                table: "project_tech_stack",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_report_ProjectId",
                schema: "plt_audit",
                table: "quality_report",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_report_SprintId",
                schema: "plt_audit",
                table: "quality_report",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_raw_requirement_ProjectId",
                schema: "plt_project",
                table: "raw_requirement",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_review_result_ArtifactId",
                schema: "plt_audit",
                table: "review_result",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_sprint_ProjectId",
                schema: "plt_project",
                table: "sprint",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_stage_definition_WorkflowId",
                schema: "plt_meta",
                table: "stage_definition",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_story_EpicId",
                schema: "plt_project",
                table: "story",
                column: "EpicId");

            migrationBuilder.CreateIndex(
                name: "IX_story_SprintId",
                schema: "plt_project",
                table: "story",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_task_dependency_DependsOnTaskId",
                schema: "plt_project",
                table: "task_dependency",
                column: "DependsOnTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_dependency_TaskId",
                schema: "plt_project",
                table: "task_dependency",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_item_StoryId",
                schema: "plt_project",
                table: "task_item",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_test_template_FrameworkId",
                schema: "plt_meta",
                table: "test_template",
                column: "FrameworkId");

            migrationBuilder.CreateIndex(
                name: "IX_traceability_record_ProjectId",
                schema: "plt_audit",
                table: "traceability_record",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_transition_rule_FromStageId",
                schema: "plt_meta",
                table: "transition_rule",
                column: "FromStageId");

            migrationBuilder.CreateIndex(
                name: "IX_transition_rule_ToStageId",
                schema: "plt_meta",
                table: "transition_rule",
                column: "ToStageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_constraint",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_conversation",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "agent_model_mapping",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_plugin_manifest",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_prompt_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_tool_definition",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "api_contract",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "api_protocol",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "approval_gate_config",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "architecture_decision_record",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "architecture_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "brd_feedback_record",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "brd_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "cicd_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "code_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "coding_standard",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "compatibility_rule",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "config_snapshot",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "data_model_definition",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "database_technology",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "devops_tool",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "docker_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "documentation_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "enriched_requirement",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "environment_config",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "file_structure_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "iac_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "llm_model_config",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "llm_routing_rule",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "naming_convention",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "package_registry",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "project_architecture",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "project_dependency",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "project_integration",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "project_metric",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "project_settings",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "project_team_member",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "project_tech_stack",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "quality_gate",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "quality_report",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "review_checklist",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "review_result",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "security_policy",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "starter_kit",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "task_dependency",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "template_variable",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "test_template",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "token_budget",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "traceability_record",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "transition_rule",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_type_definition",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "module_definition",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "raw_requirement",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "cloud_provider",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "llm_provider_config",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_artifact_record",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "framework",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "stage_definition",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_run",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "language",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "sdlc_workflow",
                schema: "plt_meta");

            migrationBuilder.DropTable(
                name: "agent_assignment",
                schema: "plt_audit");

            migrationBuilder.DropTable(
                name: "task_item",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "story",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "epic",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "sprint",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "brd_section_record",
                schema: "plt_project");

            migrationBuilder.DropTable(
                name: "project",
                schema: "plt_project");
        }
    }
}
