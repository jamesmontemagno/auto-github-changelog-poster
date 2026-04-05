# Project Guidelines

## Architecture
- This repo is a small Azure Functions app targeting .NET 10 isolated worker. The entry point is [AutoGithubChangelogPoster/Program.cs](../AutoGithubChangelogPoster/Program.cs).
- Keep the current separation of concerns: function triggers in [AutoGithubChangelogPoster/Functions](../AutoGithubChangelogPoster/Functions), feed ingestion in [AutoGithubChangelogPoster/Services/Feeds](../AutoGithubChangelogPoster/Services/Feeds), formatting in [AutoGithubChangelogPoster/Services/Formatting](../AutoGithubChangelogPoster/Services/Formatting), social posting in [AutoGithubChangelogPoster/Services/Social](../AutoGithubChangelogPoster/Services/Social), state persistence in [AutoGithubChangelogPoster/Services/State](../AutoGithubChangelogPoster/Services/State), and AI summarization in [AutoGithubChangelogPoster/Services/Summarization](../AutoGithubChangelogPoster/Services/Summarization).
- Prefer adding logic to the relevant service instead of growing the function classes. Use [AutoGithubChangelogPoster/Functions/NotifierFunction.cs](../AutoGithubChangelogPoster/Functions/NotifierFunction.cs) as the reference for orchestration-only function code.

## Build And Run
- Build with `dotnet build auto-github-changelog-poster.slnx` from the repo root. CI uses Release builds in [.github/workflows/ci-build-validation.yml](workflows/ci-build-validation.yml).
- Run locally with `func host start` from [AutoGithubChangelogPoster](../AutoGithubChangelogPoster) after populating local settings.
- There is no automated test project in this repo today. For behavior checks, use the `Test` HTTP function described in [README.md](../README.md).

## Code Style
- Match the existing C# style: nullable enabled, implicit usings enabled, async I/O, dependency injection through `Program.cs`, and structured logging with `ILogger<T>`.
- Keep environment-variable access and optional-service behavior consistent with the current app. When a feature depends on external credentials, fail gracefully and log clearly rather than throwing during startup.
- Avoid broad refactors unless they are required for the task. This repo is small and organized intentionally by service area.

## Conventions And Pitfalls
- Do not commit secrets or modify [AutoGithubChangelogPoster/local.settings.json](../AutoGithubChangelogPoster/local.settings.json) with real credentials.
- The app expects the `TWITTER_*` variable names documented in [README.md](../README.md); do not reintroduce the older `TWITTER_GITHUB_CHANGELOG_*` names.
- `ReleaseSummarizerService` is optional and should only be wired when the required AI settings are present. Preserve that conditional-registration pattern when changing summarization behavior.
- State tracking is used to avoid reposting changelog items. Changes to notifier flow should preserve idempotency and the blob-backed state model.

## GitHub And Work Tracking
- Source code, pull requests, and Git history live in the GitHub repository `jamesmontemagno/auto-github-changelog-poster`.
- Work items are tracked in Azure DevOps, not GitHub Issues. Use organization `devdiv-advocacy` and project `Community Team Projects` when referring to backlog items, bugs, or task tracking.
- When work requires both systems, treat GitHub as the source of code truth and Azure DevOps as the source of planning and work-item truth.

## References
- Use [README.md](../README.md) for setup, required settings, and test endpoint details.
- Use [.github/workflows](workflows) for CI and deployment behavior.