# Runbook: deploying a service

Deployments go to **staging** first and to **production** only after staging has been healthy
for thirty minutes.

## Before you deploy

- Check that no incident is open for the service.
- Confirm the change has passed CI on the main branch.
- For tier 1 services, announce the deployment in the team channel.

## Deploy

1. Trigger the pipeline for the service with the version tag.
2. Watch the rollout: new instances must pass their health check before old ones are drained.
3. Compare error rate and p95 latency with the previous hour.

## Rolling back

If the error rate rises or the health check fails, roll back immediately: re-deploy the previous
version from the pipeline history. Record the rollback so the `deployments` table shows the
status `rolled_back`, then open an incident if customers were affected.
