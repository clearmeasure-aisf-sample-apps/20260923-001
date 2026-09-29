---
name: feature-loop-dispatch
description: >
  Authorize a batch of work items for full autonomous implementation on the shared board
  https://github.com/orgs/clearmeasure-aisf-sample-apps/projects/678. Resolves each
  item's epic/child tree, computes a children-first execution order, and dispatches one
  dedicated Task per work item that runs the feature-loop skill end-to-end - code, tests,
  PR, API-verified Codefresh/Octopus evidence, merge, board moves, and the closing move to
  Done - with parent clamp rules enforced. Use when asked to "dispatch the feature loop on
  ...", "implement these work items", or when /feature-loop-dispatch is invoked with a
  list of issue numbers. The invoking session stays alive as the orchestrator until every
  authorized item is Done or hard-blocked.
---

# Feature-Loop Dispatch - Cursor

This session is the **orchestrator**. **The contract is
`.claude/skills/feature-loop-dispatch/SKILL.md`** plus the per-item contract
`.claude/skills/feature-loop/SKILL.md` and `.claude/factory-loop.json`. Read all three
first; every phase, gate, stall kind, clamp rule, communication rule and session-end
check there applies unchanged. This file only maps them onto Cursor tools.

## Phase 0 in Cursor - watchdog

- Same script and exit codes (0 no stalls, 1 stalls found, 2 usage error):

  ```
  pwsh -NoProfile -File .claude/skills/feature-loop-dispatch/Check-StalledLanes.ps1 -Repo <owner/repo>
  ```

- Lane state: the contract's `board.ps1 lane` records are the single source of truth here
  too; the orchestrator writes them and reads them first on resumption.
- Heartbeat: a background Shell that sleeps ~15 minutes, runs one check per repo in the
  work set, and exits unconditionally; re-arm it every turn in which it fired. Prefer
  AwaitShell for bounded waits; never wait open-ended on Task notifications alone.
- **No `-TasksDir` / `-ActiveIds`:** Cursor Tasks do not write Claude-style
  `{agentId}.output` files. Pre-PR liveness relies on Task completion notifications,
  `resume`, and the 20-minute no-progress rule.
- Acting on findings: `resume` the owning Task instead of SendMessage. If it cannot be
  resumed, the orchestrator may verify statuses and deployments, post decline replies,
  merge, request card moves and close issues - never edit application code; a needed code
  fix goes to a fresh closer Task.

## Phase 2 in Cursor - one item-coordinator Task per work item

One coordinator Task per item (not one per column; the coordinator spawns column workers
per `.cursor/skills/feature-loop/SKILL.md`). Its prompt is the verbatim feature-loop prompt
and anti-stall rules of the Claude contract, with "subagent" read as "Task", "SendMessage"
as "`resume`", and the item coordinator told to follow `.cursor/skills/feature-loop/SKILL.md`
for tool mapping.

**Environment (mandatory - match the host):**

| Host session | Task `environment` | `subagent_type` |
|--------------|-------------------|-----------------|
| Cloud agent with private worker | Omit - do not set `environment: "cloud"` (a new VM cannot attach to the private worker) | `"generalPurpose"` - not `best-of-n-runner` |
| Cloud agent without private worker | `"cloud"` | `"generalPurpose"` |
| Local Cursor agent | `"local"` (default) | `"best-of-n-runner"` preferred; `generalPurpose` if worktree isolation is unavailable |

Never dispatch `environment: "local"` from a cloud private-worker host. The coordinator
still uses its own git worktree (`git worktree add` from the host's checkout).

| Parameter | Value |
|-----------|--------|
| `subagent_type` | per the table above; exactly one coordinator per item |
| `model` | `inherit` (Auto, cost-optimized) unless the user named a listed slug |
| `run_in_background` | `true` for independent items |
| Concurrency | cap at 3 coordinator Tasks |

The orchestrator never edits application code (comments, merges, card-move requests and
issue closes are allowed when write-enabled).
