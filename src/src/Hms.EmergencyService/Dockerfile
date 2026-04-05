# Multi-stage Dockerfile for EmergencyService
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Hms.EmergencyService/Hms.EmergencyService.csproj", "Hms.EmergencyService/"]
COPY ["Hms.SharedKernel/Hms.SharedKernel.csproj", "Hms.SharedKernel/"]
RUN dotnet restore "Hms.EmergencyService/Hms.EmergencyService.csproj"
COPY . .
WORKDIR "/src/Hms.EmergencyService"
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
EXPOSE 5104
ENV ASPNETCORE_URLS=http://+:5104
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD wget -qO- http://localhost:5104/healthz || exit 1

ENTRYPOINT ["dotnet", "Hms.EmergencyService.dll"]