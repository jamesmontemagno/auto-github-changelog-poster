---
description: "Use when editing C# Azure Functions files in AutoGithubChangelogPoster. Covers DI, service boundaries, logging, environment variables, and notifier idempotency."
name: "C# Functions Guidance"
applyTo: "AutoGithubChangelogPoster/**/*.cs"
---
# C# Functions Guidance

- Keep function classes thin and orchestration-focused. Put feed parsing, formatting, social API work, state persistence, and summarization behavior into the existing service areas under [AutoGithubChangelogPoster/Services](../../AutoGithubChangelogPoster/Services).
- Use [AutoGithubChangelogPoster/Functions/NotifierFunction.cs](../../AutoGithubChangelogPoster/Functions/NotifierFunction.cs) as the model for timer-trigger orchestration and [AutoGithubChangelogPoster/Program.cs](../../AutoGithubChangelogPoster/Program.cs) as the model for dependency injection.
- Preserve the optional-registration pattern for `ReleaseSummarizerService`. Features that require external credentials should degrade gracefully and log clearly instead of failing app startup.
- Match the existing C# style: nullable enabled, implicit usings enabled, `async` I/O, constructor injection, and `ILogger<T>` structured logging.
- Keep environment variable usage consistent with the repo. Read exact setting names via `Environment.GetEnvironmentVariable(...)` and do not reintroduce deprecated `TWITTER_GITHUB_CHANGELOG_*` keys.
- Changes to notifier flow must preserve idempotency. Avoid duplicate posting behavior and keep blob-backed state tracking compatible with the existing model in [AutoGithubChangelogPoster/Services/State](../../AutoGithubChangelogPoster/Services/State).
- For behavior checks, prefer the manual verification path already used in this repo: `dotnet build auto-github-changelog-poster.slnx`, then `func host start`, then the `Test` endpoint documented in [README.md](../../README.md).