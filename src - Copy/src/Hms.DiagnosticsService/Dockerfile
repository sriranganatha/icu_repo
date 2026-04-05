# Multi-stage Dockerfile for DiagnosticsService
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Hms.DiagnosticsService/Hms.DiagnosticsService.csproj", "Hms.DiagnosticsService/"]
COPY ["Hms.SharedKernel/Hms.SharedKernel.csproj", "Hms.SharedKernel/"]
RUN dotnet restore "Hms.DiagnosticsService/Hms.DiagnosticsService.csproj"
COPY . .
WORKDIR "/src/Hms.DiagnosticsService"
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
EXPOSE 5105
ENV ASPNETCORE_URLS=http://+:5105
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD wget -qO- http://localhost:5105/healthz || exit 1

ENTRYPOINT ["dotnet", "Hms.DiagnosticsService.dll"]