# Phase Shift checkpoint report — 2026-10-08

**Implementation committed; acceptance verification is incomplete.**
Branch: `codex/phase-shift-secondary`, based on Fire Blast checkpoint `170350b`.
Checkpoints: `270ad4b` (evasion foundation/audit/harness), `eb6e97e` (content, migration and controls).
Nothing pushed.

1. **Exact files changed:** listed below, relative to this project. The three pre-existing local items remain untouched and unstaged: `.freebuff/project-id`, `docs/secondary-loadout.md`, and `ProjectSettings/SidekickToolDownloaderAutoShown.txt`.
2. **Architecture:** `PhaseShiftEffect` uses the existing `PowerDefinition`/`PowerUser.UseSecondary` activation and payment path. A single `PlayerPhaseShift` component owns temporary evasion, NPC collision pairs and pooled presentation. No new movement controller or health bookkeeping.
3. **Tuning:** duration 0.75 seconds; two charges; 5 seconds per-charge recharge; 0.2 seconds base cooldown, subject to existing hero cooldown multiplier; zero energy; no upgrade tiers. Values live in `Resources/Powers/phase-shift.asset`.
4. **Incoming damage:** ordinary `WorldSession.DamagePlayer` damage reaches `PowerUser.AbsorbIncoming` and is refused while phased. Explicit unblockable damage bypasses both evasion and shield, including the kill plane. Real melee and Gunner hits use this path. Current Gunner shots are committed hitscan shapes; there is no separate physical hostile projectile to test.
5. **Collision:** only NPC root collider pairs are ignored, including actors spawned during the phase. Existing ignores are preserved. World buildings, floor, ceiling and props remain solid and the CharacterController remains enabled. Exit searches nearby world-swept free points and recorded safe positions. It never assigns a teleport position. If all candidates are blocked, collisions restore and `UnresolvedRecoveries` records the failure; this extreme crowd/invalid-geometry case is an unresolved acceptance risk until runtime testing.
6. **Offensive lockout:** primary activation, basic/Strength melee, Hurricane Kick and synergy refuse before payment. Held props release without a throw; Laser ends cleanly. Pending paid melee windups are invalidated so they cannot resume after evasion. New attacks become eligible as soon as phase ends, subject to their existing resource/cooldown rules.
7. **Traversal:** activation detaches climbing without resetting vertical velocity or air-jump state. Normal walking/jumping/double jumping remain with the controller. Climb relatch and mantle are refused while phased. Active Speed dash, backflip, ground pound, mantle or synergy refuses phase activation. Speed and Teleport cannot start during phase.
8. **Flight migration:** all three heroes expose exactly Speed, Teleport, Force Field and Phase Shift as secondaries. Fresh profiles prefer Phase Shift. Invalid old Flight selections repair through existing loadout validation and are immediately written through normal Save. Version remains 1. Hero, valid combat powers, progression and colours remain intact by implementation; separate-process proof is pending. Retained Flight code/assets and ownership history are not deleted. Editor setup follows the new roster.
9. **Force Field comparison:** phase refuses ordinary damage briefly and prohibits attacks; shield still absorbs finite capacity and permits attacks. Starting phase drops a pre-existing shield. Dedicated controls compare actual Rusher and Gunner hits, but have not run.
10. **Teleport comparison:** phase activation does not relocate or bypass world collision. Teleport retains its supported landing and clear corridor/range rules. Measured comparison is defined but pending execution.
11. **Speed comparison:** phase uses normal movement speed and does no contact damage. Speed retains its existing faster damaging dash. Equal-duration traversal comparison is defined but not measured yet.
12. **First person:** two thin peripheral lines provide feedback without covering the crosshair or altering aim/FOV/roll. Third person has a palette outline and three cached moving echoes. Sidekick renderer materials are untouched. Actual visual readability and end-to-end aim checks remain unverified.
13. **Verification:** installed Unity SDK compilation succeeds with 0 warnings and 0 errors; diff whitespace check passes. No runtime suite executed, so there are **no fresh runtime PASS/FAIL counts**. The sequential launcher twice refused to start while the user's Editor owned this project. Its CLI has no Pipeline/live-control connection. The startup log resolves a Personal license, so this is Editor contention, not the old exit-198 licensing failure. Existing regression controls were updated; retained Flight-specific regression uses explicitly labelled in-memory scoped content fixtures without bypassing ownership/payment gates. Historical all-pairs LoadoutMatrix remains stale from its old 11-power/two-slot design.
14. **Save/reload:** dedicated migration fixture, exact whole-profile comparison, immediate disk repair check and separate-process Reload exist. All are unexecuted. No real player save was intentionally loaded or written by this task's tests.
15. **Performance:** before/after FPS, milliseconds, GC allocations and object/material/renderer/mesh counts are unavailable. A dense 18-attacker, 1280×720 harness is saved. Final stress uses test-only charge refills, labelled in output, while real enemies attack. A 20-activation resource-growth control is defined. A true pre-content baseline must still be recovered/run safely; `Baseline` on the final tree is only an inactive-phase control, not a pre-change measurement.
16. **Captures:** none generated. Planned outputs under `Verification/PhaseShift/`: `phase-third-person.png`, `phase-first-person.png`, `baseline-dense.png`, `run-dense.png`. Do not treat these as existing evidence.
17. **Unverified:** all runtime acceptance, collision recovery in crowded or invalid geometry, migration reload, visual quality, performance and required regression runs. Henry still needs to judge duration, recharge, readability, timing forgiveness, group strength, offensive-lockout downside, and distinction from Shield/Teleport. No balance or finished-build claim.

## Resume after saving and closing the open Editor

Run one Unity process at a time in this persistent project. These suites use isolated saves and stop on the first failure:

```sh
python3 tools/verify-sequential.py --out Verification/PhaseShift/first-runtime \
  PhaseShiftVerification.Run PhaseShiftVerification.Reload

python3 tools/verify-sequential.py --out Verification/PhaseShift/regression \
  SecondaryVerification.Run SecondaryVerification.Reload \
  RosterVerification.VerifyExisting RosterVerification.Reload \
  HeroForgeVerification.VerifyExisting HeroForgeVerification.Reload \
  CombatVerification.Run FirstPersonVerification.Run FirstPersonVerification.Reload \
  CameraVerification.Verify MovementVerification.Jump MovementVerification.Mantle \
  MovementVerification.Camera ClimbingVerification.Run \
  HudVerification.Run ModeVerification.VerifyExisting ModeVerification.Reload
```

Entry-point names checked in source; investigate actual failures. Roster covers Speed/Force Field and Secondary covers Teleport. Also run Climbing.City and the matched dense baseline/final performance workload. Retain failures and inspect actual captures. Never close an unsaved user Editor or run a competing process.

Evidence currently available:
- `Verification/PhaseShift/source-build.txt`
- `Verification/PhaseShift/runtime-blocked.txt`
- appended audit/checkpoint sections in `STATUS.md`

## File inventory

- `.gitignore`
- `Assets/Editor/HeroForgeSetup.cs`
- `Assets/Editor/PhaseShiftVerification.cs`
- `Assets/Editor/PhaseShiftVerification.cs.meta`
- `Assets/Editor/RosterSetup.cs`
- `Assets/Resources/Effects/PhaseShift.asset`
- `Assets/Resources/Effects/PhaseShift.asset.meta`
- `Assets/Resources/Forge/Heroes/nova.asset`
- `Assets/Resources/Forge/Heroes/titan.asset`
- `Assets/Resources/Forge/Heroes/vector.asset`
- `Assets/Resources/Powers/phase-shift.asset`
- `Assets/Resources/Powers/phase-shift.asset.meta`
- `Assets/Scripts/ClimbingVerificationRunner.cs`
- `Assets/Scripts/FirstPersonVerificationRunner.cs`
- `Assets/Scripts/Forge/HeroForgeVerificationRunner.cs`
- `Assets/Scripts/Forge/SynergyRunner.cs`
- `Assets/Scripts/LegacyFlightVerificationScope.cs`
- `Assets/Scripts/LegacyFlightVerificationScope.cs.meta`
- `Assets/Scripts/MovementVerificationRunner.cs`
- `Assets/Scripts/PlayerProgression.cs`
- `Assets/Scripts/PowerUser.cs`
- `Assets/Scripts/Powers/PhaseShiftEffect.cs`
- `Assets/Scripts/Powers/PhaseShiftEffect.cs.meta`
- `Assets/Scripts/Powers/PhaseShiftVerificationRunner.cs`
- `Assets/Scripts/Powers/PhaseShiftVerificationRunner.cs.meta`
- `Assets/Scripts/Powers/PlayerPhaseShift.cs`
- `Assets/Scripts/Powers/PlayerPhaseShift.cs.meta`
- `Assets/Scripts/Powers/RosterVerificationRunner.cs`
- `Assets/Scripts/SecondaryVerificationRunner.cs`
- `Assets/Scripts/SuperHeroController.cs`
- `Assets/Scripts/WorldSession.cs`
- `STATUS.md`
- `Verification/PhaseShift/runtime-blocked.txt`
- `Verification/PhaseShift/source-build.txt`
- `tools/verify-sequential.py`
- `docs/phase-shift-report.md` (this report)
