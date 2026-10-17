# Enemy health feedback

Implementation on `codex/enemy-health-feedback`, based on `44ba3a0`. Combat balance assets are unchanged. Implementation and required automated checks are complete. Human in-motion presentation review remains open.

## Architecture and display rules

`CityNpc.Health` and `MaxHealth` remain the only authoritative health values with private setters. Read-only `CurrentHealth`, `HealthFraction` and `Alive` expose them. `CityNpc.DamageApplied` emits `NpcDamageResult` after applying damage and existing assault processing (so the first hit can establish patrol hostility): target, attacker, source ID, archetype/role snapshot, full resolved damage, capped health lost, health before/after/max, timestamp and world position. Existing progression/combat events remain intact. Every existing attack reaching `CityNpc.Damage` automatically participates, including future modifiers. Powers never calculate UI values or call the HUD themselves.

The existing UI Toolkit overlay owns screen-projected bars and numbers. Bars read authoritative health directly; the last displayed values are cached only to avoid rebuilding unchanged text. The real `CityNpc.Hostile` relation determines enemies. Civilian/friendly damage is excluded. Player damage or committed enemy engagement holds a bar for 3.5 seconds; clear crosshair aim also reveals one briefly. Display range is 38 metres, covering current ordinary ranged engagement distances. Active on-screen candidates check solid-world visibility every 0.12 seconds. Actor/dynamic-body colliders do not count as solid buildings. Bars follow scaled NavMeshAgent height plus 0.23 m, face the screen and retain a consistent screen size. Defeated bars empty for 0.25 seconds then release; destroyed targets release immediately.

Current/max HP and damage format to at most one decimal with integer values shown without `.0`. Numbers retain full float totals internally, rise 32 panel pixels, last 0.8 seconds and fade for their last 0.25 seconds. Navy backing, cyan border and pale text use existing palette colours. Numbers close to the crosshair shift sideways. One consistent language serves both sides.

Laser Eyes and Poison merge only the same target AND same source when the next hit arrives within a rolling 0.2-second interval. Each real tick adds its resolved damage and refreshes lifetime. A sustained Laser channel can keep updating one total until the gap exceeds the window. Default Poison ticks are 0.5 seconds apart and therefore normally produce separate labels; the rapid-Poison fixture proves aggregation works if future tick intervals become shorter. Fire, Lightning and melee are immediate separate events per actual victim. Overkill reports the full attack while `HealthLost` remains capped to remaining HP.

Pools contain 20 prebuilt bars and 96 number labels, with no per-hit GameObject/material/mesh creation by this system. Saturation increments a visible verification counter; the bounded 128-result telemetry history still records authoritative events. The counters and pool lists are available for diagnostics. Constants are centralized at the top of `GameHudEnemyHealth.cs`.

## Current health and balance observations

Actual zero-Heat spawned Rusher/Gunner/Brute HP is 39/65/195. A pursuing-role Brute manually spawned at zero Heat is 195. The actual four-star pursuit actor spawned by `ReconcilePolice` has **411 HP**, matching `(65 + 4 × 18) × 3`. Actual Endless wave-1 director spawns are **39-HP Rushers**; later waves apply the director's explicit scaling independently of Heat. Police response tiers and optional Endless elite/miniboss modifiers can add further scaling; there is no universal HP for every encounter.

No balance was changed. At the tested unmodified loadout, Fire resolves 45: enough to defeat a fresh 39-HP Rusher. Lightning starts at 24 and its second target receives 20.4 (existing 0.85 falloff). Strength punch is 35, with the existing Martelo multiplier. Laser totals vary with actual channel timing; the event sum is used rather than assuming a fixed number of frames. These are observations for a later balance task, not a difficulty recommendation.

## Files

- `Assets/Scripts/CityNpc.cs`: read-only API and authoritative event.
- `Assets/Scripts/GameHud.cs`: lifecycle integration and event unsubscribe.
- `Assets/Scripts/Hud/GameHudEnemyHealth.cs` and `.meta`: pooled presentation/aggregation/telemetry.
- `Assets/Editor/EnemyHealthFeedbackVerification.cs` and `.meta`: isolated-save batch entry point.
- `Assets/Scripts/Powers/EnemyHealthFeedbackVerificationRunner.cs` and `.meta`: gameplay controls, images and stress measurements.
- `Assets/Scripts/ModeExpansionVerificationRunner.cs`: repairs the obsolete eleven-power fixture to exercise all twelve current shipping powers through legal attack/secondary slots, retaining legacy Flight refusal.
- `Assets/Scripts/CityVerificationRunner.cs`: explicit legacy Flight fixture through normal save/equip gates, with shipping unequipped control and in-memory catalog restoration.
- `STATUS.md`, this report, and `Verification/EnemyHealthFeedback/`: durable evidence.

## Captures

The six requested captures are in `Verification/EnemyHealthFeedback/`: `rusher-health.png`, `gunner-health.png`, `brute-health.png`, `fire-damage.png`, `laser-aggregated.png`, `lightning-chain.png`. Ranged-attack fixtures deliberately use 1000 HP to survive long enough for measuring and capturing; these are not shipping archetype HP changes. The archetype screenshots use real default HP.

Henry still needs to judge bar size/height, exact HP text readability, number size/lifetime, dense combat clutter, Laser update speed and post-combat hold duration in motion. Automated visibility and arithmetic checks do not establish that the UI feels good.

## Fresh performance evidence

Focused r4, twelve live enemies, batch Editor at 1280×720, 120 frames each:

| Sample | FPS | Mean frame time | Reported main-thread managed allocation |
|---|---:|---:|---:|
| Idle before active feedback | 91.37 | 10.944 ms | 0 B/frame |
| Active feedback | 102.32 | 9.773 ms | 0 B/frame |

These compare different workloads, not old/new code. They are short samples with substantial host variability: r3 measured 103.45 FPS / 9.666 ms idle versus 95.59 FPS / 10.462 ms active. Neither direction establishes a causal speedup/regression. All-thread allocation, standalone performance and prolonged human combat remain unmeasured.

The r4 sustained 72-hit burst kept **5,608 GameObjects, 113 loaded materials, 20 bar slots and 96 number labels** unchanged, with **0 dropped numbers**. Real Laser, Poison and melee run under one legal loadout; real Fire and Lightning are separately exercised in twelve-enemy fixtures. Authoritative synthetic damage bursts specifically test worst-case pool pressure without bypassing gameplay equip gates to cast four attack powers simultaneously.

## Checkpoints and retained failures

- `4c1240e`: authoritative health/damage API.
- `fc02113`: bars, numbers and aggregation.
- `c82ff97`: pooling/capture fixes and dedicated verifier.
- `5fb357e`: failed/passing focused evidence and captures.
- `81d6524`: first-assault hostility timing and extended real gameplay controls.

Focused r2 failed 25/1 due to overlapping test actors; r3 failed 29/1 because the 64-label pool could not hold a 72-hit burst. Fixture separation and expansion to 96 slots fixed their causes without reducing the stress workload. R4 passed 45/0. R5 passed 67/0 with default Poison, actual pursuit/Endless spawns, first person and real-attack overkill. ModeExpansion initially failed 38/1 on an obsolete eleven-power assertion; its corrected shipping-roster fixture passed 179/0 and its separate-process reload passed 10/0. Subsequent fixture failures and their fixes are recorded in STATUS.md. Raw logs remain local; manifests, checks, measurements and captures are committed.

## Final verification — 2026-10-17

| Suite | PASS | FAIL | Evidence folder |
|---|---:|---:|---|
| CombatVerification.Run | 176 | 0 | regression-r1 |
| RosterVerification.VerifyExisting | 215 | 0 | regression-r1 |
| RosterVerification.Reload | 3 | 0 | regression-r1 |
| MeleeVerification.Run | 42 | 0 | regression-r1 |
| FirstPersonVerification.Run | 211 | 0 | regression-r1 |
| FirstPersonVerification.Reload | 5 | 0 | regression-r1 |
| CameraVerification.Verify | 45 | 0 | regression-r1 |
| HudVerification.Run | 308 | 0 | regression-r1 |
| HeroForgeVerification.VerifyExisting | 136 | 0 | regression-r1 |
| HeroForgeVerification.Reload | 3 | 0 | regression-r1 |
| ModeVerification.VerifyExisting | 79 | 0 | regression-r1 |
| ModeVerification.Reload | 2 | 0 | regression-r1 |
| ModeExpansionVerification.Run | 179 | 0 | regression-r5 |
| ModeExpansionVerification.Reload | 10 | 0 | regression-r5 |
| CityVerification.Run | 63 | 0 | regression-r6 |
| CityVerification.Reload | 5 | 0 | regression-r6 |
| FeelVerification.Run | 143 | 0 | regression-r6 |
| EnemyHealthFeedbackVerification.Run | 69 | 0 | regression-r6 |

**Selected latest successful results: 1,694 PASS / 0 FAIL across 18 processes.** These are fresh runs from this task, not historical STATUS counts. They span checkpoints: r1's earlier broad suites precede the final first-assault publication-order fix; r5 finishes the mode fixture repairs; r6 tests final City/Feel behavior and the final feedback implementation. The focused suite was rerun after the final implementation changes. No code was changed after r6. Every selected run reports unchanged personal-save and Sidekick database hashes. Gameplay tuning, power assets, shipping rosters, vendor materials and shipping mission pools are unchanged. No push.

The final focused **69/0** includes the stronger first-Villain-assault check requiring its actual number/history entry, real-attack overkill, aim/aim-away, first-person visibility, civilian/friendly controls, wall/range controls, full default Poison duration/spread, actual pursuit/Endless spawns, and matched performance controls.

| Final matched workload (120 frames, 12 targets, 72 real damage points) | FPS | Mean frame time | Reported managed B/frame |
|---|---:|---:|---:|
| Feedback consumer suppressed | 90.99 | 10.991 ms | 0 |
| Feedback enabled | 85.68 | 11.671 ms | 0 |

Measured difference: **−5.31 FPS / +0.680 ms per frame (~6.2%)**. Same actors, camera, resolution and hit schedule; the Editor-only switch suppresses this HUD consumer while damage still occurs. Both samples prove exactly 72 health points lost; the enabled sample also starts exactly 72 pooled numbers without drops. Both hold 5,608 GameObjects, 113 materials, 20 bars and 96 number slots. This is a short sequential instrumented Editor comparison, not a standalone benchmark or a confidence interval; warming, background load and frame timing remain limitations. The final separate idle/active samples were 96.45/91.73 FPS and 10.368/10.902 ms. No detail cuts were made to hide the measured cost.

Actual Endless maximum-alive sample (r4, twelve enemies) was **112.45 FPS / 8.89 ms mean / 11.99 ms p95**, with a hovering player and fixed camera. It is not the same workload as the hit-burst benchmark.

The older failed attempts remain in their original evidence folders. Additional fixture failures were grounded Teleport refusal, Phase Shift taking precedence over the unequipped gate, invalid-save `LastError` clearing after durable repair, and unequipped legacy Flight fuel expectations. Fixes establish the required state or assert actual outcomes; no combat controls were weakened.

Final verified code checkpoint: **`d4c566e`** (committed unchanged after the r6 runs). Final Unity-bundled SDK build: **0 warnings / 0 errors**; `Verification/EnemyHealthFeedback/final-build.txt`. Earlier fixture checkpoint: `3d05a6b`.

- [rusher-health](/Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery/Verification/EnemyHealthFeedback/rusher-health.png)
- [gunner-health](/Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery/Verification/EnemyHealthFeedback/gunner-health.png)
- [brute-health](/Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery/Verification/EnemyHealthFeedback/brute-health.png)
- [fire-damage](/Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery/Verification/EnemyHealthFeedback/fire-damage.png)
- [laser-aggregated](/Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery/Verification/EnemyHealthFeedback/laser-aggregated.png)
- [lightning-chain](/Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery/Verification/EnemyHealthFeedback/lightning-chain.png)
