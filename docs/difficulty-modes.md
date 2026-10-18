# Difficulty implementation audit

Work in progress. Branch `codex/difficulty-modes`, baseline `1b0d6f9`. No completion claim.

| Multiplier | Normal | Hard | Extreme |
|---|---:|---:|---:|
| Player damage | 1 | .97 | .955 |
| Enemy health | 1 | 1.05 | 1.075 |
| Player health | 1 | .95 | .925 |
| Hostile spawn rate | 1 | 1.15 | 1.225 |
| Energy usage | 1 | 1.10 | 1.15 |
| Cooldown duration | 1 | 1.10 | 1.15 |
| Enemy outgoing damage | 1 | 1 | 1 |

Difficulty is a layer on existing modes, not duplicated mode assets. Selection is saved through PlayerProgression and copied into immutable session settings at startup. Normal preserves the corrected basic melee and TK baseline. Health rolls once after final wave and difficulty scaling; damage scales at final damage recipients. Enemy damage and individual attack cadence remain unchanged.

Recurring Heat police and mode encounter intervals divide by spawn rate. Endless currently fills available slots immediately, with no positive timer to shorten. Its zero-delay reinforcement behavior, intermission, composition, wave scaling and caps must stay unchanged to preserve Normal. Difficulty can raise Endless durability and resource pressure, but cannot make an already immediate refill faster. Scripted objective counts remain authored.

## Authoritative modifier paths

- Exactly three `DifficultyDefinition` assets under Resources/Difficulties. No copied modes/powers. Extreme values are1 +1.5×(Hard−1).
- Additive Version1 `ProgressSave.DifficultyId`; fresh, missing/invalid fields resolve Normal. `PlayerProgression.SetDifficulty` validates and saves. WorldSession copies all six multipliers into readonly SessionDifficulty before power/NPC initialization. Later save changes affect the next session only.
- Player damage: CityNpc.Damage scales the incoming player-sourced amount exactly once before health subtraction, progression reporting and NpcDamageResult publication. Thus all powers, melee, poison ticks, Laser ticks, TK, shatter and independent synergies inherit the same multiplier without changing their roll boundaries. Environmental/NPC damage with no PowerUser source is unchanged. Numeric BreakableProp, vault and armed hardpoint targets apply the same final multiplier; force-only interactions and fixed hit-count destruction rules remain intact.
- Enemy health: CityNpc.Spawn scales the final mean once before the existing health roll. Endless supplies its wave/event mean; Heat quality scaling remains multiplicative afterward with no new roll. Civilians retain fixed HP. SetCombatStats remains an explicit fixture/scaling assignment, not a second difficulty hook.
- Player health: WorldSession.MaxHealth = shared baseline × hero MaxHealth × difficulty. Initial health, respawn and HUD health fraction use it.
- Energy: PowerUser's activation affordability/payment and channel start threshold/drain use EnergyUsage. Energy maximum/regeneration stay unchanged. Flight fuel is a distinct resource and is unchanged.
- Cooldowns: PowerUser.Stats multiplies activation duration once after hero modifiers; Tick multiplies the independent ChargeRecharge threshold once. Basic melee's readiness timestamp and SynergyRunner's cooldown get the same duration multiplier. Recharge and cooldown are separate gates, not two factors applied to one timer. HUD charge/synergy rings and affordability use their resolved values. Backflip traversal cooldown, animation windups, power effect durations and side-switch timer are unchanged.
- Spawn cadence: WorldSession police/legacy crime clocks and GameModeSession encounter clock use baseline/rate. Heat thresholds, hostility, archetype selection, authored encounter counts and individual enemy attack timing are untouched. Endless already refills instantly; no artificial delay or extra enemies were added.

## Measured focused results

First complete run:1370 PASS/0 FAIL plus separate-process Reload7/0. Later strengthened/final results are pending below. The initial failed attempt reached204/1 on a fixture expecting hostile feedback from friendly criminals in Villain mode; the corrected fixture uses hostile police. Gameplay hostility was preserved. Build iteration failures (HUD variable name, private fixture setter, obsolete search overload) are retained; the corrected SDK build passed0 warnings/errors.

| Actual player MaxHealth | Normal | Hard | Extreme |
|---|---:|---:|---:|
| VECTOR |100|95|92.5|
| TITAN |140|133|129.5|
| NOVA |75|71.25|69.375|

Within every difficulty TITAN > VECTOR > NOVA. Energy maximum stays100/80/140 and regen12/12/16.8 per second respectively. Actual hero punches verify Titan's1.25 melee multiplier remains.

| Resource/timer | Normal | Hard | Extreme |
|---|---:|---:|---:|
| Ice instant energy |10|11|11.5|
| VECTOR/TITAN Ice cooldown (s) |.6|.66|.69|
| NOVA Ice cooldown (s) |.48|.528|.552|
| Ice charge recharge (s) |1.5|1.65|1.725|
| Laser gross energy/sec |64|70.4|73.6|
| Laser release cooldown (s) |.8|.88|.92|
| Mode recurring encounter (s) |50|43.47826|40.81633|
| Police base response (s) |2|1.73913|1.63265|
| Legacy sandbox crime (s) |12|10.43478|9.79592|
| Endless queued refill (s) |0|0|0|

Before/Normal intervals match the existing source baseline (50/2/12 seconds and immediate Endless). Actual first police arrivals in focused-r2 were2.00078/1.73954/1.63413 seconds; real below/above-threshold encounter ticks produce exactly one spawn at the resolved interval. Existing Heat tier arrival factors continue to compose with difficulty.

The same seeded attack gets97%/95.5% of Normal damage; health gets105%/107.5% of its corresponding Normal roll. The focused suite exercises actual Fire impacts, Ice casts, Laser channel ticks, hero punches, per-archetype health samples, HUD final numbers and stored maxima. No rerolls per frame are introduced. Independent synergy damage at the final recipient also scales; baseline authored synergy assets remain unchanged.

## UI and saves

Home has three compact selectable buttons before the existing mode cards. Navy/cyan Normal, orange Hard and red/purple Extreme have progressively stronger borders; a bright selected border is distinct from hover/focus. Details are generated from the actual definition. Existing Forge selection and mode launch routes remain. F3 includes the current session difficulty and all multipliers. Screenshots: `Verification/Difficulty/normal.png`, `hard.png`, `extreme.png`.

Real button submission persists selection; tests cover fresh save, an actual old JSON file lacking the field, invalid selection, all12 mode/difficulty combinations, Results/Home round trips, active-session immutability, and separate-process Extreme reload. Mode definitions/goals remain unchanged. Free Play still has no automatic loss condition; Endless retains wave results and budget12.

## Performance and limits

Focused-r2 matched fixed12-target/180-frame Editor workload: Normal134.58FPS/7.430ms, Hard124.01/8.064, Extreme122.90/8.137. All retained12 living fixture enemies,20 bar slots,96 number slots, peak48 active labels and1 active particle system. Short sequential samples are noisy; these are not proof of equal performance or standalone/long-duration pressure acceptance. The final sample adds real Fire casts and will be recorded separately rather than replacing this measurement silently. No caps or detail were reduced.

Human playtesting remains required for Normal feel, Hard/Extreme intensity, energy and cooldown pressure, survivability, individual power utility and any future change to spawn pressure. No claim that these difficulties feel balanced. No push or shipping mission activation.

## Checkpoints and exact file inventory

`d0cd464`: definitions and baseline audit. `e22593a`: save/Home/snapshot. `3fc8fdb`: damage/health. `e681566`: resources/timers. `330bb89`: timed spawning/HUD. `6c4b3ea`: verification, screenshots/layout fixes, runtime gates.


- `.gitignore`
- `Assets/Editor/DifficultyVerification.cs`
- `Assets/Editor/DifficultyVerification.cs.meta`
- `Assets/Editor/HeroStatsVerification.cs`
- `Assets/Resources/Difficulties.meta`
- `Assets/Resources/Difficulties/extreme.asset`
- `Assets/Resources/Difficulties/extreme.asset.meta`
- `Assets/Resources/Difficulties/hard.asset`
- `Assets/Resources/Difficulties/hard.asset.meta`
- `Assets/Resources/Difficulties/normal.asset`
- `Assets/Resources/Difficulties/normal.asset.meta`
- `Assets/Resources/DifficultyMenu.uss`
- `Assets/Resources/DifficultyMenu.uss.meta`
- `Assets/Scripts/BreakableProp.cs`
- `Assets/Scripts/CityNpc.cs`
- `Assets/Scripts/DifficultyDefinition.cs`
- `Assets/Scripts/DifficultyDefinition.cs.meta`
- `Assets/Scripts/DifficultyVerificationRunner.cs`
- `Assets/Scripts/DifficultyVerificationRunner.cs.meta`
- `Assets/Scripts/Forge/SynergyRunner.cs`
- `Assets/Scripts/GameHud.cs`
- `Assets/Scripts/GameModeSession.cs`
- `Assets/Scripts/Missions/HeistScenario.cs`
- `Assets/Scripts/Missions/StagedScenario.cs`
- `Assets/Scripts/ModeScreens.cs`
- `Assets/Scripts/ModeScreensDifficulty.cs`
- `Assets/Scripts/ModeScreensDifficulty.cs.meta`
- `Assets/Scripts/PlayerProgression.cs`
- `Assets/Scripts/PowerUser.cs`
- `Assets/Scripts/PrototypeHUD.cs`
- `Assets/Scripts/SuperHeroController.cs`
- `Assets/Scripts/WorldSession.cs`
- `STATUS.md`
- `docs/difficulty-modes.md`
- `Verification/Difficulty/`: retained manifests, checks, observations, screenshots and builds (isolated saves ignored).
