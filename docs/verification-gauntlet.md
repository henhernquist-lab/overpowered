# Verification Gauntlet

Verification-only infrastructure. Nothing here changes shipping gameplay: gauntlet files are
`Assets/Scripts/GauntletVerificationSupport.cs`, `Assets/Scripts/GauntletFlow.cs`,
`Assets/Scripts/*VerificationRunner.cs` (gauntlet-suffixed ones), and `Assets/Editor/*Verification*.cs` +
`Assets/Editor/VerificationGauntlet*.cs`. Evidence lands under `Verification/Gauntlet/<area>/`.

## Entry points

| Gauntlet item | Entry point | What it does | Evidence |
|---|---|---|---|
| 1 | `VerificationGauntletMaster.Run` | Runs every existing suite as its own Unity process against a fresh working copy of the project; one manifest + dashboard. No suite is rewritten. | `Verification/Gauntlet/master/{manifest.tsv,summary.md,logs/}` |
| 2 | built into every gauntlet batch | Save-safety sentinel: SHA-256 + metadata of the user's real progression save before/after each batch; fails loudly on any change; never prints contents. | sentinel lines in each batch's `results.txt` |
| 3 | `VerificationGauntlet.Repeatability` | N rounds (env `OP_GAUNTLET_REPEATS`, default 3) of the six historically flaky areas (HUD P2/P3, Audio, Sidekick, Humanoid, BackflipHurricane) with per-round tracking and first-failing-round record. | `Verification/Gauntlet/repeatability/{results.txt,summary.txt}` |
| 4 | built into every gauntlet batch | Contention sentinel: labels performance samples `PERF INVALID (Unity contention detected)` when other Unity processes are alive during a run. Never kills anything. | `contention` header line in every gauntlet evidence file |
| 5 | `VerificationGauntletReloadChain.Run` | Audits every Run/Reload pair; proves (in batch) that a Reload without a completed Run cannot bind the real save. | `Verification/Gauntlet/reload-chain/results.txt` |
| 6 | `VerificationGauntlet.Inventory` (alias `VerificationGauntletInventory.Run`) | Inventory of every public verification entry point vs runner vs evidence dir; flags orphans, Reload-without-Run, stale docs, obsolete suite assumptions. | `Verification/Gauntlet/inventory/results.txt` |
| 7 | `VerificationGauntletInventory.HardCodeAudit` | Static scan (report-only, exact file:line) for literal power/synergy/NPC counts, city-grid coordinates, mode IDs, fixed resolutions. | `Verification/Gauntlet/hardcode-audit/results.txt` |
| 8+9 | `StaticStateVerification.Run` | Game-flow matrix (Home → Hero/Villain/Free Play/Endless×2 → Results → Home, two cycles) + static-state audit of WorldSession/GameFlow/CityMaterials/EnemyRoster/NpcLod/TimeArbiter/audio/Forge caches with census tracking. | `Verification/Gauntlet/static-state/{results.txt,census.csv}` |
| 10 | `LongSessionWatchdog.Run` (opt-in) | Keeps a real Free Play session alive under accelerated simulation; samples NPC/crime/GameObject/renderer/material/pooled-effect/audio/announcement/managed-memory counts; reports monotonic growth per game-hour. Optional `OP_WATCHDOG_CONTROL` baseline. | `Verification/Gauntlet/watchdog/{results.txt,census.csv}` |
| 11 | every verifier above | Compact dashboards: `Verification/Gauntlet/*/summary.txt` / `summary.md` / `census.csv`. | — |

## Local run order (Unity 6000.6.0f1, from the project root)

```sh
U=/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity

# 1. cheap, static, no Play Mode (seconds):
"$U" -batchmode -projectPath . -executeMethod VerificationGauntletInventory.Run -quit -logFile Verification/Gauntlet/inventory/run.log
"$U" -batchmode -projectPath . -executeMethod VerificationGauntletInventory.HardCodeAudit -quit -logFile Verification/Gauntlet/hardcode-audit/run.log
"$U" -batchmode -projectPath . -executeMethod VerificationGauntletReloadChain.Run -quit -logFile Verification/Gauntlet/reload-chain/run.log

# 2. in-process play-mode verifiers (each exits itself; no -quit):
"$U" -batchmode -projectPath . -executeMethod StaticStateVerification.Run -logFile Verification/Gauntlet/static-state/run.log
OP_GAUNTLET_REPEATS=3 "$U" -batchmode -projectPath . -executeMethod VerificationGauntlet.Repeatability -logFile Verification/Gauntlet/repeatability/run.log

# 3. opt-in long-session watchdog (minutes of accelerated play):
"$U" -batchmode -projectPath . -executeMethod LongSessionWatchdog.Run -logFile Verification/Gauntlet/watchdog/run.log

# 4. master orchestrator (long; runs all core suites in isolated working copies):
"$U" -batchmode -projectPath . -executeMethod VerificationGauntletMaster.Run -logFile Verification/Gauntlet/master/run.log
```

Notes:
- Keep the machine idle and no other Unity Editor open: the contention sentinel will otherwise mark every
  performance-sensitive sample `PERF INVALID` (that is the sentinel doing its job, not a failure of your machine).
- The orchestrator copies the project (Assets/Packages/ProjectSettings, no Library) to
  `../overpowered-gauntlet-workcopy-shared` (or `-<Suite>` with `OP_GAUNTLET_ISOLATE=per-suite`); the first
  launch in a copy re-imports, so expect a long first suite.
- The gauntlet never kills Unity processes other than its own timed-out child.

## What the gauntlet deliberately does NOT do
- It does not rewrite, weaken or replace any existing suite.
- It does not fix any static-state offender, leak or hard-coded assumption it finds — it reports the exact
  file:line or type/field so the fix lands in the right (shipping) code review.
- It does not gate on performance thresholds. Watchdog trends are compared against a baseline you provide,
  never against invented numbers.

## Interpreting FAIL lines
- `SAVE-SAFETY: ... CHANGED` — a verification batch touched the user's real progression save. Treat as a hard
  failure of the batch infrastructure; investigate before running anything else.
- `PERF INVALID` labels — samples were taken while another Unity process was running; do not compare them with
  any other run.
- `WATCHDOG LEAK CANDIDATE` — a structural counter grew monotonically across the accelerated session. Confirm
  against a baseline census (`OP_WATCHDOG_CONTROL`) before treating it as a shipping leak.
- `STATIC AUDIT offender` — a static/singleton survived into a state where its contract says it resets. The
  exact type/field is named; fix belongs in the owning gameplay file's review.
