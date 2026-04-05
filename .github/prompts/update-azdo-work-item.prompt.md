---
name: "Update Azure DevOps Work Item"
description: "Update, assign, comment on, or change the state of an existing Azure DevOps work item for auto-github-changelog-poster."
argument-hint: "Describe the work item and the update you want to apply"
agent: "agent"
---
Update an Azure DevOps work item for this repository.

Inputs:
- Request: ${input}

Workflow:
1. Identify the target work item by ID or search by title if needed.
2. Confirm the Azure DevOps project as `Community Team Projects` unless the user asked for another one.
3. Determine whether the request is best handled as a field update, self-assignment, state change, or comment.
4. If the target work item is ambiguous, ask a focused follow-up before changing anything.
5. Apply only the requested update.
6. Report the work item ID, final title, and what changed.

Constraints:
- Keep comments concise and implementation-focused.
- Prefer comment updates for progress notes and use the companion linking skill when a code artifact should be attached.