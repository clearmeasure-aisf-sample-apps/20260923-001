---
name: feature-loop
description: >
  Run the feature loop on ONE GitHub work item: drive it across the shared project board
  https://github.com/orgs/clearmeasure-aisf-sample-apps/projects/678 one column at a time
  (Todo = design, In Progress = implement, In Review = verify, Deployed to TDD/UAT/Prod =
  post-merge verification, Done = terminal) through code, tests, a conflict-free PR,
  API-verified Codefresh/Octopus evidence, bot-finding triage, merge, and board moves.
  Use when asked to "run the feature loop on work item #N", "work issue #N through the
  board", or when /feature-loop N is invoked. For a batch of items, use
  feature-loop-dispatch instead.
---

# Feature Loop (single work item) - Cursor

**The contract is `.claude/skills/feature-loop/SKILL.md`.** Read it fully first, then
`.claude/factory-loop.json`. Every rule there applies unchanged in Cursor: board 678 and its
columns, card moves through the environment repo's board workflow (`board-status`
dispatch, fallback comment, GraphQL only locally with the project scope), closing as the
terminal move (`Refs #N`, never a closing keyword; close only after Deployed to Prod for
app items), the per-repo gates, CI proof by the `codefresh/ci` / `codefresh/release`
commit statuses and Octopus deployments, children-first, the parent clamp, and the
completion heartbeat. This file only maps those rules onto Cursor tools; it never restates
or weakens a gate. When the Claude contract changes, this file changes only if a tool
mapping changes.

## Roles (item coordinator vs column worker)

| Role | Who | Scope |
|------|-----|--------|
| **Item coordinator** | The agent running this skill (or the one Task dispatched per item by feature-loop-dispatch) | Drives #N end-to-end: resolve children, advance one column at a time, verify each column, open the PR, wait on CI and deployments, triage bots, merge, request card moves, close the issue. May spawn column workers. |
| **Column worker** | A Task the coordinator spawns for one board column | Does only that column's design/implement/verify work (or a recorded no-op). Never advances other columns. |

`factory-loop.json` has `"subagentPerColumn": true`: the coordinator spawns a fresh column
worker per column (one delegation hop). Column workers never re-delegate.

## Cursor Task mapping

| Intent | Task parameters |
|--------|-----------------|
| Column worker / writing / build / test | `subagent_type: "best-of-n-runner"` (isolated git worktree); exactly one runner per column, never N competing attempts |
| Read-only search | `subagent_type: "explore"` on the main checkout |
| Model | `inherit` (Auto, cost-optimized); a named model only if the user named a listed slug |
| Parallel independent children | `run_in_background: true`; cap at 3 writing Tasks |
| Nudge a stalled worker | `resume` with the prior Task agent id (replaces Claude SendMessage) |

- One worktree per writing Task; parallel writing Tasks never share a checkout.
- Do not use `subagent_type: "claude"` - that is not a Cursor Task type.

## Cursor tool notes

- **Branches:** `{username}/{branch-description}`; when a Cloud Agent session mandates a
  different template (for example `cursor/...-xxxx`), use that template.
- **PRs:** Cloud Agents use the ManagePullRequest tool (`create_pr` / `update_pr`); local
  agents may use `gh pr create` / `gh pr edit`. The PR body references the item with
  `Refs #N`.
- **GitHub reads and the board dispatch:** `gh api` locally; in cloud agents `curl` with
  the token passed as a header through `--config -` on standard input (contract, "Moving
  cards"). `gh` may be read-only in some Cloud Agent environments - on a refused write,
  report the exact command and use the `board-status:` fallback comment for card moves.
- **Waiting:** bounded polls with Shell / AwaitShell every 60-90 seconds; after every
  resumption re-check the PR, commit status and deployment state directly.
- **Bot triage:** follow the `bot-finding-triage` skill when available.
- **Merge blocked:** leave a clear `GREEN_UNMERGED` state for the orchestrator or a human;
  never fake completion.
