# Persistent recovery verification

Audited 2026-10-04 UTC against `748e89d`, in `op-overnight-recovery` on
`codex/overnight-recovery`. This is a work plan, not passing evidence.

## Run order

Run one Editor process at a time, directly in the persistent recovery project.
Play Mode verifiers exit themselves: omit `-quit`. Reload always starts a new
process and only follows a successful matching Run with fresh save evidence.

1. `StagedMissionVerification.Run`: recover the Docks seed 1011 node check.
2. `StageFrameworkVerification.Run`.
3. `SplineRouteVerification.Run`, then `.Reload`.
4. Cloud gates: Roster Run/Reload, Melee, HeroStats, Mission,
   SynergyAvailability Run/Reload, HeroForge Run/Reload; Mode Run/Reload,
   Combat, Camera.Verify, FirstPerson Run/Reload, HUD phases 1–3 (including
   phase 2/3 reloads).
5. World/replayability gates: EncounterSelection, Challenge, EndlessEvents,
   DistrictProfile, Pursuit, HeatResponse, CivilianLedger, DistrictDiagnostics.
6. Phase 9 presentation, then Roster and LoadoutMatrix Run/Reload, Sidekick
   Run/Reload, Humanoid, Feel, FirstPerson Run/Reload and ModeExpansion
   Run/Reload. Capture first/third person and night power readability; measure
   all eleven powers in combat.
7. Broad final regression, static-state/reload-chain checks, long-session
   watchdog and performance sweep on the final source.

Do not use `VerificationGauntletMaster.Run` unchanged for this task: its parent
Editor remains running while it launches child Editors, and it creates project
copies instead of operating in this persistent project. Reuse its suite inventory
and the existing verifier entry points directly.

Do not switch mission pools until all required cloud and core gates are green.
Record setup idempotence using before/after asset hashes. Record each process's
entry, source commit, exit, fresh PASS/FAIL counts, evidence and limitations.
Historical results under Verification are not current results.

## Existing implementation audit

- Free Play, Endless, all eleven powers, five shipping synergies, the freeze
  glyph pool, lighting cycle, Forge reveal, intro and spline routes already exist.
  Preserve and verify these implementations rather than rebuild them.
- `PowerVfx` has sixteen reusable arc lines and one continuous beam, shared lit
  palette materials, and session ownership. Laser Eyes currently draws one thin
  line from the gameplay aim origin. It lacks an eye-following visual start and
  a dedicated channel pose.
- `PlayerShield` already owns capacity/absorption math and three rotating rings.
  Improve barrier/absorption readability without changing that math.
- Lightning uses nonalloc physics queries, a pooled struck set, and reusable
  lines. Existing Roster checks prove planted target order and damage falloff.
- Darkness redraws rooted tendrils through the same bounded pool. Poison has
  tick particles and pooled death-spread arcs. Speed already has a reusable
  trail; pose and night readability still need review.
- `CityMaterials.GetVfx` already supplies cached unlit palette materials. Use
  this infrastructure instead of another material system; warm caches at setup.
- `HumanoidPresentation` owns visual overlays; `HumanoidRigging` owns the
  authored head/telekinesis constraints. Preserve Animator/action priority,
  first-person hiding, physics root and timing contracts when extending poses.
- `RosterVerification` and `LoadoutMatrixVerification` already cover much of
  the six powers' lifecycle/equip/save behavior. Extend missing controls only.

## Evidence discipline

Keep vendor materials unchanged. Inspect and restore Editor-induced Sidekick
database changes after each process, preserving any user edits that predated it.
Keep performance contention and Editor-only limitations explicit. Human pacing,
feel and player-build rendering are not established by scripted batch checks.
