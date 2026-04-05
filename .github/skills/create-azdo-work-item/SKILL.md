---
name: create-azdo-work-item
description: 'Create an Azure DevOps issue, ticket, bug, task, or backlog work item for auto-github-changelog-poster. Use when the user wants to track repo work in AZDO, turn recommendations into tickets, or file follow-up engineering work in devdiv-advocacy / Community Team Projects.'
argument-hint: 'Describe the work to track and any preferred work item type or project'
user-invocable: true
---

# Create Azure DevOps Work Item

Use this skill to create new Azure DevOps tracking items for this repository.

## Repository Defaults

Load [project context](./references/project-context.md) before acting if the repo, organization, or work-tracking defaults are unclear.

## When To Use

- Create a new Azure DevOps issue or ticket for work discovered in this repo
- Turn recommendations, review findings, or follow-up tasks into backlog items
- File bugs, tasks, or backlog items tied to auto-github-changelog-poster
- Create one or several work items from a single request

## Procedure

1. Confirm the work-tracking target.
2. Default to Azure DevOps organization `devdiv-advocacy` and project `Community Team Projects` for this repository unless the user explicitly asks for another project.
3. Interpret the requested item type:
   - Use `Bug` for broken behavior or regressions.
   - Use `Task` for implementation work, refactors, hardening, and technical follow-up.
   - Use `User Story` or `Product Backlog Item` only when the user explicitly wants backlog-style planning.
4. If the user says "issue" or "ticket" without a type, ask whether it should be an issue-style backlog item, a `Task`, or a `Bug` before creating it.
5. Ask whether the user wants the new work item self-assigned when assignee intent is not already clear.
6. Draft a concise title that states the action and target outcome.
7. Write a description with enough implementation detail to be actionable:
   - problem or goal
   - scope
   - constraints or guardrails
   - expected outcome
8. When the user asks for multiple items, create them in parallel when possible.
9. If the user asked to self-assign, set `System.AssignedTo` to the authenticated Azure DevOps identity after creation or during creation when supported.
10. After creation, report the Azure DevOps IDs, final titles, target project, type used, and assignment result.

## Guardrails

- Do not create GitHub Issues for this repo unless the user explicitly asks; Azure DevOps is the planning system.
- Do not guess project names, work item types, or hierarchy when the user supplied conflicting instructions or left the type ambiguous.
- Keep descriptions concise and implementation-focused.
- Prefer Markdown descriptions when the tool supports it.
- If the user already named an existing parent work item, offer to link the new items after creation instead of inventing a hierarchy.

## Completion Check

- The correct Azure DevOps project was used.
- Each work item has a clear title and actionable description.
- Assignment intent was confirmed and applied when requested.
- The final response includes the IDs the user needs to open or reference the work items.