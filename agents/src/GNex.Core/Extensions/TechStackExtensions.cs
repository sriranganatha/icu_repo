using GNex.Core.Models;

namespace GNex.Core.Extensions;

/// <summary>
/// Extension methods on <see cref="AgentContext"/> to resolve technology details
/// from <see cref="AgentContext.ResolvedTechStack"/> instead of hardcoding versions.
/// Every helper falls back to a sensible default when the project has no tech stack configured.
/// </summary>
public static class TechStackExtensions
{
    // ── Single technology lookups ────────────────────────────────

    /// <summary>Finds the first entry matching the given technology type and optional layer.</summary>
    public static ResolvedTechStackEntry? FindTech(this AgentContext ctx, string technologyType, string? layer = null)
        => ctx.ResolvedTechStack.FirstOrDefault(t =>
            t.TechnologyType.Equals(technologyType, StringComparison.OrdinalIgnoreCase) &&
            (layer is null || t.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Finds the first entry whose TechnologyId or TechnologyName contains the search term.</summary>
    public static ResolvedTechStackEntry? FindTechByName(this AgentContext ctx, string nameContains)
        => ctx.ResolvedTechStack.FirstOrDefault(t =>
            t.TechnologyId.Contains(nameContains, StringComparison.OrdinalIgnoreCase) ||
            t.TechnologyName.Contains(nameContains, StringComparison.OrdinalIgnoreCase));

    // ── Framework / Language ────────────────────────────────────

    /// <summary>Returns the primary backend framework version string, e.g. "10.0" or "8.0".</summary>
    public static string FrameworkVersion(this AgentContext ctx)
    {
        var fw = ctx.FindTech("framework", "backend")
              ?? ctx.FindTechByName(".NET")
              ?? ctx.FindTechByName("dotnet");
        return !string.IsNullOrWhiteSpace(fw?.Version) ? fw.Version : "10.0";
    }

    /// <summary>Returns display name like ".NET 10" or ".NET 8".</summary>
    public static string FrameworkLabel(this AgentContext ctx)
    {
        var fw = ctx.FindTech("framework", "backend")
              ?? ctx.FindTechByName(".NET")
              ?? ctx.FindTechByName("dotnet");
        var name = !string.IsNullOrWhiteSpace(fw?.TechnologyName) ? fw.TechnologyName : ".NET";
        var ver = ctx.FrameworkVersion();
        // Strip minor if it ends with ".0" for clean display: "10.0" → "10", "8.0" → "8"
        var display = ver.EndsWith(".0") ? ver[..^2] : ver;
        return $"{name} {display}";
    }

    /// <summary>Returns the TFM moniker, e.g. "net10.0" or "net8.0".</summary>
    public static string TargetFrameworkMoniker(this AgentContext ctx)
        => $"net{ctx.FrameworkVersion()}";

    /// <summary>Returns the primary language label, e.g. "C# 13" or "C#".</summary>
    public static string LanguageLabel(this AgentContext ctx)
    {
        var lang = ctx.FindTech("language", "backend") ?? ctx.FindTechByName("C#");
        var name = !string.IsNullOrWhiteSpace(lang?.TechnologyName) ? lang.TechnologyName : "C#";
        return !string.IsNullOrWhiteSpace(lang?.Version) ? $"{name} {lang.Version}" : name;
    }

    /// <summary>Returns the role description for LLM prompts, e.g. "senior .NET 10 architect".</summary>
    public static string SeniorRoleLabel(this AgentContext ctx, string role = "architect")
        => $"senior {ctx.FrameworkLabel()} {role}";

    /// <summary>Returns the expert role description, e.g. "expert .NET 10/C# developer".</summary>
    public static string ExpertRoleLabel(this AgentContext ctx, string role = "developer")
        => $"expert {ctx.FrameworkLabel()}/{ctx.LanguageLabel()} {role}";

    // ── ORM ─────────────────────────────────────────────────────

    /// <summary>Returns ORM label, e.g. "EF Core 10" or "EF Core 8".</summary>
    public static string OrmLabel(this AgentContext ctx)
    {
        var orm = ctx.FindTechByName("EF Core") ?? ctx.FindTechByName("Entity Framework");
        if (!string.IsNullOrWhiteSpace(orm?.Version))
            return $"EF Core {(orm.Version.EndsWith(".0") ? orm.Version[..^2] : orm.Version)}";
        // Default: match framework major version
        var fwVer = ctx.FrameworkVersion();
        var major = fwVer.Contains('.') ? fwVer.Split('.')[0] : fwVer;
        return $"EF Core {major}";
    }

    /// <summary>Returns EF Core NuGet package version string, e.g. "10.0.0" or "8.0.6".</summary>
    public static string EfCorePackageVersion(this AgentContext ctx)
    {
        var orm = ctx.FindTechByName("EF Core") ?? ctx.FindTechByName("Entity Framework");
        return !string.IsNullOrWhiteSpace(orm?.Version) ? orm.Version : $"{ctx.FrameworkVersion().Split('.')[0]}.0.0";
    }

    // ── Database ────────────────────────────────────────────────

    /// <summary>Returns the primary database label, e.g. "PostgreSQL 16".</summary>
    public static string DatabaseLabel(this AgentContext ctx)
    {
        var db = ctx.FindTech("database") ?? ctx.FindTechByName("PostgreSQL") ?? ctx.FindTechByName("Postgres");
        var name = !string.IsNullOrWhiteSpace(db?.TechnologyName) ? db.TechnologyName : "PostgreSQL";
        return !string.IsNullOrWhiteSpace(db?.Version) ? $"{name} {db.Version}" : $"{name} 16";
    }

    /// <summary>Returns the Docker image for database, e.g. "postgres:16-alpine".</summary>
    public static string DatabaseDockerImage(this AgentContext ctx)
    {
        var db = ctx.FindTech("database") ?? ctx.FindTechByName("PostgreSQL") ?? ctx.FindTechByName("Postgres");
        var ver = !string.IsNullOrWhiteSpace(db?.Version) ? db.Version : "16";
        return $"postgres:{ver}-alpine";
    }

    /// <summary>Returns the Npgsql EF Core NuGet version, e.g. "10.0.0" or "8.0.4".</summary>
    public static string NpgsqlPackageVersion(this AgentContext ctx)
    {
        var npgsql = ctx.FindTechByName("Npgsql");
        return !string.IsNullOrWhiteSpace(npgsql?.Version) ? npgsql.Version : $"{ctx.FrameworkVersion().Split('.')[0]}.0.0";
    }

    // ── Messaging ───────────────────────────────────────────────

    /// <summary>Returns the messaging technology label, e.g. "Apache Kafka" or "RabbitMQ".</summary>
    public static string MessagingLabel(this AgentContext ctx)
    {
        var msg = ctx.FindTechByName("Kafka") ?? ctx.FindTechByName("RabbitMQ") ?? ctx.FindTech("queue");
        return !string.IsNullOrWhiteSpace(msg?.TechnologyName) ? msg.TechnologyName : "Apache Kafka";
    }

    /// <summary>Returns the messaging Docker image, e.g. "bitnami/kafka:3.7".</summary>
    public static string MessagingDockerImage(this AgentContext ctx)
    {
        var msg = ctx.FindTechByName("Kafka") ?? ctx.FindTech("queue");
        if (msg?.TechnologyName?.Contains("Rabbit", StringComparison.OrdinalIgnoreCase) == true)
        {
            var ver = !string.IsNullOrWhiteSpace(msg.Version) ? msg.Version : "3-management-alpine";
            return $"rabbitmq:{ver}";
        }
        var kVer = !string.IsNullOrWhiteSpace(msg?.Version) ? msg.Version : "3.7";
        return $"bitnami/kafka:{kVer}";
    }

    /// <summary>Returns the Kafka client NuGet package version.</summary>
    public static string KafkaClientPackageVersion(this AgentContext ctx)
    {
        var kafka = ctx.FindTechByName("Kafka");
        return !string.IsNullOrWhiteSpace(kafka?.Version) ? kafka.Version : "2.4.0";
    }

    // ── Cache ───────────────────────────────────────────────────

    /// <summary>Returns the cache Docker image, e.g. "redis:7-alpine".</summary>
    public static string CacheDockerImage(this AgentContext ctx)
    {
        var cache = ctx.FindTechByName("Redis") ?? ctx.FindTech("cache");
        var ver = !string.IsNullOrWhiteSpace(cache?.Version) ? cache.Version : "7";
        return $"redis:{ver}-alpine";
    }

    /// <summary>Returns the cache label, e.g. "Redis 7" or "Redis".</summary>
    public static string CacheLabel(this AgentContext ctx)
    {
        var cache = ctx.FindTechByName("Redis") ?? ctx.FindTech("cache");
        var name = !string.IsNullOrWhiteSpace(cache?.TechnologyName) ? cache.TechnologyName : "Redis";
        return !string.IsNullOrWhiteSpace(cache?.Version) ? $"{name} {cache.Version}" : name;
    }

    // ── Observability ───────────────────────────────────────────

    /// <summary>Returns the metrics Docker image, e.g. "prom/prometheus:v2.51.0".</summary>
    public static string PrometheusDockerImage(this AgentContext ctx)
    {
        var prom = ctx.FindTechByName("Prometheus");
        var ver = !string.IsNullOrWhiteSpace(prom?.Version) ? prom.Version : "v2.51.0";
        return $"prom/prometheus:{ver}";
    }

    /// <summary>Returns the dashboard Docker image, e.g. "grafana/grafana:10.4.0".</summary>
    public static string GrafanaDockerImage(this AgentContext ctx)
    {
        var grafana = ctx.FindTechByName("Grafana");
        var ver = !string.IsNullOrWhiteSpace(grafana?.Version) ? grafana.Version : "10.4.0";
        return $"grafana/grafana:{ver}";
    }

    // ── Testing ─────────────────────────────────────────────────

    /// <summary>Returns the test framework label, e.g. "xUnit" or "NUnit".</summary>
    public static string TestFrameworkLabel(this AgentContext ctx)
    {
        var tf = ctx.FindTechByName("xUnit") ?? ctx.FindTechByName("NUnit") ?? ctx.FindTechByName("MSTest");
        return !string.IsNullOrWhiteSpace(tf?.TechnologyName) ? tf.TechnologyName : "xUnit";
    }

    /// <summary>Returns the mocking framework label, e.g. "Moq" or "NSubstitute".</summary>
    public static string MockFrameworkLabel(this AgentContext ctx)
    {
        var mf = ctx.FindTechByName("Moq") ?? ctx.FindTechByName("NSubstitute");
        return !string.IsNullOrWhiteSpace(mf?.TechnologyName) ? mf.TechnologyName : "Moq";
    }

    /// <summary>Returns test framework + mock framework combined, e.g. "xUnit + Moq".</summary>
    public static string TestStackLabel(this AgentContext ctx)
        => $"{ctx.TestFrameworkLabel()} + {ctx.MockFrameworkLabel()}";

    // ── Docker base images ──────────────────────────────────────

    /// <summary>Returns ASP.NET runtime Docker image, e.g. "mcr.microsoft.com/dotnet/aspnet:10.0-alpine".</summary>
    public static string AspNetDockerImage(this AgentContext ctx)
        => $"mcr.microsoft.com/dotnet/aspnet:{ctx.FrameworkVersion()}-alpine";

    /// <summary>Returns .NET SDK Docker image, e.g. "mcr.microsoft.com/dotnet/sdk:10.0-alpine".</summary>
    public static string SdkDockerImage(this AgentContext ctx)
        => $"mcr.microsoft.com/dotnet/sdk:{ctx.FrameworkVersion()}-alpine";

    // ── Environment string ──────────────────────────────────────

    /// <summary>Returns a standard environment string, e.g. ".NET 10, PostgreSQL 16".</summary>
    public static string EnvironmentLabel(this AgentContext ctx)
        => $"{ctx.FrameworkLabel()}, {ctx.DatabaseLabel()}";

    /// <summary>
    /// Returns a formatted tech stack summary suitable for LLM prompt injection.
    /// </summary>
    public static string BuildTechStackSummary(this AgentContext ctx)
    {
        if (ctx.ResolvedTechStack.Count == 0)
            return string.Empty;

        var groups = ctx.ResolvedTechStack
            .GroupBy(t => t.Layer)
            .OrderBy(g => g.Key);

        var lines = new List<string>();
        foreach (var group in groups)
        {
            var techs = string.Join(", ", group.Select(t =>
                !string.IsNullOrWhiteSpace(t.Version)
                    ? $"{t.TechnologyName} {t.Version}"
                    : t.TechnologyName));
            lines.Add($"  - {group.Key}: {techs}");
        }

        return $"## Project Tech Stack\n{string.Join("\n", lines)}";
    }

    // ── Database dialect helpers ─────────────────────────────────

    /// <summary>
    /// Returns the short database engine name stripped of version, e.g. "PostgreSQL", "MySQL",
    /// "SQL Server", or "Oracle". Used to branch DDL generation by DB type.
    /// </summary>
    public static string DatabaseEngine(this AgentContext ctx)
    {
        var db = ctx.FindTech("database") ?? ctx.FindTechByName("PostgreSQL") ?? ctx.FindTechByName("Postgres");
        var name = db?.TechnologyName ?? "PostgreSQL";

        if (name.Contains("Postgres", StringComparison.OrdinalIgnoreCase)) return "PostgreSQL";
        if (name.Contains("MySQL", StringComparison.OrdinalIgnoreCase) || name.Contains("MariaDB", StringComparison.OrdinalIgnoreCase)) return "MySQL";
        if (name.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || name.Contains("MSSQL", StringComparison.OrdinalIgnoreCase)) return "SQL Server";
        if (name.Contains("Oracle", StringComparison.OrdinalIgnoreCase)) return "Oracle";
        return name;
    }

    /// <summary>Returns the correct Docker image for the project's database type.</summary>
    public static string DatabaseDockerImageByEngine(this AgentContext ctx)
    {
        var engine = ctx.DatabaseEngine();
        var db = ctx.FindTech("database") ?? ctx.FindTechByName("PostgreSQL") ?? ctx.FindTechByName("Postgres");
        return engine switch
        {
            "MySQL" => $"mysql:{(db?.Version ?? "8.0")}",
            "SQL Server" => $"mcr.microsoft.com/mssql/server:{(db?.Version ?? "2022")}-latest",
            "Oracle" => $"container-registry.oracle.com/database/express:{(db?.Version ?? "21.3.0")}-xe",
            _ => $"postgres:{(db?.Version ?? "16")}-alpine"
        };
    }

    /// <summary>Returns the default database port for the engine type.</summary>
    public static int DatabaseDefaultPort(this AgentContext ctx) => ctx.DatabaseEngine() switch
    {
        "MySQL" => 3306,
        "SQL Server" => 1433,
        "Oracle" => 1521,
        _ => 5432
    };

    /// <summary>Returns the LLM output format instruction based on the primary language.</summary>
    public static string OutputFormatInstruction(this AgentContext ctx, string format = "code")
    {
        var lang = ctx.LanguageLabel();
        return format switch
        {
            "sql" => $"Do NOT use markdown code fences. Output raw {ctx.DatabaseEngine()} SQL only.",
            _ => $"Do NOT use markdown code fences. Output raw {lang} only."
        };
    }

    /// <summary>Returns the database connection string environment variable name.</summary>
    public static string DatabaseConnectionEnvVar(this AgentContext ctx) => ctx.DatabaseEngine() switch
    {
        "MySQL" => "MYSQL_CONNECTION_STRING",
        "SQL Server" => "SQLSERVER_CONNECTION_STRING",
        "Oracle" => "ORACLE_CONNECTION_STRING",
        _ => "POSTGRES_CONNECTION_STRING"
    };

    /// <summary>Returns the EF Core database provider NuGet package name.</summary>
    public static string EfCoreProviderPackage(this AgentContext ctx) => ctx.DatabaseEngine() switch
    {
        "MySQL" => "Pomelo.EntityFrameworkCore.MySql",
        "SQL Server" => "Microsoft.EntityFrameworkCore.SqlServer",
        "Oracle" => "Oracle.EntityFrameworkCore",
        _ => "Npgsql.EntityFrameworkCore.PostgreSQL"
    };

    /// <summary>Returns the EF Core UseXxx method name for the database type.</summary>
    public static string EfCoreUseMethod(this AgentContext ctx) => ctx.DatabaseEngine() switch
    {
        "MySQL" => "UseMySql",
        "SQL Server" => "UseSqlServer",
        "Oracle" => "UseOracle",
        _ => "UseNpgsql"
    };
}
