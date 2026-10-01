# ADR 0001: PostgreSQL for transactional data

- **Status:** accepted
- **Deciders:** Payments, Platform

## Context

Billing and ledger need multi-row transactions, strict constraints and point-in-time recovery.

## Decision

Use PostgreSQL for all transactional data. Each service gets its own database; services never
read another service's tables directly.

## Consequences

- Cross-service reporting goes through the events published to the broker, not through joins.
- Read-only analytics run against a replica so long queries cannot block the primary.
