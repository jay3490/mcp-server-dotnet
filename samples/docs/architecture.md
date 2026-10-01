# Platform architecture overview

The platform is a set of small services behind a single **api-gateway**. Each service owns its
data store and publishes events to a shared message broker. Services are grouped by team:

| Team     | Services                                   |
|----------|--------------------------------------------|
| Platform | api-gateway, notifications, feature-flags  |
| Payments | billing, ledger                            |
| Search   | search-indexer, search-api                 |
| Identity | auth                                       |

## Request path

1. Clients call the api-gateway over HTTPS.
2. The gateway validates the access token issued by **auth** and forwards the request.
3. Downstream services answer synchronously or accept the work and emit an event.

## Data stores

- **billing** and **ledger** use PostgreSQL with synchronous replication to a standby.
- **search-indexer** writes to the search cluster; **search-api** only reads from it.
- **feature-flags** keeps flag definitions in PostgreSQL and serves them from an in-memory cache.

## Service tiers

Tier 1 services page the on-call engineer at any hour. Tier 2 services page during working
hours. Tier 3 services open a ticket instead of paging. The tier of every service is recorded in
the `services` table of the engineering database.
