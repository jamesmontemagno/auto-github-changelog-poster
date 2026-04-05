---
name: link-code-change-to-ado
description: 'Link code changes, pull requests, commits, builds, or follow-up tasks from this GitHub repo to Azure DevOps work items in devdiv-advocacy / Community Team Projects. Use when updating backlog items, adding implementation comments, or creating related work for auto-github-changelog-poster.'
argument-hint: 'Describe the code change and the target work item action'
user-invocable: true
---

# Link Code Change To Azure DevOps

Use this skill when a task spans both source control and planning systems for this repository.

## Repository Defaults

Load [project context](./references/project-context.md) before acting if the repo or Azure DevOps target is unclear.

## When To Use

- Add a comment to an existing Azure DevOps work item describing a GitHub code change or PR
- Link an existing work item to a PR, commit, branch, or build when the required IDs are available
- Create follow-up child work items for uncovered technical debt or next steps discovered during implementation
- Search Azure DevOps for the matching task when the user only gives a partial title or bug description

## Procedure

1. Confirm the code artifact and the work-tracking artifact.
2. Treat GitHub as the source of truth for code and Azure DevOps as the source of truth for planning.
3. Use Azure DevOps MCP tools against organization `devdiv-advocacy` and project `Community Team Projects`.
4. If the work item is unknown, search first, then fetch the selected item before updating it.
5. Prefer lightweight updates unless the user asks for more:
   - add a work item comment for implementation notes
   - add an artifact link for a branch, commit, PR, or build when IDs are available
   - create child work items for follow-up work that should be tracked separately
6. When linking artifacts, include a short comment that explains what changed and why it matters.
7. Report back with the work item IDs touched, the action taken, and any missing identifiers that blocked stronger linking.

## Guardrails

- Do not create or update GitHub Issues for planning unless the user explicitly asks; Azure DevOps is the planning system for this repo.
- Do not guess work item IDs, repository IDs, project GUIDs, or pull request IDs. Search or ask for them.
- Keep comments concise and implementation-focused.
- If only a GitHub-side identifier is available and Azure DevOps artifact-link requirements are missing, add a work item comment instead of fabricating a broken link.