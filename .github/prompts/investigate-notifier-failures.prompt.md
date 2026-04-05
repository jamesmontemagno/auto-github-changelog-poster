---
name: "Investigate Notifier Failures"
description: "Investigate GitHub changelog notifier failures, missed posts, duplicate posts, timer-trigger issues, or posting/configuration regressions in this Azure Functions repo."
argument-hint: "Describe the symptom, environment, and any logs or recent changes"
agent: "agent"
---
Investigate a notifier problem in this repository.

Inputs:
- Symptom: ${input}

Workflow:
1. Inspect the notifier flow starting with [AutoGithubChangelogPoster/Functions/NotifierFunction.cs](../../AutoGithubChangelogPoster/Functions/NotifierFunction.cs).
2. Trace only the relevant dependencies in [AutoGithubChangelogPoster/Services/Feeds](../../AutoGithubChangelogPoster/Services/Feeds), [AutoGithubChangelogPoster/Services/Formatting](../../AutoGithubChangelogPoster/Services/Formatting), [AutoGithubChangelogPoster/Services/Social](../../AutoGithubChangelogPoster/Services/Social), [AutoGithubChangelogPoster/Services/State](../../AutoGithubChangelogPoster/Services/State), and optional summarization wiring in [AutoGithubChangelogPoster/Program.cs](../../AutoGithubChangelogPoster/Program.cs).
3. Check for configuration and environment assumptions using [README.md](../../README.md), especially storage settings, `TWITTER_*` credentials, runtime configuration, and optional AI settings.
4. Focus on root cause. Pay particular attention to idempotency, ordering, skipped entries, duplicate posting, credential gating, feed parsing failures, and thread or single-post formatting limits.
5. If the issue can be fixed safely with a focused code change, implement it and validate with the smallest appropriate check. If not, return a concise diagnosis with the most likely root cause, impacted files, and the next verification step.

Constraints:
- Keep function classes orchestration-only and prefer service-layer fixes.
- Preserve the optional `ReleaseSummarizerService` registration pattern.
- Do not change secrets files with real credentials.
- When work tracking is relevant, remember code changes live in GitHub while work items live in Azure DevOps under `devdiv-advocacy` / `Community Team Projects`.

Output:
- Summary of the failure mode
- Root cause or strongest hypothesis
- Fix applied or recommended
- Validation performed and any remaining risk