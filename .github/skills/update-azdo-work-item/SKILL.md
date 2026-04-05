---
name: update-azdo-work-item
description: 'Update, assign, comment on, reprioritize, or change the state of an Azure DevOps work item for auto-github-changelog-poster. Use when the user wants to manage an existing AZDO ticket in devdiv-advocacy / Community Team Projects.'
argument-hint: 'Describe the work item ID or title and the update to apply'
user-invocable: true
---

# Update Azure DevOps Work Item

Use this skill to manage an existing Azure DevOps work item for this repository.

## Repository Defaults

Load [project context](./references/project-context.md) before acting if the Azure DevOps target is unclear.

## When To Use

- Assign or self-assign an existing work item
- Change state, priority, area, or iteration on an existing item
- Add an implementation note or follow-up comment
- Find a work item by partial title before updating it

## Procedure

1. Identify the target work item.
2. Default to Azure DevOps organization `devdiv-advocacy` and project `Community Team Projects` unless the user explicitly asks for another project.
3. If the user did not provide a work item ID, search first and confirm the intended item before making changes.
4. Determine the minimal update needed:
   - assign or self-assign
   - state change
   - priority update
   - area or iteration update
   - work item comment
5. For self-assignment, use the authenticated Azure DevOps identity rather than guessing an email address.
6. Prefer comments for implementation notes when the user is sharing progress, context, or links rather than asking to edit core fields.
7. After the update, report the work item ID, title, fields changed, and any unresolved ambiguity.

## Guardrails

- Do not guess a work item when search results are ambiguous.
- Do not overwrite multiple fields unless the user asked for them.
- Keep comments concise and implementation-focused.
- If a stronger code artifact link is needed, use the companion `link-code-change-to-ado` skill instead of stuffing raw URLs into a field.

## Completion Check

- The correct work item was updated.
- Only the requested fields or comments were changed.
- The final response clearly states what changed.