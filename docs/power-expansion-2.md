# Power Expansion 2.0 implementation report

**VERIFIED: 3,784 PASS / 0 FAIL in 20 selected serial native Unity processes.** Final runtime source: `10b05c7`. All phases A–G are complete. This is a local, unmerged feature branch; the original checkout still belongs to the concurrent health-on-kill task.

## Workspace and architecture

Unity 6000.6.0f1, Built-in RP. Dedicated `feature/power-expansion-2` in `/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery`, based on d70ccc1 (complete three-hero art plus the verified two-mode/Pulsebreaker integration). The original checkout is concurrently used for health-on-kill; no merge, push, asset installation, city rewrite, URP migration or independent healing system.

`PowerDefinition`/`PowerEffect`/`PowerRuntime` remain the data and activation contract. `PowerUser.Use` retains ownership/equip/charge/energy/cooldown checks. New effects use `CityNpc.Damage` and re-enter `PowerUser.Credit` for deferred hits. `SynergyRunner` keeps signature eligibility, payment and coroutine cleanup. `PlayerProgression` remains the only persistent selection authority; gameplay caches the two primaries. Existing Forge, UI Toolkit HUD, ThirdPersonCamera, Sidekick suits/Avatar/controller, shared palette and combat/destruction systems remain.

## Implemented mechanics and tuning

Values below are tier-zero authored means before hero stats, difficulty and variance. All six new resources have 3 charges and 1.5s charge recharge. Duration safety caps remain 8s for Grapple and Mirage, including upgrades. Standard PowerTier upgrades remain +1 charge; damage/force ×1.4, duration ×1.3, range/radius ×1.15, cooldown ×0.9 per tier, two tiers.

| Ability | Damage | Range | Base cooldown | Base energy | Force | Mechanics |
|---|---:|---:|---:|---:|---:|---|
| Aerokinesis | 20 | 14m | 0.9s | 12 | 450 | 24° half-cone, max 8, finite 3m displacement over .3s; .1s interruption |
| Sonic Manipulation | 18 | 11m | 1.2s | 14 | 180 | 38° half-cone, max 8, .28s stagger |
| Combat Morphing | 24 | 2.8m | .45s | 10 | 500 | Three deliberate forms; existing universal melee stays separate |
| Plasma Blades | 18 | 2.6m | .25s | 10 | 260 | Three-strike combo and committed charge |
| Grapple Line | — | 35m | 3s | 12 | — | Static collider anchor, one tether, max 8s; stats range bounded at 60m; velocity capped 22m/s |
| Mirage Decoy | — | 14m AI eligibility | 12s | 20 | — | One holographic appearance copy, 50 health, 6s lifetime |

Wind displacement: ordinary ×1, Slam/heavy ×.4, shield ×.3, boss ×.12. Per-target displacement restart: 1s ordinary / 3s boss. Capsule and NavMesh sweeps constrain movement. Sonic/wind/hammer interruption refractory: 1.2s ordinary / 6s boss; control duration capped .4s ordinary / .15s boss before existing root resistance. This never disables the boss permanently.

Morph Blade Arms: .14s windup, ×1 damage, ×.75 recovery. Hammer Fists: .42s windup, ×1.6 damage, ×1.9 recovery, ×2 force plus finite stagger/push. Extended Claws: .2s windup, ×.9 damage, ×1.4 recovery, +1.25m reach, short 7m/s collision-safe lunge. Form input queues the next selection during a committed strike; its current weapon stays consistent until impact.

Plasma strikes: multipliers 1/.9/1.5; windups .1/.1/.25s; recovery multipliers 1/1/2.2. Combo resets after .95s. Hold G at least .6s, then release: ×2.2 damage, .4s windup, ×4 recovery, additional 8 energy. Both base and extra energy scale once with centralized difficulty. Charged damage does not bypass shields/invulnerability.

| Signature | Owner | Damage budget | Duration | Cooldown | Energy | Bounds |
|---|---|---:|---:|---:|---:|---|
| Tempest Funnel | Aerokinesis | 60 | 3s | 18s | 30 | 6 distinct targets over the whole activation, 5m radius, six ticks, moving center 3m/s, ground-constrained resistance |
| Resonance Cascade | Sonic | 66 authored | 3 pulses .3s apart | 18s | 30 | 6 marks, 18m range/65° half-cone; each pulse /3 with pulse and target falloff |
| Apex Mutation | Morph | Morph-specific ×1.45 | 7s | 20s | 25 | +.6m morph reach, recovery ×.8, weapon scale ×1.35; no capsule, health or universal-melee buff |
| Blade Cyclone | Plasma | 70 per reached target | Finite sequence | 18s | 35 | 4 targets within 8m; ≤.25s collision-safe 12m/s approach; 3 hits at 20/20/60%; damage only within 3.2m and LOS |

Resonance's first/second/third pulse factors are 1/.85/.7; target falloff reduces by .08 per marked target to a .55 floor. An isolated first mark therefore receives 56.1 total at the authored mean, before hero/difficulty. No infinite chaining.

## Inputs and presentation

1/2 select primaries, LMB activates, F secondary, C signature. V cycles morph form; hold/release G charges plasma. V/G are configurable in PowerExpansionTuning. Grapple: F releases; Space releases and attempts the existing jump without resetting jump accounting; WASD adds bounded tangent steering. E combo, RMB Hurricane Kick, Space double jump, Q backflip, wall climb and B first-person remain.

Shared native low-poly blade/hammer/claw/plasma meshes fit actual forearm/hand bones in world space, outside the imported bone scale. Their updates run after weighted humanoid presentation so flight/lean poses retain alignment. Front-facing cones test the actual hostile capsule nearest the aim ray, including point-blank head-height crosshair hits; full-wall and outside-cone negative controls still pass. No replacement body, collider or Animator. New melee fits the existing Punch clip marker to its windup and avoids a competing Cast cue. First-person hides weapon renderers with ShadowsOnly; slash/wave effects remain forward of the eye. Bespoke slash/transformation animations are not supplied.

Wind uses directional translucent arcs and near-ground palette dust. Sonic uses expanding ring wavefronts. Plasma uses bright forearm edges and short slash arcs; morph uses physical metal weapon silhouettes. Grapple is one finite tether plus anchor/release cues. Mirage clones transforms and shared visible meshes/material references into a separate finite hologram, without gameplay components or another Animator. It is a frozen-pose copy with shader shimmer, not an animated second player. Eligible regular AI redirects acquisition, committed shape and real attack release; Pulsebreaker ignores illusion targeting. Expired committed decoy attacks miss rather than unexpectedly striking the player.

## Forge and persistence

The same Forge provides 10 primaries, 6 secondaries, descriptions, base costs/cooldowns, consistent existing glyph fallbacks, and exactly two owner signatures for every distinct primary pair. The Home owned-power strip and results upgrade offers also filter retired resources; the results handler refuses retired upgrade purchases without spending points. The character preview continues to show cosmetics; summoned power weapons appear in gameplay when their primary is selected. All three hero definitions reference the same current roster. Stable `Selectable` flags retain legacy resources while hiding Strength, Laser Eyes and Flight from current selection. The retained six signatures plus four new signatures form the complete catalog; Seismic Slam and Prism Barrage are retained assets outside normal selection.

Version 1 additive aliases: `strength`→`combat-morphing`, `laser-eyes`→`plasma-blades`, `seismic-slam`→`apex-mutation`, `prism-barrage`→`blade-cyclone`. The migration is enabled only with a complete final catalog. It preserves old ownership/stats records, transfers the maximum owned upgrade tier, maps equipped IDs, repairs duplicate slots deterministically and resolves a valid equipped signature. Unknown records survive. XP, levels, points, difficulty, hero, cosmetics, other primary, secondary, mode records, parkour, hints and first-person preference are preserved. Migration is durable and idempotent. Strength, Laser Eyes, duplicate-slot and unknown-primary fixtures also reopen in a second native process with exact nonzero XP/level/cosmetics/records and valid equipment. Legacy Flight was already outside the normal Forge roster; its old equipped form follows the existing Phase Shift fallback. Legacy usage records stay under their original IDs rather than being destructively merged into new counters.

## Performance and limitations

ExpansionVfx owns 32 reusable renderer slots with native shared meshes/materials and cached property blocks. Scan buffers are finite (96 overlap colliders, 48 ray hits, target list ≤16; default attacks 8, signatures 4–6). Tempest uses allocation-free insertion sorting. Two weapon renderers, one tether and one finite hologram are bounded per player. No per-cast mesh/material creation or per-frame appearance reconstruction. The decoy adds finite hierarchy/skinning work once per summon and additional skin rendering while active; profile this in a populated standalone city.

Measured on the native empty Combat Lab: 30 warm frames, 120 idle, 120 repeated-wind Editor frames; latest median/p95 1.22/1.44ms idle and 1.33/1.61ms active. The earlier run measured 1.24/1.42ms idle and 1.30/1.57ms active; both are retained. This includes the Editor/batch environment and is not standalone FPS, GPU timing, or a causal skinning measurement. Human balance, swing feel, dense-city performance and final artistic polish remain UNVERIFIED. Air Cutter/reflection optional extensions are deferred; there is no second incompatible primary input system.

## Verification and commits



The final selected evidence records actual exits, assertion counts, source revisions, pre-run source changes and personal-save/vendor-database hash checks. Later targeted fixes were reverified; earlier tests of unchanged systems keep their actual revisions. These are native Editor import/compilation and Play Mode harnesses using real scene/menu/equip/damage/movement APIs, not inferred source-only passes. Counts follow literal PASS/FAIL lines; an older lab summary counted an editor-only annotation as a pass, and that summary counter is now fixed. No standalone executable or physical keyboard/mouse automation was certified.

| Native suite | PASS | Result |
|---|---:|---|
| `PowerExpansionVerification.AimControls` | 50 | exit 0, no failed checks |
| `PowerExpansionVerification.Secondaries` | 35 | exit 0, no failed checks |
| `PowerExpansionVerification.Gameplay` | 577 | exit 0, no failed checks |
| `PowerExpansionVerification.BossCombat` | 94 | exit 0, no failed checks |
| `PowerExpansionVerification.Signatures` | 74 | exit 0, no failed checks |
| `PowerExpansionVerification.Run` | 189 | exit 0, no failed checks |
| `PowerExpansionVerification.Reload` | 9 | exit 0, no failed checks |
| `PowerExpansionVerification.Presentation` | 67 | exit 0, no failed checks |
| `CombatLabVerification.Run` | 369 | exit 0, no failed checks |
| `CombatLabVerification.Reload` | 4 | exit 0, no failed checks |
| `CombatLabVerification.Matrix` | 943 | exit 0, no failed checks |
| `HumanoidVerification.Run` | 79 | exit 0, no failed checks |
| `FirstPersonVerification.Run` | 211 | exit 0, no failed checks |
| `FirstPersonVerification.Reload` | 5 | exit 0, no failed checks |
| `SidekickVerification.Run` | 102 | exit 0, no failed checks |
| `SidekickVerification.Reload` | 7 | exit 0, no failed checks |
| `HeroForgeArtVerification.Run` | 499 | exit 0, no failed checks |
| `HeroForgeArtVerification.Reload` | 44 | exit 0, no failed checks |
| `TwoModeVerification.Run` | 412 | exit 0, no failed checks |
| `TwoModeVerification.Reload` | 14 | exit 0, no failed checks |

Staged implementation evidence remains separate: ranged 85, melee 49, secondaries 35, signatures 74, and the initial roster 178 + reload 5. Six successful native authoring/import processes are also retained separately and are not added to the 3,784 total.

**FAILED attempts retained / resolved:** 14 intermediate invocations are recorded, including early test-only compilation/threshold/profile mistakes, temporary staged-catalog reloading, old Strength/Flight and cooldown fixtures, a scene-exit palette/VFX null reference, and the confirmed point-blank cone miss. The latter two were real runtime defects and are fixed with positive/negative lifecycle and aiming controls. No unresolved functional failure remains in the selected acceptance set.

**EDITOR-ONLY UNRESOLVED:** this Unity installation raises an `ArgumentOutOfRangeException` in `UnityEditor.Search.SearchInit.IndexationOnStartup`. Raw native output is retained. The lab harness annotates only that exact external startup trace; project-script and other unexpected errors still fail. Existing vendor importer/API diagnostics also remain. The entire Editor Console is not claimed clean.

**UNVERIFIED:** human balance/readability/art approval, physical inputs, dense-city/long-session/standalone CPU/GPU performance, platform builds, and combined behavior with the separate health-on-kill branch. No new healing pipeline was added; all player-owned kills enter the existing attacker/source boundary.

## Changed files and assets

The exact source/asset inventory and hashes are linked below. Main extension points:

- `PowerDefinition.cs`, `PowerUser.cs` and the new `PowerUser.Expansion.cs`: selectable data, new melee stat flag, per-activation recovery, lazy per-player runtime and existing resource/ownership gates.
- `ForgeCatalog.cs`, `SignatureCatalog.cs`, `HeroForgeScreen.cs`, `ModeScreens.cs`, all three hero definition assets and the power/signature resource assets: the complete 10/6/10 roster, visible descriptions/costs and consistent current upgrade choices.
- `PlayerProgression.cs`: additive stable aliases, maximum ownership-tier transfer, selection validation and durable Version 1 repair.
- `SuperHeroController.cs`, `HumanoidPresentation.cs`: narrow motion handoff without air-jump reset, existing Punch playback fitted to new windups, existing camera/movement/animation events preserved.
- `CityNpc.cs`, guard/evasion targeting hooks and new `CityNpc.DecoyTarget.cs` / `CityNpc.PowerControl.cs`: temporary real target substitution and bounded shared interrupt/displacement leases.
- `Assets/Scripts/Powers/Expansion/`: six primary/secondary effect classes, four signature strategies, runtime helpers, decoy, finite movement/VFX and native verification harness partials.
- `PowerExpansionArt.asset`, `PowerExpansionTuning.asset`, seven shared native meshes, two authored shared materials and `PowerExpansionUnlit.shader`: bounded original weapon/effect geometry in Built-in RP. No new body mesh or rig.
- Editor authoring/verification scripts and current-roster updates to existing camera, art and lab fixtures. Historical retained-resource tests opt in only in memory; they are not shipping-selector evidence. The new normal-roster tests reject retired equipment.

## Local commits

| Phase | Commit | Work |
|---|---|---|
| A | `6dc27d6` | Architecture/migration audit |
| B | `5758c68` | Wind and sonic primaries, staged |
| C | `eabd608` | Morph forms, plasma combo and fitted shared weapons, staged |
| D | `6bb772e` | Collision-aware grapple and finite real-AI decoy, staged |
| E | `b807c90` | Four bounded signature effects, staged |
| F | `33997e3` | Complete roster, Forge and safe migration enablement |
| G | `65756c0` | Difficulty/enemy/rig/movement matrix and cleanup limits |
| G | `748c2f6` | Fitted marker timing, scene cleanup, upgrade/target caps and live-boss evidence |
| G | `fe4a076` | Full cosmetic/mode regressions and second-process legacy migration checks |
| G | `10b05c7` | Confirmed close-aim fix and final current-roster gameplay acceptance |

## Preservation and evidence

1,195 protected baseline asset/package paths match d70ccc1, with no changes to the audited bodies, cosmetic geometry, Sidekick suits/vendor assets, Avatar/controller/tuning, city palette/settings, mode/director/enemy/encounter definitions or packages. All existing top-level authored values and complete upgrade blocks for the six retained primaries and four retained secondaries match the baseline. No balance edits or detail cuts are hidden.

All selected processes left the personal progression file and this worktree's Sidekick database byte-identical. 1,007 tracked historical verification files were restored and checked byte-for-byte; 85 fresh changed artifacts were archived by attempt rather than replacing history. Native error traces retain their raw formatting, including trailing spaces. Source-only whitespace checks pass.

The task-start original-checkout snapshot had 1,018 entries: 1,016 file hashes remain unchanged, one directory entry had no file hash, and `Verification/EndlessEvents/results.txt` changed independently during concurrent work. This task wrote only the isolated worktree and did not roll back that concurrent output.

- [Architecture audit](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/docs/power-expansion-2-audit.md)
- [Selected native evidence](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/Verification/PowerExpansion2/selected-evidence.json)
- [All changed source/asset paths](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/Verification/PowerExpansion2/source-files.txt)
- [Changed-asset/source SHA-256 inventory](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/Verification/PowerExpansion2/source-fingerprint.json)
- [Protected baseline and original-checkout audit](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/Verification/PowerExpansion2/preservation.json)
- [Historical artifact preservation](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/Verification/PowerExpansion2/historical-preservation.json)
- [Retained failed attempts](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/Verification/PowerExpansion2/retained-failed-attempts.json)
- [Current captures](/Users/melaniehernquist/.codex/worktrees/power-expansion-2/op-overnight-recovery/Verification/PowerExpansion2/images)

## Re-run and human playtest

Keep all Editors and other Unity jobs closed. From this worktree, choose a fresh output directory and run the current acceptance entries (the old PhaseB/C/D entries intentionally assert staged rosters and are historical):

```sh
python3 tools/verify-sequential.py --out Verification/PowerExpansion2/recheck-YYYYMMDD-HHMM \
  PowerExpansionVerification.Run PowerExpansionVerification.Reload \
  PowerExpansionVerification.AimControls PowerExpansionVerification.Secondaries \
  PowerExpansionVerification.Gameplay PowerExpansionVerification.Presentation \
  PowerExpansionVerification.BossCombat PowerExpansionVerification.Signatures
```

The wrapper refuses a competing Unity process, stops on failures and checks personal-save/vendor-database preservation. Run/Reload belong in the same invocation.

- Open this worktree's `Assets/Scenes/Home.unity`. In the existing Forge, try VECTOR, TITAN and NOVA, both primary slots, a secondary and one owner signature; verify suit/mask/emblem/colors remain independent.
- Test Wind at close/medium range, near walls and against light/shield/heavy/boss targets. Time Sonic against actual windups; verify enemies resume attacks and the boss resists repeated interruptions.
- Cycle V through blades/hammer/claws; try queued form changes during a committed attack. Check attachment alignment in running, jump/backflip, lean, wall climb and first/third-person views.
- Test plasma's three-click rhythm, .95s reset and G charge/release. Judge reach, recovery, energy and vulnerability; compare it with morph and with ordinary E/RMB melee.
- Grapple to city facades in both views; steer, F-release and Space-release. Test corners, invalid/destroyed anchors, momentum and a consumed double jump. It is a simplified pull/swing, not a full rope simulation.
- Summon Mirage in a live group, reposition, let real attacks destroy it, and wait for expiry. Check return aggro, one-copy bounds and boss resistance. Expect a frozen hologram pose with shimmer.
- Use all four signatures in real fights, then inspect live HP/damage numbers and cancellation. Verify Tempest's moving six-target cohort, finite sonic pulses, Apex's timed restoration and Cyclone's blocked/short movement.
- Check Free Play stays round-free and Endless reaches guaranteed Pulsebreaker rounds 5/10/15 with clear HP/phase UI. Use copied/sandboxed legacy saves for manual migration checks.
- Profile a populated standalone city before accepting performance. Approve or replace the simple original weapon/effect geometry and fitted existing animations; high-end specialized art, trails/refraction/bloom, sound design and final combat feel remain human work.

No implementation phase is incomplete. Optional Air Cutter/reflection and the explicitly unverified polish/performance/integration work are not claimed as delivered. No merge or push was performed.
