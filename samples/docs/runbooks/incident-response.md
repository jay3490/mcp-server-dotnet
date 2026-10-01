# Runbook: incident response

## Severity levels

- **sev1** - customers cannot use a core feature (login, payments). Page immediately.
- **sev2** - a feature is degraded or a subset of customers is affected.
- **sev3** - internal impact only, or a workaround exists.

## First fifteen minutes

1. Acknowledge the page and declare an incident commander.
2. Open an incident channel and post what is known so far.
3. Check the most recent deployments of the affected service; a rollback is the fastest fix for
   most incidents caused by a change.
4. Post an update every thirty minutes for sev1, every hour for sev2.

## After the incident

Write a short review within five working days: timeline, root cause, and follow-up actions with
owners. Record the root cause on the incident so it can be searched later.
