# ADR 0002: an in-house feature-flags service

- **Status:** accepted
- **Deciders:** Platform

## Context

Teams were toggling behaviour with configuration files, which needed a deployment for every
change and could not target a percentage of users.

## Decision

Build a small **feature-flags** service. Flags are stored in PostgreSQL and cached in memory by
each client library with a short time-to-live.

## Consequences

- Flags can change without a deployment.
- The cache TTL bounds how stale a flag can be; it is configured in milliseconds.
