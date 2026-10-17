# syntax=docker/dockerfile:1.7
# ReworkGuard API.
#   docker build -t reworkguard-api backend                    -> runtime image (default target)
#   docker build -t reworkguard-migrator --target migrator backend -> EF Core migrations bundle
#
# Migrations: the runtime image applies them at startup ONLY when Database__MigrateOnStartup=true
# (local compose sets it). Production runs the `migrator` image as a one-off task before the
# new API version is rolled out, and keeps Database__MigrateOnStartup=false.

ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
COPY .config/ .config/
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ReworkGuard.Domain/ReworkGuard.Domain.csproj src/ReworkGuard.Domain/
COPY src/ReworkGuard.Application/ReworkGuard.Application.csproj src/ReworkGuard.Application/
COPY src/ReworkGuard.Infrastructure/ReworkGuard.Infrastructure.csproj src/ReworkGuard.Infrastructure/
COPY src/ReworkGuard.Api/ReworkGuard.Api.csproj src/ReworkGuard.Api/
RUN dotnet restore src/ReworkGuard.Api/ReworkGuard.Api.csproj && dotnet tool restore
COPY src/ src/
RUN dotnet publish src/ReworkGuard.Api/ReworkGuard.Api.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM build AS bundle
ARG TARGETARCH
RUN rid="linux-$( [ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64 )" \
 && dotnet ef migrations bundle -p src/ReworkGuard.Infrastructure -s src/ReworkGuard.Api \
      --configuration Release --self-contained -r "$rid" -o /bundle/efbundle --force

# One-off migration runner. Needs ConnectionStrings__Default (a user allowed to run DDL and create triggers).
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION} AS migrator
WORKDIR /migrator
COPY --from=bundle --chown=root:root /bundle/efbundle ./efbundle
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec ./efbundle --connection \"$ConnectionStrings__Default\" \"$@\"", "efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
# Pick up Debian security fixes newer than the base image (keeps the Trivy gate green between base releases).
# APPLY_OS_UPDATES=false skips it for builds without access to deb.debian.org.
ARG APPLY_OS_UPDATES=true
RUN if [ "$APPLY_OS_UPDATES" = "true" ]; then \
      sed -i 's|http://|https://|g' /etc/apt/sources.list.d/debian.sources \
      && apt-get update --error-on=any \
      && apt-get upgrade -y --no-install-recommends \
      && rm -rf /var/lib/apt/lists/*; \
    fi
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=0 \
    Database__MigrateOnStartup=false \
    Database__SeedOnStartup=false
COPY --from=build --chown=root:root /app/publish ./
# Non-root (the `app` user, UID 1654, ships with the .NET 8 images). Files stay root-owned and read-only.
USER $APP_UID
EXPOSE 8080
# No curl in the base image: probe liveness with bash's /dev/tcp.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "ReworkGuard.Api.dll"]
