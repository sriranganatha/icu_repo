using GNex.Database;
using GNex.Database.Entities.Platform.Technology;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GNex.Services.Platform;

public class TemplateDataSeeder(GNexDbContext db, ILogger<TemplateDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.BrdTemplates.AnyAsync(ct)) return;
        logger.LogInformation("Seeding industry-standard templates...");

        // Lookup seeded language/framework IDs
        var langs = await db.Languages.ToDictionaryAsync(l => l.Name, l => l.Id, ct);
        var fws = await db.Frameworks.ToDictionaryAsync(f => f.Name, f => f.Id, ct);
        var clouds = await db.CloudProviders.ToDictionaryAsync(c => c.Name, c => c.Id, ct);

        await SeedBrdTemplates(ct);
        await SeedArchitectureTemplates(ct);
        SeedCodeTemplates(langs, fws);
        SeedFileStructureTemplates(fws);
        SeedCiCdTemplates(langs);
        SeedDockerTemplates(langs, fws);
        SeedTestTemplates(fws);
        SeedIaCTemplates(clouds);
        SeedDocumentationTemplates();

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Template seeding completed");
    }

    private async Task SeedBrdTemplates(CancellationToken ct)
    {
        var webAppSections = """
        [
          {"type":"executive_summary","title":"Executive Summary","order":1,"prompt":"High-level overview of the project, its goals, and expected business value."},
          {"type":"business_objectives","title":"Business Objectives","order":2,"prompt":"Measurable business goals this project aims to achieve (SMART format)."},
          {"type":"stakeholders","title":"Stakeholders","order":3,"prompt":"Key stakeholders, their roles, and level of involvement."},
          {"type":"scope","title":"Project Scope","order":4,"prompt":"In-scope and out-of-scope items. Clear boundary definitions."},
          {"type":"functional_requirements","title":"Functional Requirements","order":5,"prompt":"Detailed functional requirements grouped by feature area. Use MoSCoW prioritisation."},
          {"type":"non_functional_requirements","title":"Non-Functional Requirements","order":6,"prompt":"Performance, scalability, availability, security, and compliance requirements with measurable targets."},
          {"type":"user_personas","title":"User Personas & Journeys","order":7,"prompt":"Target user personas with demographics, goals, pain points, and key user journeys."},
          {"type":"data_requirements","title":"Data Requirements","order":8,"prompt":"Data entities, relationships, storage needs, retention policies, and GDPR/privacy considerations."},
          {"type":"integration_points","title":"Integration Points","order":9,"prompt":"External systems, APIs, third-party services, and data exchange formats."},
          {"type":"security_requirements","title":"Security Requirements","order":10,"prompt":"Authentication, authorisation, encryption, audit logging, and vulnerability management requirements."},
          {"type":"compliance","title":"Regulatory & Compliance","order":11,"prompt":"Applicable regulations (GDPR, HIPAA, SOC2, PCI-DSS) and compliance measures."},
          {"type":"assumptions","title":"Assumptions & Constraints","order":12,"prompt":"Technical and business assumptions. Budget, timeline, and resource constraints."},
          {"type":"dependencies","title":"Dependencies","order":13,"prompt":"Internal and external dependencies, risks associated with each."},
          {"type":"acceptance_criteria","title":"Acceptance Criteria","order":14,"prompt":"Measurable acceptance criteria for each major feature. Definition of Done."},
          {"type":"risks","title":"Risk Assessment","order":15,"prompt":"Risk register with likelihood, impact, mitigation strategies, and owners."},
          {"type":"glossary","title":"Glossary","order":16,"prompt":"Domain-specific terms and acronyms used in this document."}
        ]
        """;

        var apiSections = """
        [
          {"type":"executive_summary","title":"Executive Summary","order":1,"prompt":"API purpose, target consumers, and business value."},
          {"type":"business_objectives","title":"Business Objectives","order":2,"prompt":"Measurable goals: adoption targets, SLA commitments, revenue impact."},
          {"type":"stakeholders","title":"Stakeholders","order":3,"prompt":"API consumers, internal teams, partner organisations."},
          {"type":"scope","title":"API Scope","order":4,"prompt":"Endpoints in scope, versioning strategy, deprecation policy."},
          {"type":"functional_requirements","title":"Functional Requirements","order":5,"prompt":"Resource definitions, CRUD operations, query capabilities, pagination, filtering."},
          {"type":"non_functional_requirements","title":"Non-Functional Requirements","order":6,"prompt":"Throughput (RPS), latency (p50/p95/p99), availability (SLA %), rate limiting."},
          {"type":"api_design","title":"API Design Standards","order":7,"prompt":"REST/GraphQL/gRPC conventions, naming, versioning, error response format."},
          {"type":"data_requirements","title":"Data Model","order":8,"prompt":"Request/response schemas, data validation rules, transformation logic."},
          {"type":"integration_points","title":"Integration & Dependencies","order":9,"prompt":"Upstream/downstream services, message queues, webhooks, event streams."},
          {"type":"security_requirements","title":"Security & Authentication","order":10,"prompt":"OAuth2/OIDC flows, API key management, CORS policy, input validation."},
          {"type":"compliance","title":"Compliance","order":11,"prompt":"Data residency, audit logging, retention, regulatory requirements."},
          {"type":"assumptions","title":"Assumptions & Constraints","order":12,"prompt":"Infrastructure assumptions, backward compatibility constraints."},
          {"type":"acceptance_criteria","title":"Acceptance Criteria","order":13,"prompt":"Contract tests, load test thresholds, documentation completeness."},
          {"type":"risks","title":"Risk Assessment","order":14,"prompt":"Breaking changes risk, dependency failures, capacity planning risks."},
          {"type":"glossary","title":"Glossary","order":15,"prompt":"API-specific terminology and abbreviations."}
        ]
        """;

        var mobileSections = """
        [
          {"type":"executive_summary","title":"Executive Summary","order":1,"prompt":"App purpose, target platforms (iOS/Android), and business value."},
          {"type":"business_objectives","title":"Business Objectives","order":2,"prompt":"Download targets, engagement metrics, retention goals."},
          {"type":"stakeholders","title":"Stakeholders","order":3,"prompt":"Product owner, design team, QA, app store review contacts."},
          {"type":"scope","title":"App Scope","order":4,"prompt":"Features per platform, offline capabilities, device support matrix."},
          {"type":"functional_requirements","title":"Functional Requirements","order":5,"prompt":"Screen-by-screen feature list with user interactions and navigation flows."},
          {"type":"non_functional_requirements","title":"Non-Functional Requirements","order":6,"prompt":"App size, cold start time, battery usage, crash rate targets."},
          {"type":"ux_requirements","title":"UX/UI Requirements","order":7,"prompt":"Design system, accessibility (WCAG), platform HIG compliance, responsive layouts."},
          {"type":"data_requirements","title":"Data & Offline Strategy","order":8,"prompt":"Local storage, sync strategy, conflict resolution, cache policies."},
          {"type":"integration_points","title":"Backend & Integration","order":9,"prompt":"API endpoints, push notifications, deep linking, analytics SDKs."},
          {"type":"security_requirements","title":"Security","order":10,"prompt":"Biometric auth, certificate pinning, secure storage, OWASP Mobile Top 10."},
          {"type":"compliance","title":"App Store & Compliance","order":11,"prompt":"App Store/Play Store guidelines, privacy policy, COPPA/GDPR."},
          {"type":"assumptions","title":"Assumptions & Constraints","order":12,"prompt":"Minimum OS versions, device capabilities, network assumptions."},
          {"type":"acceptance_criteria","title":"Acceptance Criteria","order":13,"prompt":"Functional tests, performance benchmarks, accessibility audit pass."},
          {"type":"risks","title":"Risk Assessment","order":14,"prompt":"App rejection risks, fragmentation issues, third-party SDK risks."},
          {"type":"glossary","title":"Glossary","order":15,"prompt":"Mobile-specific terms and platform abbreviations."}
        ]
        """;

        var dataPipelineSections = """
        [
          {"type":"executive_summary","title":"Executive Summary","order":1,"prompt":"Pipeline purpose, data sources, and business intelligence value."},
          {"type":"business_objectives","title":"Business Objectives","order":2,"prompt":"Data freshness targets, reporting SLAs, analytics use cases."},
          {"type":"stakeholders","title":"Stakeholders","order":3,"prompt":"Data engineers, analysts, data governance, business users."},
          {"type":"scope","title":"Pipeline Scope","order":4,"prompt":"Source systems, transformation stages, target destinations."},
          {"type":"functional_requirements","title":"Functional Requirements","order":5,"prompt":"Ingestion methods (batch/stream), transformation rules, output formats."},
          {"type":"non_functional_requirements","title":"Non-Functional Requirements","order":6,"prompt":"Throughput (GB/hr), latency, data quality SLAs, retry policies."},
          {"type":"data_requirements","title":"Data Model & Quality","order":7,"prompt":"Schema definitions, data contracts, quality checks, lineage tracking."},
          {"type":"integration_points","title":"Source & Sink Systems","order":8,"prompt":"Database connections, API sources, file drops, warehouse targets."},
          {"type":"security_requirements","title":"Security & Governance","order":9,"prompt":"Encryption at rest/in transit, PII masking, access controls, audit trail."},
          {"type":"compliance","title":"Compliance","order":10,"prompt":"Data residency, retention policies, right-to-erasure support."},
          {"type":"assumptions","title":"Assumptions & Constraints","order":11,"prompt":"Data volume estimates, network bandwidth, processing windows."},
          {"type":"acceptance_criteria","title":"Acceptance Criteria","order":12,"prompt":"Data accuracy thresholds, reconciliation checks, monitoring alerts."},
          {"type":"risks","title":"Risk Assessment","order":13,"prompt":"Schema drift, source unavailability, data corruption scenarios."},
          {"type":"glossary","title":"Glossary","order":14,"prompt":"Data engineering and ETL terminology."}
        ]
        """;

        db.BrdTemplates.AddRange(
            new BrdTemplate { Name = "Web Application BRD", ProjectType = "web_app", SectionsJson = webAppSections.Trim(), IsDefault = true },
            new BrdTemplate { Name = "REST/GraphQL API BRD", ProjectType = "api", SectionsJson = apiSections.Trim(), IsDefault = true },
            new BrdTemplate { Name = "Mobile Application BRD", ProjectType = "mobile_app", SectionsJson = mobileSections.Trim(), IsDefault = true },
            new BrdTemplate { Name = "Data Pipeline BRD", ProjectType = "data_pipeline", SectionsJson = dataPipelineSections.Trim(), IsDefault = true }
        );

        await Task.CompletedTask;
    }

    private Task SeedArchitectureTemplates(CancellationToken ct)
    {
        db.ArchitectureTemplates.AddRange(
            new ArchitectureTemplate
            {
                Name = "Clean Architecture (Monolith)",
                Pattern = "monolith",
                DiagramTemplate = """
                graph TD
                  subgraph Presentation
                    API[API Controllers]
                    UI[Razor Pages / Blazor]
                  end
                  subgraph Application
                    UC[Use Cases / Commands / Queries]
                    DTO[DTOs & Validators]
                  end
                  subgraph Domain
                    E[Entities & Aggregates]
                    VS[Value Objects]
                    DS[Domain Services]
                    I[Interfaces / Ports]
                  end
                  subgraph Infrastructure
                    DB[(Database)]
                    MQ[Message Queue]
                    EXT[External APIs]
                    REPO[Repositories]
                  end
                  API --> UC
                  UI --> UC
                  UC --> I
                  I --> REPO
                  REPO --> DB
                  UC --> MQ
                  UC --> EXT
                """
            },
            new ArchitectureTemplate
            {
                Name = "Microservices Architecture",
                Pattern = "microservices",
                DiagramTemplate = """
                graph TD
                  GW[API Gateway] --> SVC1[Service A]
                  GW --> SVC2[Service B]
                  GW --> SVC3[Service C]
                  SVC1 --> DB1[(DB A)]
                  SVC2 --> DB2[(DB B)]
                  SVC3 --> DB3[(DB C)]
                  SVC1 <-->|Events| MB{{Message Broker}}
                  SVC2 <-->|Events| MB
                  SVC3 <-->|Events| MB
                  SD[Service Discovery] --- SVC1
                  SD --- SVC2
                  SD --- SVC3
                  OBS[Observability Stack] -.-> SVC1
                  OBS -.-> SVC2
                  OBS -.-> SVC3
                """
            },
            new ArchitectureTemplate
            {
                Name = "Serverless Architecture",
                Pattern = "serverless",
                DiagramTemplate = """
                graph TD
                  CDN[CDN / Edge] --> APIGW[API Gateway]
                  APIGW --> FN1[Function: Auth]
                  APIGW --> FN2[Function: Business Logic]
                  APIGW --> FN3[Function: Notifications]
                  FN1 --> IDP[Identity Provider]
                  FN2 --> DDB[(Serverless DB)]
                  FN2 --> S3[Object Storage]
                  FN3 --> SES[Email / Push Service]
                  EVT[Event Source] --> FN4[Function: Event Processor]
                  FN4 --> DDB
                  FN4 --> Q[Dead Letter Queue]
                """
            },
            new ArchitectureTemplate
            {
                Name = "Modular Monolith",
                Pattern = "modular_monolith",
                DiagramTemplate = """
                graph TD
                  subgraph Host[Host Process]
                    API[Shared API Layer]
                    subgraph ModA[Module: Identity]
                      A_C[Controllers]
                      A_S[Services]
                      A_D[(Schema: identity)]
                    end
                    subgraph ModB[Module: Orders]
                      B_C[Controllers]
                      B_S[Services]
                      B_D[(Schema: orders)]
                    end
                    subgraph ModC[Module: Billing]
                      C_C[Controllers]
                      C_S[Services]
                      C_D[(Schema: billing)]
                    end
                    BUS{{In-Process Event Bus}}
                  end
                  API --> A_C
                  API --> B_C
                  API --> C_C
                  A_S --> BUS
                  B_S --> BUS
                  C_S --> BUS
                """
            }
        );
        return Task.CompletedTask;
    }

    private void SeedCodeTemplates(Dictionary<string, string> langs, Dictionary<string, string> fws)
    {
        db.CodeTemplates.AddRange(
            new CodeTemplate
            {
                Name = "ASP.NET Core Web API Scaffold",
                LanguageId = langs.GetValueOrDefault("C#"),
                FrameworkId = fws.GetValueOrDefault("ASP.NET Core"),
                TemplateType = "scaffold",
                VariablesJson = """["project_name","namespace","db_provider"]""",
                Content = """
                // Program.cs — {{project_name}}
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddControllers();
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();
                builder.Services.AddDbContext<AppDbContext>(o =>
                    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
                builder.Services.AddHealthChecks();

                var app = builder.Build();
                if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
                app.UseHttpsRedirection();
                app.UseAuthorization();
                app.MapControllers();
                app.MapHealthChecks("/healthz");
                app.Run();
                """
            },
            new CodeTemplate
            {
                Name = "FastAPI Application Scaffold",
                LanguageId = langs.GetValueOrDefault("Python"),
                FrameworkId = fws.GetValueOrDefault("FastAPI"),
                TemplateType = "scaffold",
                VariablesJson = """["project_name","db_url"]""",
                Content = """
                # main.py — {{project_name}}
                from fastapi import FastAPI
                from fastapi.middleware.cors import CORSMiddleware
                from contextlib import asynccontextmanager
                from .database import engine, Base
                from .routers import health, api_v1

                @asynccontextmanager
                async def lifespan(app: FastAPI):
                    async with engine.begin() as conn:
                        await conn.run_sync(Base.metadata.create_all)
                    yield

                app = FastAPI(title="{{project_name}}", lifespan=lifespan)
                app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])
                app.include_router(health.router)
                app.include_router(api_v1.router, prefix="/api/v1")
                """
            },
            new CodeTemplate
            {
                Name = "React Component",
                LanguageId = langs.GetValueOrDefault("TypeScript"),
                FrameworkId = fws.GetValueOrDefault("React"),
                TemplateType = "component",
                VariablesJson = """["component_name","props_interface"]""",
                Content = """
                import React from 'react';

                interface {{component_name}}Props {
                  {{props_interface}}
                }

                export const {{component_name}}: React.FC<{{component_name}}Props> = (props) => {
                  return (
                    <div data-testid="{{component_name}}">
                      {/* TODO: implement */}
                    </div>
                  );
                };
                """
            },
            new CodeTemplate
            {
                Name = "Spring Boot REST Controller",
                LanguageId = langs.GetValueOrDefault("Java"),
                FrameworkId = fws.GetValueOrDefault("Spring Boot"),
                TemplateType = "component",
                VariablesJson = """["entity_name","base_path"]""",
                Content = """
                package com.example.controller;

                import org.springframework.web.bind.annotation.*;
                import org.springframework.http.ResponseEntity;
                import lombok.RequiredArgsConstructor;
                import java.util.List;

                @RestController
                @RequestMapping("{{base_path}}")
                @RequiredArgsConstructor
                public class {{entity_name}}Controller {
                    private final {{entity_name}}Service service;

                    @GetMapping
                    public ResponseEntity<List<{{entity_name}}Dto>> findAll() {
                        return ResponseEntity.ok(service.findAll());
                    }

                    @PostMapping
                    public ResponseEntity<{{entity_name}}Dto> create(@RequestBody @Valid Create{{entity_name}}Request req) {
                        return ResponseEntity.status(201).body(service.create(req));
                    }
                }
                """
            },
            new CodeTemplate
            {
                Name = "Go HTTP Service Module",
                LanguageId = langs.GetValueOrDefault("Go"),
                FrameworkId = fws.GetValueOrDefault("Gin"),
                TemplateType = "module",
                VariablesJson = """["module_name","port"]""",
                Content = """
                package main

                import (
                    "net/http"
                    "github.com/gin-gonic/gin"
                )

                func main() {
                    r := gin.Default()
                    r.GET("/healthz", func(c *gin.Context) { c.JSON(http.StatusOK, gin.H{"status": "ok"}) })

                    v1 := r.Group("/api/v1")
                    {
                        v1.GET("/{{module_name}}", list{{module_name}})
                        v1.POST("/{{module_name}}", create{{module_name}})
                    }

                    r.Run(":{{port}}")
                }
                """
            }
        );
    }

    private void SeedFileStructureTemplates(Dictionary<string, string> fws)
    {
        db.FileStructureTemplates.AddRange(
            new FileStructureTemplate
            {
                Name = "Clean Architecture (.NET)",
                FrameworkId = fws.GetValueOrDefault("ASP.NET Core"),
                TreeJson = """
                {
                  "src": {
                    "Domain": {"Entities": {}, "ValueObjects": {}, "Interfaces": {}, "Exceptions": {}},
                    "Application": {"Commands": {}, "Queries": {}, "DTOs": {}, "Validators": {}, "Mappings": {}},
                    "Infrastructure": {"Persistence": {}, "ExternalServices": {}, "Messaging": {}},
                    "WebApi": {"Controllers": {}, "Filters": {}, "Middleware": {}}
                  },
                  "tests": {
                    "Domain.Tests": {},
                    "Application.Tests": {},
                    "Infrastructure.Tests": {},
                    "WebApi.Tests": {}
                  },
                  "docs": {},
                  "docker-compose.yml": null,
                  "README.md": null
                }
                """
            },
            new FileStructureTemplate
            {
                Name = "Next.js Full-Stack",
                FrameworkId = fws.GetValueOrDefault("Next.js"),
                TreeJson = """
                {
                  "src": {
                    "app": {"(auth)": {}, "(dashboard)": {}, "api": {}, "layout.tsx": null, "page.tsx": null},
                    "components": {"ui": {}, "forms": {}, "layouts": {}},
                    "lib": {"db.ts": null, "auth.ts": null, "utils.ts": null},
                    "hooks": {},
                    "types": {},
                    "styles": {}
                  },
                  "prisma": {"schema.prisma": null, "migrations": {}},
                  "public": {},
                  "tests": {"__tests__": {}, "e2e": {}},
                  ".env.example": null,
                  "next.config.js": null,
                  "tailwind.config.ts": null
                }
                """
            },
            new FileStructureTemplate
            {
                Name = "FastAPI Microservice",
                FrameworkId = fws.GetValueOrDefault("FastAPI"),
                TreeJson = """
                {
                  "src": {
                    "app": {
                      "api": {"v1": {"endpoints": {}, "dependencies.py": null}},
                      "core": {"config.py": null, "security.py": null},
                      "models": {},
                      "schemas": {},
                      "services": {},
                      "repositories": {},
                      "main.py": null
                    }
                  },
                  "tests": {"unit": {}, "integration": {}, "conftest.py": null},
                  "alembic": {"versions": {}, "env.py": null},
                  "docker": {"Dockerfile": null, "docker-compose.yml": null},
                  "pyproject.toml": null,
                  "README.md": null
                }
                """
            },
            new FileStructureTemplate
            {
                Name = "Spring Boot Microservice",
                FrameworkId = fws.GetValueOrDefault("Spring Boot"),
                TreeJson = """
                {
                  "src/main/java/com/example": {
                    "controller": {},
                    "service": {},
                    "repository": {},
                    "model": {"entity": {}, "dto": {}},
                    "config": {},
                    "exception": {},
                    "Application.java": null
                  },
                  "src/main/resources": {"application.yml": null, "db/migration": {}},
                  "src/test/java/com/example": {"controller": {}, "service": {}, "integration": {}},
                  "Dockerfile": null,
                  "pom.xml": null
                }
                """
            }
        );
    }

    private void SeedCiCdTemplates(Dictionary<string, string> langs)
    {
        db.CiCdTemplates.AddRange(
            new CiCdTemplate
            {
                Name = "GitHub Actions — .NET Build & Test",
                Provider = "github_actions",
                LanguageId = langs.GetValueOrDefault("C#"),
                PipelineYaml = """
                name: .NET CI
                on:
                  push:
                    branches: [main, develop]
                  pull_request:
                    branches: [main]

                jobs:
                  build:
                    runs-on: ubuntu-latest
                    services:
                      postgres:
                        image: postgres:16
                        env:
                          POSTGRES_PASSWORD: test
                        ports: ['5432:5432']
                        options: --health-cmd pg_isready --health-interval 10s --health-timeout 5s --health-retries 5
                    steps:
                      - uses: actions/checkout@v4
                      - uses: actions/setup-dotnet@v4
                        with:
                          dotnet-version: '9.0.x'
                      - run: dotnet restore
                      - run: dotnet build --no-restore --configuration Release
                      - run: dotnet test --no-build --configuration Release --collect:"XPlat Code Coverage"
                      - uses: codecov/codecov-action@v4
                        if: always()
                """
            },
            new CiCdTemplate
            {
                Name = "GitHub Actions — Python CI",
                Provider = "github_actions",
                LanguageId = langs.GetValueOrDefault("Python"),
                PipelineYaml = """
                name: Python CI
                on:
                  push:
                    branches: [main]
                  pull_request:

                jobs:
                  test:
                    runs-on: ubuntu-latest
                    strategy:
                      matrix:
                        python-version: ['3.11', '3.12']
                    steps:
                      - uses: actions/checkout@v4
                      - uses: actions/setup-python@v5
                        with:
                          python-version: ${{ matrix.python-version }}
                      - run: pip install -e ".[dev]"
                      - run: ruff check .
                      - run: mypy src/
                      - run: pytest --cov=src --cov-report=xml -q
                      - uses: codecov/codecov-action@v4
                """
            },
            new CiCdTemplate
            {
                Name = "GitHub Actions — Node.js CI",
                Provider = "github_actions",
                LanguageId = langs.GetValueOrDefault("TypeScript"),
                PipelineYaml = """
                name: Node.js CI
                on:
                  push:
                    branches: [main]
                  pull_request:

                jobs:
                  build:
                    runs-on: ubuntu-latest
                    strategy:
                      matrix:
                        node-version: [20, 22]
                    steps:
                      - uses: actions/checkout@v4
                      - uses: actions/setup-node@v4
                        with:
                          node-version: ${{ matrix.node-version }}
                          cache: 'npm'
                      - run: npm ci
                      - run: npm run lint
                      - run: npm run type-check
                      - run: npm test -- --coverage
                      - run: npm run build
                """
            },
            new CiCdTemplate
            {
                Name = "GitLab CI — .NET",
                Provider = "gitlab_ci",
                LanguageId = langs.GetValueOrDefault("C#"),
                PipelineYaml = """
                stages: [build, test, publish]

                variables:
                  DOTNET_VERSION: '9.0'

                build:
                  stage: build
                  image: mcr.microsoft.com/dotnet/sdk:9.0
                  script:
                    - dotnet restore
                    - dotnet build --configuration Release --no-restore
                  artifacts:
                    paths: [bin/]

                test:
                  stage: test
                  image: mcr.microsoft.com/dotnet/sdk:9.0
                  services:
                    - postgres:16
                  script:
                    - dotnet test --configuration Release --collect:"XPlat Code Coverage"
                  coverage: '/Total\s*\|\s*(\d+\.?\d*)%/'

                publish:
                  stage: publish
                  image: mcr.microsoft.com/dotnet/sdk:9.0
                  script:
                    - dotnet publish -c Release -o out
                  artifacts:
                    paths: [out/]
                  only: [main]
                """
            }
        );
    }

    private void SeedDockerTemplates(Dictionary<string, string> langs, Dictionary<string, string> fws)
    {
        db.DockerTemplates.AddRange(
            new DockerTemplate
            {
                Name = "ASP.NET Core Multi-Stage",
                LanguageId = langs.GetValueOrDefault("C#"),
                FrameworkId = fws.GetValueOrDefault("ASP.NET Core"),
                DockerfileContent = """
                FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
                WORKDIR /src
                COPY *.sln .
                COPY src/*/*.csproj ./
                RUN for f in *.csproj; do mkdir -p "src/$(basename $f .csproj)" && mv "$f" "src/$(basename $f .csproj)/"; done
                RUN dotnet restore
                COPY . .
                RUN dotnet publish src/WebApi/WebApi.csproj -c Release -o /app/publish --no-restore

                FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
                WORKDIR /app
                COPY --from=build /app/publish .
                EXPOSE 8080
                HEALTHCHECK CMD curl -f http://localhost:8080/healthz || exit 1
                ENTRYPOINT ["dotnet", "WebApi.dll"]
                """,
                ComposeContent = """
                services:
                  api:
                    build: .
                    ports: ['8080:8080']
                    environment:
                      - ConnectionStrings__Default=Host=db;Database=app;Username=app;Password=secret
                    depends_on:
                      db:
                        condition: service_healthy
                  db:
                    image: postgres:16-alpine
                    environment:
                      POSTGRES_DB: app
                      POSTGRES_USER: app
                      POSTGRES_PASSWORD: secret
                    volumes: ['pgdata:/var/lib/postgresql/data']
                    healthcheck:
                      test: ['CMD-SHELL', 'pg_isready -U app']
                      interval: 5s
                volumes:
                  pgdata:
                """
            },
            new DockerTemplate
            {
                Name = "Python FastAPI Multi-Stage",
                LanguageId = langs.GetValueOrDefault("Python"),
                FrameworkId = fws.GetValueOrDefault("FastAPI"),
                DockerfileContent = """
                FROM python:3.12-slim AS builder
                WORKDIR /app
                RUN pip install --no-cache-dir uv
                COPY pyproject.toml uv.lock ./
                RUN uv sync --frozen --no-dev
                COPY src/ src/

                FROM python:3.12-slim
                WORKDIR /app
                COPY --from=builder /app/.venv /app/.venv
                COPY --from=builder /app/src /app/src
                ENV PATH="/app/.venv/bin:$PATH"
                EXPOSE 8000
                HEALTHCHECK CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/health')" || exit 1
                CMD ["uvicorn", "src.app.main:app", "--host", "0.0.0.0", "--port", "8000"]
                """,
                ComposeContent = """
                services:
                  api:
                    build: .
                    ports: ['8000:8000']
                    environment:
                      DATABASE_URL: postgresql+asyncpg://app:secret@db:5432/app
                    depends_on:
                      db:
                        condition: service_healthy
                  db:
                    image: postgres:16-alpine
                    environment:
                      POSTGRES_DB: app
                      POSTGRES_USER: app
                      POSTGRES_PASSWORD: secret
                    healthcheck:
                      test: ['CMD-SHELL', 'pg_isready -U app']
                volumes:
                  pgdata:
                """
            },
            new DockerTemplate
            {
                Name = "Next.js Production",
                LanguageId = langs.GetValueOrDefault("TypeScript"),
                FrameworkId = fws.GetValueOrDefault("Next.js"),
                DockerfileContent = """
                FROM node:22-alpine AS deps
                WORKDIR /app
                COPY package.json package-lock.json ./
                RUN npm ci --only=production

                FROM node:22-alpine AS builder
                WORKDIR /app
                COPY --from=deps /app/node_modules ./node_modules
                COPY . .
                RUN npm run build

                FROM node:22-alpine
                WORKDIR /app
                ENV NODE_ENV=production
                COPY --from=builder /app/.next/standalone ./
                COPY --from=builder /app/.next/static ./.next/static
                COPY --from=builder /app/public ./public
                EXPOSE 3000
                CMD ["node", "server.js"]
                """
            }
        );
    }

    private void SeedTestTemplates(Dictionary<string, string> fws)
    {
        db.TestTemplates.AddRange(
            new TestTemplate
            {
                Name = "xUnit Unit Test",
                TestType = "unit",
                FrameworkId = fws.GetValueOrDefault("ASP.NET Core"),
                TestFramework = "xunit",
                TemplateContent = """
                using Xunit;
                using Moq;
                using FluentAssertions;

                namespace {{namespace}}.Tests;

                public class {{class_name}}Tests
                {
                    private readonly Mock<I{{dependency}}> _mockDep;
                    private readonly {{class_name}} _sut;

                    public {{class_name}}Tests()
                    {
                        _mockDep = new Mock<I{{dependency}}>();
                        _sut = new {{class_name}}(_mockDep.Object);
                    }

                    [Fact]
                    public async Task {{method}}_WhenValidInput_ReturnsExpectedResult()
                    {
                        // Arrange
                        _mockDep.Setup(x => x.GetAsync(It.IsAny<string>(), default))
                            .ReturnsAsync(new {{entity}}());

                        // Act
                        var result = await _sut.{{method}}("test-id");

                        // Assert
                        result.Should().NotBeNull();
                    }

                    [Fact]
                    public async Task {{method}}_WhenNotFound_ThrowsNotFoundException()
                    {
                        // Arrange
                        _mockDep.Setup(x => x.GetAsync(It.IsAny<string>(), default))
                            .ReturnsAsync(({{entity}}?)null);

                        // Act & Assert
                        await Assert.ThrowsAsync<NotFoundException>(
                            () => _sut.{{method}}("missing-id"));
                    }
                }
                """
            },
            new TestTemplate
            {
                Name = "pytest Unit Test",
                TestType = "unit",
                FrameworkId = fws.GetValueOrDefault("FastAPI"),
                TestFramework = "pytest",
                TemplateContent = """
                import pytest
                from unittest.mock import AsyncMock, patch
                from httpx import AsyncClient
                from src.app.main import app

                @pytest.fixture
                def mock_service():
                    with patch("src.app.services.{{service_name}}") as mock:
                        mock.return_value = AsyncMock()
                        yield mock.return_value

                @pytest.mark.asyncio
                async def test_{{endpoint}}_returns_200(mock_service):
                    mock_service.get_all.return_value = [{"id": "1", "name": "test"}]
                    async with AsyncClient(app=app, base_url="http://test") as client:
                        response = await client.get("/api/v1/{{resource}}")
                    assert response.status_code == 200
                    assert len(response.json()) == 1

                @pytest.mark.asyncio
                async def test_{{endpoint}}_not_found(mock_service):
                    mock_service.get_by_id.return_value = None
                    async with AsyncClient(app=app, base_url="http://test") as client:
                        response = await client.get("/api/v1/{{resource}}/999")
                    assert response.status_code == 404
                """
            },
            new TestTemplate
            {
                Name = "Jest + React Testing Library",
                TestType = "unit",
                FrameworkId = fws.GetValueOrDefault("React"),
                TestFramework = "jest",
                TemplateContent = """
                import { render, screen, fireEvent, waitFor } from '@testing-library/react';
                import { {{component_name}} } from './{{component_name}}';

                describe('{{component_name}}', () => {
                  const defaultProps = {
                    // TODO: define default props
                  };

                  it('renders without crashing', () => {
                    render(<{{component_name}} {...defaultProps} />);
                    expect(screen.getByTestId('{{component_name}}')).toBeInTheDocument();
                  });

                  it('displays data when loaded', async () => {
                    render(<{{component_name}} {...defaultProps} />);
                    await waitFor(() => {
                      expect(screen.getByText(/expected text/i)).toBeInTheDocument();
                    });
                  });

                  it('handles user interaction', () => {
                    const onAction = jest.fn();
                    render(<{{component_name}} {...defaultProps} onAction={onAction} />);
                    fireEvent.click(screen.getByRole('button', { name: /submit/i }));
                    expect(onAction).toHaveBeenCalledTimes(1);
                  });
                });
                """
            },
            new TestTemplate
            {
                Name = "xUnit Integration Test",
                TestType = "integration",
                FrameworkId = fws.GetValueOrDefault("ASP.NET Core"),
                TestFramework = "xunit",
                TemplateContent = """
                using Microsoft.AspNetCore.Mvc.Testing;
                using Microsoft.Extensions.DependencyInjection;
                using System.Net;
                using System.Net.Http.Json;
                using Xunit;
                using FluentAssertions;

                namespace {{namespace}}.IntegrationTests;

                public class {{controller}}Tests : IClassFixture<WebApplicationFactory<Program>>
                {
                    private readonly HttpClient _client;

                    public {{controller}}Tests(WebApplicationFactory<Program> factory)
                    {
                        _client = factory.WithWebHostBuilder(builder =>
                        {
                            builder.ConfigureServices(services =>
                            {
                                // Replace real DB with in-memory
                                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("test"));
                            });
                        }).CreateClient();
                    }

                    [Fact]
                    public async Task Get_ReturnsOkWithList()
                    {
                        var response = await _client.GetAsync("/api/{{resource}}");
                        response.StatusCode.Should().Be(HttpStatusCode.OK);
                    }

                    [Fact]
                    public async Task Post_ValidPayload_Returns201()
                    {
                        var payload = new { Name = "Test", Description = "Integration test" };
                        var response = await _client.PostAsJsonAsync("/api/{{resource}}", payload);
                        response.StatusCode.Should().Be(HttpStatusCode.Created);
                    }
                }
                """
            }
        );
    }

    private void SeedIaCTemplates(Dictionary<string, string> clouds)
    {
        db.IaCTemplates.AddRange(
            new IaCTemplate
            {
                Name = "Terraform — AWS ECS Fargate",
                CloudProviderId = clouds.GetValueOrDefault("AWS"),
                Tool = "terraform",
                TemplateContent = """
                terraform {
                  required_version = ">= 1.6"
                  required_providers {
                    aws = { source = "hashicorp/aws", version = "~> 5.0" }
                  }
                  backend "s3" {
                    bucket = "{{state_bucket}}"
                    key    = "{{project_name}}/terraform.tfstate"
                    region = "{{region}}"
                  }
                }

                provider "aws" { region = var.region }

                module "vpc" {
                  source  = "terraform-aws-modules/vpc/aws"
                  version = "5.0"
                  name    = "${var.project}-vpc"
                  cidr    = "10.0.0.0/16"
                  azs             = ["${var.region}a", "${var.region}b"]
                  private_subnets = ["10.0.1.0/24", "10.0.2.0/24"]
                  public_subnets  = ["10.0.101.0/24", "10.0.102.0/24"]
                  enable_nat_gateway = true
                }

                module "ecs" {
                  source       = "terraform-aws-modules/ecs/aws"
                  cluster_name = "${var.project}-cluster"
                }

                resource "aws_ecs_service" "app" {
                  name            = "${var.project}-service"
                  cluster         = module.ecs.cluster_id
                  task_definition = aws_ecs_task_definition.app.arn
                  desired_count   = var.desired_count
                  launch_type     = "FARGATE"
                  network_configuration {
                    subnets         = module.vpc.private_subnets
                    security_groups = [aws_security_group.app.id]
                  }
                }
                """
            },
            new IaCTemplate
            {
                Name = "Terraform — Azure Container Apps",
                CloudProviderId = clouds.GetValueOrDefault("Azure"),
                Tool = "terraform",
                TemplateContent = """
                terraform {
                  required_version = ">= 1.6"
                  required_providers {
                    azurerm = { source = "hashicorp/azurerm", version = "~> 3.0" }
                  }
                }

                provider "azurerm" { features {} }

                resource "azurerm_resource_group" "rg" {
                  name     = "rg-${var.project}-${var.env}"
                  location = var.location
                }

                resource "azurerm_container_app_environment" "env" {
                  name                = "cae-${var.project}"
                  location            = azurerm_resource_group.rg.location
                  resource_group_name = azurerm_resource_group.rg.name
                }

                resource "azurerm_container_app" "app" {
                  name                         = "ca-${var.project}"
                  container_app_environment_id = azurerm_container_app_environment.env.id
                  resource_group_name          = azurerm_resource_group.rg.name
                  revision_mode                = "Single"

                  template {
                    container {
                      name   = var.project
                      image  = "${var.acr_login_server}/${var.project}:${var.image_tag}"
                      cpu    = 0.5
                      memory = "1Gi"
                    }
                    min_replicas = 1
                    max_replicas = 10
                  }

                  ingress {
                    external_enabled = true
                    target_port      = 8080
                    traffic_weight { percentage = 100 latest_revision = true }
                  }
                }
                """
            },
            new IaCTemplate
            {
                Name = "Terraform — GCP Cloud Run",
                CloudProviderId = clouds.GetValueOrDefault("GCP"),
                Tool = "terraform",
                TemplateContent = """
                terraform {
                  required_version = ">= 1.6"
                  required_providers {
                    google = { source = "hashicorp/google", version = "~> 5.0" }
                  }
                }

                provider "google" {
                  project = var.gcp_project
                  region  = var.region
                }

                resource "google_cloud_run_v2_service" "app" {
                  name     = var.project
                  location = var.region

                  template {
                    containers {
                      image = "${var.region}-docker.pkg.dev/${var.gcp_project}/${var.project}/${var.project}:${var.image_tag}"
                      resources {
                        limits = { cpu = "1", memory = "512Mi" }
                      }
                      ports { container_port = 8080 }
                    }
                    scaling {
                      min_instance_count = 0
                      max_instance_count = 10
                    }
                  }
                }

                resource "google_cloud_run_v2_service_iam_member" "public" {
                  name     = google_cloud_run_v2_service.app.name
                  location = var.region
                  role     = "roles/run.invoker"
                  member   = "allUsers"
                }
                """
            }
        );
    }

    private void SeedDocumentationTemplates()
    {
        db.DocumentationTemplates.AddRange(
            new DocumentationTemplate
            {
                Name = "README.md",
                DocType = "readme",
                TemplateContent = """
                # {{project_name}}

                > {{one_liner}}

                ## Prerequisites
                - {{runtime}} {{version}}+
                - Docker & Docker Compose
                - PostgreSQL 16+

                ## Quick Start
                ```bash
                git clone {{repo_url}}
                cd {{project_name}}
                cp .env.example .env
                docker compose up -d
                {{run_command}}
                ```

                ## Architecture
                {{architecture_overview}}

                ## Project Structure
                ```
                {{file_tree}}
                ```

                ## API Documentation
                Once running, visit `http://localhost:{{port}}/swagger`

                ## Testing
                ```bash
                {{test_command}}
                ```

                ## Deployment
                See [docs/deployment.md](docs/deployment.md)

                ## Contributing
                1. Fork → Branch (`feature/xyz`) → Commit → Push → PR
                2. All PRs require passing CI and one approval

                ## License
                {{license}}
                """
            },
            new DocumentationTemplate
            {
                Name = "Architecture Decision Record (ADR)",
                DocType = "adr",
                TemplateContent = """
                # ADR-{{number}}: {{title}}

                **Date:** {{date}}
                **Status:** Proposed | Accepted | Deprecated | Superseded by ADR-xxx
                **Deciders:** {{deciders}}

                ## Context
                {{context_description}}

                ## Decision
                We will {{decision}}.

                ## Consequences

                ### Positive
                - {{positive_1}}
                - {{positive_2}}

                ### Negative
                - {{negative_1}}

                ### Risks
                - {{risk_1}}

                ## Alternatives Considered
                | Option | Pros | Cons |
                |--------|------|------|
                | {{alt_1}} | {{alt_1_pros}} | {{alt_1_cons}} |
                | {{alt_2}} | {{alt_2_pros}} | {{alt_2_cons}} |

                ## References
                - {{reference_1}}
                """
            },
            new DocumentationTemplate
            {
                Name = "API Documentation (OpenAPI Supplement)",
                DocType = "api_doc",
                TemplateContent = """
                # {{api_name}} API Documentation

                **Base URL:** `{{base_url}}`
                **Version:** {{version}}
                **Authentication:** {{auth_method}}

                ## Overview
                {{api_overview}}

                ## Authentication
                ```
                Authorization: Bearer <token>
                ```
                Obtain tokens via `POST /auth/token` with client credentials.

                ## Rate Limiting
                - **Limit:** {{rate_limit}} requests per minute
                - **Headers:** `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`

                ## Error Format
                ```json
                {
                  "type": "https://api.example.com/errors/{{error_type}}",
                  "title": "{{error_title}}",
                  "status": {{status_code}},
                  "detail": "{{error_detail}}",
                  "traceId": "{{trace_id}}"
                }
                ```

                ## Endpoints
                ### {{resource}}
                | Method | Path | Description |
                |--------|------|-------------|
                | GET    | /api/v1/{{resource}} | List all |
                | GET    | /api/v1/{{resource}}/:id | Get by ID |
                | POST   | /api/v1/{{resource}} | Create |
                | PUT    | /api/v1/{{resource}}/:id | Update |
                | DELETE | /api/v1/{{resource}}/:id | Delete |

                ## Pagination
                ```
                GET /api/v1/{{resource}}?page=1&pageSize=20&sort=createdAt:desc
                ```
                """
            },
            new DocumentationTemplate
            {
                Name = "Runbook",
                DocType = "runbook",
                TemplateContent = """
                # Runbook: {{service_name}}

                **Last Updated:** {{date}}
                **On-Call Team:** {{team}}
                **Escalation:** {{escalation_path}}

                ## Service Overview
                - **Purpose:** {{purpose}}
                - **SLA:** {{sla}}
                - **Dependencies:** {{dependencies}}

                ## Health Checks
                | Endpoint | Expected | Interval |
                |----------|----------|----------|
                | /healthz | 200 OK | 10s |
                | /readyz  | 200 OK | 30s |

                ## Common Alerts & Remediation

                ### Alert: High Error Rate (>1%)
                1. Check logs: `kubectl logs -l app={{service_name}} --tail=100`
                2. Check dependencies: `curl {{dependency_health_url}}`
                3. If DB issue → check connection pool: `SELECT * FROM pg_stat_activity;`
                4. Restart if needed: `kubectl rollout restart deployment/{{service_name}}`

                ### Alert: High Latency (p99 > 500ms)
                1. Check resource usage: `kubectl top pods -l app={{service_name}}`
                2. Scale up: `kubectl scale deployment/{{service_name}} --replicas={{max_replicas}}`
                3. Check DB slow queries: `SELECT * FROM pg_stat_statements ORDER BY mean_time DESC LIMIT 10;`

                ### Alert: Pod CrashLoopBackOff
                1. Check events: `kubectl describe pod <pod-name>`
                2. Check logs: `kubectl logs <pod-name> --previous`
                3. Common causes: OOM (increase memory), config error (check configmaps), dependency down

                ## Disaster Recovery
                - **RPO:** {{rpo}}  |  **RTO:** {{rto}}
                - **Backup:** {{backup_strategy}}
                - **Restore:** {{restore_command}}
                """
            }
        );
    }
}
