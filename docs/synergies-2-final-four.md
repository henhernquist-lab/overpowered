# Synergies 2.0: final four signatures

Feature branch: `codex/synergies-2-final-four`, based on `b8248b5` on the actual foundation branch. No push is authorized or performed.

## Architecture and scope

Extends the existing `PowerSynergyDefinition` → `SignatureCatalog` → `PlayerProgression` → session-cached `PowerUser.Synergy` → `SynergyRunner` path. Forge and HUD remain the existing interfaces. No parallel manager, cooldown, save architecture, terrain/destruction system, navigation system, material system or pipeline change.

The foundation's four working effects remain unchanged. Their local authored damage buffs (150 Seismic, 100 Solar, 66 Glacial, 150 Thunderstorm) are preserved and used as the reference baseline. Those pre-existing asset values and their tuning note are included unchanged in the final feature commit so its tested balance is reproducible. Existing unrelated vendor, verification and configuration edits remain unstaged as well.

## Exact authored Normal baseline

Damage below is before the one activation variance roll, hero power damage and central difficulty modifier. Normal damage ±10%; Hard ×0.97 and Extreme ×0.955, once in `CityNpc.Damage`. Energy and cooldown scale ×1 / ×1.1 / ×1.15. No ordinary power tuning changes.

| Signature / owner | Damage | Energy | Cooldown | Targeting | Lifetime / limits |
|---|---|---|---|---|---|
| Meteor Crush / Telekinesis | 210 core; 58.8 outer shock (28%) | 35 | 28 s | 24 m ground; 2.6 m direct radius; 6 m shock radius | 8 targets; 1.05 s assembly, .22 s fall, .65 s dust; 8 m overhead height; .75 s stagger / .3 s Brute |
| Abyssal Grasp / Darkness | 70 collapse | 30 | 26 s | 24 m ground; 7 m capture radius | 6 targets; 3.2 s total; 2 s pull at 4.5 m/s toward 1.25 m slots; Brute pull ×.4, hold .5 s; .18 s renewable restraint leases |
| Prism Barrage / Laser Eyes | 115 first; each further branch ×.78: 89.7, 69.966, 54.57348, 42.5673144 | 30 | 24 s | 32 m precise hostile; 8 m refracting fan around initial impact | 5 total targets; .24 s charge, .065 s between beams, .28 s afterglow |
| Plague Bloom / Poison | 180 potential main-bloom poison over six pulses when continuously exposed | 35 | 28 s | 24 m ground; 6 m main radius | 6.3 s; 6 pulses at 1.05 s intervals; 8 targets per bloom/pulse; max 4 total blooms per cast; secondary radius 2.5 m, ≤2 s and clipped to main lifetime, ×.5 DPS |

Meteor Force is 2600 for impact feedback (prop physical impulse capped at 1400); the others use 600. Meteor never spawns a damaging collider: each enemy receives either core damage or the lesser shock, once. Debris are visual and independent of throwable props. All ground placements require a nearby visible walkable-facing solid surface; walls, missing ground, unreachable aim and overhead obstructions refuse before resource payment. Target searches and ray queries are fixed-size, with bounded target lists.

Prism is a charged optical fan with straight cream/cyan/amber paths rooted at its initial impact. It does not hop its origin between enemies like Lightning. The target list is deduplicated; initial-target death during charge ends the paid cast without retargeting. Live LOS and range are rechecked; movable props block optical branches, matching ordinary laser cover rules. Near-camera geometry is inset beyond the near plane.

Abyss uses navigation-aware incremental movement, not the legacy Void Grasp spring/Rigidbody handoff. Static geometry/props and navigation boundaries block movement. Brutes move less and escape the hold sooner. Restraint expires shortly after cancel without clearing another source's longer root/freeze. No curse/aura is installed.

Bloom calls the existing `Poisoned.Apply` and reuses its single-component refresh, tick-count accounting and bounded ordinary death spread (3 targets, 3 generations). An optional pre-rolled DPS argument avoids rolling the signature's already-rolled damage again. Existing ordinary callers are unchanged. Poison rolls once per application, not per tick. One pulse supplies at most one pulse interval of poison; overlapping blooms refresh that component, rather than stacking components. Only enemies exposed by the main bloom can seed secondary blooms, with a lifetime total cap of four, so no recursive bloom growth is possible. Existing ordinary poison death spreading remains independent and bounded. Poison ticks already applied may finish shortly after the visual bloom closes.

## Presentation and cleanup

`SignatureVfx.FinalFour.cs` extends the same `SignatureVfx` session pool. It prebuilds nine meteor cubes, 24 solid tendril segments, four seven-piece organic blooms, eight pairs of optical beam lines, rings and shared palette materials. All solids share the built-in cube mesh. No unique per-prop mesh, new runtime geometry generation, per-cast material creation, physics obstacle or static marking of movable props.

All four use existing cast presentation. Meteor assembly/drop and optical afterglow animate visual objects only; no physics root or camera animation. Camera feedback uses existing additive FOV. Every effect has finite loops and a `finally` cleanup. Runner cancellation now explicitly disposes nested effect iterators, releasing pools and the bloom's scoped authoritative damage-event subscription on interruption. VFX also have safety expiry and session ownership.

## Integration and saves

Mappings are primary-specific, with the four unique IDs `meteor-crush`, `abyssal-grasp`, `prism-barrage`, `plague-bloom`. Legacy pair assets remain untouched. The existing foundation already contains an older `solar-flare` legacy/signature name overlap; this work adds no new ID collisions and does not reinterpret legacy assets by name.

All 28 unordered primary pairs resolve two distinct implemented choices; the existing selection API and Forge buttons equip exactly one. Forge choice/status labels now show actual selected-difficulty cooldown and energy requirements, including fractional values (Prism Hard 26.4 s / 33 energy; Extreme 27.6 s / 34.5 energy; Glacial Extreme 28.75 s). The layout and selection flow are unchanged. Saves keep the additive `SignatureId` / `UnresolvedSignatureId` foundation fields, with no migration/version bump. Existing fallback retains invalid/stale IDs for recovery. No XP, level, hero, suit, upgrade, primary or secondary fields are replaced by this work.

## VERIFIED — final selected automated evidence, 2026-10-20

**6,044 PASS / 0 FAIL in 22 serial Unity 6000.6.0f1 processes**, all exit 0, all personal-save and Sidekick database hashes unchanged. Historical/repeated runs are excluded from this total. These are actual Unity compilation, asset imports, Play Mode scene loads, gameplay calls, authoritative damage and separate-process reloads, rather than source-only checks or human playtesting.

The 48-scenario matrix covers all four new effects × three difficulties × Hero/Villain/Free Play/Endless Fight, with explicit first/third-person switching. A further 12 Prism scenarios verify the final movable-cover rule, dynamic Rigidbody preservation and difficulty-correct Forge labels. The original four effects passed at the preserved buffed damage values. The final suite also covers all 28 pairs, actual Forge buttons, one selected ability, invalid/missing/legacy IDs, preserved XP/level/points/upgrades/hero/suit/view/secondary, ownership/energy refusal, max-five Prism targets, cooldowns, startup deaths, wall blocking, finite poison, four-bloom cap, cancellation, movement recovery and real NavMesh ownership. Ordinary damage/variance, secondary controls, camera, movement, HUD and modes passed the regression suites below.

| Evidence folder | Entry | PASS | FAIL | Seconds |
|---|---|---:|---:|---:|
| `acceptance-r1` | `FinalSignaturesVerification.Safety` | 43 | 0 | 36.72 |
| `acceptance-r1` | `FinalSignaturesVerification.Matrix` | 696 | 0 | 293.75 |
| `acceptance-r1` | `Signatures2Verification.Run` | 70 | 0 | 49.95 |
| `acceptance-r1` | `Signatures2Verification.Reload` | 4 | 0 | 25.12 |
| `regression-r1` | `SynergyAvailabilityVerification.Run` | 39 | 0 | 60.64 |
| `regression-r1` | `SynergyAvailabilityVerification.Reload` | 9 | 0 | 26.8 |
| `regression-r1` | `SecondaryVerification.Run` | 43 | 0 | 31.95 |
| `regression-r1` | `FirstPersonVerification.Run` | 211 | 0 | 148.03 |
| `regression-r1` | `FirstPersonVerification.Reload` | 5 | 0 | 27.83 |
| `regression-r1` | `CombatBalanceVerification.Run` | 2346 | 0 | 229.92 |
| `regression-r1` | `CombatVerification.Run` | 176 | 0 | 131.88 |
| `final-r1` | `FinalSignaturesVerification.Run` | 114 | 0 | 51.9 |
| `final-r1` | `FinalSignaturesVerification.Reload` | 3 | 0 | 24.14 |
| `final-r1` | `FinalSignaturesVerification.PrismMatrix` | 222 | 0 | 62.02 |
| `final-r1` | `HeroForgeVerification.Run` | 136 | 0 | 64.47 |
| `final-r1` | `HeroForgeVerification.Reload` | 3 | 0 | 24.72 |
| `final-r1` | `CameraVerification.Verify` | 45 | 0 | 27.56 |
| `final-r1` | `MovementVerification.Run` | 27 | 0 | 24.04 |
| `final-r1` | `HudVerification.Run` | 316 | 0 | 63.42 |
| `final-r1` | `DifficultyVerification.Run` | 1415 | 0 | 106.1 |
| `final-r1` | `ModeVerification.Run` | 79 | 0 | 44.39 |
| `final-r1` | `EndlessEventsVerification.Run` | 42 | 0 | 31.61 |

`HeroForgeVerification` and `SynergyAvailabilityVerification` use an explicit in-memory legacy signature-gate scope to retain their original fallback controls. They do not count as default-roster evidence. Default-roster Forge/HUD/selection/activation evidence comes from the signature suites, HUD, secondary and matrix tests. No ownership, damage or cooldown assertion was relaxed.

Process manifests and retained check output: `Verification/FinalSignatures/selected-evidence.json`, `acceptance-r1/`, `regression-r1/`, `final-r1/`. Raw `.log` files remain local because the repository ignores logs. Fresh legacy-suite reports/screenshots were copied into `regression-artifacts/`; **1,475 earlier local verification files were restored byte-for-byte**, with 90 fresh verification artifacts retained separately (`evidence-preservation.json`). Working test-save directories remain local and ignored; regression-artifacts also retains sandbox fixture copies and reload pointers alongside reports/screenshots.

## Performance evidence and limits

The fixed signature pool has **116 transforms**, including 70 solids sharing one built-in cube mesh (8 original ice pillars + 62 new solids) and 38 fixed lines. Shared palette materials enable instancing where supported. Twenty repeated presentations kept mesh/material/transform counts unchanged. No per-cast GameObjects, meshes, or materials are created by these presentations; small bounded coroutine/collection state is used by gameplay. Existing BreakableProp destruction remains the authority for impacted props.

Acceptance isolated 180-frame Editor samples: idle median **1.877 ms**, p95 **2.074 ms**, whole-frame GC **1,546 B/frame**; active Abyss fixed-pool median **1.859 ms**, p95 **2.117 ms**, GC **1,583 B/frame**. Global loaded mesh/material counts in that fixture: 1,018 / 115. An earlier safety sample measured active median 3.912 ms / p95 4.371 ms; retained rather than hidden. These context-sensitive short samples show no significant median difference and do not prove sustained standalone performance. The pool adds bounded draw calls and first-use construction cost; first-use hitch cost was not separately profiled. Historical city performance entries in STATUS remain intact.

## FAILED attempts — resolved, retained

- Phase B r1: verifier compile error CS1503 (treating void `Save()` as bool); corrected verifier to inspect `LastError`. No gameplay verdict in that attempt.
- Integration r1 Matrix: 24 PASS / 1 FAIL. Neutral zero-Heat Villain patrol correctly took zero damage; fixture now uses an active `AlwaysAggro` responder. Hostility rules unchanged. Corrected full matrices passed 649/0 and final 696/0; final optical matrix passed 222/0.
- Regression r1 camera invocation: `CameraVerification.Run` requires an argument; corrected invocation to its public zero-argument `Verify`. No camera gameplay checks ran in the failed process; corrected run passed 45/0.

No unresolved failed gameplay check remains in the selected evidence. Unity compilation passed; existing unrelated serialization warnings remain. No standalone player build or hands-on playtest is claimed.

## UNVERIFIED — human acceptance / known limits

Human balance, power usefulness, animation/impact/audio feel, optical/organic/shadow readability in busy night combat, near-camera/feet placement and long-running dynamic standalone performance remain unverified. Automated batch calls exercise gameplay methods rather than physical keyboard/gamepad input. No new clips were authored: existing Cast presentation and additive camera feedback are reused. No universal traversal, enemy AI, ordinary-power tuning, save migration or game-mode redesign occurred.

**Readiness:** the complete eight-signature roster is implemented, registered, compiled, automatically verified and enabled for normal gameplay without developer preview. It is ready for human gameplay acceptance; polish and sustained-performance sign-off remain human work.

## Branch and checkpoints

- Branch: `codex/synergies-2-final-four`, based on the actual `b8248b5` foundation checkout, not main.
- `c53d0a9`: Prism Barrage / Plague Bloom, Phase B verification.
- `7f22f1e`: Meteor Crush / Abyssal Grasp, Phase C verification; complete roster held gated pending integration.
- `cbe5c13`: full roster enabled after six-process 926/0 acceptance; final refinements/regressions follow in the final feature commit.
- Final regression/report commit is the branch tip reported in the completion message. No push or PR was created.
- The pre-existing four first-batch asset damage buffs and their historical tuning note are recorded unchanged so the branch reproduces the tested balance. Other unrelated local edits remain unstaged.

## Human playtest checklist

- Forge: try Telekinesis/Poison and Darkness/Laser; switch the chosen signature, restart, change a primary, inspect HUD cooldown/energy and existing cosmetic/progression selections.
- Meteor: aim ground in both views in empty city and crowds, near walls/roofs; judge assembly scale, anticipation, impact/dust readability and core/shock usefulness.
- Abyss: group Rusher/Gunner/Brute mixes near corners, stairs, props and navigation edges; confirm recovery after interruption and judge useful grouping/Brute resistance.
- Prism: precision aim in both views at near/far targets; inspect refraction fan against walls, target death during charge, crowded optical readability and separation from Lightning/ordinary Laser.
- Bloom: hold/leave/re-enter its area; kill exposed targets with other attacks; inspect secondary cap, cleanup and poison spread; judge organic silhouette and area readability.
- Compare all eight on Normal, Hard, Extreme in Hero/Villain/Free Play/Endless, with normal powers, melee, secondary and traversal between casts.
- Sustained standalone city combat, effects overlap, dense dynamic enemies and camera/animation feel require human validation. Automated checks cannot establish polish or long-running FPS acceptance.


## Changed source, configuration and assets

New Unity sources/assets also include their `.meta` siblings; durable evidence is under `Verification/FinalSignatures/`.

```text
.gitignore
Assets/Editor/FinalSignaturesVerification.cs
Assets/Resources/Forge/SignatureEffects/abyssal-grasp.asset
Assets/Resources/Forge/SignatureEffects/meteor-crush.asset
Assets/Resources/Forge/SignatureEffects/plague-bloom.asset
Assets/Resources/Forge/SignatureEffects/prism-barrage.asset
Assets/Resources/Forge/Signatures/abyssal-grasp.asset
Assets/Resources/Forge/Signatures/glacial-prison.asset
Assets/Resources/Forge/Signatures/meteor-crush.asset
Assets/Resources/Forge/Signatures/plague-bloom.asset
Assets/Resources/Forge/Signatures/prism-barrage.asset
Assets/Resources/Forge/Signatures/seismic-slam.asset
Assets/Resources/Forge/Signatures/solar-flare.asset
Assets/Resources/Forge/Signatures/thunderstorm.asset
Assets/Resources/SignatureCatalog.asset
Assets/Scripts/Forge/AbyssalGraspEffect.cs
Assets/Scripts/Forge/FinalSignaturesVerificationRunner.cs
Assets/Scripts/Forge/HeroForgeScreen.cs
Assets/Scripts/Forge/HeroForgeVerificationRunner.cs
Assets/Scripts/Forge/LegacySignatureVerificationScope.cs
Assets/Scripts/Forge/MeteorCrushEffect.cs
Assets/Scripts/Forge/PlagueBloomEffect.cs
Assets/Scripts/Forge/PrismBarrageEffect.cs
Assets/Scripts/Forge/SignatureTargeting.cs
Assets/Scripts/Forge/SignatureVfx.FinalFour.cs
Assets/Scripts/Forge/SignatureVfx.cs
Assets/Scripts/Forge/Signatures2VerificationRunner.cs
Assets/Scripts/Forge/SynergyAvailabilityVerificationRunner.cs
Assets/Scripts/Forge/SynergyRunner.cs
Assets/Scripts/HudVerificationRunner.cs
Assets/Scripts/Powers/PoisonEffect.cs
Assets/Scripts/SecondaryVerificationRunner.cs
STATUS.md
docs/synergies-2-final-four.md
docs/synergies-2.md
```
