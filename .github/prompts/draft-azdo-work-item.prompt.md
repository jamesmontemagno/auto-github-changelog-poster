---
name: "Draft Azure DevOps Work Item"
description: "Draft a clean Azure DevOps ticket title and description for auto-github-changelog-poster before creating it in AZDO."
argument-hint: "Describe the work, bug, or follow-up you want to track"
agent: "agent"
---
Draft an Azure DevOps work item for this repository.

Inputs:
- Request: ${input}

Workflow:
1. Interpret whether the request sounds like a `Bug`, `Task`, or backlog-style issue.
2. If the type is ambiguous, ask whether it should be an issue-style backlog item, a `Task`, or a `Bug`.
3. Draft a concise title that starts with the action or problem.
4. Draft a compact description using these sections when helpful:
   - Goal
   - Scope
   - Constraints or guardrails
   - Expected outcome
5. Ask whether the user wants the future work item self-assigned if that intent is not already clear.
6. Return the proposed type, title, and description without creating anything.

Constraints:
- Assume Azure DevOps in `devdiv-advocacy` / `Community Team Projects` unless the user says otherwise.
- Keep the description actionable and implementation-focused.
- Prefer `Task` for engineering follow-up, `Bug` for broken behavior, and backlog-style items only when explicitly requested.

Output:
- Proposed work item type
- Proposed title
- Proposed description
- Any clarification still needed before creation