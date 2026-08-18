# The container serves the HTTP transport. stdio is for a desktop host that launches the
# process itself and owns its stdin and stdout, which is not a thing a container gives you.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG VERSION=0.0.0-docker

WORKDIR /src

# Restore on its own layer, keyed only on the files that decide the dependency graph. A
# source edit then re-uses the restore rather than downloading the world again.
COPY global.json Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/McpCarbonServer/McpCarbonServer.csproj src/McpCarbonServer/
RUN dotnet restore src/McpCarbonServer/McpCarbonServer.csproj -p:UseLocalGhgAccounting=false

COPY src/ src/

# UseLocalGhgAccounting is pinned off rather than left to resolve itself. No sibling
# checkout exists here so it would resolve that way anyway, but a build that silently fell
# back to somebody's working copy is exactly what an image must never do.
#
# Not trimmed and not AOT: tools, resources and prompts are discovered by reflection over
# this assembly, and a trimmer has no way to see that. Trimming would produce an image that
# starts cleanly and serves an empty tool list.
RUN dotnet publish src/McpCarbonServer/McpCarbonServer.csproj \
    -c Release \
    --no-restore \
    -p:UseLocalGhgAccounting=false \
    -p:Version=${VERSION} \
    -o /app


FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# APP_UID is defined by the base image. Running as root in a container that speaks HTTP to
# the outside is a default worth not accepting.
USER $APP_UID

# /health is the only endpoint that answers without a protocol handshake, which is what
# makes it usable from here.
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -q -O- http://127.0.0.1:8080/health || exit 1

# Invoked through the muxer rather than the apphost: the publish is portable IL with no
# runtime identifier, so the apphost binary it produces is built for the SDK image's libc
# and would not run on this musl-based one.
ENTRYPOINT ["dotnet", "mcp-carbon-server.dll", "--http"]
