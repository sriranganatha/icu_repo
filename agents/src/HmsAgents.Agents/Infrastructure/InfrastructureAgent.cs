using System.Diagnostics;
using HmsAgents.Core.Enums;
using HmsAgents.Core.Interfaces;
using HmsAgents.Core.Models;
using Microsoft.Extensions.Logging;

namespace HmsAgents.Agents.Infrastructure;

/// <summary>
/// AI-powered infrastructure-as-code agent. Generates multi-stage Dockerfiles,
/// Kubernetes manifests (Deployment, Service, HPA, PDB, NetworkPolicy),
/// Helm charts, docker-compose, CI/CD pipelines, and database migration runners
/// for all 9 HMS microservices.
/// </summary>
public sealed class InfrastructureAgent : IAgent
{
    private readonly ILlmProvider _llm;
    private readonly ILogger<InfrastructureAgent> _logger;

    public AgentType Type => AgentType.Infrastructure;
    public string Name => "Infrastructure Agent";
    public string Description => "Generates Dockerfiles, Kubernetes manifests, Helm charts, docker-compose, CI/CD pipelines, and DB migration runners for all HMS services.";

    private static readonly (string Name, int Port)[] Services =
    [
        ("PatientService",     5101), ("EncounterService",   5102),
        ("InpatientService",   5103), ("EmergencyService",   5104),
        ("DiagnosticsService", 5105), ("RevenueService",     5106),
        ("AuditService",       5107), ("AiService",          5108),
        ("ApiGateway",         5100),
    ];

    public InfrastructureAgent(ILlmProvider llm, ILogger<InfrastructureAgent> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        context.AgentStatuses[Type] = AgentStatus.Running;
        _logger.LogInformation("InfrastructureAgent starting — AI-powered IaC generation");

        var artifacts = new List<CodeArtifact>();

        try
        {
            // Per-service Dockerfiles
            foreach (var (name, port) in Services)
            {
                ct.ThrowIfCancellationRequested();
                artifacts.Add(GenerateDockerfile(name, port));
            }

            artifacts.Add(GenerateDockerCompose());
            artifacts.Add(await GenerateKubernetesManifests(ct));
            artifacts.Add(await GenerateHelmValues(ct));
            artifacts.Add(GenerateCiCdPipeline());
            artifacts.Add(GenerateDatabaseMigrationRunner());

            context.Artifacts.AddRange(artifacts);
            context.AgentStatuses[Type] = AgentStatus.Completed;

            return new AgentResult
            {
                Agent = Type, Success = true,
                Summary = $"Infrastructure Agent: {artifacts.Count} IaC artifacts for {Services.Length} services (AI: {_llm.ProviderName})",
                Artifacts = artifacts,
                Messages = [new AgentMessage { From = Type, To = AgentType.Orchestrator,
                    Subject = "Infrastructure artifacts generated",
                    Body = $"{Services.Length} Dockerfiles, docker-compose, K8s manifests, Helm chart, CI/CD pipeline, DB migration runner." }],
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            context.AgentStatuses[Type] = AgentStatus.Failed;
            _logger.LogError(ex, "InfrastructureAgent failed");
            return new AgentResult { Agent = Type, Success = false, Errors = [ex.Message], Duration = sw.Elapsed };
        }
    }

    private static CodeArtifact GenerateDockerfile(string serviceName, int port)
    {
        var projectDir = serviceName == "ApiGateway" ? "Hms.ApiGateway" : $"Hms.{serviceName}";
        return new CodeArtifact
        {
            Layer = ArtifactLayer.Infrastructure,
            RelativePath = $"src/{projectDir}/Dockerfile",
            FileName = "Dockerfile",
            Namespace = string.Empty,
            ProducedBy = AgentType.Infrastructure,
            TracedRequirementIds = ["NFR-INFRA-01"],
            Content = $"""
                # Multi-stage Dockerfile for {serviceName}
                # Stage 1: Build
                FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
                WORKDIR /src
                COPY ["{projectDir}/{projectDir}.csproj", "{projectDir}/"]
                COPY ["Hms.SharedKernel/Hms.SharedKernel.csproj", "Hms.SharedKernel/"]
                RUN dotnet restore "{projectDir}/{projectDir}.csproj"
                COPY . .
                WORKDIR "/src/{projectDir}"
                RUN dotnet build -c Release -o /app/build

                # Stage 2: Publish
                FROM build AS publish
                RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

                # Stage 3: Runtime
                FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
                WORKDIR /app

                # Security: non-root user
                RUN addgroup -S hms && adduser -S hmsuser -G hms
                USER hmsuser

                COPY --from=publish /app/publish .
                EXPOSE {port}
                ENV ASPNETCORE_URLS=http://+:{port}
                ENV ASPNETCORE_ENVIRONMENT=Production

                HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
                  CMD wget -qO- http://localhost:{port}/healthz || exit 1

                ENTRYPOINT ["dotnet", "{projectDir}.dll"]
                """
        };
    }

    private static CodeArtifact GenerateDockerCompose()
    {
        var serviceEntries = string.Join("\n", Services.Select(s =>
        {
            var dockerfile = s.Name == "ApiGateway" ? "Hms.ApiGateway" : $"Hms.{s.Name}";
            var lower = s.Name.ToLowerInvariant();
            return
                $"  {lower}:\n" +
                $"    build:\n" +
                $"      context: ./src\n" +
                $"      dockerfile: {dockerfile}/Dockerfile\n" +
                $"    ports: [\"{s.Port}:{s.Port}\"]\n" +
                 "    environment:\n" +
                 "      - ConnectionStrings__Default=Host=postgres;Database=hms;Username=hms;Password=${POSTGRES_PASSWORD:-hms_dev_pw}\n" +
                 "      - Kafka__BootstrapServers=kafka:9092\n" +
                 "      - Redis__ConnectionString=redis:6379\n" +
                 "    depends_on:\n" +
                 "      postgres:\n" +
                 "        condition: service_healthy\n" +
                 "      kafka:\n" +
                 "        condition: service_healthy";
        }));

        var content =
            "version: '3.9'\n\n" +
            "services:\n" +
            "  # ─── Infrastructure ─────────────────────────────────\n" +
            "  postgres:\n" +
            "    image: postgres:16-alpine\n" +
            "    environment:\n" +
            "      POSTGRES_USER: hms\n" +
            "      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-hms_dev_pw}\n" +
            "      POSTGRES_DB: hms\n" +
            "    volumes:\n" +
            "      - pgdata:/var/lib/postgresql/data\n" +
            "    ports: [\"5432:5432\"]\n" +
            "    healthcheck:\n" +
            "      test: [\"CMD-SHELL\", \"pg_isready -U hms\"]\n" +
            "      interval: 10s\n" +
            "      timeout: 5s\n" +
            "      retries: 5\n\n" +
            "  kafka:\n" +
            "    image: bitnami/kafka:3.7\n" +
            "    environment:\n" +
            "      KAFKA_CFG_NODE_ID: 1\n" +
            "      KAFKA_CFG_PROCESS_ROLES: broker,controller\n" +
            "      KAFKA_CFG_CONTROLLER_QUORUM_VOTERS: 1@kafka:9093\n" +
            "      KAFKA_CFG_LISTENERS: PLAINTEXT://:9092,CONTROLLER://:9093\n" +
            "      KAFKA_CFG_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092\n" +
            "      KAFKA_CFG_CONTROLLER_LISTENER_NAMES: CONTROLLER\n" +
            "      KAFKA_CFG_INTER_BROKER_LISTENER_NAME: PLAINTEXT\n" +
            "    ports: [\"9092:9092\"]\n" +
            "    healthcheck:\n" +
            "      test: [\"CMD\", \"kafka-topics.sh\", \"--bootstrap-server\", \"localhost:9092\", \"--list\"]\n" +
            "      interval: 15s\n" +
            "      timeout: 10s\n" +
            "      retries: 5\n\n" +
            "  redis:\n" +
            "    image: redis:7-alpine\n" +
            "    ports: [\"6379:6379\"]\n" +
            "    healthcheck:\n" +
            "      test: [\"CMD\", \"redis-cli\", \"ping\"]\n" +
            "      interval: 10s\n\n" +
            "  prometheus:\n" +
            "    image: prom/prometheus:v2.51.0\n" +
            "    volumes:\n" +
            "      - ./infrastructure/prometheus:/etc/prometheus\n" +
            "    ports: [\"9090:9090\"]\n\n" +
            "  grafana:\n" +
            "    image: grafana/grafana:10.4.0\n" +
            "    volumes:\n" +
            "      - ./infrastructure/grafana:/etc/grafana/provisioning/dashboards\n" +
            "    ports: [\"3000:3000\"]\n" +
            "    environment:\n" +
            "      GF_SECURITY_ADMIN_PASSWORD: ${GRAFANA_PASSWORD:-admin}\n\n" +
            "  # ─── HMS Microservices ──────────────────────────────\n" +
            serviceEntries + "\n\n" +
            "volumes:\n" +
            "  pgdata:";

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Infrastructure,
            RelativePath = "docker-compose.yml",
            FileName = "docker-compose.yml",
            Namespace = string.Empty,
            ProducedBy = AgentType.Infrastructure,
            TracedRequirementIds = ["NFR-INFRA-01"],
            Content = content
        };
    }

    private async Task<CodeArtifact> GenerateKubernetesManifests(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a Kubernetes expert for healthcare platforms. Generate production-grade K8s manifests.",
            UserPrompt = $"Generate Kubernetes manifests for these HMS services: {string.Join(", ", Services.Select(s => $"{s.Name}:{s.Port}"))}. Include: Deployment (2 replicas, resource limits, liveness/readiness probes), Service (ClusterIP), HPA (2-10 replicas, 70% CPU), PDB (maxUnavailable: 1), NetworkPolicy (only allow ingress from API Gateway). Output as a single multi-document YAML.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Infrastructure,
            RelativePath = "infrastructure/k8s/hms-services.yaml",
            FileName = "hms-services.yaml",
            Namespace = string.Empty,
            ProducedBy = AgentType.Infrastructure,
            TracedRequirementIds = ["NFR-INFRA-01"],
            Content = response.Success ? response.Content : GenerateK8sFallback()
        };
    }

    private async Task<CodeArtifact> GenerateHelmValues(CancellationToken ct)
    {
        var response = await _llm.GenerateAsync(new LlmPrompt
        {
            SystemPrompt = "You are a Helm chart expert. Generate values.yaml for a healthcare microservices platform.",
            UserPrompt = $"Generate a Helm values.yaml for HMS with global settings (image registry, namespace, TLS), per-service overrides ({string.Join(", ", Services.Select(s => s.Name))}), database config, Kafka config, Redis config, and monitoring config. Include healthcare-specific settings: HIPAA encryption key ref, PHI audit retention days, break-the-glass timeout.",
            Temperature = 0.1, RequestingAgent = Name
        }, ct);

        return new CodeArtifact
        {
            Layer = ArtifactLayer.Infrastructure,
            RelativePath = "infrastructure/helm/values.yaml",
            FileName = "values.yaml",
            Namespace = string.Empty,
            ProducedBy = AgentType.Infrastructure,
            TracedRequirementIds = ["NFR-INFRA-01"],
            Content = response.Success ? response.Content : GenerateHelmFallback()
        };
    }

    private static CodeArtifact GenerateCiCdPipeline() => new()
    {
        Layer = ArtifactLayer.Infrastructure,
        RelativePath = ".github/workflows/hms-ci-cd.yml",
        FileName = "hms-ci-cd.yml",
        Namespace = string.Empty,
        ProducedBy = AgentType.Infrastructure,
        TracedRequirementIds = ["NFR-INFRA-01", "SOC2-CC8"],
        Content = """
            name: HMS CI/CD Pipeline

            on:
              push:
                branches: [main, master, develop]
              pull_request:
                branches: [main, master]

            env:
              DOTNET_VERSION: '8.0.x'
              REGISTRY: ghcr.io
              IMAGE_PREFIX: ${{ github.repository_owner }}/hms

            jobs:
              build-and-test:
                runs-on: ubuntu-latest
                strategy:
                  matrix:
                    service:
                      - PatientService
                      - EncounterService
                      - InpatientService
                      - EmergencyService
                      - DiagnosticsService
                      - RevenueService
                      - AuditService
                      - AiService
                      - ApiGateway
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: ${{ env.DOTNET_VERSION }}

                  - name: Restore dependencies
                    run: dotnet restore src/Hms.${{ matrix.service }}/Hms.${{ matrix.service }}.csproj

                  - name: Build
                    run: dotnet build src/Hms.${{ matrix.service }}/Hms.${{ matrix.service }}.csproj -c Release --no-restore

                  - name: Run unit tests
                    run: dotnet test src/Hms.Tests/Hms.Tests.csproj --filter Category=${{ matrix.service }} --no-build -c Release

              security-scan:
                runs-on: ubuntu-latest
                needs: build-and-test
                steps:
                  - uses: actions/checkout@v4
                  - name: Run security scan (dotnet-security)
                    run: |
                      dotnet tool install --global security-scan
                      security-scan src/

              docker-build:
                runs-on: ubuntu-latest
                needs: [build-and-test, security-scan]
                if: github.event_name == 'push'
                strategy:
                  matrix:
                    service:
                      - PatientService
                      - EncounterService
                      - InpatientService
                      - EmergencyService
                      - DiagnosticsService
                      - RevenueService
                      - AuditService
                      - AiService
                      - ApiGateway
                steps:
                  - uses: actions/checkout@v4
                  - name: Log in to GHCR
                    uses: docker/login-action@v3
                    with:
                      registry: ${{ env.REGISTRY }}
                      username: ${{ github.actor }}
                      password: ${{ secrets.GITHUB_TOKEN }}

                  - name: Build and push Docker image
                    uses: docker/build-push-action@v5
                    with:
                      context: ./src
                      file: ./src/Hms.${{ matrix.service }}/Dockerfile
                      push: true
                      tags: |
                        ${{ env.REGISTRY }}/${{ env.IMAGE_PREFIX }}-${{ matrix.service }}:${{ github.sha }}
                        ${{ env.REGISTRY }}/${{ env.IMAGE_PREFIX }}-${{ matrix.service }}:latest

              deploy-staging:
                runs-on: ubuntu-latest
                needs: docker-build
                if: github.ref == 'refs/heads/develop'
                environment: staging
                steps:
                  - uses: actions/checkout@v4
                  - name: Deploy to staging
                    run: |
                      helm upgrade --install hms-staging infrastructure/helm/ \
                        --namespace hms-staging \
                        --set global.image.tag=${{ github.sha }} \
                        --values infrastructure/helm/values-staging.yaml

              deploy-production:
                runs-on: ubuntu-latest
                needs: docker-build
                if: github.ref == 'refs/heads/main' || github.ref == 'refs/heads/master'
                environment: production
                steps:
                  - uses: actions/checkout@v4
                  - name: Deploy to production
                    run: |
                      helm upgrade --install hms-prod infrastructure/helm/ \
                        --namespace hms-production \
                        --set global.image.tag=${{ github.sha }} \
                        --values infrastructure/helm/values-production.yaml
            """
    };

    private static CodeArtifact GenerateDatabaseMigrationRunner() => new()
    {
        Layer = ArtifactLayer.Infrastructure,
        RelativePath = "Hms.SharedKernel/Infrastructure/DatabaseMigrationRunner.cs",
        FileName = "DatabaseMigrationRunner.cs",
        Namespace = "Hms.SharedKernel.Infrastructure",
        ProducedBy = AgentType.Infrastructure,
        TracedRequirementIds = ["NFR-INFRA-01"],
        Content = """
            using Microsoft.Extensions.Logging;

            namespace Hms.SharedKernel.Infrastructure;

            /// <summary>
            /// EF Core migration runner for startup. Each service calls RunAsync()
            /// in Program.cs to auto-apply pending migrations.
            /// </summary>
            public sealed class DatabaseMigrationRunner
            {
                private readonly ILogger<DatabaseMigrationRunner> _logger;

                public DatabaseMigrationRunner(ILogger<DatabaseMigrationRunner> logger) => _logger = logger;

                /// <summary>
                /// Apply pending EF Core migrations at startup with retry logic.
                /// </summary>
                public async Task RunAsync(object dbContext, CancellationToken ct = default)
                {
                    const int maxRetries = 5;
                    var delay = TimeSpan.FromSeconds(2);

                    for (var attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            _logger.LogInformation("Applying database migrations (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);

                            // Uses reflection to call dbContext.Database.MigrateAsync()
                            var dbProp = dbContext.GetType().GetProperty("Database")
                                ?? throw new InvalidOperationException("DbContext must have a Database property");
                            var database = dbProp.GetValue(dbContext)!;
                            var migrateMethod = database.GetType().GetMethod("MigrateAsync",
                                [typeof(CancellationToken)]);

                            if (migrateMethod is not null)
                            {
                                var task = (Task?)migrateMethod.Invoke(database, [ct]);
                                if (task is not null) await task;
                            }

                            _logger.LogInformation("Database migrations applied successfully");
                            return;
                        }
                        catch (Exception ex) when (attempt < maxRetries)
                        {
                            _logger.LogWarning(ex, "Migration attempt {Attempt} failed, retrying in {Delay}s", attempt, delay.TotalSeconds);
                            await Task.Delay(delay, ct);
                            delay *= 2; // Exponential backoff
                        }
                    }
                }
            }
            """
    };

    private static string GenerateK8sFallback()
    {
        var docs = new List<string>();
        foreach (var (name, port) in Services)
        {
            var lower = name.ToLowerInvariant();
            var proj = name == "ApiGateway" ? "hms-apigateway" : $"hms-{lower}";
            docs.Add($"""
                ---
                apiVersion: apps/v1
                kind: Deployment
                metadata:
                  name: {proj}
                  namespace: hms
                  labels:
                    app: {proj}
                spec:
                  replicas: 2
                  selector:
                    matchLabels:
                      app: {proj}
                  template:
                    metadata:
                      labels:
                        app: {proj}
                    spec:
                      containers:
                        - name: {proj}
                          image: ghcr.io/hms/{proj}:latest
                          ports:
                            - containerPort: {port}
                          resources:
                            requests:
                              cpu: 200m
                              memory: 256Mi
                            limits:
                              cpu: 500m
                              memory: 512Mi
                          livenessProbe:
                            httpGet:
                              path: /healthz
                              port: {port}
                            initialDelaySeconds: 15
                            periodSeconds: 30
                          readinessProbe:
                            httpGet:
                              path: /healthz
                              port: {port}
                            initialDelaySeconds: 5
                            periodSeconds: 10
                ---
                apiVersion: v1
                kind: Service
                metadata:
                  name: {proj}
                  namespace: hms
                spec:
                  selector:
                    app: {proj}
                  ports:
                    - port: {port}
                      targetPort: {port}
                  type: ClusterIP
                ---
                apiVersion: autoscaling/v2
                kind: HorizontalPodAutoscaler
                metadata:
                  name: {proj}
                  namespace: hms
                spec:
                  scaleTargetRef:
                    apiVersion: apps/v1
                    kind: Deployment
                    name: {proj}
                  minReplicas: 2
                  maxReplicas: 10
                  metrics:
                    - type: Resource
                      resource:
                        name: cpu
                        target:
                          type: Utilization
                          averageUtilization: 70
                ---
                apiVersion: policy/v1
                kind: PodDisruptionBudget
                metadata:
                  name: {proj}
                  namespace: hms
                spec:
                  maxUnavailable: 1
                  selector:
                    matchLabels:
                      app: {proj}
                """);
        }
        return string.Join("\n", docs);
    }

    private static string GenerateHelmFallback() => """
        global:
          namespace: hms
          image:
            registry: ghcr.io/hms
            pullPolicy: IfNotPresent
            tag: latest
          tls:
            enabled: true
            certSecretName: hms-tls-cert
          hipaa:
            encryptionKeySecret: hms-encryption-key
            phiAuditRetentionDays: 2555  # 7 years per HIPAA
            breakTheGlassTimeoutHours: 4

        database:
          host: postgres
          port: 5432
          name: hms
          username: hms
          passwordSecret: hms-db-password

        kafka:
          bootstrapServers: kafka:9092
          partitions: 6
          replicationFactor: 3

        redis:
          connectionString: redis:6379

        monitoring:
          prometheus:
            enabled: true
            scrapeInterval: 15s
          grafana:
            enabled: true
            adminPasswordSecret: grafana-admin-password

        services:
          patientService:
            replicas: 2
            port: 5101
            resources:
              requests: { cpu: 200m, memory: 256Mi }
              limits: { cpu: 500m, memory: 512Mi }
          encounterService:
            replicas: 2
            port: 5102
          inpatientService:
            replicas: 2
            port: 5103
          emergencyService:
            replicas: 3  # Higher for ER load
            port: 5104
          diagnosticsService:
            replicas: 2
            port: 5105
          revenueService:
            replicas: 2
            port: 5106
          auditService:
            replicas: 2
            port: 5107
          aiService:
            replicas: 2
            port: 5108
            resources:
              requests: { cpu: 500m, memory: 1Gi }
              limits: { cpu: 2000m, memory: 4Gi }
          apiGateway:
            replicas: 3
            port: 5100
        """;
}
