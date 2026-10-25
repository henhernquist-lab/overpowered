# Enemy Spawn Director 2.0

**Branch:** `feature/enemy-spawn-director-2` · **worktree:** `../op-enemy-spawn-director`
**Base:** `eb5290c` (the playable checkout) fast-forwarded to `3301e39`, which brings the committed Pulsebreaker miniboss in
without any unrelated commit.
**No merge, no push.** Unity 6000.6.0f1, Built-in Render Pipeline.

This document covers what the spawn system now does, the exact numbers, and what is verified versus still a human call.

---

## 1. What the repository actually had

Read from the checkout, not from older notes:

- One round system exists: `EndlessWaveDirector` + `EndlessWaveState` + optional `EndlessWaveEvents`.
- `Modes/endless-fight.asset` and `Modes/endless-fight-villain.asset` both point at
  `Resources/ModeDirectors/EndlessWaves.asset` (one director asset serves Hero and Villain Endless).
- `Modes/hero.asset`, `Modes/villain.asset`, `Modes/free-play.asset` have `Director: {fileID: 0}`. **Hero and Villain are
  encounter-driven** (crime encounters, Heat police, `Encounters`), not round-driven. Free Play has no director at all.
  BATTLE LAB is a practice session with no director.
- The director already dealt archetypes from a `Composition` table (`ArchetypeShare`: archetype, `FromWave`, `Weight`) with
  a **deterministic smooth weighted round-robin**: weights are accumulated per spawn, the highest accumulator wins, and the
  total is subtracted. That gives an exact, repeatable mix per wave instead of per-spawn randomness.
- That table was fixed for a whole run: Rusher 1/3, Gunner 2/2, Brute 3/1, Skirmisher 3/2, Bulwark 5/1. It never changed
  with the round, so late waves kept the same 33/22/11/22/11 split as wave 5.
- `EndlessWaves.asset` had **no `Events` assigned**, so the events asset (modifiers, elites, minibosses) existed but was
  never live. Practically: **Pulsebreaker never spawned in the real game**, only in tests.

## 2. What changed

1. **Wave curves.** New `EnemyCompositionCurve` (`Assets/Scripts/Enemies/EnemyCompositionCurve.cs`): the five regular
   archetypes, the round each unlocks, and a weight that is a function of the wave. Two shapes ship: `Standard` and
   `Endless`. `EndlessWaveEvents` now carries `CurveKind`, `StandardCurve`, `EndlessCurve`.
2. **One engine, two weight sources.** `EndlessWaveDirector.ArchetypeFor` runs the existing round-robin over either the
   curve (new) or the `Composition` table (unchanged, `CurveKind = WeightTable`). Same maths in both paths.
3. **Boss rounds trade bodies for the boss.** `EndlessWaveEvents.MinibossRegularCost = 3`: a boss wave spawns 3 fewer
   regulars, then the boss, so the round gets a real threat instead of a longer wave.
4. **The events asset is now wired** to `EndlessWaves.asset` with the **miniboss schedule and the curve only**: elites and
   modifier waves stay off (`EliteFromWave/ModifierFromWave = 99`) and the flawless bonus is 0, so existing wave scoring is
   unchanged. Those features remain one asset edit away.
5. **Pulsebreaker is the miniboss** (`MinibossArchetype`), every 5th round, one at a time.

## 3. Exact formulas

`r` = round. All weights are normalised over the archetypes unlocked in that round, so they always sum to 1.

### Standard (`CurveKind = Standard`)

| Rounds | Rusher | Gunner | Brute | Skirmisher | Bulwark |
|---|---:|---:|---:|---:|---:|
| 1–2 | 0.48 | 0.34 | 0.18 | 0 | 0 |
| 3–4 | 0.39 | 0.29 | 0.19 | 0.13 | 0 |
| 5+ | `0.32 − 0.07t` | `0.29 − 0.03t` | 0.19 | `0.12 + 0.05t` | `0.08 + 0.05t` |

`t = clamp01((r − 5) / 15)`, so the curve settles at round 20 and stays there: 25 / 26 / 19 / 17 / 13.

### Endless (`CurveKind = Endless`, shipping)

| Rounds | Rusher | Gunner | Brute | Skirmisher | Bulwark |
|---|---:|---:|---:|---:|---:|
| 1–2 | 0.48 | 0.34 | 0.18 | 0 | 0 |
| 3–4 | 0.38 | 0.30 | 0.20 | 0.12 | 0 |
| 5+ | remainder | 0.28 | 0.20 | `min(0.22, 0.12 + 0.005(r−5))` | `min(0.16, 0.08 + 0.004(r−5))` |

The Rusher is the remainder, floored at `MinRusherWeight = 0.05` and renormalised, so it can never go negative or vanish:
0.32 at round 5, 0.23 at 15, 0.14 at 25 and at 100.

### Unlock rounds (preserved, and why)

Gunner **2**, Brute **3**, Skirmisher **3**, Bulwark **5** — the existing teaching order was kept. The brief's round-1
vector puts Gunner and Brute on the field immediately; that would replace the game's deliberate melee-only opening. Instead
the requested weights apply from the round each archetype unlocks, renormalised until then:

- round 1 → 100% Rusher (only one unlocked)
- round 2 → 58.5% Rusher / 41.5% Gunner (0.48 : 0.34 renormalised)
- rounds 3–4 → 39 / 29 / 19 / 13 exactly as specified
- round 5 onward → **exactly** the specified tables

## 4. How the mix is drawn

Weights feed the existing deterministic round-robin, not a per-spawn RNG. For spawn index `i` of a wave the director
replays the accumulator from index 0, so the same wave always produces the same ordered mix (verified: 200 spawns of the
round-5 curve give exactly 64 / 56 / 40 / 24 / 16). This was chosen over sampling because the project's scene-free
`EndlessSimulation`, the score economy and the existing verification all depend on repeatable waves; a seeded per-spawn
draw would make the composition random per run for no gameplay gain. Boss/modifier *selection* still uses the seeded
`System.Random` (`EndlessWaveEvents.Seed`, `RandomizePerRun`).

Consequences that come free with the engine:

- a locked archetype simply is not in the pool, so it cannot be drawn early;
- proportions are exact (±1 enemy over any window), so no wave can randomly become all Bulwarks or all Skirmishers;
- no reroll loop, so there is no termination risk and no fallback path needed;
- the pool always has at least the Rusher, so an empty selection is impossible.

## 5. Counts, cadence, caps

Unchanged on purpose: `WaveSize(r) = FirstWaveCount + EnemiesPerWave × (r − 1)` (3 + 2 per wave), `MaxAlive = 12`
simultaneous, `IntermissionSeconds = 4`, ring spawn at `SpawnRadius 22` with `MinSpawnDistanceFromPlayer 12`, ground-only
`NavMesh.CalculatePath` validation. The brief's fallback `N(r)`/`I(r)` formulas were **not** layered on top — the existing
growth is already the authoritative curve, and multiplying it would have produced a second, unreviewed escalation.

Boss rounds in shipping data:

| Round | Base regulars | Regulars after trade | Boss | Wave size |
|---:|---:|---:|---:|---:|
| 4 | 9 | 9 | 0 | 9 |
| 5 | 11 | 8 | 1 | 9 |
| 10 | 21 | 18 | 1 | 19 |
| 15 | 31 | 28 | 1 | 29 |

Simultaneous pressure is bounded by `MaxAlive` and the boss's `MinibossAliveCost = 4` (8 regulars + 4 = 12 at round 5).

## 6. Difficulty

Verified, not re-derived: `CityNpc.Spawn` applies `Difficulty.EnemyHealthMultiplier` exactly once to the wave value
(`Rolls.Health(mean × multiplier)`), and the director's `Compose` supplies no difficulty factor. With a fixed combat roll
on Extreme the round-5 Rusher spawns at **67.08 HP = 104 × 0.6 × 1.075**; a doubled application would be 72.11. Enemy type
probabilities are identical on Normal / Hard / Extreme - difficulty changes pressure, not composition.

## 7. Files

New:
```
Assets/Scripts/Enemies/EnemyCompositionCurve.cs        the two wave curves (data + maths + safety)
Assets/Scripts/Enemies/SpawnCurveVerificationRunner.cs live suite
Assets/Editor/SpawnCurveVerification.cs                Run entry point
docs/enemy-spawn-director.md                           this document
```
Changed:
```
Assets/Scripts/EndlessWaveEvents.cs        CurveKind / StandardCurve / EndlessCurve / MinibossRegularCost + Plan trade
Assets/Scripts/EndlessWaveDirector.cs      ArchetypeFor routes to the curve, same round-robin (legacy path untouched)
Assets/Resources/ModeDirectors/EndlessWaves.asset        Events assigned
Assets/Resources/ModeDirectors/EndlessWaveEvents.asset   curve + intro rounds, miniboss only, flawless 0
Assets/Scripts/EndlessEventsVerificationRunner.cs        shipping-truth assertions (was: "no Events assigned")
Assets/Editor/EndlessSimulation.cs                       independent re-derivation includes the boss trade
```
Untouched: every enemy archetype asset and its AI, `CityNpc` combat, the Combat Lab, Hero/Villain/Free Play mode assets,
power and signature tuning, all shared meshes/materials, the render pipeline.

## 8. Using the Standard curve

Nothing shipped consumes it today because no shipped mode has rounds. To run a finite, campaign-shaped round mode on it:
give that mode a director asset whose `Events` uses `CurveKind = Standard`, and keep `MinibossEvery` for its boss rounds.
No code change is required, and the Endless modes are unaffected.

## 9. Verification

```
python3 tools/verify-sequential.py --out Verification/SpawnCurve/gates SpawnCurveVerification.Run
```

**1962 PASS / 0 FAIL**, exit 0. Highlights:

- both curves at rounds 1, 2, 3, 4, 5, 10, 15, 20, 25, 50, 100: weights exist, finite, non-negative, sum to 1.0000,
  locked archetypes exactly 0, Rusher never 0, no archetype above 60%;
- the published tables at rounds 5 / 15 / 20 / 25 / 100 match to 3 decimals;
- 200 spawns at rounds 5, 15 and 25 match the curve exactly (64/56/40/24/16, 46/56/40/34/24, 28/56/40/44/32);
- introduction rounds from the real spawn order: wave 1 Rusher only, wave 2 Rusher+Gunner, wave 3 adds Brute+Skirmisher,
  wave 4 still no Bulwark, wave 5 all five;
- legacy control: `CurveKind = WeightTable` still yields the old five-archetype table;
- plans 1–30: boss exactly on multiples of 5, regulars reduced by exactly 3 on boss rounds, one boss slot, size = regulars
  + boss, no elites;
- live Endless on Extreme: wave 1 has no boss, wave 5 is the boss round with 8 regulars + Pulsebreaker, the boss body
  carries the boss brain, exactly one boss appears, health matches `HealthFor(5) × archetype × 1.075` exactly, the alive
  budget holds every frame, the kill is counted once and the round advances to 6 with the bar hidden;
- Free Play: no director, no boss at any frame, no boss bar, ambient population intact.

Regressions re-run on the new shipping data: `EndlessSimulation.Run`, `EndlessEventsVerification.Run`,
`PulsebreakerVerification.Run` (see the report and `Verification/SpawnCurve/regressions/manifest.json`).

**Not verified:** how the mix feels at human speed, whether the round-5 boss round is well-paced, whether 8 regulars + a
boss reads as fair, and sustained standalone frame rate. See `STATUS.md` for the recorded limits.

## 10. Human playtest checklist

1. **Endless, round 1** — only Rushers. Round 2 adds Gunners. Round 3 adds Brutes and Skirmishers. Confirm the ramp reads as
   teaching, not as a wall.
2. **Round 5** — Pulsebreaker arrives with 8 regulars. Check the wave banner names him, the boss bar appears, and the wave
   does not feel longer than round 4.
3. **Round 6** — no boss, mix still varied; confirm the transition back does not feel like a letdown or a spike.
4. **Rounds 10 / 15** — Skirmisher/Bulwark share grows. Watch for Bulwark clusters (should be impossible) and for a wave
   that feels like only Rushers (should also be impossible).
5. **Difficulty** — Normal vs Extreme: same types, more pressure, no doubled health surprise.
6. **Free Play** — roam, fight Heat police, trigger crimes: no waves, no boss, no boss bar.
7. **Hero and Villain campaign** — encounters still spawn the same enemies as before this change.
8. **Performance** — a full round-15 wave with the boss and a phase-2 fight; watch frame time on the target machine.
