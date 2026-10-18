# Combat balance and ranges

Branch: `codex/combat-balance-ranges`, based on `905abdb`. Implementation and requested automated regressions are complete. Human balance acceptance remains pending. All means below are tier-zero VECTOR values. Hero/upgrade multipliers continue through existing stat resolution.

| System | Old mean | New mean | Minimum | Maximum |
|---|---:|---:|---:|---:|
| Rusher HP (0 stars) | 39 | 39 | 35.1 | 42.9 |
| Gunner HP (0 stars) | 65 | 65 | 58.5 | 71.5 |
| Brute HP (0 stars) | 195 | 195 | 175.5 | 214.5 |
| Pursuing Brute HP (4 stars) | 411 | 411 | 369.9 | 452.1 |
| Fire Blast | 45 | 27 | 24.3 | 29.7 |
| Punch | 35 | 35 | 31.5 | 38.5 |
| E→E Martelo combo kick | 83.3 | 41.65 | 37.485 | 45.815 |
| Standalone Hurricane | 59.5 | 11.9 | 10.71 | 13.09 |
| Telekinesis 45 kg | 35 | 10 | 9 | 11 |
| Telekinesis 400 kg | 104.35 | 29.814 | 26.833 | 32.796 |
| Ice initial | 5 | 10 | 9 | 11 |
| Ice shatter bonus | 30 | 30 | 30 | 30 |
| Laser DPS | 42 | 42 | 37.8 | 46.2 |
| Laser gross energy/sec | 32 | 64 | 64 | 64 |
| Darkness | 4 | 12 | 10.8 | 13.2 |
| Poison DPS | 10 | 12.5 | 11.25 | 13.75 |
| Poison six-second total | 60 | 75 | 67.5 | 82.5 |
| Lightning first hit | 24 | 40.8 | 36.72 | 44.88 |
| Speed pass damage (mean unchanged) | 8 | 8 | 7.2 | 8.8 |

The actual pre-change combo was 35 × 1.4 × 1.7 = 83.3. The new independent combo multiplier is 1.19; Hurricane is 0.34. Basic melee therefore keeps mean8, with combo19.04→9.52 and Hurricane13.6→2.72. Heavy/pound means are unchanged and receive the same variance. Hurricane force remains ×1.5; combo force remains ×1.5×1.6.

## Architecture

`GameTuning.CombatRanges` centralizes separate health and player-damage half-widths (both0.1). `CombatRolls` owns two independent xorshift32 streams per WorldSession, without consuming Unity global random or layout/AI RNG. Means scale before sampling. No per-roll helper object, asset clone, collection, or material is created. Stats queries always return intended means.

CityNpc samples health once on spawn and stores MaxHealth/Health. Civilians retain fixed HP. Endless supplies its final mean (65 × (1 + .15 × (wave−1)) × archetype × optional event/elite/miniboss scaling) into the spawn call. Existing Heat/stage quality multipliers scale the stored roll without drawing again; tier removal retains its existing behavior. Enemy outgoing damage is not randomized. Explicit SetCombatStats remains an exact assignment for fixtures and existing scaling paths. No difficulty implementation.

Fire caches its roll in the pooled projectile; Lightning derives all chain values from one cast roll and0.85 falloff. Melee rolls at impact after windup. TK retains mass scaling, then multiplies by2/7 and rolls on real NPC collision; launch speed, mass and momentum are unchanged. Ice and Darkness roll after finding a valid target. Laser stores one rolled DPS in BeamState. Poison stores one strength per application, retains existing strongest-reapplication semantics, and rolls spreads from the intended DPS rather than compounding parent variance. Ticks divide DPS×duration by tick count for exact total accounting. Ice shatter stays fixed30.

## Synergy audit

All five shipping synergies use independent PowerSynergyDefinition damage: Sonic Slam40, Thermal Shock25 plus conditional17.5, Solar Flare60, Void Grasp45, Eclipse Beam160. None inherits the changed base power damage. These assets and damage paths are unchanged. No shipping mission pools were enabled.

## Deterministic checks

The dedicated suite resets session streams to seed101, clears any fixed-roll override, and performs real paid power activations, physics throws, and windup impacts. It samples32 spawns per archetype per seed pass, repeats seed101 and contrasts seed102. It checks stored maxima/current health, scaled spawns, no update rerolls, diversity, bounds and sample means. Twenty attacks/applications per requested move use an independent expected seeded stream, with shared Fire/Lightning and stable Laser/Poison checks.

Regression runs explicitly use `--combat-roll 0.5`, recorded in their manifests. This preserves exact arithmetic controls; it is not the variation test. The dedicated suite uses random seeded rolls instead. The override is Editor-only and opt-in; normal play uses a fresh session seed. Personal saves are isolated and the sequential launcher verifies save/database hashes.

The existing HUD consumes authoritative NpcDamageResult events and stored MaxHealth. The dedicated suite checks rolled maximum text, resolved/aggregated number amounts and one-decimal label formatting. Progression still records capped health loss; full resolved damage remains distinct for overkill.

## Results and limitations

Dedicated final seeded Run: **1,919 PASS / 0 FAIL**, exit0. Full-energy Laser uptime **1.9241s** vs predicted1.9231s. Two complete runs produced **220 byte-identical attack-sample lines** with seed101. 10,000 warmed seeded damage rolls allocated **0 managed bytes** on the measured thread. SDK build: **0 warnings / 0 errors**. Selected final results total **3,704 PASS / 0 FAIL across 20 processes**, including the dedicated suite. All exited 0 and preserved personal-save and Sidekick database hashes. Before baseline: EnemyHealthFeedback69/0, exit0. Failed attempts remain in Verification/CombatBalance, never counted as passes.

Before12-enemy active feedback109.56FPS/9.127ms versus first midpoint after109.52FPS/9.131ms. Matched enabled72-hit workload102.39FPS/9.767ms before versus101.75FPS/9.828ms after. Both held5608 GameObjects/113 materials/20 bars/96 labels, zero drops, and reported0 main-thread managed B/frame. Short sequential Editor samples are noisy and not standalone benchmarks; midpoint runs exercise the range plumbing but bypass PRNG draws. Dedicated allocation checks exercise seeded PRNG draws separately. No content cuts.

## Balance observations for Henry

Fire's new maximum29.7 cannot one-shot a full zero-star Rusher (minimum35.1). Punch31.5–38.5 can sometimes defeat a low-HP Rusher. Combo37.485–45.815 remains stronger in mean than punch, though ranges overlap. Hurricane10.71–13.09 retains its larger force while dealing much less damage. Heavy TK remains about2.98 times light TK damage, with unchanged physical launch. Poison now totals67.5–82.5 against a surviving target; actual fights may end earlier or spread.

Laser's gross drain doubles, but VECTOR net drain changes20→52/sec due to unchanged12/sec regen: ideal full-bar duration5→1.923s (about38.5% of old uptime). This follows the requested gross drain exactly; no regeneration change was made.

Poison retains its existing strongest-reapplication rule. Repeated applications to an already-poisoned actor can retain a high roll, so the fresh-application sample mean is not a claim about sustained overlapping-cast DPS. This behavior needs playtesting alongside the requested buff.

These are arithmetic observations and automated controls, not a claim that combat feels balanced. Henry must playtest before designing difficulty.

## Exact implementation and verification file inventory

- `.gitignore`
- `Assets/Editor/CombatBalanceVerification.cs`
- `Assets/Editor/CombatBalanceVerification.cs.meta`
- `Assets/Editor/ProjectDataSetup.cs`
- `Assets/Editor/RosterSetup.cs`
- `Assets/Resources/Effects/Telekinesis.asset`
- `Assets/Resources/GameTuning.asset`
- `Assets/Resources/Powers/darkness.asset`
- `Assets/Resources/Powers/fire.asset`
- `Assets/Resources/Powers/ice.asset`
- `Assets/Resources/Powers/laser-eyes.asset`
- `Assets/Resources/Powers/lightning.asset`
- `Assets/Resources/Powers/poison.asset`
- `Assets/Scripts/BackflipHurricaneVerificationRunner.cs`
- `Assets/Scripts/CityNpc.cs`
- `Assets/Scripts/CombatRolls.cs`
- `Assets/Scripts/CombatRolls.cs.meta`
- `Assets/Scripts/EndlessWaveDirector.cs`
- `Assets/Scripts/FireBlastEffect.cs`
- `Assets/Scripts/GameTuning.cs`
- `Assets/Scripts/HeroAbilityTuning.cs`
- `Assets/Scripts/IceEffect.cs`
- `Assets/Scripts/MeleeVerificationRunner.cs`
- `Assets/Scripts/PowerPayoffVerificationRunner.cs`
- `Assets/Scripts/PowerUser.cs`
- `Assets/Scripts/Powers/CombatBalanceVerificationRunner.cs`
- `Assets/Scripts/Powers/CombatBalanceVerificationRunner.cs.meta`
- `Assets/Scripts/Powers/DarknessEffect.cs`
- `Assets/Scripts/Powers/LaserEyesEffect.cs`
- `Assets/Scripts/Powers/LightningEffect.cs`
- `Assets/Scripts/Powers/PoisonEffect.cs`
- `Assets/Scripts/Powers/RosterVerificationRunner.cs`
- `Assets/Scripts/Powers/SpeedDashEffect.cs`
- `Assets/Scripts/SuperHeroController.cs`
- `Assets/Scripts/TelekinesisEffect.cs`
- `Assets/Scripts/WorldSession.cs`
- `STATUS.md`
- `tools/verify-sequential.py`
- `docs/combat-balance-ranges.md` (this report)
- `Verification/CombatBalance/` (fresh manifests/checks/observations/build logs; isolated saves ignored)

## Final regression evidence — 2026-10-18

The first three selected results are from `regression-r1`; the remaining seventeen are from `regression-r2`. This excludes the baseline, repeated seeded passes, and failed attempts. Exact-value legacy regressions use the explicit midpoint override; the dedicated suite clears it and exercises seeded variation.

| Entry | PASS | FAIL | Seconds |
|---|---:|---:|---:|
| EnemyHealthFeedbackVerification.Run | 69 | 0 | 78.14 |
| CombatVerification.Run | 176 | 0 | 146.34 |
| PowerPayoffVerification.Run | 99 | 0 | 48.66 |
| CombatBalanceVerification.Run | 1919 | 0 | 138.54 |
| RosterVerification.VerifyExisting | 216 | 0 | 83.63 |
| RosterVerification.Reload | 3 | 0 | 24.87 |
| MeleeVerification.Run | 42 | 0 | 39.16 |
| IceVerification.After | 34 | 0 | 44.05 |
| FirstPersonVerification.Run | 211 | 0 | 147.71 |
| FirstPersonVerification.Reload | 5 | 0 | 25.49 |
| FeelVerification.Run | 143 | 0 | 83.61 |
| HeroForgeVerification.VerifyExisting | 136 | 0 | 61.57 |
| HeroForgeVerification.Reload | 3 | 0 | 22.37 |
| HudVerification.Run | 308 | 0 | 60.59 |
| ModeVerification.VerifyExisting | 79 | 0 | 44.39 |
| ModeVerification.Reload | 2 | 0 | 22.33 |
| ModeExpansionVerification.Run | 179 | 0 | 92.15 |
| ModeExpansionVerification.Reload | 10 | 0 | 23.84 |
| EndlessEventsVerification.Run | 42 | 0 | 33.72 |
| HeatResponseVerification.Run | 28 | 0 | 34.59 |

Retained failures: `seeded-r1` reached 405/1 on an exact floating comparison (`45*.6`); a 0.001 arithmetic tolerance fixed the assertion without changing the target. `regression-r1` Roster reached 41/1 because its performance measurement continued an already-drained Laser channel beyond the new budget. It now normally regenerates to full before that separate measurement; depletion, refusal, cooldown and exact energy controls remain. Final Roster is 216/0 plus Reload 3/0. Melee expectations reflect the independent new multipliers. PowerPayoff's lethal heavy-throw fixture uses 10 HP because the new 400 kg mean is 29.814; nonlethal/full-health, physical impulse and off-axis controls remain.

Implementation checkpoints: `94de986` health ranges; `1d6a2dd` damage boundaries; `9d69ac1` Fire/Strength; `374cb48` TK/Ice; `dfe3efb` Laser/Darkness/Poison/Lightning; `51dfb73` setup defaults and fixtures; `d96010f` seeded verification; `3b61e79` comments/status; `1d71109` initial evidence/report. Final regressions ran on `1d71109`; the completion checkpoint changes only documentation and evidence.

Unverified: human feel and visual acceptance, sustained overlapping Poison balance, standalone/platform performance and long-duration production sessions. The matched Editor frame-time change is +0.061 ms (~0.62%), too small in these short noisy samples to establish a material regression or a speedup. No difficulty, enemy damage variance, shipping mission activation or push was performed. Existing unrelated user changes remain outside these checkpoints. This completes this balance task's automated work, not the entire overnight game build.
