# Runbook: PostgreSQL failover

Applies to the **billing**, **ledger** and **feature-flags** databases, which run a primary with
one synchronous standby.

## When to fail over

Fail over when the primary is unreachable for more than two minutes, or when replication lag on
the standby is zero and the primary host must be taken down for maintenance.

## Steps

1. Pause the writers: scale the service deployment to zero.
2. Promote the standby and wait until it accepts writes.
3. Point the service connection string at the new primary and scale the service back up.
4. Rebuild a new standby from the promoted primary.

## Connection pool sizing

A failover briefly doubles connection attempts. Keep the pool's maximum size at least twice the
steady-state number of active connections; a pool that is too small turns a short failover into
timeouts for every request.
