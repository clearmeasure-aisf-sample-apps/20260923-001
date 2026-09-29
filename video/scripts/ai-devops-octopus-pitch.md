# AI DevOps with Octopus Deploy — 86-second pitch (storyboard)

Composition `AiDevopsOctopusPitch` · 1920×1080 · 30 fps · 2,580 frames · scenes in
`src/aiDevopsOctopusPitch.tsx`.

Narration and captions share one source: `src/aiDevopsOctopusPitch.narration.json` (`caption` is shown on
screen, `spoken` is the same sentence written for the TTS voice, e.g. "forty" for "#40"). Voice:
`en-US-AndrewNeural` via `msedge-tts`; each clip starts 0.2 s into its scene.

```
npm install
npm run narration:pitch   # TTS into public/audio/pitch-*.mp3
npm run render:pitch      # writes out/ai-devops-octopus-pitch.mp4
```

| # | Scene | Time | On-screen text | Narration |
|---|-------|------|----------------|-----------|
| 1 | Hook | 0–8 s | "A work item goes in. / Production software comes out. / No hand-offs." Board columns Todo → In Progress → In Review → Deployed to TDD → Deployed to UAT → Deployed to Prod → Done; card #40 glides from Todo to Done. | A work item goes in. Production software comes out. No hand-offs. |
| 2 | The work item | 8–18 s | Issue #40 "Show remaining-character counter on the work order Instructions field"; AI agent design comment; card moves Todo → In Progress. | Take issue #40: a remaining-character counter on the Instructions field. An AI agent posts the design comment and moves the card. |
| 3 | Build and test | 18–30 s | "Code and tests, written together"; files (razor markup, bUnit, Playwright); ✓ Private build (unit + integration), ✓ Acceptance suite; PR "Refs #40". | The agent writes the code with bUnit and Playwright tests, runs the private build and the acceptance suite, then opens a pull request marked Refs #40. |
| 4 | CI gate | 30–40 s | "Nothing merges without a green check"; PR head → `codefresh/ci` (running → success) → Merge; branch protection: PR required, required status check, no bypass. | Codefresh CI runs on the pull request head. The branch is protected: pull request required, required check, no bypass. Green means merge. |
| 5 | Release | 40–52 s | "Every merge becomes a release"; Merge → Codefresh release pipeline → Octopus release **2.5.753** with release notes (build information, CI summary). | The merge triggers the Codefresh release pipeline. It creates Octopus release 2.5.753, with build information and the CI summary in the release notes. |
| 6 | Progressive delivery | 52–66 s | "TDD → UAT → Prod, automatically", lifecycle platform-continuous; three tiles turn green in turn: Argo CD image tag → Git, Argo CD sync Healthy, TDD smoke + acceptance tests. | The Octopus lifecycle deploys to TDD, then UAT, then production, automatically. Each step updates Argo CD image tags in Git and waits for the sync. TDD also runs smoke and acceptance tests. |
| 7 | Evidence and close | 66–78 s | ✓ Octopus API: TDD, UAT, Prod deployed · ✓ Production screenshot · ✓ Evidence comment on #40 · ✓ Issue #40 closed; card In Review → Deployed to TDD/UAT/Prod → Done; "Evidence comes from the APIs, not from the agent's word." | The agent verifies every environment through the Octopus API, captures a production screenshot, posts the evidence and closes the issue. Evidence comes from the APIs, not from the agent's word. |
| 8 | Scale and call to action | 78–86 s | "Three work items, in parallel, the same afternoon"; 2.5.753 (#40), 2.5.754 (#41), 2.5.755 (#42), each ✓ Prod; end card: AI Software Factory · Clear Measure · Octopus Deploy · Codefresh · Argo CD · GitHub. | Three work items, in parallel, the same afternoon: releases 2.5.753, 2.5.754 and 2.5.755, all in production. |

Visual rules: dark background, one accent (`theme.series1`), green only for pass/deployed status and always
with a ✓; key lines ≥ 48 px; diagrams are React/SVG; no stock footage and no third-party logos.
