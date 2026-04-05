# Multi-stage Dockerfile for EncounterService
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Hms.EncounterService/Hms.EncounterService.csproj", "Hms.EncounterService/"]
COPY ["Hms.SharedKernel/Hms.SharedKernel.csproj", "Hms.SharedKernel/"]
RUN dotnet restore "Hms.EncounterService/Hms.EncounterService.csproj"
COPY . .
WORKDIR "/src/Hms.EncounterService"
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
EXPOSE 5102
ENV ASPNETCORE_URLS=http://+:5102
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD wget -qO- http://localhost:5102/healthz || exit 1

ENTRYPOINT ["dotnet", "Hms.EncounterService.dll"]