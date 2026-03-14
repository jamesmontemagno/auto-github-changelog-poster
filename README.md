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

## Test endpoint
- Function name: `Test`
- Route: `GET /api/test`
- Auth level: `Function` (include the function key as `?code=...` when calling)

### Query parameters
- `mode`: output shape for the latest changelog entry
   - `single` (default): returns a single-post preview
   - `thread`: returns a multi-post thread preview
   - `premium`: returns a premium single-post preview
- `ai`: `true`/`false` flag to enable AI summarization for the preview (default: `true`)

### Example requests
- Local (default single mode):
   - `http://localhost:7071/api/test?code=<function-key>`
- Local (thread mode, no AI):
   - `http://localhost:7071/api/test?mode=thread&ai=false&code=<function-key>`
- Azure (premium mode):
   - `https://<function-app>.azurewebsites.net/api/test?mode=premium&code=<function-key>`

The endpoint returns plain-text preview output with entry metadata and computed post length details. It does not publish to X.

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
