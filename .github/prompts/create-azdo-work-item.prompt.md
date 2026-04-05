---
name: "Create Azure DevOps Work Item"
description: "Create one or more Azure DevOps work items for auto-github-changelog-poster in Community Team Projects."
argument-hint: "Describe the work item or list the work items to create"
agent: "agent"
---
Create Azure DevOps work items for this repository.

Inputs:
- Request: ${input}

Workflow:
1. Use the `create-azdo-work-item` skill behavior for this repository.
2. Confirm or infer the target project as `Community Team Projects` unless the user asked for another project.
3. If the type is ambiguous, ask whether the item should be an issue-style backlog item, a `Task`, or a `Bug`.
4. Ask whether the user wants the new work item or work items self-assigned if assignment intent is not already clear.
5. Create the work item or work items in Azure DevOps with concise titles and actionable descriptions.
6. Report the resulting IDs, titles, type used, project used, and assignment result.

Constraints:
- Do not create GitHub Issues unless the user explicitly asks.
- Prefer Markdown descriptions when supported.
- If several independent items are requested, create them in parallel.