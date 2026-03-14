# auto-github-changelog-poster

Posting-only Azure Functions app for GitHub Changelog updates to X.

## Scope
- Includes: periodic GitHub changelog posting (`GitHubChangelogNotifier`)
- Excludes: weekly recap and Copilot lookup endpoint

## Project layout
- `AutoGithubChangelogPoster/` - function app source

## Local run
1. Fill in `AutoGithubChangelogPoster/local.settings.json` credentials.
2. Build:
   - `dotnet build g:\auto-github-changelog-poster\auto-github-changelog-poster.slnx`
3. Run (from project folder):
   - `func host start`

## Required settings
- `TWITTER_GITHUB_CHANGELOG_API_KEY`
- `TWITTER_GITHUB_CHANGELOG_API_SECRET`
- `TWITTER_GITHUB_CHANGELOG_ACCESS_TOKEN`
- `TWITTER_GITHUB_CHANGELOG_ACCESS_TOKEN_SECRET`
- `AZURE_STORAGE_CONNECTION_STRING`
- `STATE_CONTAINER_NAME`

## Optional settings
- `ENABLE_AI_SUMMARIES`
- `AI_ENDPOINT`
- `AI_API_KEY`
- `AI_MODEL`
- `X_GITHUB_CHANGELOG_PREMIUM_MODE`
- `X_GITHUB_CHANGELOG_SINGLE_POST_MODE`
- `GITHUB_CHANGELOG_FEED_URL`

## Isolation note
Use separate storage and deployment settings from the original `auto-tweet-rss` app to avoid state collisions.
