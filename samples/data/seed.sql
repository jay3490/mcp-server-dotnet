-- Demo dataset for mcp-server-dotnet: a small fictional platform team's services,
-- deployments and incidents. Executed once to create samples/data/demo.db.

CREATE TABLE teams (
    id            INTEGER PRIMARY KEY,
    name          TEXT NOT NULL UNIQUE,
    chat_channel  TEXT NOT NULL
);

CREATE TABLE services (
    id          INTEGER PRIMARY KEY,
    name        TEXT NOT NULL UNIQUE,
    team_id     INTEGER NOT NULL REFERENCES teams(id),
    tier        INTEGER NOT NULL CHECK (tier BETWEEN 1 AND 3),
    language    TEXT NOT NULL,
    created_at  TEXT NOT NULL
);

CREATE TABLE deployments (
    id                INTEGER PRIMARY KEY,
    service_id        INTEGER NOT NULL REFERENCES services(id),
    version           TEXT NOT NULL,
    environment       TEXT NOT NULL CHECK (environment IN ('staging', 'production')),
    status            TEXT NOT NULL CHECK (status IN ('succeeded', 'failed', 'rolled_back')),
    deployed_at       TEXT NOT NULL,
    deployed_by       TEXT NOT NULL,
    duration_seconds  INTEGER NOT NULL
);

CREATE TABLE incidents (
    id           INTEGER PRIMARY KEY,
    service_id   INTEGER NOT NULL REFERENCES services(id),
    severity     TEXT NOT NULL CHECK (severity IN ('sev1', 'sev2', 'sev3')),
    title        TEXT NOT NULL,
    opened_at    TEXT NOT NULL,
    resolved_at  TEXT,
    root_cause   TEXT
);

CREATE INDEX ix_deployments_service ON deployments(service_id, deployed_at);
CREATE INDEX ix_incidents_service ON incidents(service_id, opened_at);

CREATE VIEW deployment_stats AS
SELECT s.name AS service,
       COUNT(*) AS deployments,
       SUM(d.status = 'succeeded') AS succeeded,
       SUM(d.status <> 'succeeded') AS not_succeeded,
       ROUND(AVG(d.duration_seconds), 1) AS avg_duration_seconds
FROM deployments d
JOIN services s ON s.id = d.service_id
GROUP BY s.name;

INSERT INTO teams (id, name, chat_channel) VALUES
    (1, 'Platform',  '#team-platform'),
    (2, 'Payments',  '#team-payments'),
    (3, 'Search',    '#team-search'),
    (4, 'Identity',  '#team-identity');

INSERT INTO services (id, name, team_id, tier, language, created_at) VALUES
    (1, 'api-gateway',      1, 1, 'C#',     '2023-02-14T09:00:00Z'),
    (2, 'billing',          2, 1, 'C#',     '2023-03-01T09:00:00Z'),
    (3, 'ledger',           2, 1, 'Go',     '2023-05-22T09:00:00Z'),
    (4, 'search-indexer',   3, 2, 'Python', '2023-07-10T09:00:00Z'),
    (5, 'search-api',       3, 2, 'C#',     '2023-07-10T09:00:00Z'),
    (6, 'auth',             4, 1, 'C#',     '2023-01-09T09:00:00Z'),
    (7, 'notifications',    1, 3, 'TypeScript', '2024-01-15T09:00:00Z'),
    (8, 'feature-flags',    1, 2, 'Go',     '2024-04-02T09:00:00Z');

-- 320 deployments spread over the services, two environments and roughly five months.
-- The arithmetic is deterministic so every fresh database holds the same rows.
WITH RECURSIVE seq(n) AS (
    SELECT 1
    UNION ALL
    SELECT n + 1 FROM seq WHERE n < 320
)
INSERT INTO deployments (service_id, version, environment, status, deployed_at, deployed_by, duration_seconds)
SELECT
    (n % 8) + 1,
    '1.' || (n / 16) || '.' || (n % 16),
    CASE WHEN n % 3 = 0 THEN 'staging' ELSE 'production' END,
    CASE WHEN n % 17 = 0 THEN 'failed'
         WHEN n % 29 = 0 THEN 'rolled_back'
         ELSE 'succeeded' END,
    strftime('%Y-%m-%dT%H:%M:%SZ', '2026-05-01T08:00:00', '+' || (n * 11) || ' hours'),
    CASE n % 5 WHEN 0 THEN 'priya' WHEN 1 THEN 'marcus' WHEN 2 THEN 'lena' WHEN 3 THEN 'tomas' ELSE 'ci-bot' END,
    60 + (n * 37) % 540
FROM seq;

INSERT INTO incidents (service_id, severity, title, opened_at, resolved_at, root_cause) VALUES
    (2, 'sev1', 'Card authorisations timing out',            '2026-05-06T14:12:00Z', '2026-05-06T15:40:00Z', 'Connection pool exhausted after a config change lowered max pool size'),
    (1, 'sev2', 'Elevated 502s from the gateway',            '2026-05-19T03:05:00Z', '2026-05-19T03:52:00Z', 'Upstream health check path renamed without updating the gateway route'),
    (4, 'sev3', 'Index lag above 10 minutes',                '2026-05-27T10:30:00Z', '2026-05-27T13:00:00Z', 'Backfill job competing with the live indexer for the same queue'),
    (6, 'sev1', 'Login failures for SSO users',              '2026-06-03T08:47:00Z', '2026-06-03T09:31:00Z', 'Expired signing certificate on the identity provider metadata'),
    (3, 'sev2', 'Ledger reconciliation mismatch',            '2026-06-11T22:15:00Z', '2026-06-12T02:05:00Z', 'Timezone bug in the end-of-day cut-off'),
    (7, 'sev3', 'Duplicate email notifications',             '2026-06-20T12:00:00Z', '2026-06-20T16:45:00Z', 'Retry without idempotency key after a broker failover'),
    (5, 'sev2', 'Search p95 latency doubled',                '2026-07-02T17:20:00Z', '2026-07-02T18:10:00Z', 'Query planner change after an index rebuild'),
    (2, 'sev2', 'Invoices generated twice for annual plans', '2026-07-15T06:00:00Z', '2026-07-15T11:30:00Z', 'Scheduler ran on two nodes after a leader election race'),
    (8, 'sev3', 'Flag evaluation returning stale values',    '2026-07-29T09:10:00Z', '2026-07-29T09:55:00Z', 'Cache TTL set in seconds where milliseconds were expected'),
    (1, 'sev1', 'Gateway rejecting all requests with 401',   '2026-08-08T13:02:00Z', '2026-08-08T13:21:00Z', 'Key rotation published the new key before clients trusted it'),
    (6, 'sev3', 'Password reset emails delayed',             '2026-08-21T07:40:00Z', '2026-08-21T08:30:00Z', 'Notification queue throttled by provider rate limit'),
    (4, 'sev2', 'Search results missing new products',       '2026-09-04T11:11:00Z', NULL, NULL);
