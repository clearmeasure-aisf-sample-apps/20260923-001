---
name: /feature-loop-dispatch
id: feature-loop-dispatch
category: Workflow
description: "Orchestrate a batch of work items on project board 678: children-first tree, one Task per item, parent clamp, stall watchdog"
---

Run **feature-loop dispatch** as the orchestrator for a batch of authorized work items.

**Input:** Space-separated GitHub issue numbers after `/feature-loop-dispatch` (e.g. `/feature-loop-dispatch 100 101 102`; `owner/repo#N` for the other repository on the board). If none are given, ask once for the list.

1. Read and follow `.claude/skills/feature-loop-dispatch/SKILL.md` and `.claude/skills/feature-loop/SKILL.md` completely (the contract), with `.claude/factory-loop.json` for board/build config.
2. Apply the Cursor tool mapping in `.cursor/skills/feature-loop-dispatch/SKILL.md` and `.cursor/skills/feature-loop/SKILL.md`.
3. Start the stall watchdog (`Check-StalledLanes.ps1`, no Cursor `-TasksDir`), resolve the epic/child tree, dispatch **one item-coordinator Task per issue** (cap 3), enforce the parent board clamp, and run until every authorized item is Done or hard-blocked.

Do not edit application code in the orchestrator session — only dispatch, verify, clamp, merge/card/issue closeout, and spawn closer Tasks when a stall needs code fixes.
