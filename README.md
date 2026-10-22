# engineering-mcp

A [Model Context Protocol](https://modelcontextprotocol.io) server in C# / .NET 8 that gives MCP
clients **read-only** access to the data an engineering team looks at every day: the operational
database, the internal docs, the git history and the health endpoints of running services.

It is built on the official [`ModelContextProtocol`](https://www.nuget.org/packages/ModelContextProtocol)
C# SDK and ships two hosts that share one core library:

| Host | Transport | Use it for |
| --- | --- | --- |
| `engineering-mcp-stdio` | stdio | A client that launches the server as a local process |
| `engineering-mcp-http` | Streamable HTTP at `/mcp` | A shared server behind bearer-token auth (Docker image included) |

```
                     ┌────────────────────────── EngineeringMcp.Core ───────────────────────────┐
 MCP client ──stdio──┤                                                                          │
                     │  ToolCallPipeline: allow-list → per-tool timeout → audit log (JSONL)     │
 MCP client ──HTTP───┤      │                                                                   │
   (bearer token,    │      ├── sql_query / sql_schema ──── SqlValidator ── SQLite | PostgreSQL │
    origin check)    │      ├── search_docs / read_doc ──── DocsSandbox ── BM25 index           │
                     │      ├── git_log / git_diff_summary ─ LibGit2Sharp                       │
                     │      └── http_status ─────────────── allow-listed URLs, no redirects     │
                     │  Resources: docs://{+path}     Prompts: explain_schema,                  │
                     │                                         summarise_recent_changes         │
                     └──────────────────────────────────────────────────────────────────────────┘
```

## Tools

Every tool is annotated `readOnlyHint: true` and `destructiveHint: false`. Results are markdown
(tables for rows and commits) so they read well in any client. Failures come back as tool
results with `isError: true` and a short category, never as protocol errors, so the model can
see why a call failed and try again.

| Tool | What it does |
| --- | --- |
| `sql_query` | Runs one `SELECT` (or `WITH … SELECT`) and returns a markdown table, capped at `MaxRows` |
| `sql_schema` | Lists tables and views with column types, nullability and primary keys |
| `search_docs` | BM25 search over the docs folder with highlighted snippets |
| `read_doc` | Returns one markdown document by its path relative to the docs root |
| `git_log` | Recent commits, optionally from a revision and limited to a path |
| `git_diff_summary` | Files added, modified, renamed and deleted between two revisions, with line counts |
| `http_status` | Probes the configured health endpoints and reports status code and latency |

Docs are also published as MCP **resources** (`docs://runbooks/deploying.md`, …) through a
resource template, and two **prompts** wrap the tools for common requests: `explain_schema` and
`summarise_recent_changes`.

### Example output

These are real responses from the HTTP host running against the bundled demo data.

`sql_query` with `SELECT name, tier FROM services ORDER BY name LIMIT 5`:

```
| name | tier |
| --- | --- |
| api-gateway | 1 |
| auth | 1 |
| billing | 1 |
| feature-flags | 2 |
| ledger | 1 |

5 row(s) in 4 ms.
```

`sql_query` with `DELETE FROM services` (`isError: true`):

```
Error (invalid_argument): Query rejected: Only SELECT statements (optionally starting with WITH) are allowed.
```

`search_docs` with `database failover`, limit 2:

```
1. **Runbook: PostgreSQL failover** (`runbooks/database-failover.md`, score 3.616)
   Runbook: PostgreSQL **failover** Applies to the billing, ledger and feature-flags **databases**, which run a primary with one synchronous standby. When to fail over Fail over when the primary is…
2. **Engineering onboarding** (`onboarding.md`, score 0.817)
   Engineering onboarding Welcome to the platform group. This page lists what to set up in your first week. Access - Request access to the engineering **database** through the access portal. Read…
```

`read_doc` with `../appsettings.json` (`isError: true`):

```
Error (forbidden): Paths may not contain '..' segments.
```

## Safety model

Read-only is enforced in layers, so no single check has to be perfect.

**SQL**
- A dialect-aware tokenizer (SQLite and PostgreSQL quoting, dollar-quoted strings, nested block
  comments) feeds a validator that accepts exactly one statement, which must start with `SELECT`
  or `WITH`. Write and DDL keywords anywhere outside strings and identifiers are rejected, as are
  row-locking clauses (`FOR UPDATE`, `FOR SHARE`, …) and side-effecting functions such as
  `pg_sleep`, `pg_read_file`, `dblink` or `load_extension`.
- SQLite connections open with `Mode=ReadOnly` and `PRAGMA query_only = ON`.
- PostgreSQL runs every query inside `SET TRANSACTION READ ONLY` with `statement_timeout` and
  `lock_timeout` set locally, and the transaction is always rolled back.
- Rows are capped by the server (`MaxRows`) whatever the caller asks for; the result says when
  it was truncated.

Use a database role with `SELECT`-only grants as well. The validator is a second line, not the
first.

**Docs**
- Paths must be relative, may not contain `..`, control characters or segments ending in a dot
  or space, and must resolve inside the docs root after normalisation.
- Symbolic links and junctions are not followed, only listed extensions (`.md`, `.markdown`) are
  readable, and documents over `MaxDocumentBytes` are refused.

**Health checks**
- Only URLs in the configured allow-list are ever requested; a caller picks one by name.
- Redirects are not followed, so an allowed endpoint cannot bounce the request elsewhere.

**Every call**
- `Tools:Allowed` exposes a subset of tools; an unknown name fails startup, so a typo cannot
  silently disable the allow-list.
- Each tool has a timeout (`Tools:Timeouts`, falling back to `DefaultTimeoutSeconds`).
- Each call is written to a rolling JSON-lines audit file with the tool, caller, arguments (long
  values cut short), outcome and duration.

**HTTP host**
- `/mcp` requires `Authorization: Bearer <token>`. Tokens come from configuration, are kept only
  as SHA-256 digests and compared in constant time. Startup fails if none are configured, if a
  token is shorter than 32 characters or if two share a name. The token's name is recorded as the
  caller in the audit log.
- Browser requests whose `Origin` is not in `Http:AllowedOrigins` get `403`, as the transport
  specification recommends against DNS rebinding. Non-browser clients send no `Origin` and are
  unaffected.
- `/healthz` is anonymous and reveals only the server name and version.

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The demo SQLite
database is created from `samples/data/seed.sql` on first start, and `samples/docs` holds a small
set of runbooks and ADRs to search.

```bash
dotnet build
dotnet test
```

### stdio

```bash
dotnet run --project src/EngineeringMcp.Stdio
```

Logs go to stderr, so stdout carries only protocol messages. Most MCP clients take a server entry
like this:

```json
{
  "mcpServers": {
    "engineering": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/mcp-server-dotnet/src/EngineeringMcp.Stdio"],
      "env": {
        "EngineeringMcp__Git__RepositoryPath": "/path/to/your/repo"
      }
    }
  }
}
```

For a faster start, `dotnet publish src/EngineeringMcp.Stdio -c Release -o out` and point
`command` at `out/engineering-mcp-stdio`.

To poke at it interactively, the MCP Inspector works against either host:

```bash
npx @modelcontextprotocol/inspector dotnet run --project src/EngineeringMcp.Stdio
```

### Streamable HTTP

```bash
export EngineeringMcp__Auth__Tokens__0__Name=me
export EngineeringMcp__Auth__Tokens__0__Token="$(openssl rand -hex 32)"
dotnet run --project src/EngineeringMcp.Http
```

The server listens on `http://localhost:5080`.

```bash
curl -s localhost:5080/healthz
# {"status":"ok","server":"engineering-mcp","version":"0.1.0"}

curl -s -o /dev/null -w "%{http_code}\n" -X POST localhost:5080/mcp
# 401

curl -s -X POST localhost:5080/mcp \
  -H "Authorization: Bearer $EngineeringMcp__Auth__Tokens__0__Token" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'
```

### Docker

The image runs the HTTP host as the unprivileged `app` user on port 8080. Git tools are off
unless a repository is mounted and configured.

```bash
docker build -t engineering-mcp .
docker run -p 8080:8080 \
  -e EngineeringMcp__Auth__Tokens__0__Name=me \
  -e EngineeringMcp__Auth__Tokens__0__Token="$(openssl rand -hex 32)" \
  -v "$PWD:/repo:ro" -e EngineeringMcp__Git__RepositoryPath=/repo \
  engineering-mcp
```

## Configuration

All settings live under `EngineeringMcp` in `appsettings.json` and can be overridden with
environment variables (`EngineeringMcp__Database__Provider=Postgres`). Relative paths resolve
against the content root.

| Key | Default | Meaning |
| --- | --- | --- |
| `Database:Provider` | `Sqlite` | `Sqlite` or `Postgres` |
| `Database:ConnectionString` | `Data Source=samples/data/demo.db` | Use a read-only role for Postgres |
| `Database:SeedScript` | `samples/data/seed.sql` | SQLite only: creates the demo database if it is missing |
| `Database:MaxRows` | `200` | Hard cap on rows returned by `sql_query` |
| `Database:CommandTimeoutSeconds` | `10` | Statement timeout |
| `Docs:Root` | `samples/docs` | Folder searched and served; empty disables the docs tools |
| `Docs:Extensions` | `.md`, `.markdown` | Readable file types |
| `Docs:MaxDocumentBytes` | `262144` | Larger documents are refused |
| `Docs:MaxSearchResults` | `20` | Cap on `search_docs` results |
| `Git:RepositoryPath` | `.` | Repository for the git tools; empty disables them |
| `Git:MaxCommits` | `50` | Cap on `git_log` |
| `HealthChecks:Endpoints` | `[]` | `{ "Name", "Url", "ExpectedStatus" }` entries; the only URLs ever requested |
| `HealthChecks:TimeoutSeconds` | `5` | Per-request timeout |
| `Tools:Allowed` | all tools | Tool names to expose |
| `Tools:DefaultTimeoutSeconds` | `30` | Timeout for tools without their own entry |
| `Tools:Timeouts` | `sql_query: 15`, `http_status: 10` | Per-tool timeouts in seconds |
| `Audit:Path` | `logs/audit-.jsonl` | Rolling audit file; empty disables it |
| `Audit:RetainedFileCount` | `14` | Days of audit files kept |
| `Auth:Tokens` | `[]` | HTTP only: `{ "Name", "Token" }` entries, tokens at least 32 characters |
| `Http:AllowedOrigins` | `[]` | HTTP only: browser origins allowed to call `/mcp` |

Example: expose only the docs and health tools, with two endpoints to watch.

```json
{
  "EngineeringMcp": {
    "Tools": { "Allowed": ["search_docs", "read_doc", "http_status"] },
    "HealthChecks": {
      "Endpoints": [
        { "Name": "billing", "Url": "https://billing.internal/healthz" },
        { "Name": "auth", "Url": "https://auth.internal/ready", "ExpectedStatus": 204 }
      ]
    }
  }
}
```

## Project layout

```
src/
  EngineeringMcp.Core/    tools, resources, prompts, validation, sandboxing, audit pipeline
    Sql/                  tokenizer, validator, SQLite and PostgreSQL read-only sessions
    Docs/                 sandbox, analyzer, BM25 index, snippets
    Git/                  LibGit2Sharp history and diff summaries
    Health/               allow-listed endpoint probes
    Server/               tool pipeline, policy, audit log, resources, prompts
  EngineeringMcp.Stdio/   stdio host
  EngineeringMcp.Http/    Streamable HTTP host, bearer-token auth, origin check
tests/
  EngineeringMcp.Tests/   xUnit: unit tests per component, plus end-to-end tests that drive
                          both hosts through the SDK client (in-memory pipes and TestServer)
samples/
  data/seed.sql           demo schema: teams, services, deployments, incidents
  docs/                   runbooks, ADRs and onboarding notes
```

## Tests

```bash
dotnet test
```

267 tests: the SQL validator against injection and bypass attempts in both dialects, the docs
sandbox against traversal, absolute paths and links, BM25 ranking, git history over a temporary
repository, health checks through a stub handler, and full MCP round trips through the SDK client
over both transports (tool listing, calls, resources, prompts, the allow-list, timeouts, auth,
origin checks and the audit record). One test skips on machines where the account cannot create
symbolic links.

## License

MIT
