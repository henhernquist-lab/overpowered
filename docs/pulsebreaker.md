# PULSEBREAKER — Endless miniboss

**Branch:** `feature/pulsebreaker-miniboss` (worktree `../op-pulsebreaker`, based on `eb5290c`).
**Status:** implemented and verified in batch Play Mode. Human feel/readability and standalone performance are NOT verified.

Pulsebreaker is a superpowered armored enemy that takes over an Endless miniboss slot. It is a `CityNpc` body driven by its
own bounded state machine — it never runs the archetype attack cycle — with three telegraphed attacks, a 40% phase shift, a
damage cap and a boss health bar. Every existing system (health, damage, tokens, HUD feedback, difficulty, progression) is
reused, not duplicated.

---

## 1. How it plugs into the existing enemy architecture

| Concern | Existing system (unchanged) | Boss usage |
|---|---|---|
| Body, animator, colliders, death | `CityNpc` + `HumanoidPresentation` | same; the boss only adds a brain component |
| Damage to the boss | `CityNpc.Damage(...)` → `NpcDamageResult`, guard, stats, `OnDefeat` | same boundary, so damage numbers, XP, Heat and quest credit all work |
| Damage to the player | `WorldSession.DamagePlayer` (shield first), `FeelDirector.PlayerHit` | the boss never writes player health directly |
| Crowd rhythm | `WorldSession.AttackTokens` (budget 2) | the boss takes exactly one token per attack and releases it in recovery |
| Telegraphs | `AttackTelegraph` (pooled disc / bar) | reused as-is |
| Materials | `CityMaterials.Get` / `GetVfx` (shared palette) | every boss piece uses a shared swatch |
| Clips | `HumanoidAnimationTuning` on the shared controller | `HumanoidPresentation.BossAttack(...)` fits the existing clip's impact marker to the boss windup |
| Difficulty | `DifficultyDefinition.EnemyHealth/EnemyDamage` already applied at spawn | no second application anywhere in the boss code |

Shared-file edits are deliberately tiny (each is a two- to seven-line insertion):

- `Assets/Scripts/CityNpc.cs` — `if (boss != null) { boss.Tick(); return; }` at the top of `Update` (the boss owns its frame,
  bypassing `NpcLod`'s reduced think rate), `FreezeResistance`/`RootResistance` multipliers in `Freeze`/`Root`, and
  `boss?.Abort("cancel")` in `CancelAttack`. All four are inert for ordinary enemies (`Boss` is null, resistances are 1).
- `Assets/Scripts/Enemies/CityNpc.Boss.cs` — the partial-class hook (`Boss`, `IsBoss`, attach/detach, both resistances).
- `Assets/Scripts/EndlessWaveDirector.cs` — `EnemySpec.Boss` flag, `spec.Boss = slot == Slot.Miniboss`, and
  `PulsebreakerBoss.Attach(npc, Wave)` at the single spawn site; the wave banner names the boss instead of the word "MINIBOSS".
- `Assets/Scripts/GameHud.cs` — `BuildBossBar()` / `TickBossBar()` in the existing build and update passes.
- `Assets/Scripts/Hud/GameHudEnemyHealth.cs` — a boss never gets a small floating bar (it owns the big one).
- `Assets/Scripts/HumanoidPresentation.cs` — `BossAttack/BossAttackRelease/BossAttackCancel` + a windup override so the clip's
  impact marker lands on the boss release instead of the archetype `WindupSeconds`.
- `Assets/Resources/ModeDirectors/EndlessWaveEvents.asset` — `MinibossArchetype` now points at `Enemies/Pulsebreaker`.

## 2. Files

```
Assets/Scripts/Enemies/PulsebreakerBoss.cs               the brain: states, attacks, phases, bolt pool, damage cap
Assets/Scripts/Enemies/PulsebreakerTuning.cs             every number (Resources/Enemies/PulsebreakerTuning)
Assets/Scripts/Enemies/PulsebreakerVfx.cs                reactor, gauntlets, one shared seismic ring mesh
Assets/Scripts/Enemies/CityNpc.Boss.cs                   inert-by-default CityNpc hook
Assets/Scripts/Enemies/PulsebreakerVerificationRunner.cs live suite
Assets/Scripts/Enemies/PulsebreakerReloadRunner.cs       separate-process save/asset reload
Assets/Scripts/Hud/GameHudBoss.cs                        boss bar (reads PulsebreakerBoss.Active, no new events)
Assets/Resources/Enemies/Pulsebreaker.asset              archetype: Slam, health x1, damage x1, accent Cyan, scale 1.25
Assets/Resources/Enemies/PulsebreakerTuning.asset        tuning asset
Assets/Editor/PulsebreakerVerification.cs                Run + Reload entry points
docs/pulsebreaker.md                                     this document
```

## 3. State machine

`Idle → Approach → (Windup → Execution → Recovery) → Approach | Reposition`, plus `Staggered`, `PhaseTransition`, `Dead`.
One decision per frame, no behaviour tree, no planner.

- **Idle**: planted, faces the player, `ArrivalSeconds` (1.2 s) of "arrival" after attaching.
- **Approach**: holds a 3.5–7 m band (closes when farther, backs off when closer, slow sideways drift in band), asks for an
  attack when `Time.time >= nextAttackAt` and it can acquire a token.
- **Windup**: agent stopped, rotation locked once, telegraph shown, head/gesture clip fitted to the windup.
- **Execution**: slam ring travels, cannon bolt flies, rush moves.
- **Recovery**: the vulnerable window. Token released here; phase 2 can chain a follow-up or reposition.
- **Staggered**: any control effect; the attack is aborted, no token is leaked.
- **Dead**: telegraphs hidden, ring cleared, `Active` cleared, existing death clip.

Attack choice is distance-gated (`Slam ≤ 4.6 m`, `Cannon 5–26 m with line of sight`, `Rush 4–16 m with a validated lane`),
never repeats the same attack twice in a row while another is legal, and falls back to the slam (which ignores cover) so a
cramped corner can never silence the boss.

## 4. Attacks

| | Shockwave Slam | Pulse Cannon | Overdrive Rush |
|---|---|---|---|
| Windup | **1.00 s** | **1.35 s** | **0.80 s** |
| Telegraph | 3 m core disc at the impact point + the travelling ring | lane bar, 0.55 m wide, along the locked aim | lane bar along the locked charge line |
| Execution | ring expands 0 → **8 m** in **0.45 s** | bolt 24 m/s, 30 m max, 0.45 m radius | 26 m/s, hard cap 0.45 s of travel |
| Recovery | **1.20 s** | **1.00 s** | **1.50 s** miss / 0.60 s hit |
| Damage | ×1.0 base | ×1.25 base | ×0.9 base |
| Counterplay | step out of 8 m, or be > 1.3 m above the impact plane (jump/hover) | move out of the locked lane; cover blocks the bolt | sidestep the telegraph; walls/steps stop the charge |

Rules that make them fair:

- The ring damages each player at most once per slam (`slamHit`), and only while the band crosses them — no "everything in
  an 8 m circle instantly" hit.
- The ring's radius is read from the visible VFX (`PulsebreakerVfx.RingRadiusNow`), so the danger and the visual are the
  same object.
- The cannon's direction is locked at commit (`committedDirection`); it never tracks after the telegraph.
- The bolt pool is 3 reusable cubes with a maximum range; nothing is instantiated per shot.
- The rush validates its lane with a capsule sweep **and** `NavMesh.SamplePosition` + `NavMesh.Raycast` before it is even a
  legal choice, shortens the lane if blocked, re-checks every step, and stops on contact (`RushBlocked`).
- All three route player damage through one `HitPlayer(...)` that applies
  `min(base × multiplier, MaxHealth × DamageCapFraction = 25%)`.

## 5. Phases

Phase 2 begins the first frame `HealthFraction <= 0.40` (fraction, not absolute HP, because spawn HP varies ±10%).
It is idempotent, happens once, and death always wins: `Tick` checks `Npc.Dead` before the threshold.

During `PhaseTransition` (0.6 s) the boss is planted and **damageable** — no invulnerability, no healing, no adds, no reset.
Afterwards: attack gaps ×0.6, recovery ×0.7, move ×1.15, and a 40% chance to chain a second attack with a 0.35 s gap.
Telegraph durations are never shortened. Presentation: reactor/gauntlets cyan → fire, an expanding ring, `TAUNT` clip, HUD
`PHASE 2`, banner `PULSEBREAKER — OVERDRIVE`.

## 6. Numbers against the real game values

Round 5, Normal, Endless: `HealthFor(5) = 104`, slot `×8`, `Brute`-class archetype multipliers removed (`HealthMultiplier = 1`)
→ **832 HP mean**, ±10% spawn roll (observed 770–906 in verification). Base hit `DamageFor(5) × 1.6 = 17.9` → slam 17.9,
cannon 22.4, rush 16.1 on a 100 HP VECTOR bar, capped at 25.

That is ~18 s of reference-DPS (45) pressure on a body that also has up to 8 adds, versus the old Brute-slot miniboss's
2,496 HP / 55 s and 40%-of-a-bar slam. Rounds 10/15/20 scale automatically through `HealthFor`/`DamageFor`; the cap keeps
late waves from becoming instant kills. Nothing double-applies difficulty: the director's `spec.Health` already includes it
and the boss multiplies nothing.

## 7. Spawning

Only the Endless director spawns bosses. `hero`, `villain` and `free-play` have `Director: {fileID: 0}`, so they cannot spawn
one at all; BATTLE LAB is a practice session with no director. `EndlessWaveEvents.MinibossEvery = 5` gives round 5, 10, 15…
and `MinibossAliveCost = 4` leaves at most 8 adds beside it. One live boss at a time (`Attach` refuses a second and counts
`AttachRefusals`); re-attaching the same body is idempotent. The miniboss kill flows through `CityNpc.Damage → OnDefeat →
EndlessWaveState.Defeated`, which is the existing round-completion path — `MinibossKills` is recorded once and the round
advances. Free Play was verified to contain no boss at any frame and to never show the bar.

## 8. HUD

`GameHudBoss.ShowBossBar/TickBossBar` build one bar in the existing HUD root: name, `PHASE 1/2`, a real health fraction read
from `PulsebreakerBoss.Active.Npc.HealthFraction` every frame, and hide on death/despawn. It allocates nothing per frame and
adds no event; the small floating enemy bar is suppressed for the boss so there is exactly one health readout.

## 9. Presentation and assets

- Reactor: one sphere on the visual root at chest height, `CityMaterials.GetVfx(Cyan → Fire)`, breathing scale.
- Gauntlets: two cubes parented to the hand bones (never the physics root), `CityMaterials.Get(Metal → Fire)`.
- Seismic ring: a single process-wide annulus mesh (48 segments, both windings), one shared Fire VFX material.
- Arrival/phase/death banners ride the existing `World.Message` feed; camera punctuation uses `FeelDirector.Impact/PlayerHit`.
- Clips are existing shared ones (`Hurricane Kick` slam, `Shoot` cannon, `Punch` rush, `Taunt` phase, existing death).
- **Missing art (honest):** there is no bespoke armored boss model, no reactor/gauntlet VFX art, no custom animation. The
  silhouette is a scaled (1.25) mannequin plus primitives. A real artist pass is the biggest remaining presentation gap.

## 10. Performance notes

No per-frame `Instantiate`/`Destroy`, no per-frame material creation, one shared ring mesh for the whole process, a 3-object
bolt pool, one `SphereCastNonAlloc` per live bolt, one `CapsuleCastNonAlloc` per rush step, fixed 12-hit buffers, and
telegraphs/VFX are pooled per boss. The boss is a single object and is exempt from `NpcLod`'s reduced think rate (its
telegraph timings must be exact). No measured FPS claim is made — see STATUS for what was and was not measured.

## 11. Verification

`PulsebreakerVerification.Run` + `PulsebreakerVerification.Reload`, in fresh processes:

```
python3 tools/verify-sequential.py --out Verification/Pulsebreaker/final \
    PulsebreakerVerification.Run PulsebreakerVerification.Reload
```

Covers data/tuning, mode gating, Free Play (no boss, no bar), rounds 1–6 of a 1-per-wave Endless clone (no boss before 5,
exactly one at 5, kill → round advances, none at 6), real HP vs `HealthFor(5) × 8 × difficulty`, the HUD bar vs the
authoritative fraction, slam (telegraph, measured 1.00 s windup, ring, grounded hit, distance dodge, jump dodge, token
release), cannon (telegraph, one bolt from a bounded pool, hit, lane dodge, wall block), rush (telegraph, 10.2 m travel,
dodged → miss recovery, wall stop, never stuck), phase two (once at 38–40%, token released, bar switches, never repeats),
death in phase two and *during* the phase transition, plus regression controls (ordinary enemies keep freeze resistance 1,
a Rusher still runs its own cycle, a 2 s freeze on the boss becomes 1 s, freezing mid-windup cancels and hides the
telegraph, no token leak).

**Automated limitations:** batch Play Mode can assert state, timing, damage and layout. It cannot judge feel, readability at
combat speed, physical input, day/night contrast or sustained standalone frame rate. The human checklist below is required
before calling the fight good.

## 12. Human playtest checklist

1. **Arrival** — Endless with a clone/console: the banner reads `PULSEBREAKER — ELITE THREAT`, the bar appears with full HP.
2. **Shockwave Slam** — stand still: you are hit and knocked back. Jump as the gauntlets come down: you clear it. Back off
   past 8 m: nothing hits. Rate the ring's readability at 720p and 1080p.
3. **Pulse Cannon** — strafe out of the lane: the bolt misses. Stand still: it connects. Put a building in the way: it stops.
4. **Overdrive Rush** — sidestep the lane: the charge misses and you get a 1.5 s punish window. Stand in the lane: you are
   hit. Watch it near a corner: it must never clip through geometry.
5. **Phase two** — at ~40% HP the reactor turns orange, the banner changes, the bar says `PHASE 2`. Confirm the fight gets
   faster, not unfair: no one-shot, no immunity, no healing, at most one extra chained attack.
6. **Damage feel** — the boss must die to powers, not to waiting: time a full VECTOR kit kill; flag if it exceeds ~45 s at
   round 5 on Normal.
7. **Control powers** — Ice/Darkness should slow and briefly lock it (halved), never permanently. Telekinesis should move it.
8. **Round flow** — kill it: the round ends when the adds are cleared, score/XP land, the bar disappears, round 6 has no boss.
9. **Free Play** — load Free Play, roam, fight Heat police: no boss, no boss bar, ever.
10. **Other modes** — Hero and Villain campaigns must be untouched; BATTLE LAB must be unchanged.
