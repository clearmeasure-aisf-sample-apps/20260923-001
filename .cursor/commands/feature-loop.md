---
name: /feature-loop
id: feature-loop
category: Workflow
description: "Drive ONE GitHub work item across project board 678 end-to-end (design → implement → verify → deployed, CI- and deployment-verified)"
---

Run the **feature loop** on a single work item.

**Input:** The argument after `/feature-loop` is the GitHub issue number (e.g. `/feature-loop 1234`), or `owner/repo#N` for the other repository on the board.

1. Read and follow `.claude/skills/feature-loop/SKILL.md` completely (the contract).
2. Apply the Cursor tool mapping in `.cursor/skills/feature-loop/SKILL.md`.
3. Load board/build config from `.claude/factory-loop.json`.
4. Resolve children first (sub_issues), then drive #N one board column at a time through Todo → In Progress → In Review → Deployed to TDD/UAT/Prod, with private build, acceptance tests, `codefresh/ci` / `codefresh/release` statuses and Octopus deployments verified by API, bot-finding triage, merge, card moves via the board workflow, and the issue close as the move to Done.

For a batch of issues, use `/feature-loop-dispatch` instead.
