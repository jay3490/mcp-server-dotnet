# Multi-stage build for the Streamable HTTP host.
#   docker build -t engineering-mcp .
#   docker run -p 8080:8080 \
#     -e EngineeringMcp__Auth__Tokens__0__Name=me \
#     -e EngineeringMcp__Auth__Tokens__0__Token="$(openssl rand -hex 32)" \
#     engineering-mcp

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the package layer is reused when only source files change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/EngineeringMcp.Core/EngineeringMcp.Core.csproj src/EngineeringMcp.Core/
COPY src/EngineeringMcp.Http/EngineeringMcp.Http.csproj src/EngineeringMcp.Http/
RUN dotnet restore src/EngineeringMcp.Http/EngineeringMcp.Http.csproj

COPY src/EngineeringMcp.Core/ src/EngineeringMcp.Core/
COPY src/EngineeringMcp.Http/ src/EngineeringMcp.Http/
COPY samples/ samples/
RUN dotnet publish src/EngineeringMcp.Http/EngineeringMcp.Http.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app ./

# The demo database is created on first start and the audit log is written under /app/logs;
# those are the only paths the unprivileged user can write to.
RUN mkdir -p /app/logs /app/samples/data \
    && chown -R "$APP_UID" /app/logs /app/samples/data

USER $APP_UID

# Unprefixed "Urls" outranks the localhost-only value in appsettings.json.
ENV Urls=http://+:8080 \
    DOTNET_NOLOGO=1 \
    EngineeringMcp__Git__RepositoryPath=""

EXPOSE 8080

ENTRYPOINT ["dotnet", "engineering-mcp-http.dll"]
