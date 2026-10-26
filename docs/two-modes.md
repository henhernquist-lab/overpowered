# TWO MODES + GUARANTEED PULSEBREAKER ROUNDS

Branch `feature/two-modes-boss-encounters`, worktree `../op-two-modes` (base: `44acb44`, which already carries the
Pulsebreaker miniboss `3301e39` and the round-weighted enemy curves). No merge, no push.

## 1. Why Pulsebreaker never appeared

Repo truth, not documentation: the boss existed **only on its own feature branch**. The checkout the game was opened from
(`op-overnight-recovery` at `eb5290c`) predates `3301e39`, so it had no `PulsebreakerBoss`, no `Enemies/Pulsebreaker.asset`
and no miniboss slot on the Endless director. On top of that the round director's `Events` asset was not assigned in that
baseline, so even a build containing the boss would have had no boss round to spawn it into.

That is why "every 5 rounds" was true in the docs and invisible in the game. The fix is not a new subsystem: this branch is
the tree that actually contains the boss, the wave-events asset is assigned to `ModeDirectors/EndlessWaves.asset`, and the
boss slot is now a reserved, retried, diagnosable spawn instead of a budget-dependent one.

## 2. The two player-facing modes

The Home menu shows exactly two cards, in `MenuOrder`:

| Card | Definition id | Director | Results | Notes |
| --- | --- | --- | --- | --- |
| ENDLESS FIGHT | `endless-fight` | `ModeDirectors/EndlessWaves.asset` | Survival | Infinite rounds, all five archetypes, boss every 5th round |
| FREE PLAY | `free-play` | none | none (`ShowResults = false`) | Open city, Heat/police, no waves, no boss |

`hero`, `villain` and `endless-fight-villain` remain **Playable data** with `MenuHidden = 1`: they are still loadable by id
(`GameFlow.Select`, `ModeScreens.LaunchModeById`) for internal side behaviour, the verification suites and old saves, but are
no longer menu choices. Nothing was deleted from the enums, the save schema or the gameplay code, so old saves keep loading.

The Home screen is data-driven: `ModeScreens.BuildHome` takes the two visible definitions as cards and puts every other
visible playable mode plus the Battle Lab route in the extras row. Adding a third menu mode later is a data change.

Hero Forge is a button (`hero-forge-open`), not a mode; BATTLE LAB keeps its own route below the cards.

## 3. Boss schedule (`EndlessWaveEvents`)

```
bossRound = round >= 5 && round % 5 == 0
```

- `MinibossEvery = 5` on `ModeDirectors/EndlessWaveEvents.asset`, `MinibossArchetype = Enemies/Pulsebreaker`.
- The boss is **not** drawn from the archetype weights: it is slot 0 of a boss round (`SlotOf(plan, 0)`), reserved before
  any regular body, so a full alive budget cannot block or replace it.
- Boss rounds trade regular bodies for the boss: `MinibossRegularCost = 3`.
- `MinibossAliveCost = 4` of `MaxAlive 12`, so the boss occupies real simultaneity budget without starving the wave.
- `MinibossHealthMultiplier = 8`, `MinibossDamageMultiplier = 1.6` on the wave formulas, so difficulty applies once through
  the existing `WorldSession.Difficulty` path (no second multiplier).
- One live boss per city: `PulsebreakerBoss.Active` gates both `Attach` and `EndlessWaveState.SpawnBossSlot`, so a retry can
  never duplicate a boss, and a body the brain would refuse is never created.
- Placement: the normal ring (three rings, 4x candidates) then, after `BossSpawnRetryLimit = 8` attempts, the arena floor.
  Failure writes `BossSpawnDiagnostic` and a HUD message instead of silently treating the boss as defeated; the wave cannot
  complete without it.
- Free Play has no `Director`, so no boss and no waves can be started there by construction. The Combat Lab is untouched.

Round 5 on Normal, measured: **832 max HP, 17.9 contact damage** (`HealthFor(5) = 104 x 8`). Round 10 is higher through the
existing 15%/wave health growth. The boss reads as a threat because of its telegraphs, not a giant HP bar: it is ~13x a
round-5 Rusher, not a 5-minute sponge, and it has no immunity to any power.

## 4. Boss health bar (`GameHud` / `Hud/GameHudBoss.cs`)

- Top centre, 560 logical px wide at y = 104, built once per HUD, driven every frame from `PulsebreakerBoss.Active` and
  `CityNpc.CurrentHealth / MaxHealth` (the authoritative values — no predicted percentage, no cached estimate).
- Reads `PULSEBREAKER`, `ELITE THREAT`, `PHASE 1`, and `current / max` HP as whole numbers.
- Phase 1 fill and trim are crimson (`CityColor.Red`); Overdrive switches the whole panel to `CityColor.Fire`, relabels the
  tag to `OVERDRIVE` and the phase chip to `PHASE 2 · OVERDRIVE`, and thickens the bottom rule.
- Visible only while the boss is alive, hostile, within `BossBarRange = 90 m`, the session is running and no menu is open.
  It disappears on death, despawn, pause, results and city unload, and is hidden for every ordinary enemy
  (`CityNpc.IsBoss` gates the floating bar).
- It is one element added to the existing `OverlayRoot`, so it does not accumulate and has no per-frame allocation.

## 5. Attacks (all values in `Resources/Enemies/PulsebreakerTuning.asset`)

| Attack | Windup | Telegraph | Payoff | Recovery | Counterplay |
| --- | --- | --- | --- | --- | --- |
| Shockwave Slam | 1.0 s | ground disc at the impact point | 8 m ring expanding over 0.45 s, 1.0x contact damage, knockback | 1.2 s | step outside 8 m or be 1.3 m off the ground |
| Pulse Cannon | 1.35 s | locked line, 30 m long | bolt at 24 m/s, up to 30 m, 1.25x damage | 1.0 s | move out of the lane; walls stop the bolt |
| Overdrive Rush | 0.8 s | lane on the ground | 26 m/s for up to 0.45 s, 0.9x damage | 1.5 s on a miss, 0.6 s on a hit | sidestep the locked lane; geometry stops it |

Player damage per hit is capped at `DamageCapFraction = 0.25` of max health, so no single attack is an instant kill.
Freeze and root are scaled (`FreezeResistance 0.5`, `RootResistance 0.6`) and a control effect during a windup cancels the
attack through the existing `CityNpc` control path — no per-power exceptions.

Phase 2 at 40% HP: `Phase2GapMultiplier 0.6`, `Phase2RecoveryMultiplier 0.7`, `Phase2MoveSpeedMultiplier 1.15`, a 40%
chance of chaining a follow-up (`ComboGap 0.35`). Telegraph durations are never shortened. The transition fires once,
releases the attack token, and death always wins over it.

## 6. Verification

`Verification/TwoModes/results.txt` — `TwoModesVerification.Run` (batch Play Mode, one process, fresh isolated save):

- the catalog is exactly two visible modes, with Hero / Villain / Endless (Villain) Playable but hidden;
- the two real cards render side by side, the Forge button and Battle Lab route exist, no Hero/Villain card does;
- real card submits start Endless (rounds 1-2 clear and progress) and Free Play (no director, no wave state);
- Free Play holds 3 s with no boss on any frame and no boss bar;
- the boss schedule is exact for rounds 1-30 (boss on 5, 10, 15, 20, 25, 30 only, first slot reserved, -3 regulars, same
  answer for every run seed) with negative controls on 1-4, 6-9, 11, 14, 16;
- live rounds 5, 10 and 15 each spawn a real Pulsebreaker: hostile, aggroed, bar visible with the authoritative fraction,
  following real damage, one bar element, kill counted once toward round completion, bar gone afterwards;
- rounds 11-14 carry no boss on any frame;
- the finished run's save resolves every mode id and keeps the best score/wave.

`PulsebreakerVerification.Run` covers the three attacks, the dodges, the walls, the rush bounds, the phase shift, the bar
labels and the ordinary-enemy regression. `EndlessEventsVerification.Run` covers the wave plan and score accounting.

**UNVERIFIED**: hands-on feel, readability at dusk/night, sustained standalone frame rate, and the authored boss model (the
boss currently wears the existing humanoid rig with a scaled silhouette and energy VFX; a bespoke armor set is still the
largest presentation gap).

## 7. How to see it

1. Open **`/Users/melaniehernquist/Documents/ChatGPT/op-two-modes`** in Unity 6000.6.0f1 (not the old checkout — that one has
   no boss code).
2. Start at `Assets/Scenes/Home.unity`, pick ENDLESS FIGHT, keep Normal.
3. Play to round 5. The banner reads `WAVE 5 · PULSEBREAKER` and `ELITE THREAT`; the crimson bar appears at the top.
4. `PulsebreakerVerification.Run` also exposes a fast path if you want the boss immediately in a test session.

## 8. Human playtest checklist

1. Menu: exactly ENDLESS FIGHT and FREE PLAY; Forge opens; Battle Lab opens; Hero/Villain are gone.
2. Endless: rounds 1-4 are basic enemies; Skirmisher from 3, Bulwark from 5 is still readable.
3. Round 5: the banner announces the boss before it reaches you; the bar names it and fills/empties with real damage.
4. Slam: can you tell where it will land, and can you jump or leave the ring?
5. Cannon: can you see the lane before the shot and step out of it?
6. Rush: can you see the lane and does a miss give you a real opening?
7. At 40%: the bar switches to `PHASE 2 · OVERDRIVE`; the fight gets faster but telegraphs stay readable.
8. Kill it: bar disappears, round completes, round 6 starts; no boss in rounds 6-9.
9. Round 10: the boss is back, harder, not unfair.
10. Free Play: 2-3 minutes of play with no boss and no waves; Heat/police still work.
11. Pause/return home mid-fight: no stuck bar on the next screen or session.
