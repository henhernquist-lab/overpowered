# Prototype Status

## Phase 4: game feel + HUD-pass final verification — 2026-09-25 (current)

**Every feel value lives in ONE place:** the new `FeelSettings Feel` section of `GameTuning.asset`, beside the existing
`Camera` section, which stays the only source for offset, look height and FOV.
Values used: heavy = outgoing impulse ≥ 1000 N·s (punch 1350, kick 2025, synergies 1500–3400; basic melee 160 and
Fire Blast 450 are light) or incoming damage ≥ 15 (Brute 27 heavy, Rusher 9 light). Hit pause 0.06 s, minimum
interval 0.4 s. Camera impulse 0.16 m / 0.22 s / 18 Hz (incoming ×0.8). FOV kick 2.5° / 0.2 s. Hard landing
≥ 14 m/s (×0.7). Particles: pool of 8, counts 6 / 14 / 12 / 8 (light / heavy / break / landing), size 0.13,
life 0.55 s. **Camera: offset (0, 2.6, −6.5) → (0, 2.8, −7.6), look height 1.05 → 2.5**; pitch and FOV unchanged.
- `Feel/TimeArbiter`: the ONLY writer of hit-pause time. The pause menu always wins and chained freezes are
  rate-limited.
- `Feel/FeelDirector`: decides "heavy" from data; hooked with one additive line each in `CombatImpact` and the
  CityNpc hit on the player, plus the existing `Landed` / `Destroyed` events.
- Camera kick: applied only for the render and undone after, so the aim ray and physics root never see it.
- `Feel/ImpactParticlePool`: 8 pooled systems, the shared cube mesh and shared palette materials, `Emit()` only. The
  existing prop shards are untouched.

**Two real bugs found and fixed:**
1. A hit pause produced zero-deltaTime frames that made the CharacterController report "not grounded", so backflip
   and jump were refused inside a Brute's hit pause (Combat caught this). Zero-length moves are now skipped
   (`SuperHeroController` ×2, CityNpc knockback ×1).
2. **Aim (lead fix, `PowerUser.AimDirection`):** the crosshair raycast was capped at the power's Range measured from
   the CAMERA. With the camera 7.6 m behind, Fire Blast missed beyond ~13 m. Anything between the camera and the hero
   also counted as the aim target. Range is now measured from the hero, and hits nearer than the hero are ignored.
   Miss distance, projectile contact vs the crosshair hit:

   | Target | Before | After |
   |---|---|---|
   | 15 m | 0.185 m | 0.01 m |
   | 22 m | 1.02 m | 0.10 m |
   | 30 m | 1.99 m | 0.71 m |

   The 30 m target is beyond Fire Blast's 20 m range, so its aim converges at the end of range. A car or deliberate
   obstacle in the line of fire takes the hit (correct). Ice and Telekinesis select the crosshair target, with a
   CONTROL crate. `Verification/Feel/results-after-aim-fix.txt`.

**Verification — FeelVerification 142 PASS:**
- Hit pause measured at 60.3 ms against 60 configured, with 0 physics steps during it and resumed after.
- CONTROLS: a light hit gives no pause; the pause menu opened mid-hit-pause stays paused; 5 heavy hits in 175 ms give
  1 pause.
- HUD tweens advance at timeScale 0.
- Physics: 4 pause/no-pause pairs give identical velocity (20.386 m/s, difference 0.0000).
- Audio: the impact cue plays exactly once and keeps advancing through the pause.
- Camera: kick + FOV return to rest within 220 ms; CONTROL: a soft landing gives none.
- Particles: pool constant; Material count 78 → 78 (none created).
- Particle cost: −4.4% (+0.16 ms), interleaved on/off.

**Task 0 — the Phase 2 flake has a real, observed root cause** (2 failures in 12 traced runs). The test teleported the
hero into a supply crate. When a civilian ALSO overlapped the spot, the controller pushed the hero out to 3.12 m,
beyond the 3 m interact radius, so no prompt could show. It is a test race, not a game bug. The test now picks a free
standing spot; 10/10 consecutive Run + Reload pairs pass.
**Transient feedback vs centre-clear (lead ruling):** "centre clear" governs the PERSISTENT HUD. +XP popups land at
the defeat, usually in front of the crosshair, as requested, but are pushed off the crosshair itself. Both HUD suites
assert zero popup/crosshair overlap (0 in 3,756 + 322 samples).

**FPS, before this HUD pass (0ce58f1) vs after,** same clone and scenario, alternating: **76.98 → 70.66 FPS (−8.2%,
+1.16 ms)**. That is an upper bound (the old IMGUI HUD never draws in batch mode), and run-to-run noise is ~5 FPS.

**Final regression, all exit 0:** HUD P1 318, P2 485 + Reload 31, P3 562 + Reload 11, Feel 142, Mode 79 + 2,
ModeExpansion 111 + 10, HeroForge 132 + 3, Combat 170, Audio 126, City 53 + 5, Humanoid 52, BackflipHurricane 63,
CityArt 31, Menus 42. After the aim fix, re-run: Feel, Humanoid, City + Reload, Audio, HeroForge + Reload and Combat
all exit 0.

**NOT CLAIMED — needs a human playtest:** whether any of this FEELS good (hit-pause length, kick strength, particle
density, camera distance), readability at speed, and the alert → travel → fight → reward rhythm. Batch mode has no
keyboard/mouse; the input paths were exercised through the same methods the handlers call.

## HUD Phase 3: connect the dots — 2026-09-25 (previous; its flake is resolved above)

Every meaningful action now visibly feeds a system (`Hud/GameHudFeedback.cs`; gameplay files only gained additive
events and an overload).
- **Honest "+XP" popups:** they float from the event's world position. The new `AddXp(amount, where, reason)`
  overload and `XpGranted` / `LevelUp` events are additive; `AddXp(int)` and `XpAwarded` are unchanged. The overload is
  used for defeats, destruction, crimes and objective success. Grants within 0.3 s and 4 m merge into one popup, from
  a pool of 16; XP with no position floats beside the level badge.
- **Heat feedback:** `WorldSession.HeatAdded` (only from `AddHeat`, with the delta actually applied) flashes the stars
  and shows +/−; decay only flashes when a whole star is lost.
- **Mid-session level-up:** a badge burst plus a "LEVEL UP — TAB TO UPGRADE" banner. It never pauses or touches
  timeScale.
- **Banners:** objective complete (side colour, name, +XP), objective failed (muted) and Endless wave cleared. They
  are queued one at a time, and alerts wait for them.
- **Waypoint enlarged:** all sizes are named constants in `GameHudGuidance.cs`. At 720p the distance label is 16.8 px
  (was 11.2) and the arrow 52 px (was 34).

**Verification — HudPhase3 530 PASS + separate-process Reload 10, exit 0** (clone):
- HONESTY: SUM(popups) == shown total == real XP gained (Hero 410, Villain 145, Endless 280)
- MERGE: 3 defeats in 0.026 s → one "+105 XP"; CONTROLS: 1 s apart → separate, 12 m apart → separate; a 5-kill
  Endless burst → one "+175 XP"
- timeScale was 1 on every sampled frame of the level-up, and NPCs kept moving
- the objective banner shows THEN the alert, with 0 frames overlapping
- IDLE CONTROL (30 s): zero popups, banners and alerts
- Heat: 1,258 frames of decay without a star change → 0 flashes, and the one star drop flashed exactly once. (The
  brief's "zero flashes in 30 s" was physically impossible, because decay must cross a star in that window.)
- Reload CONTROL: loading a levelled save replays no burst or popup

Rescues grant 0 XP today, so they show no popup, by the honesty rule.
Regressions exit 0: HUD P1 318, Mode 79 + 2, ModeExpansion 111 + 10, HeroForge 132 + 3, Combat 170, Audio 126,
City 53 + 5. Evidence: `Verification/Hud/phase3/`.

**KNOWN FLAKE, from Phase 2's committed code (3bd73dd), not this pass:** HudPhase2 fails 1 run in 3–4 at "Next to
an interactable objective → prompt text ''". It reproduces on 3bd73dd without any Phase 3 code. The root cause is
not yet known: it may be a test race or a real intermittent missing interact prompt. Being diagnosed.
**Cosmetic:** the popping heat star overlaps the "HEAT" caption; the waypoint count label is still ~12 px at 720p.

## HUD Phase 2: make the goal obvious — 2026-09-25 (previous)

Built on Phase 1. Modal and world-anchored guidance lives on a second OVERLAY panel (`Hud/GameHudGuidance.cs`), so the
Phase 1 centre-clear pixel check keeps its meaning for the persistent HUD.
- **Mission briefing:** a side-coloured card with GOAL / WIN / LOSE. The text is DATA: new `GameModeDefinition`
  briefing fields, filled for all five mode assets. Free Play reads "No rules. Go wild." It dismisses on input or
  after 7 s of real, unpaused time.
- **Objective line:** "STOP THE ROBBERS 0/3" + "+N MORE TASKS" + time left. Task labels moved from hardcoded C# in
  Hero/VillainModeRules into a `Tasks` list on the ModeRules assets. `Objective()` is built from the same data.
- **Waypoint:** tracks the nearest unresolved target of the current task, re-picked every frame, with an edge arrow
  when off-screen (the bearing is correct behind the camera). It is pushed out of a 72 px crosshair radius, with a
  count of the remaining targets. The Villain escape target is the nearest point on the escape circle.
- **Alerts:** an additive `WorldSession.EncounterSpawned` event drives a card that slides in with name, direction
  arrow and distance. The objective line then updates.
- **First-time prompts:** move/fly, punch, backflip-dodge and hold-R interact. Each hooks the real action; seen state
  is an additive `SeenHints` list in the existing save, null-guarded.

**Verification — HudPhase2 460 PASS + separate-process Reload 28, all exit 0** (clone; the user's Editor held the tree):
- briefing text == asset data, in the side colour
- CONTROLS: empty briefing → no card; dismissal by timeout
- objective == rules label + progress; CONTROL: editing the label on a CLONED rules asset changes the line
- edge-arrow angle equals the independently computed bearing (−139.2° / 86.8°)
- stopping the targeted robber retargets to the next-nearest in ONE frame, and the count goes 3 → 2
- CONTROL: 30 s idle → 0 alerts; the alert arrow's bearing asserted
- prompts: once performed, they don't reappear in a SEPARATE process; CONTROL: a fresh save shows them again; the
  legacy save loads
- 3-resolution layout re-check clean

Regressions all exit 0: HUD P1 318 (was 319: an EMPTY bottom stack is now hidden rather than drawn as an invisible box,
and the P1 runner itself is unchanged), Mode 79 + 2, ModeExpansion 111 + 10, HeroForge 132 + 3, Combat 170, Menus 42,
Audio 126. Evidence: `Verification/Hud/phase2/`.

**For review/playtest:**
- The waypoint marker and edge arrow are SMALL (34 px arrow, ~10 px label at 720p) and hard to read over busy scenes.
- For ~4 s during an alert, the objective line and waypoint still point at the OLD encounter even when the new one is
  nearer. This follows "alert, then objective updates", but may confuse players.
- Encounters spawned at session start raise no alert (the briefing covers them).
- Combat keeps running under the briefing; it doesn't pause.
- The mode Description message is suppressed when a briefing exists.

## HUD Phase 1: real in-game HUD — 2026-09-24 (previous)

The IMGUI developer panel (~40% of the screen, clipped at fullscreen) is replaced by `GameHud` (UI Toolkit, the menus'
visual language: palette colours, MenuGlyph icons, rounded 0.9-alpha panels). Edges only: level + XP top-left; Heat
stars (pop on change) + remaining-time timer top-right; Endless wave/left/score/best top-centre, or the objective slot;
health (with damage trail) + energy bottom-left; bottom-centre, a power bar BUILT FROM THE HERO FORGE LOADOUT
(EquippedA/B + synergy), with charge pips, fuel bar, radial cooldown and data-driven key hints (synergy key read from
ForgeCatalog). Only the crosshair sits in the centre 40%×40%, at exact screen centre, because aiming uses the viewport
centre. All tweens use unscaled time. The old text boxes are behind **F3** (unbound; off by default); Esc pause, the
Tab menu and the defeated notice are unchanged (still IMGUI). Scaling uses Expand around 1600×900, so no aspect clips.
Visibility is driven only by existing mode data (Hud flags, SessionSeconds, Director); no new flag was added.

**Verification — HudVerification 319 PASS / 0 FAIL** (run in the regression clone; the user's Editor held the tree):
- composited world+HUD captures of all 4 modes at 1280×720, 1920×1080 and 2560×1080
- asserted: no element outside the safe margin, no truncated label, no group overlap, crosshair 0 px from centre
- centre zone pixel-identical to a camera-only render
- mode visibility, with CONTROLS (flags cleared on a CLONED definition hide exactly their elements)
- F3 hidden by default with its draw path never run, and the toggle CONTROL
- loadout swap: default `flight|strength|sonic-slam`, then Fire+Ice → `fire|ice|thermal-shock`, plus a melee hint
- pips and radials driven by real punches, casts and synergy use
- Heat star pop while timeScale=0

FPS at the gameplay camera: 59.1 → 58.3 (−1.5%; the HUD update itself costs ~0.03 ms). That is an UPPER bound, since
batch mode never draws IMGUI, so the old panel's cost is missing from the A side.
Regressions all exit 0: MenuPresentation 42, ModeExpansion 111 + Reload 10, HeroForge 132 + Reload 3, Combat 170,
Audio 126. Evidence: `Verification/Hud/`.

**Known weak spots (visual, for review):**
- 9px captions at 1280×720; the Endless labels and objective meta line are cramped.
- Every power slot is orange because all power assets use the Fire palette colour. That's a data issue that also
  colours projectiles, so it was not changed silently.
- The crosshair sits on the hero's back (camera framing; Phase 4).
- Raw objective text wraps awkwardly (Phase 2 replaces it).
- The popping star briefly overlaps its neighbours.
- In-world encounter labels moved behind F3 until Phase 2's waypoints.
- F/E key hints are mirrored in `HudBindings`, because `SuperHeroController` hardcodes those keys.

## Hero Forge merged onto main — 2026-09-24 (previous)

`codex/hero-forge` (9b299ac: loadouts of two equipped powers + ten synergies, synergy key **C**) was built on cc1a3b7,
before the FPS fix, Free Play/Endless and combat. The lead merged it. Its own entry follows this one.

**Conflicts, resolved additively:** `PlayerProgression` keeps BOTH `ModeRecords` and `Loadout`. Old saves still load:
the ModeRecords null-guard and `ValidateLoadout()` both run on load. `HumanoidPresentation`: the player uses loadout
colours; NPCs use a role body colour + archetype joint accent. The two scale paths are disjoint (hero-definition model
scale for the player, archetype visual-root scale for NPCs). No STATUS lines were lost.

**Merge interactions found by the full regression, and how each was fixed:**
- **REAL BUG — player footsteps were silent in every session.** Hero Forge creates the hero's presentation AFTER
  `WorldSession.Initialize`. The audio director's spawn-time `Bind()` (added in Step 4) then cached a null player and
  never looked it up again. `AudioDirector.Bind()` now re-resolves a missing player on every bind. Proven: 3/3 audio
  runs pass the footstep check and its stationary control.
- **Stale tests, not bugs:** Hero Forge refuses unequipped powers (a fresh save equips Flight + Strength), and several
  suites fired Fire/Ice/Telekinesis/the test "seventh" power without equipping them. Humanoid, City and Audio now equip
  through the real `PlayerProgression.SetLoadout`, and each gained a CONTROL that the same power is refused while
  unequipped: nothing charged, spawned or heard. The City test hero is registered in the loaded catalog **in memory
  only** (`CreateInstance`, never dirty or saved, restored in `finally`). No shipping asset is modified, even during
  a run.
- Hero Forge's thermal-synergy test measured damage as health lost on what is now a 39-HP Rusher. The target died, so
  the hit was capped. The target now gets explicit headroom; the exact-value assertion is unchanged (42.5 vs clean 25).
- A rare Humanoid failure (1 in 5) came from Step 4: a Gunner that starts to aim stops dead, and the "moving cop" gait
  check could sample it mid-windup. The test now samples a genuinely moving cop outside a windup (threshold unchanged)
  and passes 5/5.

**Final sweep, all 14 suites exit 0:** Audio 126, Mode 79 + Reload 2, ModeExpansion 111 + Reload 10, Humanoid 52,
City 53 + Reload 5, Abilities 63, CityArt 31, Menus 42, Combat 170, HeroForge 132 + Reload 3. dotnet 0/0.
`Verification/Forge/merge-sweep-summary.txt`.
Audio note: while a Unity Editor is open on the project, the audio suite's DSP check is flaky (4 of 7 failed in one
batch). The pre-merge commit 8854e95 fails the same way under the same conditions, so it is environmental.
Also cleared iCloud duplicate refs (`.git/refs/remotes/origin/HEAD 2`/`3`) that were breaking `git fetch`. They were
moved to the scratchpad, not deleted.

## Combat depth: telegraphed attacks, enemy archetypes, crowd rhythm — 2026-09-23 (previous)

Before this pass, a hostile NPC in range damaged the player THE SAME FRAME: instant, unavoidable contact damage
("punch until dead"). That is replaced, for every hostile NPC in every mode, by a telegraphed attack.

### Design
- **Three archetypes as data** (`Resources/Enemies/{Rusher,Gunner,Brute}.asset`). **Role decides hostility and
  colour; the archetype decides behaviour.** `EnemyRoster.asset` maps Criminal → Rusher, Cop → Gunner,
  PursuingHero → Brute, and holds the crowd tuning.
  - **Rusher:** 7 m/s, 0.6× health, 0.75× damage; short melee, 0.45 s windup, 0.8 s cooldown; visual 0.9×, amber accent.
  - **Gunner:** 5.5 m/s; holds 10 ± 2 m; 0.7 s windup; **aim locked at windup start**; 22 m line check with line of
    sight (buildings, props and cars block shots); cyan accent.
  - **Brute:** 3 m/s, 3× health, 2.25× damage; ground slam locked 1.8 m ahead, radius 3.2 m, 0.9 s windup, 2.5 m
    knockback; visual 1.3×, purple accent.
- **Attack cycle** (`CityNpc`): Approach → Engage → Windup (stopped, shape locked, telegraph shown) → Release → Recover.
  At release the COMMITTED shape is re-checked against the player's CURRENT position. Damage lands only if the player
  is still inside it, and `Attacked` fires either way. Death, freeze (Ice), pause, session end, disable and side switch
  all cancel a windup, so **a killed or frozen enemy never lands its hit**.
- **Attack-token budget:** at most 2 enemies may be winding up at once, and the rest circle on a 4.5 m ring,
  spread by angle. The pool drops dead or destroyed NPCs before every grant, so a dead enemy can't deadlock the crowd.
- **Telegraphs:** one pooled renderer per NPC, shared Fire palette material, no collider or shadow — a growing ground
  disc for melee and slam, a line for shots. The attack clip starts at windup with playback FITTED so its impact frame
  lands on the release frame (0 frames of offset, measured for all three).
- **Backflip deliberately has NO i-frames; dodging is geometric.** A backflip carries you out of melee reach and slam
  radius, but NOT off a Gunner's locked aim line — backing straight away keeps you on it. Strafing beats Gunners.
- **Endless** mixes archetypes via a composition table on `EndlessWaves.asset` (rushers from wave 1, gunners from 2,
  brutes from 3). Each enemy's stats are the wave value × its archetype multiplier, still Heat-independent.

### Verification — `CombatVerification.Run` 170 PASS / 0 FAIL, identical on two back-to-back runs
Per archetype, a real enemy against the real player, measured rather than read from config:
- **Distinct behaviour:** approach speeds 6.99 / 5.50 / 3.00 m/s (Rusher / Gunner / Brute). The Gunner held
  9.77–12.00 m for 6 s and never closed to melee. Spawned health 39 : 65 : 195. Measured windups 450.3 / 701.0 /
  900.1 ms against 450 / 700 / 900 configured.
- **Hit if you stand still, miss if you move:** standing inside each attack takes EXACTLY its damage at release
  (6 / 8 / 18), and zero damage on every windup frame before it. Backflip at windup start → no damage from the Rusher
  (1.22 m clear) or the Brute (1.62 m clear). A 3.6 m strafe → no damage from the Gunner (2.66 m clear).
- **CONTROLS:**
  - **Asymmetry:** backflipping straight away from a Gunner is STILL hit (−0.98 m).
  - **Late:** a backflip after release cannot undo the hit.
  - **Death:** killing an enemy at 50% of its windup → no damage, telegraph hidden, token returned.
  - **Budget:** with budget 2, the observed max was 2 simultaneous windups over 10 s (19 releases by 9 enemies), and
    waiting enemies stayed at a mean 4.57 m against the 4.5 m ring. Raising the budget to 99 → up to 7 simultaneous.
  - **Leaked token:** another enemy receives the token within milliseconds of a holder's death.
  - Engage timeout, freeze, pause and session end all return the token.
- Hero and Villain sessions map roles to the roster. The cop gunshot sounds AT the releasing cop on its release
  frame. Endless wave 5 composition is 5 Rusher / 4 Gunner / 2 Brute.
- Combat adds no measurable per-frame managed allocation (crowd active vs frozen: −18.5 B/frame).
- **Wave-5 FPS at the gameplay camera, grounded hero in the real fight:** 99.8–142.1 across four runs.

### Integration fixes by the lead
- The NPC attack SOUND now follows the attack kind (Ranged → gunshot, otherwise punch). Before, it followed role, so
  a Criminal Gunner "shot" with a punch sound.
- **Newly spawned enemies were silent for up to 1 s.** `AudioDirector` discovered NPCs by polling every
  `ActorRefreshInterval`, and Endless Gunners can fire within a second of spawning. `CityNpc.Spawned` (additive
  static event) now lets audio register an NPC the moment it spawns. A new AudioVerification assertion proves an NPC
  is tracked in the SAME call stack as its `Spawn`, where the periodic refresh cannot have run.
- Three existing runners encoded pre-combat assumptions; each was corrected without weakening what it proves:
  - **ModeVerification:** the police stand-off height rises 15 → 30 m. 15 m was chosen for 2 m contact damage, and
    Gunners reach 22 m.
  - **AudioVerification:** the cop-gunshot check now waits for THAT cop's release and requires the shot there, a hit,
    and a health drop. It previously accepted any gunshot, which a different cop's miss could satisfy — strictly
    stronger.
  - **ModeExpansionVerification:** per-enemy stats = wave value × archetype multiplier (the new truth). A logged
    test-only health restore keeps one-life Endless alive through its FPS sample.

### Final regression sweep — every suite, on the finished code (`Verification/Combat/regression/final-sweep-summary.txt`)
Run after all of the lead's fixes, sequentially, in an APFS clone byte-verified against the working tree (30 changed
or new files, 0 mismatches). CombatVerification ran in the real tree. Mac held awake with `caffeinate`.

| Suite | Exit | PASS | Suite | Exit | PASS |
|---|---|---|---|---|---|
| AudioVerification | 0 | 111 | AudioVerification.Reload (2nd process) | 0 | 49 |
| ModeVerification | 0 | 79 | ModeVerification.Reload (2nd process) | 0 | 2 |
| ModeExpansionVerification | 0 | 111 | ModeExpansionVerification.Reload (2nd process) | 0 | 10 |
| HumanoidVerification | 0 | 46 | CityVerification | 0 | 39 |
| CityVerification.Reload (2nd process) | 0 | 5 | BackflipHurricaneVerification | 0 | 63 |
| CityArtVerification | 0 | 31 | MenuPresentationVerification | 0 | 42 |
| **CombatVerification** | 0 | **170** | | | |

13 of 13 runs exit 0, with zero FAIL lines. dotnet build: 0 errors, 0 warnings.

**AudioVerification now passes IN FULL, resolving the open item in the Free Play/Endless entry below.** The Mac's
CoreAudio output recovered, and `Music DSP sample cursor advances` passes again. On the idle machine the audio
system costs **0.59 FPS** (57.63 → 57.04). That settles the earlier 6.6–9.1 FPS reading as contention noise from the
runaway editors.

### For a human playtest
- **Readability is partial.** The Brute is clearly bigger, but the Rusher and Gunner read as nearly the same
  silhouette at mid-range; only their small amber/cyan accents differ. A bolder differentiator (headgear, weapon,
  colour band) needs art beyond the palette.
- **The pursuing superhero is now a Brute:** 3 m/s instead of 8 m/s. Thematically a fast chaser became a slow tank;
  retune in `Brute.asset`, or give PursuingHero its own archetype, if that's wrong.
- **Existing-mode balance changed:** Hero-mode robbers (Rushers) have 39 HP instead of 65, and Villain-mode cops now
  shoot from 10 m instead of touching at 2 m. Whether Villain mode is now harder or easier needs hands-on play.
- **Reaction windows are unproven against human reflexes:** 0.45 s for Rushers is short. All windups, reaches and
  the token budget are Inspector tunables.
- No audio telegraph plays at windup start; there is no clip for it. It's a cheap readability win once one is sourced.
- A Gunner's line of sight is cached for 0.35 s. It can commit on a stale view, but it re-checks at release.

## Free Play and Endless Fight — 2026-09-22 (previous; its AudioVerification gap is resolved above)

Both former "Coming Soon" placeholders are real modes, built by EXTENDING the data-driven architecture. There are
no mode-ID switches in gameplay code (checked mechanically); every difference is data on the mode assets.

### Architecture additions (defaults preserve every existing mode exactly)
- `GameModeDefinition`: `SideFromProfile`, `AllowSideSwitch`, `ShowResults` (default true),
  `Results` (Objectives | Survival) and `Director`.
- `ModeDirector` is a strategy asset, following the `ModeRules` pattern. It holds tuning ONLY. `Begin()` adds a runtime
  `ModeDirectorState` component that owns all run state, so nothing leaks between Editor play sessions. It is ticked
  only through `GameModeSession.Tick`, so pause/end handling stays in one place.
- `GameFlow.Select` requires Rules + Encounters only when a mode actually spawns encounters.
- `WorldSession.RequestSideSwitch()` is the H-key action; it is blocked unless the definition sets `AllowSideSwitch`.
- `PlayerProgression`: `ModeRecords` hold per-mode BestScore/BestWave/Runs, additively. `BestSessionScore` keeps
  its global meaning. Old saves load with an empty list.
- `CityNpc` (additive): `MaxHealth`, `AlwaysAggro`, `SetCombatStats`, `ContactDamage`. Without explicit stats,
  `ContactDamage` returns exactly the old Heat formula.

### Free Play (`free-play.asset`)
No objectives, no timer, no defeat limit, no encounters. **Heat stays active (lead's call):** Heat/police is the
city's only reactive system, and without it Free Play is a dead diorama. It gives consequence without failure: you
always respawn and the session never ends by itself. It uses the saved profile side; H / the Tab menu switch sides
in-session. The pause menu offers only Resume and Return home.

### Endless Fight (`endless-fight.asset` Hero → Criminals; `endless-fight-villain.asset` Villain → Cops)
Both share ONE tuning asset, `Resources/ModeDirectors/EndlessWaves.asset`, which holds every escalation constant:
- 3 enemies + 2 per wave
- `MaxAlive` 12 (the rest arrive as reinforcements)
- health 65 × (1 + 0.15·(wave−1)), damage 8 × (1 + 0.10·(wave−1)), set explicitly per enemy — **Heat stars never
  scale them**
- 4 s intermission; spawn ring 22 m, ≥ 12 m from the player
- score = 10·wave per kill + 50·wave per cleared wave

The arena is the intersection nearest the city centre; all four are equidistant, so the one nearest the spawn wins,
at (−20, 0, −20). No civilians and no police. One life: death ends the run and shows the Survival results screen
(WAVE / ENEMIES DEFEATED / SCORE / BEST, "NEW BEST" when earned). Best scores persist per variant. `Compose()` is
the marked extension point for enemy types.

### Verification — `ModeExpansionVerification.Run` 111 PASS / 0 FAIL; separate-process `Reload` 10 PASS / 0 FAIL
- **Free Play:** 150 s of accelerated time (3× Hero's spawn interval) → 0 encounters. **CONTROL:** Hero mode, same
  150 s → 2 encounters. 4 deaths → 4 respawns, session never ends. A real prop break raises Heat and police go
  2 → 4 in real time. Switching sides flips hostility (criminals hostile → 4/4 cops hostile). **CONTROLS:** the cooldown
  refuses an immediate re-switch; Hero mode still blocks switching; the relaxed launch guard still rejects an
  encounter mode with no rules and still rejects unplayable modes.
- **Endless escalation matched the formula exactly — with 3 Heat stars active at wave 5:**
  wave 1 → 3 alive, 65 health, 8 damage; wave 5 → 11 alive, 104 health, 11.2 damage. The Heat formula would have given
  119 / 14. Real enemy hits took exactly 8.00 and 11.20. **CONTROLS:** with one enemy alive for 12 s the wave does not
  advance; the MaxAlive cap holds, with a reinforcement after a kill; a lower-scoring replay (10) keeps the best
  (2120). A real wave-6 crowd killed the hero → "WAVE 6 REACHED / NEW BEST".
- **Separate Unity process:** both variants' bests reloaded (2120 / 80), and the in-game HUD showed BEST 2120. A REAL
  save written on 2026-09-20 (no `ModeRecords` field) loaded and upgraded in place. **CONTROL:** a version-2 save is
  still rejected.
- **FPS at the real gameplay camera:** wave 1 (4 humanoids) 196 FPS → wave 5 (12 humanoids) **99 FPS** single-render;
  a second process measured 126 → 79. Skinning ≈ 0.04–0.05 ms per added humanoid.
- **Regressions:** MenuPresentationVerification 42/42 (real tree; its obsolete "Coming Soon" assertions were replaced
  with the new truth). ModeVerification 79 PASS + Reload 2 PASS, exit 0. Its line 81 still asserted Free Play/Endless
  refuse to launch; it now asserts that an unplayable COPY of a mode is refused — the same guarantee, stated truthfully.
  AudioVerification line 61's disabled-button control now uses a genuinely disabled button whose callback WOULD launch
  Free Play, which is stronger than before. dotnet 0 errors / 0 warnings.
- **AudioVerification could not be re-verified in this pass.** After the Mac entered maintenance sleep at 22:21,
  every Unity process logged `FMOD failed to initialize the output device` and fell back to no-sound output, so
  `Music DSP sample cursor advances` fails. **CONTROL:** HEAD 8774aee, before any Step 2–3 code, fails identically at
  the same check. A further re-run with the Mac held awake (23:30) failed identically, 42 PASS then the same FAIL,
  still logging `FMOD failed to initialize the output device`. **The Mac's CoreAudio output has been broken since the
  sleep; this is a machine-state problem, not a code regression.** Resetting it needs admin rights
  (`sudo killall coreaudiod` or a reboot), after which AudioVerification must be re-run. That earlier run took
  48 min because the Mac slept mid-run; later runs took 85–100 s.

### For a human playtest — not verifiable in batch mode
- **Endless Fight is currently the biggest XP faucet in the game.** Enemy defeats still award the existing EnemyXp
  (35), so a wave-6 run (37 kills) is ~1,300 XP — several levels per run. This is a progression-balance decision, not
  tuned here.
- The three new modes sit in small "extras" pills under the two large side cards. They work, but they are easy to miss.
- Enemies converge on one point and ring the hero (see `endless-wave-5-gameplay-camera.png`). Combat depth, the next
  step, addresses this.
- Real keyboard/mouse: H and the pause buttons were exercised by calling the same methods the input handlers call.

## Integration of parallel branches + city batching FPS fix — 2026-09-22 (previous)

### Step 0: ground truth and merge
- `feat/content-docs` (GLM) was **never pushed** despite being reported as pushed; `origin` held only `main`.
- `feat/audio` (Codex) was reported as "out of usage mid-task". In fact Codex was **live** and committed its audio
  work (`ebf7d46`, `4032699`) while this pass was inspecting its worktree at `/private/tmp/op-audio-delivery`.
  Merging waited until the worktree was quiet (HEAD unchanged, no file writes, nothing uncommitted for 60s).
- `feat/backflip-hurricane-kick` was built on top of `feat/content-docs`. Main fast-forwarded to it (bringing content
  + abilities), then merged `feat/audio` (`6ac3422`). The only conflict was this file: both agents appended at the same
  place. Both entries were kept; a line-by-line check confirmed only the three relabelled headings differ.
- A stray uncommitted copy of the audio work in the main working tree was byte-compared (63/63 files identical to
  `4032699`) and stashed, not deleted.
- **Integration defect fixed:** audio played the punch cue only on `PunchImpacted`, but the kick fires its own
  `HurricaneKickImpacted`, so kicks were silent after the merge. Kick impact → punch cue, backflip start → jump cue
  (`cc1a3b7`). Merge gate: audio 109/109 PASS (94 prior + 15 new, including windup, zero-charge and cooldown refusal
  controls that must stay silent), abilities 63/63 PASS.
- **Correction to the 2026-09-20 entry:** `dotnet` IS available. Unity bundles it at
  `/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet`, and
  `dotnet build Overpowered.Build.csproj -p:UseSharedCompilation=false` works with it: **0 errors, 0 warnings**.
  The earlier "cannot currently be run" was overstated.
- Five unrelated Unity editors (other projects, some running 6–20 days, one a hung `-createproject`) were pegging
  ~100% CPU each and distorting every FPS sample. They were terminated with the user's permission before the
  measurements below. Earlier numbers in this file were taken under unknown background load.

### Step 1: the fix
**Root cause (measured 2026-09-20):** `CityArt.Combine` built a NEW mesh per building root and per prop root, per
material: 1,021 distinct meshes from 1,060 MeshFilters. Static batching, GPU instancing and dynamic batching could
never merge anything; `enableInstancing` on the palette materials was inert. **The defect was never combining — it was
producing a unique mesh per instance.**

- **Props** (Rigidbody physics bodies — they move, so they can never be static-batched): each prop kind's geometry is
  built ONCE into a shared mesh, cached per city and keyed by kind + size + each piece's colour (a teal car and a red
  car are separate entries). One renderer per prop with one submesh per material. 15 shared meshes serve all 247
  props, and runtime encounter cars (`CrimeEncounter`) share the same cache. **Built-in RP does instance
  multi-submesh renderers** (measured), so the per-material fallback was unnecessary. Prop renderers: 772 → 247.
- **Buildings and streets** (never move, unique per seed): kept as primitive pieces and static-batched once via `StaticBatchingUtility` (**S1, the shipped default**).
  A whole-city per-material combine (**S2**, `StaticGeometry: 2`) was implemented and measured head-to-head. At the
  real gameplay (street) camera the two are a statistical tie: S1 won 2 of 3 paired rounds, and in the profiled
  samples S2 was slightly slower. S1 also culls per piece, so it scales better at street level as the city grows, and
  it is what the user specified. S2 is cheaper in elevated/flight views (overview rendering 2.1 vs 3.3 ms), so it
  remains a one-line switch. A per-block chunked combine would likely capture both, but it is not implemented.
- `CityMaterials` no longer writes every material every frame; it applies only when a palette colour or smoothness
  actually changes. Live palette editing still works (control: 400 renderers updated within 2 frames).
- `CombineMeshes` is replaced by `PropMeshMode` / `StaticGeometryMode` in `CityArtSettings.asset`. Both legacy
  behaviours stay selectable, which is what allowed before/after to be measured in one process.
  `CityArt.FinishStaticGeometry()` runs after streets in `CityDistrict.Build` and before the menu skyline capture.

### Results — measured at THREE cameras, including the real gameplay view
Every earlier benchmark in this file used a god's-eye overview camera, where the whole city is in view. Players never
see that. This pass added the real `ThirdPersonCamera` placement at street level (hero on a sidewalk) and on a
mid-height rooftop. Same seed and population (26 civilians, 10 cops), the hero stationary, LEGACY/candidates
interleaved over 3 rounds in one process, machine idle (`Verification/Performance/comparison-gameplay.txt`).

| View (single-render, the true figure) | LEGACY | **Shipped: shared props + static batching** | Alt: city combine |
|---|---|---|---|
| **Street — gameplay camera** | 67.8 FPS | **70.4 (+6%)** | 73.1 (+10%, carried by one outlier round) |
| Rooftop / flight | 54.0 | **58.7 (+9%)** | 60.8 (+13%) |
| Overview (historical benchmark view) | 50.2 | **56.4 (+13%)** | 55.3 (+11%) |

Draw calls in the overview: **1,111 → 201** per render (−82%). Batches 1,029 → 121. The earlier 5-round
overview run measured the shipped config at **1.20× LEGACY single-render and 1.31× on the historical double-render
harness** (37.2 → 48.4 FPS double). Build time for the city art dropped 199 → 116 ms.

**The honest reframe:** the "26–40 FPS" regression recorded earlier was mostly how it was measured — an overview
camera that sees the whole city, through a harness that renders every frame twice. At the gameplay camera, the
legacy build was already ~68 FPS single-render; street level culls most of the city. The batching defect was real
and mattered most in elevated/flight views, which is where this fix recovers +9–13%.

**Where the remaining frame time goes — measured directly** with `ProfilerRecorder` (street view, ~14 ms/frame).
This is the first per-system timing in this project; earlier attribution came only from on/off toggles.
| Phase | ms | Share |
|---|---|---|
| `PostLateUpdate.BatchModeUpdate` — **batch-mode editor overhead, not the game** | 5.8 | 41% |
| CPU skinning of the 40 humanoids (`PostLateUpdate.UpdateAllSkinnedMeshes`) | 3.1 | 22% |
| Rendering: culling + draw submission | 1.9 | 14% |
| Animators (`Director.PrepareFrameJob` + `ProcessFrame`) | 1.2 | 9% |
| Scripts (Update + presentation LateUpdate), NavMesh AI, physics | ~1.2 | 9% |
| GPU frame time (`FrameTimingManager`) | ~2 | GPU mostly idle |

Consequences: (1) **Every FPS figure in this file includes ~5.8 ms of batch-mode overhead** that a player build does
not run; the game's own cost is roughly 8 ms/frame at street level. (2) The frame is **completely CPU-bound**, so
triangle count is not the constraint on this machine. (3) The largest remaining real cost is **CPU skinning of the
humanoids (~0.1 ms per character)**. That sets the per-enemy budget for Endless Fight, and it is the next lever
(GPU/compute skinning), but it is not attempted here. In the overview, rendering work fell 5.1 → 3.3 ms (shipped) /
2.1 ms (alt).

Recorder names vary by Unity version. The ones that actually exist in 6000.6 are listed in
`Verification/Performance/profiler-available.txt`.

### Visual identity — proven, not asserted
Captures from 4 fixed views (overview, street, rooftop, menu skyline) with all humanoids hidden and props frozen on
frame 0. **Noise-floor control: two separate LEGACY builds differ by 0 pixels.** LEGACY vs the new default differs by
**5 pixels out of ~3.1 million** (overview 3, roof 2, street 0, skyline 0). Each is a place where two faces share one
plane (parapet vs roof-deck edge, billboard post vs panel), so draw order decides which shows — a pre-existing
geometry quirk that batching reorders. These numbers were independently re-derived by the lead with a separate PNG
decoder, and they match exactly. Captures and amplified diffs: `Verification/Performance/identity/`.

### Regression (new default)
CityArtVerification 31/31, CityVerification 39/39, ModeVerification 80/80 (incl. 2 runtime `CreateProp` cars),
MenuPresentationVerification 38/38 — all exit 0. Run in an APFS clone of the working tree so that tracked
`Verification/{Art,City,Modes,Menus}` files were not overwritten; the clone was confirmed byte-identical to the final
code, and its results post-date the last source edit. Logs: `Verification/Performance/regression/`.

### Measurement notes
- Every historical benchmark renders the scene **twice** per sampled frame (enabled camera with a `targetTexture`
  plus a manual `Render()`). Both numbers are reported: SINGLE is the true figure, and DOUBLE is comparable with the
  155/50/40 FPS history.
- FPS is Editor Play Mode throughput on an Intel i9-10910 / Radeon Pro 5300, not a standalone-player figure. No human
  has played on the new build.

## Hero Forge — 2026-09-23 (codex/hero-forge 9b299ac; merged to main 2026-09-24)

### Delivery and flow

Implemented on isolated branch `codex/hero-forge`, based on main `cc1a3b7`, in /private/tmp/op-hero-forge. The original checkout's unrelated city/performance/combat work is untouched. At final handoff that checkout is clean at `8854e95` (telegraphed archetype combat), having advanced independently during this work. Those later changes are not included or integration-tested here; merging them requires a separate reconciliation pass. This feature is not merged into that checkout. Local delivery only; no new push attempted.

Home -> Hero Forge / Select Hero -> hero, two palette suit colors, two distinct owned powers -> Save & Back -> existing Hero/Villain mode cards. Changes persist immediately through the existing PlayerProgression save. In play, **C / joystick button 5** activates the pair synergy; a second press releases either orbit early. The HUD shows pair, synergy, cooldown and refusal feedback. Bindings and basic melee/VFX budgets are editable in Resources/ForgeCatalog.asset.

Three definitions (VECTOR, TITAN, NOVA), ten unordered pair definitions and ten reusable effect configurations ship in Resources/Forge. The roster UI and pair resolver read assets, not hero/pair-ID switches. Existing PowerDefinition assets are reused, not duplicated. Gameplay caches the equipped pair for a session and rejects unequipped abilities in PowerUser, including actual flight fuel calls. Live loadout mutation is refused. ProgressSave.Loadout is additive to version 1; older/fresh saves repair to an owned starter pair. Separate-process persistence and fresh-save controls passed.

Sonic Slam was built and verified first (39 passing controls, Verification/Forge/sonic-first.txt) before adding the remaining effects progressively. All ten now have real gameplay implementations: Sonic Slam, Phoenix Dive, Frostwake, Orbit Throw, Thermal Shock, Meteor Punch, Inferno Orbit, Glacier Fist, Cryo Crush and Meteor Slam. docs/hero-forge.md contains the full file inventory, pair mechanics, ownership, tuning, and recipes for adding heroes/synergies. No Inspector/scene wiring is needed; missing assets can be authored with Overpowered > Forge > Create missing assets.

### Judgments, integration and explicit limits

- Retained existing unlock economy. Fresh saves own Flight/Strength; selecting a hero never grants locked powers. Unlock buttons call existing PlayerProgression.Buy with existing points. Locked hero defaults fall back to two owned powers. All three starter silhouettes therefore initially share the starter pair.
- All heroes reuse the existing humanoid mannequin (widths 1.0, 1.2, 0.92) with shared-palette suits. These are placeholder variations, not three unique models. Normalized height remains 1.8m; no CharacterController dimensions or movement stats changed. Future definitions can reference unique Humanoid prefabs/animation tuning. No licensed character/logo assets were introduced.
- Without Strength, E/RMB still work as weaker basic melee: 160 N.s / 8 base damage, 0.65s shared cooldown before kick multipliers. Equipped Strength preserves original charges, cooldown, upgrades and impact windup. Backflip remains a spatial dodge without invulnerability.
- Synergies have their own default 10s cooldown. Frostwake additionally uses real flight fuel. Meteor Punch requires ready charged Strength but does not spend a normal charge; other synergies do not consume base-power energy/charges. This is explicit prototype balance, not final tuning.
- Existing Animator/procedural ownership is retained. Ability motion uses CharacterController.Move and emits landing presentation for audio/squash; no animated teleport of the physics root. Pause freezes motion/cooldown and refuses activation; death/disable restores held-body gravity and temporary actor-collision settings.
- Reused CombatImpact, ThrownProp and BreakableProp. Physical NPC reactions use a temporary navigation-to-Rigidbody handoff, not ragdolls. Navigation recovery samples nearby NavMesh and may fall back to the prior valid NPC position. Props are never teleported or made static. Fire's timed burning marker enables Thermal Shock bonus; no new damage-over-time system.
- Built-in Particle System module enabled; no URP/Shader Graph/volume dependency. Six scene-lifetime pooled slots, each with a 40-point ring and at most 28 mesh particles. Every slot shares one built-in cube mesh and existing palette materials. No per-cast mesh/material creation; existing combat allocations and first-use components remain. No zero-GC claim.
- VFX are geometric placeholders, not polished fire/ice art. Camera feedback is a 3-degree, 0.16s FOV kick, not global hit-stop or long shake. Visual inspection caught inflated opaque particles; Shape scaling plus shared mesh particles fixed this. Tests now prove particles move over real frames and share their mesh.
- Free Play / Endless Fight are STILL DISABLED Coming Soon definitions. Forge integrates at the shared city bootstrap; this packet does not implement those missing modes. Hero and Villain loadout transitions were exercised. No claim that all historical user requests are complete.
- Tests drive real gameplay/UI event entry points, not physical keyboard/controller input. No human feel test, full-length session or hardware-controller test. Historical all-powers-at-once verifiers need explicit loadout setup now; they were not all rerun. Current controls cover original punch/kick charge refusal, original force, backflip damage vulnerability, flight fuel and mode/save integration.

### Verification and measured performance

Unity 6000.6.0f1 isolated test project /private/tmp/op-audio-verify-6s0bUo. Final HeroForgeVerification.Run and separate-process HeroForgeVerification.Reload both exited 0. **131 gameplay/UI/physics controls + 3 reload controls passed.** Final supplemental dotnet build: **0 warnings, 0 errors** (Verification/Forge/build.txt). Tested Forge source matches the delivery source.

Commands (runner exits itself; no -quit):

```sh
Unity -batchmode -projectPath /private/tmp/op-audio-verify-6s0bUo -executeMethod HeroForgeVerification.Run -logFile /private/tmp/op-forge-complete.log
Unity -batchmode -projectPath /private/tmp/op-audio-verify-6s0bUo -executeMethod HeroForgeVerification.Reload -logFile /private/tmp/op-forge-complete-reload.log
dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false
```

Actual populated-city ABBA test: 26 civilians + 14 cops, one 1280x720 camera, effects emitted every 100ms for stress phases. Idle **49.44 FPS**, repeated FX **49.08 FPS**, reported delta **0.37 FPS / 0.152ms** (rounding from unrounded samples). Draw calls about **1118 idle / 1127 stress**. This is Editor wall-clock throughput and within likely noise, not a GPU-time or allocation measurement. It is well below the historical 155 FPS number, but that old benchmark is not the same population/assets/test setup; no performance recovery is claimed and no content was cut to conceal cost. Another task's later city/performance work is excluded from this feature.

Captures: Verification/Forge/forge-menu.png, forge-preview.png, sonic-impact.png. Inspected actual rendering: cyan/navy Forge panel, mannequin suit colors, five selector rows and automatic synergy label; Sonic ground ring, small particles and a launched physical cube. Captures are evidence of rendering, not proof of human-perceived feel.

Console is NOT wholly error-free: the known UnityEditor.Search.SearchDatabase startup ArgumentOutOfRangeException appears in both final runs. No Forge gameplay errors were reported by the runtime error gate. Earlier failed runs exposed a real MissingComponentException caused by Unity's fake-null Rigidbody wrapper with ??; explicit Unity null checking fixed it. The particle module omission and oversized particles were also corrected rather than hidden. Older progressive output remains labelled in Verification/Forge; results.txt is authoritative final output.

Verbatim final output:

```text
PASS Three selectable data-defined heroes.
PASS Home button opens Forge in existing panel.
PASS Character preview render texture exists.
PASS Duplicate-pair CONTROL rejected by save API.
PASS Locked-power CONTROL rejected by Forge.
PASS Existing progression unlock fire
PASS Existing progression unlock ice
PASS Existing progression unlock telekinesis
PASS Select NOVA definition.
PASS Suit palette roles saved.
PASS Slot 1 collision repairs slot 2 immediately.
PASS Slot 2 UI excludes duplicate.
PASS Select Fire + Ice.
PASS Pair lookup is order-independent.
PASS Enter Hero with saved build.
PASS Gameplay receives exact equipped pair.
PASS Unequipped flight CONTROL refuses real fuel API.
PASS Unequipped Strength CONTROL cannot be selected or fired directly.
PASS Basic punch remains available without Strength.
PASS Basic melee force is reduced, not hidden super strength: 160 N.s.
PASS Live loadout mutation CONTROL refused.
PASS Selected humanoid model instantiated.
PASS Save TITAN reverse Sonic pair.
PASS Sonic Slam accepted on valid ground.
PASS Active/cooldown CONTROL refuses immediate spam.
PASS Pause CONTROL freezes synergy motion/cooldown and rejects activation.
PASS Sonic Slam rises then collides with actual ground exactly once.
PASS Sonic force moves real 45kg Rigidbody.
PASS Outside-radius CONTROL has no horizontal launch.
MEASURED Sonic peak=154.340m, force=2600.0 N.s, bodies=1, near velocity=34.052m/s, displacement=0.672m; cooldown=9.591s.
PASS Cooldown still blocks after animation ends.
PASS Actual pooled ParticleSystem / ring emitted once.
PASS Real ParticleSystem contains bounded live particles.
PASS Particle positions advance over real frames (not a frozen emission counter).
PASS All six VFX slots share the same particle mesh.
PASS Equipped Flight positive control consumes configured fuel.
PASS Equipped Strength uses existing paid punch.
PASS Shared punch/kick cooldown still rejects kick.
PASS Equipped Strength preserves original force.
PASS Zero Strength charges still reject Hurricane Kick.
PASS Backflip remains available.
PASS Backflip CONTROL has no invincibility.
PASS Save final loadout for separate-process test.
PASS Villain mode switch preserves build and progression.
PASS All ten unordered pairs have effect assets.
PASS Unique symmetric pair fire + flight
PASS Unique symmetric pair fire + ice
PASS Unique symmetric pair fire + strength
PASS Unique symmetric pair fire + telekinesis
PASS Unique symmetric pair flight + ice
PASS Unique symmetric pair flight + strength
PASS Unique symmetric pair flight + telekinesis
PASS Unique symmetric pair ice + strength
PASS Unique symmetric pair ice + telekinesis
PASS Unique symmetric pair strength + telekinesis
PASS Equip Phoenix Dive
PASS Phoenix Dive activation accepted.
PASS Phoenix Dive completes within bounded duration.
PASS Phoenix Dive cooldown remains after action.
PASS Phoenix Dive reaches forward ground target and impacts.
PASS Phoenix Dive emits bounded pooled effects.
SYNERGY Phoenix Dive: impacts=1, VFX emissions=1, cooldown=9.40
PASS Equip Frostwake
PASS Frostwake empty-fuel CONTROL.
PASS Frostwake activation accepted.
PASS Frostwake completes within bounded duration.
PASS Frostwake cooldown remains after action.
PASS Frostwake moves, drains fuel, hits nearby actor; far actor untouched.
PASS Frostwake emits bounded pooled effects.
SYNERGY Frostwake: impacts=0, VFX emissions=8, cooldown=8.89
PASS Equip Orbit Throw
PASS Orbit Throw empty-area CONTROL costs no cooldown.
PASS Orbit Throw activation accepted.
PASS Orbit Throw only movable in-budget prop captured; heavy CONTROL excluded.
PASS Orbit Throw orbit uses real force displacement.
PASS Second C-equivalent requests volley, not another paid activation.
PASS Orbit Throw completes within bounded duration.
PASS Orbit Throw cooldown remains after action.
PASS Thrown prop restores gravity and receives a real forward launch impulse.
PASS Heavy prop CONTROL has no launch impulse.
PASS Orbit cancellation setup accepted.
PASS Orbit cancellation setup actually holds body.
PASS Death CONTROL cancels orbit and restores held-body gravity.
PASS Orbit Throw emits bounded pooled effects.
SYNERGY Orbit Throw: impacts=0, VFX emissions=4, cooldown=9.99
PASS Equip Thermal Shock
PASS Thermal Shock activation accepted.
PASS Thermal Shock completes within bounded duration.
PASS Thermal Shock cooldown remains after action.
PASS Thermal affected-target bonus CONTROL: 42.5 vs clean 25
PASS Thermal Shock emits bounded pooled effects.
SYNERGY Thermal Shock: impacts=1, VFX emissions=1, cooldown=9.69
PASS Equip Meteor Punch
PASS Meteor Punch zero-charge eligibility CONTROL.
PASS Meteor Punch activation accepted.
PASS Meteor Punch completes within bounded duration.
PASS Meteor Punch cooldown remains after action.
PASS Meteor Punch is heavier than Hurricane Kick: 3400 N.s
PASS Meteor Punch emits bounded pooled effects.
SYNERGY Meteor Punch: impacts=1, VFX emissions=5, cooldown=9.24
PASS Equip Inferno Orbit
PASS Inferno Orbit empty-area CONTROL costs no cooldown.
PASS Inferno Orbit activation accepted.
PASS Inferno Orbit only movable in-budget prop captured; heavy CONTROL excluded.
PASS Inferno Orbit orbit uses real force displacement.
PASS Second C-equivalent requests volley, not another paid activation.
PASS Inferno Orbit completes within bounded duration.
PASS Inferno Orbit cooldown remains after action.
PASS Thrown prop restores gravity and receives a real forward launch impulse.
PASS Heavy prop CONTROL has no launch impulse.
PASS Inferno projectile collision causes a real fiery blast.
PASS Orbit cancellation setup accepted.
PASS Orbit cancellation setup actually holds body.
PASS Death CONTROL cancels orbit and restores held-body gravity.
PASS Inferno Orbit emits bounded pooled effects.
SYNERGY Inferno Orbit: impacts=1, VFX emissions=5, cooldown=9.99
PASS Equip Glacier Fist
PASS Glacier Fist activation accepted.
PASS Glacier buff leaves normal melee available.
PASS Glacier empowered punch uses existing charge gate.
PASS Glacier impact raises force and applies freeze.
PASS Glacier Fist completes within bounded duration.
PASS Glacier Fist cooldown remains after action.
PASS Glacier Fist emits bounded pooled effects.
SYNERGY Glacier Fist: impacts=0, VFX emissions=5, cooldown=9.67
PASS Equip Cryo Crush
PASS Cryo Crush no-enemy CONTROL.
PASS Cryo Crush activation accepted.
PASS Cryo Crush completes within bounded duration.
PASS Cryo Crush cooldown remains after action.
PASS Cryo Crush physically lifts enemy and hits ground; peak=156.01
PASS Cryo Crush emits bounded pooled effects.
SYNERGY Cryo Crush: impacts=1, VFX emissions=8, cooldown=8.48
PASS Equip Meteor Slam
PASS Meteor Slam no-target CONTROL.
PASS Meteor Slam activation accepted.
PASS Meteor Slam completes within bounded duration.
PASS Meteor Slam cooldown remains after action.
PASS Meteor Slam lifts a prop and produces collision-driven shockwave.
PASS Meteor Slam emits bounded pooled effects.
SYNERGY Meteor Slam: impacts=1, VFX emissions=9, cooldown=8.56
BENCH population civilians=26, cops=14; 1280x720 single camera.
MEASURED Forge idle phase=0: FPS=49.16, draws=1118.9, pool=6.
MEASURED Forge FX stress phase=1: FPS=48.86, draws=1126.8, pool=6.
MEASURED Forge FX stress phase=2: FPS=49.29, draws=1126.7, pool=6.
MEASURED Forge idle phase=3: FPS=49.73, draws=1118.1, pool=6.
FORGE COST paired means: idle=49.44 FPS, repeated pooled FX=49.08 FPS, difference=0.37 FPS, frame delta=0.152ms. Stress emits every 100ms; not a standalone GPU-time measurement.
LIMIT: no human feel test; real gameplay entry points, not hardware key injection. Free Play/Endless Fight remain disabled existing definitions.
```

Separate-process reload:

```text
PASS Three selectable data-defined heroes.
PASS SECOND PROCESS restores exact hero, pair and suit roles.
PASS Fresh save CONTROL starts level 1 / default pair.
```


## Pooled audio — 2026-09-22 (merged to main 2026-09-22; previous)

### Delivery

Implementation commit: ebf7d46 on feat/audio. Push was attempted with `git push -u origin feat/audio` and failed (exit 128): `fatal: could not read Username for 'https://github.com': Device not configured`. Local commits are retained; a human must authenticate GitHub and retry the push. Nothing was merged to main. Delivery worktree: /private/tmp/op-audio-delivery; the original checkout remains on the other agent's abilities branch. Authored code/docs pass the whitespace check; untouched Unity-generated YAML and original licence bytes retain their source whitespace.

### Built and ownership

- Automatic persistent AudioDirector: 24 preallocated AudioSources, four reserved for city/siren/flight/music beds and 20 round-robin one-shots. Per-cue caps, pitch/volume variation, distance culling, positional mono world cues and 2D UI/music. Saturated requests are dropped, not allocated. Audio randomness does not change the gameplay seed.
- 18 cue types / 21 real CC0 clips: punch, three footsteps, flight start/loop, landing, destruction, fire, ice, telekinesis, gunshot, hit, death, jump, UI click/hover, city/siren beds and music. Every file's source URL, author, licence and download date is in Assets/Audio/ATTRIBUTION.md; original Kenney licences retained in Assets/Audio/Licenses. Offline conversion is reproducible with prepare_clips.py. All positional files are mono; all files are Ogg Vorbis; music streams.
- Inspector tuning: Assets/Resources/AudioTuning.asset owns clips, levels, jitter, pitch, caps, distances/rolloff, pool budget, actor cadence, footstep spacing, fades and Heat response. Assets/Audio/Overpowered.mixer routes Master -> Music/SFX/UI/Ambient; all five volume parameters are exposed. Overpowered/Audio/Create missing audio assets authors missing assets without overwriting existing tuning.
- Existing events only: powers, player damage/death/respawn, jump/landing, punch impact and NPC attacks. Footsteps use measured speed plus a central distance accumulator; NPC hit/death counters are sampled centrally. No new per-NPC Update or per-sound objects. Registry refresh is once per second; state sampling every 50ms; actor capacity 128.
- Important hook corrections to the packet: holding Flight does NOT emit PowerUser.Activated, so start/loop follow the existing Flying presentation state and actual fuel remains gameplay-owned. Punch audio subscribes to PunchImpacted, not activation, preserving the configured 125ms windup. Strength costs zero energy, so its refusal control is zero charges; Fire/Ice/Telekinesis use real zero-energy controls, Flight uses empty fuel.
- The ONLY existing gameplay edit is two additive lines in BreakableProp: static Destroyed event and invocation where Break actually succeeds. No damage, health, shards, physics, power, character, menu or progression logic changed.
- UI callbacks are delegated from the existing panel root (including dynamically added buttons); disabled buttons are ignored. Scene/disable cleanup unsubscribes hooks and stops world audio; music/UI tails can survive transitions. Heat 0..5 drives calm vs siren/tension, not a new wanted system.
- Branch isolation: based on 9c3c67f. Another agent changed the shared checkout to feat/backflip-hurricane-kick, so delivery uses a separate feat/audio worktree; that agent's branch/commit is untouched and is NOT merged here. Original shared-checkout audio working files are retained, not discarded.

### Judgment calls, cuts and verification limits

No human has heard or judged this mix. Source playback/DSP cursors are verified, not speaker output, loudness balance or perceived sound quality. Flight is an engine/wind-like texture; ice/telekinesis are electronic power textures; death is a descending feedback tone, not a human vocal. City is the source's near-seamless loop; music repeats a full track with edge fades, not a musically seamless composition. Cop audio uses the existing contact-range attack event: this does not implement ranged combat.

Flight and footsteps are controlled at the public presentation boundary after real fuel controls; jump/landing use real CharacterController motion. No physical F-key input automation. UI home/disabled controls and Hero/Villain scene transitions were exercised; a full results/upgrade-button listening pass was not. NPC registration can lag by one second and ignores actors beyond 128; no large-crowd saturation benchmark beyond the recorded population. No allocation-profiler measurement was made (bounded, centralized design is not a measured zero-GC claim). No volume-options UI or saved preferences added.

The original Tabasco gunshot archive was rejected: its CC0 page conflicts with an included CC-BY licence. The shipped 1911 report instead comes from the explicitly CC0 Free Firearm Sound Library (four authors recorded in attribution).

### Build and measured verification

Unity 6000.6.0f1 isolated project /private/tmp/op-audio-verify-6s0bUo, shipping city/assets plus this packet. Both AudioVerification.Run and AudioVerification.Reload exited **0** in separate processes. Isolation avoids shared-project Editor/ILPP locks; no existing Library or Logs were deleted. Tested audio source/assets match the delivery worktree byte-for-byte.

Commands (no -quit; each runner exits itself):

```sh
Unity -batchmode -projectPath /private/tmp/op-audio-verify-6s0bUo -executeMethod AudioVerification.Run -logFile /private/tmp/op-audio-run3.log
Unity -batchmode -projectPath /private/tmp/op-audio-verify-6s0bUo -executeMethod AudioVerification.Reload -logFile /private/tmp/op-audio-reload.log
```

Unity compile/runtime checks passed. The logs are NOT error-free: UnityEditor.Search.SearchDatabase threw an indexing ArgumentOutOfRangeException during startup, and licensing refresh logged token/entitlement errors before succeeding; neither is an audio/runtime assertion failure. Earlier verification attempts exposed test mistakes (a zero-energy Strength control despite its zero cost; checking footsteps during a still-playing cast). These were corrected to valid controls, not replaced with bookkeeping assertions.

A bundled dotnet SDK was found inside Unity; none was installed. Supplemental build in the delivery worktree succeeded: **0 errors, 16 pre-existing CS0618 warnings in PerformanceProfileRunner.cs** (untouched). Full compiler output: Verification/Audio/build.txt. Thus this is not advertised as a warning-free build.

ABBA samples: 26 civilians + 14 cops, one enabled 1280x720 camera, 6s per segment, actual beds and SFX active in enabled samples (8–9 simultaneous sources), zero playing sources when disabled. Paired mean: disabled **34.39 FPS**, enabled **34.33 FPS**; reported loss **0.07 FPS**, frame-time difference **0.058ms**. This is within noise, not proof of a precise 0.058ms cost. No >2 FPS regression observed in this run. Editor throughput only, not standalone performance; draw calls ~1115–1119, slight population/activity drift remains.

Verbatim main output (also Verification/Audio/results.txt):

```text
PASS Single automatic director, exactly 24 preallocated AudioSources.
PASS Punch references real clips and a mixer group.
PASS punch-1 positional mono.
PASS punch-2 positional mono.
PASS Footstep references real clips and a mixer group.
PASS step-1 positional mono.
PASS step-2 positional mono.
PASS step-3 positional mono.
PASS FlightStart references real clips and a mixer group.
PASS flight-start positional mono.
PASS FlightLoop references real clips and a mixer group.
PASS flight-loop positional mono.
PASS Land references real clips and a mixer group.
PASS land positional mono.
PASS Destruction references real clips and a mixer group.
PASS debris positional mono.
PASS Fire references real clips and a mixer group.
PASS fire positional mono.
PASS Ice references real clips and a mixer group.
PASS ice positional mono.
PASS Telekinesis references real clips and a mixer group.
PASS telekinesis positional mono.
PASS Gunshot references real clips and a mixer group.
PASS gunshot positional mono.
PASS Hit references real clips and a mixer group.
PASS hit positional mono.
PASS Death references real clips and a mixer group.
PASS death positional mono.
PASS Jump references real clips and a mixer group.
PASS jump positional mono.
PASS UiClick references real clips and a mixer group.
PASS UiHover references real clips and a mixer group.
PASS CityBed references real clips and a mixer group.
PASS SirenBed references real clips and a mixer group.
PASS Music references real clips and a mixer group.
PASS All 18 cue types / 21 clip assignments populated (no silent placeholders).
PASS Exposed mixer parameter MasterVolume is valid.
PASS Exposed mixer parameter MusicVolume is valid.
PASS Exposed mixer parameter SFXVolume is valid.
PASS Exposed mixer parameter UIVolume is valid.
PASS Exposed mixer parameter AmbientVolume is valid.
PASS Music: actual AudioSource.isPlaying, assigned clip=music, volume=0.072, group=Music, spatialBlend=0.
PASS Music DSP sample cursor advances (not just bookkeeping).
PASS Disabled UI CONTROL emits no click and launches nothing.
PASS UiHover: actual AudioSource.isPlaying, assigned clip=ui-hover, volume=0.160, group=UI, spatialBlend=0.
PASS UiClick: actual AudioSource.isPlaying, assigned clip=ui-click, volume=0.600, group=UI, spatialBlend=0.
PASS Director binds shipping world without gameplay/bootstrap edits.
PASS Undamaged player CONTROL has no Hit source.
PASS Hit: actual AudioSource.isPlaying, assigned clip=hit, volume=0.522, group=SFX, spatialBlend=1.
PASS Real paid punch accepted.
PASS Windup CONTROL: no punch-impact audio at activation.
PASS Punch: actual AudioSource.isPlaying, assigned clip=punch-2, volume=0.617, group=SFX, spatialBlend=1.
TIMING accepted=1.6305, actual impact=1.7628, source timeSamples=969; configured windup=125.0ms; audio callback is PunchImpacted.
PASS Zero-charge punch refused.
PASS Zero-charge CONTROL plays no punch cue after windup.
PASS Shipping Strength costs 0 energy: its rejection CONTROL is charges/cooldown, not energy. Flight uses fuel; Fire/Ice/Telekinesis energy controls follow.
PASS Damaged-but-unbroken prop CONTROL emits no destruction.
PASS Destruction: actual AudioSource.isPlaying, assigned clip=debris, volume=0.556, group=SFX, spatialBlend=1.
PASS Fire Blast paid activation succeeds.
PASS Fire: actual AudioSource.isPlaying, assigned clip=fire, volume=0.585, group=SFX, spatialBlend=1.
PASS Fire Blast no-energy activation refused.
PASS Fire Blast no-energy CONTROL emits no cue.
PASS Ice paid activation succeeds.
PASS Ice: actual AudioSource.isPlaying, assigned clip=ice, volume=0.440, group=SFX, spatialBlend=1.
PASS Ice no-energy activation refused.
PASS Ice no-energy CONTROL emits no cue.
PASS Telekinesis paid activation succeeds.
PASS Telekinesis: actual AudioSource.isPlaying, assigned clip=telekinesis, volume=0.361, group=SFX, spatialBlend=1.
PASS Telekinesis no-energy activation refused.
PASS Telekinesis no-energy CONTROL emits no cue.
PASS Real flight resource consumption accepted.
PASS FlightStart: actual AudioSource.isPlaying, assigned clip=flight-start, volume=0.300, group=SFX, spatialBlend=1.
PASS FlightLoop: actual AudioSource.isPlaying, assigned clip=flight-loop, volume=0.160, group=SFX, spatialBlend=1.
PASS Empty-fuel flight CONTROL refuses consumption.
PASS Not-flying CONTROL fades/stops flight sources.
PASS Central distance accumulator plays real footstep sources: speed=6.00, state=Locomotion, health=99.0 (waits for prior cast to finish).
PASS Stationary CONTROL has no footsteps.
PASS Land: actual AudioSource.isPlaying, assigned clip=land, volume=0.525, group=SFX, spatialBlend=1.
PASS Real grounded jump accepted.
PASS Jump: actual AudioSource.isPlaying, assigned clip=jump, volume=0.177, group=SFX, spatialBlend=1.
PASS Death: actual AudioSource.isPlaying, assigned clip=death, volume=0.384, group=SFX, spatialBlend=1.
PASS Heat 0 CONTROL: calm bed playing, siren stopped at volume 0.
PASS SirenBed: actual AudioSource.isPlaying, assigned clip=siren-bed, volume=0.240, group=Ambient, spatialBlend=0.
PASS Heat 0->5: city 0.3500->0.1925, siren 0.0000->0.2400; intensity=1.0000.
PASS Concurrency CONTROL: 13 simultaneous requests, exactly 3 actual sources playing (cap 3); pool remains 24.
PASS Near spatial CONTROL plays.
PASS Far spatial CONTROL does not consume a voice.
BENCH population civilians=26, cops=14; single enabled camera rendering 1280x720 (no manual Camera.Render double-render).
MEASURED audio disabled A: frames=201, seconds=6.007, FPS=33.47, p95=38.611ms, drawCalls=1119.0, peak playing sources=0.
PASS Disabled benchmark CONTROL has zero playing sources.
MEASURED audio enabled B: frames=210, seconds=6.016, FPS=34.91, p95=36.455ms, drawCalls=1119.0, peak playing sources=9.
PASS Enabled benchmark includes actual beds and simultaneous SFX.
MEASURED audio enabled B2: frames=203, seconds=6.016, FPS=33.74, p95=39.785ms, drawCalls=1119.0, peak playing sources=8.
PASS Enabled benchmark includes actual beds and simultaneous SFX.
MEASURED audio disabled A2: frames=212, seconds=6.003, FPS=35.32, p95=34.922ms, drawCalls=1114.6, peak playing sources=0.
PASS Disabled benchmark CONTROL has zero playing sources.
AUDIO COST paired means: disabled=34.39 FPS, enabled=34.33 FPS, loss=0.07 FPS; frame-time delta=0.058ms. Within requested ~2 FPS budget in this run; noise/Editor limits apply.
PASS Return Home keeps one pool; no stale world/flight/city audio.
PASS Gunshot: actual AudioSource.isPlaying, assigned clip=gunshot, volume=0.601, group=SFX, spatialBlend=1.
PASS Cop gunshot is driven by a real hostile NPC attack/damage event.
PASS Mode switch CONTROL retains exactly one director/pool.
LIMIT: no human has heard or judged the mix. Tests assert real source playback, clips, volumes and routing, not audible-device capture. Flight state is controlled at the public presentation boundary after real fuel checks; no hardware F-key automation. FPS is Editor throughput, not standalone performance.
```

Verbatim separate-process asset reload (also Verification/Audio/reload.txt):

```text
PASS Single automatic director, exactly 24 preallocated AudioSources.
PASS Punch references real clips and a mixer group.
PASS punch-1 positional mono.
PASS punch-2 positional mono.
PASS Footstep references real clips and a mixer group.
PASS step-1 positional mono.
PASS step-2 positional mono.
PASS step-3 positional mono.
PASS FlightStart references real clips and a mixer group.
PASS flight-start positional mono.
PASS FlightLoop references real clips and a mixer group.
PASS flight-loop positional mono.
PASS Land references real clips and a mixer group.
PASS land positional mono.
PASS Destruction references real clips and a mixer group.
PASS debris positional mono.
PASS Fire references real clips and a mixer group.
PASS fire positional mono.
PASS Ice references real clips and a mixer group.
PASS ice positional mono.
PASS Telekinesis references real clips and a mixer group.
PASS telekinesis positional mono.
PASS Gunshot references real clips and a mixer group.
PASS gunshot positional mono.
PASS Hit references real clips and a mixer group.
PASS hit positional mono.
PASS Death references real clips and a mixer group.
PASS death positional mono.
PASS Jump references real clips and a mixer group.
PASS jump positional mono.
PASS UiClick references real clips and a mixer group.
PASS UiHover references real clips and a mixer group.
PASS CityBed references real clips and a mixer group.
PASS SirenBed references real clips and a mixer group.
PASS Music references real clips and a mixer group.
PASS All 18 cue types / 21 clip assignments populated (no silent placeholders).
PASS Exposed mixer parameter MasterVolume is valid.
PASS Exposed mixer parameter MusicVolume is valid.
PASS Exposed mixer parameter SFXVolume is valid.
PASS Exposed mixer parameter UIVolume is valid.
PASS Exposed mixer parameter AmbientVolume is valid.
PASS Music: actual AudioSource.isPlaying, assigned clip=music, volume=0.072, group=Music, spatialBlend=0.
PASS Music DSP sample cursor advances (not just bookkeeping).
PASS Disabled UI CONTROL emits no click and launches nothing.
PASS UiHover: actual AudioSource.isPlaying, assigned clip=ui-hover, volume=0.160, group=UI, spatialBlend=0.
PASS UiClick: actual AudioSource.isPlaying, assigned clip=ui-click, volume=0.600, group=UI, spatialBlend=0.
PASS Director binds shipping world without gameplay/bootstrap edits.
PASS CityBed: actual AudioSource.isPlaying, assigned clip=city-bed, volume=0.350, group=Ambient, spatialBlend=0.
PASS SECOND PROCESS loads persisted mixer with all five groups.
SECOND PROCESS asset/reference reload passed; fresh isolated save used.
```

## Backflip and Hurricane Kick gestures — 2026-09-21 (merged to main 2026-09-22; previous)

Abilities packet (`feat/backflip-hurricane-kick`). Wires the two supplied animation clips that
the humanoid pass imported and deliberately left unused. Both gestures are additive and
self-contained: no new power type, resource, data asset category, or dodge/invulnerability
system, and the power architecture (Flight/Strength/Telekinesis/Fire/Ice) is untouched.

### What was added

- **Backflip — `Q`.** A short backward dash/hop: 4.2 m over 0.90 s plus a 5.5 m/s upward launch
  handled by the **existing** gravity arc (`verticalVelocity`), i.e. the same
  `CharacterController`/`Move` path walking and jumping already use. Its own cooldown constant
  (`HeroAbilityTuning.BackflipCooldown = 2.5 s`), grounded only, and it never rotates the physics
  root. It is repositioning only — there are no i-frames and no dodge state. `Q` was chosen
  because it is the only sensible unused movement key: `WASD/Shift/Space/F/E/R/H/Tab/Esc`, LMB,
  digits 1–9 and the harness's `V` are all taken.
- **Hurricane Kick — right mouse button.** A secondary melee gesture beside the `E` punch. It is
  paid for by the **existing Super Strength runtime**: `TryHurricaneKick` sets a pending flag and
  calls the same `PowerUser.Use(Strength)`, so the charge pool, 0.45 s cooldown, energy gate and
  `Blocked: …` messages are the shipping ones. `PerformPunch` routes a pending activation to the
  kick instead of the punch, which is why there is no new `PowerEffect` subclass, no new power
  asset and no new HUD entry. **Decision: shared charge pool rather than its own count** — it
  cannot desynchronise from progression tiers, energy or the HUD's charge display, and it needed
  zero edits inside `PowerUser` (the shared ability script most likely to collide with another
  packet). Consequence: a kick and a punch cannot be thrown inside the same 0.45 s cooldown.
- **Kick strength** is expressed as multipliers on the already-tier-scaled Strength stats
  (`x1.50` force, `x1.70` damage) plus an absolute wider radius (4.6 m vs the punch's 3.3 m) and
  a flat 1.4 m origin offset, so an upgraded Super Strength keeps improving the kick instead of
  the kick falling behind. All of it lives in `Assets/Scripts/HeroAbilityTuning.cs` (new).
- **Clip dispatch** uses the existing presentation-event pattern: `HumanoidPresentation`
  subscribes to `hero.BackflipStarted` / `hero.HurricaneKickStarted` and plays the states, with
  a backflip-specific guard so the shared `Land` clip is not layered over a flip that is still
  finishing. The backflip's playback rate is derived so the clip ends exactly when the dash does.
- **Timing is sampled, not guessed.** `BackflipHurricaneVerification.Sample` re-uses the
  importer's technique (`Clip.SampleAnimation`, per-frame bone positions) and wrote
  `Verification/Abilities/clip-sample.txt`: the backflip launches near frame 18 of 65 and lands
  near frame 40, and the hurricane kick's striking (left) leg lifts near frame 10 and peaks at
  frame 28 of 55 — hence clip start 0.600 s / 0.333 s and impact 0.933 s.
- **State rename.** The controller's two `Unwired …` states are now `Backflip` and
  `Hurricane Kick`; `HumanoidSetup.Build` was updated in the same change so a later rebuild of
  `Assets/Resources/SharedHumanoid.controller` reproduces the same names.

### Shared-code edits, called out (small and deliberate)

`Assets/Scripts/SuperHeroController.cs` (2 added input lines in `Update`, one added early-return
line at the top of `PerformPunch`), `Assets/Scripts/HumanoidPresentation.cs` (event subscribe/
unsubscribe, `Land` guard, kick marker), `Assets/Scripts/PrototypeHUD.cs` (control-legend
string), `Assets/Editor/HumanoidSetup.cs` (the two state names). Everything else is new files or
the animation tuning asset.

### Verification (real output)

```sh
Unity -batchmode -projectPath /tmp/op-abilities-verify \
  -executeMethod BackflipHurricaneVerification.Run -logFile .../Verification/Abilities/run.log
Unity -batchmode -projectPath /tmp/op-abilities-verify \
  -executeMethod HumanoidVerification.Run          -logFile .../Verification/Humanoid/run.log
```

Isolated project copy, editor closed, **no `-quit`** (each verifier exits itself).

- **New abilities suite: exit 0, 63 PASS / 0 FAIL** (`Verification/Abilities/results.txt`).
  Backflip: accepted from the ground, dispatches the renamed state, moved the hero **-4.22 m**
  backward against a configured 4.2 m, hop peak **+0.595 m**, physics root not rotated, then
  refused with `Blocked: cooldown` (**1.43 s of 2.50 s still remaining**, 1 use, no second
event) and refused while airborne (`Blocked: airborne`), and after the cooldown elapsed it
  fired again (use 2) and returned the presentation to locomotion. Kick: `PUNCH force=1350 N·s,
  1 body, 2.8 m target 22.270 m/s, 5.6 m target 0.000 m/s` versus `HURRICANE KICK force=2025
  N·s, 2 bodies, 2.8 m target 30.203 m/s, 5.6 m target 5.181 m/s` — the punch's 5.00 m reach
  versus the kick's 6.00 m. NPC damage on same-role cops: punch **35.0**, kick **59.5**
  (**1.70x**). Shared pool: 3 → 2 → 1 charges, kick refused immediately after a punch with
  `Blocked: cooldown`, `Blocked: 0 charges` at zero, and the existing 1.25 s recharge restored
  the pool. Kick force landed on the **same rendered frame** as its evaluated clip impact marker
  (offset 0.00 ms, 0 frames), matching the punch's contract.
- **Pre-existing control suite: exit 0, 46 PASS / 0 FAIL** (movement, punch, charges, recharge,
  flight fuel, landing, panic, casting, cop behaviour all unchanged — e.g. `Real force=1350 N·s,
  mass=45kg, velocity=17.192m/s`).
- `dotnet build Overpowered.Build.csproj` via the Unity-bundled SDK: **0 errors, 16 pre-existing
  CS0618 warnings** (unchanged from the previous entry's baseline).

### Two test faults found and fixed (recorded so they are not re-derived)

- The first width control placed targets in the street: the punch also pushed the far target
  (city props were in the sphere), so the comparison proved nothing. The measurement now runs
  200 m up in clean air with 0.5 m cubes at 2.8 m/5.6 m — outside the punch's 5.00 m reach and
  inside the kick's 6.00 m reach — and the punch control scores exactly 1 body / 0.000 m/s.
- At that altitude the hero free-falls at ~36 m/s, so the 300 ms kick windup moved the origin
  far below the targets (first run: kick `bodies=0`). Both measurements now start from a freshly
  reset position, so the only difference between them is the gesture's own windup.

### Not verified / limits (explicit)

- **No human has played either gesture.** Balance (2.5 s cooldown, 4.2 m dash, x1.5/x1.7, 4.6 m
  radius) and especially *feel* are design judgment; nothing here claims the flip reads correctly
  in motion. The measured hop (0.595 m) lands slightly before the sampled clip's feet touch, so a
  small visual mismatch is possible; the clip was never watched by a human.
- Visual/CameraQA of the two clips was not performed — verification covers state dispatch, timing
  alignment, physics and resources, not whether the animation looks right on screen.
- `Q`/RMB are wired through the real input loop but were not driven through a synthetic
  `Input` device; the verification calls `TryBackflip`/`TryHurricaneKick`, the same entry points
  the input lines call.
- **Blocker for a human: `feat/audio` has no commits of its own.** Its audio work exists only as
  uncommitted files in the shared working tree (`Assets/Audio/`, `Assets/Scripts/Audio/`,
  `Assets/Editor/AudioSetup.cs`, `AudioVerification.cs`, `Assets/Resources/AudioTuning.asset`,
  and a 2-line `BreakableProp.cs` edit) on top of this branch's base commit. Collision check per
  the packet: that edit is in the destructible-prop script, **not** the attack/movement/input
  script this packet touches, so work continued — but nothing was pushed, because the branch
  situation is unresolved. This branch is committed locally only; pushing still needs a human
  (same HTTPS credential failure as the Sep 20/21 entries).

## Content data and documentation accuracy pass — 2026-09-21 (earlier)

Content/docs packet (`feat/content-docs`). Data-only encounter content plus a README accuracy
pass. **No C# was written or changed**; no tuning asset owned by another packet was touched.
Nothing here raises any spawn or population count.

### Content added (all inside the existing encounter envelope)

Three new `EncounterDefinition` assets in `Assets/Resources/Encounters/`, plus `.meta` files.
They reuse only existing `CrimeKind` semantics and existing `ModeRules` — no new mechanics,
matching the count envelope of `bank`/`convoy`/`arson` (2–3 actors per list, 1–2 cars, 1–3
loot nodes, ≤2 hazards; total live objects per encounter does not exceed the shipping set):

- **`vault.asset` — Vault run** (Kind 1 Robbery): multi-loot heist — 3 robbers / 1 civilian /
  2 cops / 1 car / 5 crates / **3 loot** / 0 fire; 180 s deadline, robbers run at 35 s.
  Hero framing: one fast hostage, three stops. Villain framing: three-loot sweep, escape 20 m.
- **`siege.asset` — Cop siege** (Kind 0 Mugging): rescue-heavy standoff — 2 robbers /
  **3 civilians** / 3 cops / 1 car / 5 crates / 1 loot; 240 s deadline, slow runners (2.5 m/s),
  runners wait 55 s. Hero framing: three rescues against a big police presence. Villain
  framing: longest escape (24 m).
- **`blackout.asset` — Blackout blitz** (Kind 2 Fire): hazard sweep — 2 robbers / 2 civilians /
  2 cops / 1 car / 5 crates / 1 loot / **2 fire**; 180 s deadline, robbers run at 30 s,
  civilians endangered at 75 s. Hero framing: two simultaneous fires plus fast runners.
  Villain framing: sabotage-plus-escape.

Both shipping mode definitions (`Assets/Resources/Modes/hero.asset`, `villain.asset`) now
reference all six encounters in their `Encounters` pools. The three pre-existing encounters
remain first in pool order, so pool-cycling sequences and the first-spawn encounter are
unchanged. `free-play`/`endless-fight` (not playable) were not modified.

### Documentation

- **`README.md` rewritten** against the code as it exists today. Corrections: the build gate
  (see environment note below), an explicit Built-in Render Pipeline section (no URP/HDRP
  package, `m_CustomRenderPipeline: {fileID: 0}`, zero `.shader` files, Standard shader only —
  there is no pipeline asset or Renderer Feature to configure), the performance section now
  states the measured **~26–40 FPS Editor** range and that the quoted 155 FPS was a different,
  much simpler gray-box city (with the harness double-render caveat), every `Overpowered →`
  menu item re-checked against `Assets/Editor/` (added the existing
  `Create menu presentation data` item), mode-limits wording matched the data (SuccessGoal 5,
  3 failures/defeats, 900 s), encounter timing ranges stated as ranges, and the power-menu
  select keys described correctly. `Assets/Resources/Effects/` and `ModeRules/` asset paths
  verified to exist and are now listed.
- **`docs/architecture.md`** (new): per-system table of code/data ownership, flow, extension
  points, and the performance diagnosis summary. Every path in it was checked.
- **`docs/agent-scope.md`** (new): exclusive file-ownership map for the concurrent agent
  packets (content-docs / audio / performance), shared-source rules, append-only STATUS rule,
  and the conflict protocol.

### Verification (real output)

Ran per the repo's own instructions — an isolated project copy, editor closed, **without
`-quit`** (each verification exits itself; the packet's suggested command included `-quit`,
which contradicts the repo docs — noted as a correction):

```sh
Unity -batchmode -projectPath /tmp/op-content-verify -executeMethod ModeVerification.Run \
  -logFile .../Verification/Content/unity-run.log
```

**Exit code 0. 80 PASS / 0 FAIL** (`Verification/Content/mode-results.txt`, copied from the
run's `Verification/Modes/results.txt`; the raw log stays in `Verification/Content/` and is
excluded by the project's global `*.log` ignore rule). Controls all passed, including the
pre-existing-content ones this packet must not regress: the first populated event is still
`bank` (`3 robbers, 2 civilians, 2 responders, 8 props, 2 loot`), COMING SOON refusal,
population cap, failure/timeout/defeat limits, third data-only mode, and both shipping-mode
flows. All three new encounter GUIDs appear in the import log (confirmed loaded, not silently
dropped). Benchmark sample from the same run: `civilians=28, cops=12, events=2 … FPS=45.82,
p95=25.85ms` — within the run-to-run spread of previous samples, no population increase.

### Environment notes (checked, honestly)

- **`dotnet` is not on PATH** (previous entry's claim holds), **but** the Unity-bundled SDK at
  `…/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet` **does exist** and compiling with
  it by full path works today: `dotnet build Overpowered.Build.csproj --no-restore
  -p:UseSharedCompilation=false` → **0 errors, 16 pre-existing CS0618 warnings**. The previous
  entry's "cannot currently be run" is therefore stale in one respect: the command needs the
  full path (or `dotnet` restored to PATH), and the README now documents exactly that.
- **Batch-mode Unity on the main project path currently fails to reach compile**: every run
  stalls/exits at `ILPPTrigger: Can't find file /tmp/ilpp.sock-…` retries. Root cause traced:
  an ILPP runner (injected by `com.unity.ai.assistant`, added to the manifest on Sep 20) leaves
  `Library/ilpp.pid`; after any killed editor session subsequent runs loop on the dead socket.
  Clearing `Library/ilpp.pid` + `Library/Bee` did not clear it on the main path this session;
  the successful run above used a fresh copy. Also: three unrelated Unity editors have been
  running at ~100% CPU on other projects for days (load average ~8), which slows everything.
  Neither issue is caused by this packet's content; both are recorded so they are not
  rediscovered. The repo's own README already mandates running verifiers on a copy.

- `git push -u origin feat/content-docs` fails with the same credential error recorded in the
  2026-09-20 entry (`could not read Username for 'https://github.com'`) — HTTPS remote, no
  `gh` CLI, no SSH keys. **This branch's work is committed locally (`7dadc51`) and ready;
  pushing it requires a human.**

### Not verified / limits (explicit)

- **No human has playtested the new encounters.** Balance numbers (deadlines, runner speeds,
  loot counts, escape distances) are design judgment inside the existing envelope; nothing
  claims they are fun or correctly tuned. Hero-mode failure risk is real in `siege`
  (3 rescues, 240 s) and `blackout` (fast runners + fires) — unmeasured.
- The automated flow exercises the pool's first three spawns per session; later-cycle
  weighting of the six-encounter pool is not separately verified.
- The main-project ILPP failure above is diagnosed, not fixed — fixing it would mean touching
  tooling/library state outside this packet's scope. The successful verification was obtained
  on an isolated copy, which the repo docs already require.
- `siege`/`blackout`/`vault` have not been exercised individually end-to-end by the harness
  (it drives the shipping first-spawn encounter and completes events generically); they were
  loaded, pooled, and the full suite passed with them present.

## Performance diagnosis, menu completion and agent handoff — 2026-09-20

Planning/architecture pass. **No optimization was implemented.** This entry records a measured
diagnosis, the completion of the interrupted menu work, and two delegated work packets.

### State verification against this document

All 6 local commits through `42b62da` are real and consistent with what this file claims, and
all 64 previously referenced verification artifacts exist and are git-tracked. **No stale or
fabricated claim was found in STATUS.md.** Two corrections belong to the surrounding tooling,
not to this document:

- **`dotnet` is not installed on this machine.** The `dotnet build Overpowered.Build.csproj
  --no-restore -p:UseSharedCompilation=false` gate named in AGENTS.md and throughout this file
  **cannot currently be run** (no `dotnet`, `mono` or `msbuild` on PATH or in standard locations).
  `bin/`, `obj/` and `Verification/Humanoid/build.txt` show it worked on Sep 16 and is now gone.
  **Unity's own batch-mode compile is the build gate** until that is restored.
- **There is no outline Renderer Feature and no URP in this project.** A briefing to this pass
  asserted one existed and was toggleable, and asked for it as a performance control. Confirmed
  at runtime: `GraphicsSettings.currentRenderPipeline == null`, no URP package in
  `Packages/manifest.json`, no pipeline asset, zero custom shaders, `Shader.Find("Standard")`
  throughout. **That control could not be run because the thing does not exist.** It is recorded
  here so the claim does not resurface.

### Measured FPS diagnosis

New diagnostic harness, `Assets/Editor/PerformanceProfile.cs` + `Assets/Scripts/PerformanceProfileRunner.cs`.
Reverts every control; changes nothing permanently. Run with
`-executeMethod PerformanceProfile.Run`. Exit 0. Full output and the attribution table are in
`Verification/Performance/results.txt`; capture in `city.png` / `city-uncombined.png`.

```text
POPULATION: buildings=36 (234 renderers), props=247 (772 renderers), civilians=26, cops=10, animators=40, skinnedRenderers=80, propRigidbodies=211, totalRenderers=1140
SETTINGS: qualityLevel=5 'Ultra', shadows=All, pixelLightCount=4, lodBias=2, shadowDistance=150, GPU=AMD Radeon Pro 5300, CPU=Intel(R) Core(TM) i9-10910 CPU @ 3.60GHz
MATERIALS: palette materials=17, enableInstancing=true on 17 of them, shader=Standard
MESHES: 1060 MeshFilters reference 1021 DISTINCT sharedMeshes.
```

**The mesh-combining optimization is the primary cost, not a mitigation.** `CityArt.Combine`
merges per root and per material, producing 1,021 distinct meshes from 1,060 MeshFilters — a
~1:1 ratio, so static batching and GPU instancing cannot merge anything. `enableInstancing=true`
on all 17 palette materials was doing nothing. Disabling the existing `CombineMeshes` toggle and
rebuilding drops the city to **3 distinct shared primitive meshes**:

| Sample | draw calls | batches | FPS |
|---|---|---|---|
| 00 baseline, combining ON (as shipped) | 2,222 | 2,058 | 37.68 |
| 13 rebuild, combining OFF | **266** | **102** | **45.28** (+20.2%) |

**Draw calls are nevertheless not the bottleneck.** An 88% draw-call reduction buys only +20%,
while disabling prop *renderers* (control 02) removes fewer draw calls and gains **+47.3%**. The
dominant cost is per-renderer CPU work — culling, sorting and submission across ~1,140
renderers — which batching does not remove. Second load source: the shared humanoid mesh is
**28,374 vertices × 40 actors ≈ 1.13M of the 1.24M single-render vertices (91%)**.

**Hypotheses the data did NOT support.** The drift control re-measured baseline at **+7.9%**
against its own 8% tolerance, which sets the noise floor. At or below it, and therefore
**unmeasured rather than measured-as-zero**: shadows off +5.6%, `updateWhenOffscreen=false`
+6.3%, `AnimatorCullingMode.CullUpdateTransforms` +6.6%, NPC animators disabled +8.9%, prop
Rigidbody `Discrete` −0.2%. Shadows were predicted to roughly double cost and did not, at
`Ultra` with `shadowDistance=150`. The one modest confirmed win is `CityMaterials.LateUpdate`'s
unconditional per-frame `Apply()` at **+12.7%**.

**A measurement artifact affects every recorded number in this file.** All benchmark harnesses
set `camera.targetTexture` on a still-enabled camera *and* call `camera.Render()` in the sample
loop, rendering the scene **twice per sampled frame**. Removing it measures **+32.0%** (37.68 →
49.73 FPS). The 155 FPS baseline carried the same artifact, so the *regression* comparison
stands, but absolute throughput has been understated by roughly a third throughout.

Ranked by impact-to-effort for the next pass, against these numbers as the before-baseline:
(1) stop combining into unique meshes — share one mesh per prop kind so instancing applies;
(2) reduce live renderer count / cull distant props, which is where the +47% actually sits;
(3) reduce humanoid vertex count or add character LOD; (4) make `CityMaterials.Apply()` event-driven;
(5) fix the double-render in the harnesses so future numbers are real.

**Limits:** Editor Play Mode at 1280×720, fixed camera, player parked airborne. Not a
standalone-player or hands-on figure. City population is not static across the run (NPCs die
during controls), which contributes to the 7.9% drift; sub-10% effects need a tighter re-run to
resolve. Two `MissingReferenceException` crashes from destroyed NPCs were fixed by re-filtering
to live objects; **no control was weakened or removed** to obtain a pass.

### Menu presentation completed and verified

The interrupted home/results rebuild is finished. The only change was the backdrop capture in
`Assets/Resources/MenuPresentationTuning.asset`, reached over 4 rendered iterations:
`SkylineCamera (-65,22,-105) → (-22,7,-138)`, `SkylineLook (0,19,0) → (0,27,0)`, and
`SkylineFieldOfView 48 → 32`. The FOV change was load-bearing: at 48 the city subtends too small
an angle and reads as a low band of boxes regardless of camera position. No code was changed.

`MenuPresentationVerification.Run` exits **0** with **38 PASS / 0 FAIL**
(`Verification/Menus/results.txt`), covering both Hero and Villain Home → Play → Results → Home
flows, the disabled Coming Soon modes under both a submit-event and a flow-guard control, and
the real upgrade purchase routed through the existing `PlayerProgression.Buy`
(`Strength tier 0→1, points 1→0, force 1350→1890`) with a zero-point repeat-purchase rejection
control. UI throughput, A/B/B/A:

```text
MEASURED home static A: FPS=2152.17, mean=0.464ms, p95=0.559ms, drawCalls=2.0
MEASURED home animated B: FPS=906.77, mean=1.103ms, p95=1.284ms, drawCalls=4.0
MEASURED home animated B2: FPS=900.63, mean=1.110ms, p95=1.303ms, drawCalls=4.0
MEASURED home static A2: FPS=2261.26, mean=0.442ms, p95=0.515ms, drawCalls=2.0
MEASURED villain results animated: FPS=894.27, mean=1.118ms, p95=1.309ms, drawCalls=5.0
```

The repeats are tight, so motion's ~2.4× cost is a real effect, not drift. All eight captures
were visually inspected. The backdrop now genuinely reads as a **street-level city view** — no
building top faces, sky above the rooflines, road receding to a vanishing point. **Honest limit:
it is a low-rise street, not a dramatic high-rise skyline, because the source buildings are only
6–28m.** A towering skyline would require taller buildings in `CityLayout`, which was not changed.

Two defects found and recorded, not fixed:
- **Latent:** `MenuSkyline.Get` calls `art.Initialize()`, reassigning the global
  `CityMaterials.Current`, then destroys that root — whose `OnDestroy` destroys every palette
  material and nulls `Current`. Safe today because it only runs in Home/Results with no live
  city, but it would corrupt a live city's materials if a capture ever ran during gameplay.
- **Cosmetic:** on the results screen the "YOUR PROGRESS IS SAVED." label collides with the
  bottom edge of the XP panel.

### Delegation and repo state

Two standalone work packets are in `handoff/`, with non-overlapping exclusive file scopes:
`grok-audio.md` (branch `feat/audio` — the whole audio system, sourcing CC0 clips included; the
game currently has zero audio) and `glm-content-docs.md` (branch `feat/content-docs` — encounter
and mode-rule data plus a README accuracy pass). GLM's packet forbids touching
`CityArtSettings.asset` / `CityLayout.asset` or raising any spawn count, so new content cannot
fight the performance work above.

**The 6 outstanding commits were NOT pushed.** `git push origin main` fails with
`could not read Username for 'https://github.com': Device not configured` — HTTPS remote, no
`gh` CLI, no SSH keys, and the `osxkeychain` helper has no credential available to a
non-interactive shell. The commits are intact and ready; the push requires a human.

**No human playtest has been performed.** Nothing in this entry claims gameplay feel, audio
quality, encounter balance, or standalone-player performance.


## Shared humanoid / Mixamo presentation (previous milestone)

Prerequisites committed: camera repair **16fd426**, city art **a5f8278**. The capsule visuals are replaced by the supplied Mixamo Beta humanoid (`Idle.fbx` model, two skinned meshes / 28,374 vertices). Player, cops, civilians, criminals and the pursuing Hero instantiate the **same model and `Assets/Resources/SharedHumanoid.controller`**, not copied controllers. Surface/joint materials use the existing city palette (blue player, amber civilians, teal cops, red other NPCs, dark metal joints); no imported material instances or texture pipeline were introduced.

### Assets, configuration and ownership

- `Overpowered → Animation → Batch import Mixamo Humanoids` is the explicit one-time Editor importer for all 15 supplied FBXs. Every model uses Humanoid / Create From This Model; all avatars were checked valid/human. Locomotion clips loop; actions do not. Root transforms are baked and `Animator.applyRootMotion=false`. Bone optimization is disabled so procedural code can access the skeleton. The actual landing filename is **`Fall A Land To Run Forward.fbx`**, not the shorter name in the request. No extra animation downloads were assumed.
- `Overpowered → Animation → Build shared humanoid presentation` runs that importer and rebuilds the single controller in place, preserving its GUID and the tuning asset. This is an explicit authoring command, not an automatic callback that overwrites manual Animator edits. `Assets/Resources/HumanoidAnimationTuning.asset` contains clip/model/controller references and all new speed thresholds, playback rates, transitions, punch timing, flight poses and panic parameters. Thresholds remap actual damped velocity into a continuous blend coordinate, so Inspector changes take effect without rebuilding the controller. Backflip and Hurricane Kick have references and clearly named **Unwired** states, with no gameplay dispatch or transition into them.
- Animator owns the base Humanoid skeleton: Idle → Walking → Jog Forward → Standing Run Forward is a continuous 1D blend at 0 / 1.8 / 5 / 9 m/s. Negative local forward velocity selects Standing Run Back. The existing controller still turns toward travel; this does **not** introduce a new backward-strafe control mode. Cops use Pistol Run when moving and Shooting Gun on their existing attack event. Jump/Land/Hit/Death/Punch/Cast are action states dispatched by presentation events, with smooth returns to locomotion. Repeated actions restart their clips. Fire Blast and Ice set the same data flag (`CastingPresentation`) and trigger Casting Spell only after successful activation. Backflip/Kick remain intentionally unused.
- **Flight:** `HumanoidPresentation.LateUpdate` blends all mapped bone rotations toward the imported neutral pose and neutralizes hips translation, then aims upper/lower arms, straightens the legs and lifts the head. Hover has relaxed knees, arms slightly out and a 0.065m / 0.7Hz visual bob. Horizontal velocity blends to 78° body pitch, both arms extended overhead (forward in the flying pose), straight trailing legs and lifted head. Exponential blend rates govern entering/exiting flight and hover/forward changes. Animator supplies the fading base pose during transitions, but full flight is procedural, not a hidden flight clip. Active punch/cast/hit actions retain upper-body bone authority; flight still positions the visual root/lower body. Physics roots never receive these pose rotations.
- **Panic:** real civilian alarms select Standing Run Forward at 1.45× playback, add 24° visual forward lean, raise/flail both arms (28° oscillation at 2.7Hz), and turn the head periodically (72° overlay at 0.65Hz). The existing flee destination receives a perpendicular sine offset (up to 1.5m at 0.75Hz), then still passes through the existing NavMesh sampling/path logic. Civilian gameplay flee speed remains 5m/s. Arms/head yield to active action clips; dead NPCs fade out of panic. This is not a second AI/navigation system.
- **Landing overlap:** the existing `ProceduralHeroAnimation` remains attached in `HumanoidSquashOnly` mode and writes only a dedicated visual parent's scale. Its existing `ProceduralAnimationTuning.asset` remains the one authority for landing compression/recovery. The Land clip supplies the pose; old procedural punch, ground bob/lean and landing back-tilt are disabled for humanoids. A separate child owns new flight/ground/panic lean. Thus no transform is simultaneously written by the old procedural component and the new adapter. The old full procedural component remains usable independently; its capsule-hierarchy verifier is historical and superseded for the shipping humanoid by `HumanoidVerification`.

### Gameplay boundary / judgments

Reference-pose **actual skinned vertices**, not conservative animation bounds, are measured once at spawn: 1.7971m height, uniformly scaled 1.0016× to 1.8000m with feet aligned to the physics root. Player CharacterController remains **height 1.8m / radius .38m / center Y .9m**; NPC CapsuleCollider/NavMeshAgent remain **1.8m / .35m**. No limb colliders or ragdolls were added. Animated limbs and horizontal flight can extend outside the unchanged upright capsule; detailed per-limb environment collision is not claimed.

Punch timing necessarily gains a short authoritative windup, rather than moving force to a fragile Animator event: accepted activation still pays charges/energy and starts cooldown immediately; the controller schedules the existing `CombatImpact.Blast` after **125ms**, independent of whether a renderer is visible. This comes from source clip start 11/30s, impact 17/30s, playback 1.6×. Import sampling found the left hand's strongest extension near source frame 17. The evaluated Animator pose crossing that same source-time marker is recorded independently for verification. Death/session end cancels a pending impact; interruption by a nonlethal hit defers the hit pose until the punch marker, not the damage itself. Root motion, punch force/damage/range/charges/cooldown/recharge, flight fuel, Heat, progression, mode rules and movement speeds remain their existing systems. Timed tests now wait for the real windup before asserting physics results.

NPC death disables navigation/collision immediately, plays Death instead of rotating the entire root 90°, and keeps the body for at least the clip's 3.033s duration (rather than the old 1.5s removal). This is presentation lifetime, not delayed defeat/reward logic. Player damage/respawn events drive hit/death/recovery. Inspection found that Shooting Gun is a **left-handed side draw**, not a straight-ahead rifle pose: its useful firing segment is configured at source 1.25–2.5s, 1.5× playback, with a smoothly applied 90° visual-only yaw toward the NPC's forward target direction. These values are Inspector tunables. It presents the **existing contact-range cop damage action**; no new gun projectile, ranged combat, firearm model, muzzle flash or sound system is claimed. Casting does not lock movement or delay existing Fire/Ice effects. Clip playback defaults shorten lengthy hit/cast/shoot performances, not their gameplay cooldowns.

### Verification

`HumanoidVerification.Run` runs real Unity Play Mode in the isolated project, with isolated saves and assembly reload protection. It moves the real CharacterController over a speed range, feeds its measured velocity to the presentation adapter, exercises real paid punches / damage / powers / jump / fall / cop attacks, records evaluated clip weights and pose timing, captures rendered humanoids, and benchmarks the populated city. Flight visual checks deliberately feed controlled airborne states through the shipping overlay; fuel is tested separately through the real power resource path. These are **automated pose and gameplay controls**, not claimed keyboard/mouse human feel acceptance or a standalone player benchmark.

Final Humanoid Unity run **exited 0**. Real output (`Verification/Humanoid/results.txt`):

```text
BLEND requested=0.90 actual controller=0.900 animator Speed=0.899 m/s: Idle=0.501, Walking=0.499
BLEND requested=3.40 actual controller=3.400 animator Speed=3.398 m/s: Walking=0.501, Jog Forward=0.499
BLEND requested=7.00 actual controller=7.000 animator Speed=6.998 m/s: Jog Forward=0.501, Standing Run Forward=0.499
PUNCH source start=0.3667s impact=0.5667s playback=1.60; configured windup=125.00ms; visual marker t=6.16452 frame=522; real force t=6.16452 frame=522; offset=0.00ms/0 frames.
PASS Real force=1350 N·s, mass=45kg, velocity=17.192m/s, displacement=3.782m.
PASS Zero-charge CONTROL refuses punch.
PASS Undamaged NPC CONTROL plays neither hit nor death.
PASS Lethal damage triggers Death, disables navigation/collision; no root 90-degree flip.
FLIGHT hover weight=0.998 pitch=0.00 hand=(-0.355, 0.922, 0.130)
FLIGHT transition frame 4: blend=0.426, pitch=33.22
FLIGHT transition frame 12: blend=0.750, pitch=58.51
FLIGHT transition frame 59: blend=0.998, pitch=77.83
PASS Hover vs forward distinct: body difference=77.83deg, hand height 0.922->1.977m; maximum rendered-frame pitch change=8.60deg.
PASS Live fleeing civilian: Standing Run Forward at 1.45x, lean=23.99deg, raised/flailing hand Y=1.11..1.70m; head yaw=-87.7..51.6deg.
PASS Actual controller fall: 1 impact at 15.540m/s; Land clip played with single existing squash minY=0.786, recovered=1.000; collider=1.800m.
PASS SAME controller asset on player and all 33 civilians/cops/criminals.
MEASURED populated skinned/Animator city: civilians=26, cops=10, active animators=40; 1280x720, frames=199, seconds=5.002, FPS=39.78, p95=26.85ms, drawcalls median/max=2220/2222; previous city seed2409=50.25 FPS (-20.8%), historical155=(-74.3%); AMD Radeon Pro 5300.
```

The speed sweep also measured 0, 1.8, 5 and 9 m/s; intermediate blend weights were genuinely nonzero. Cooldown, windup/no-premature-force, zero-charge/no-extra-event and recharge controls passed. Flight fuel retained **6 → 4 → 0**, then **2.5 after 1s grounded**. Real Fire and Ice both played Cast; actual jumping played Jump. A live hostile cop reduced health **100 → 92**, moving cops played Pistol Run, and the sampled shooting hand was **(0.064, 1.469, 0.732)m** ahead of its local root after visual yaw alignment. Full Hero/Villain Home → Play → Results → Home, third-mode definition, failure/timeout/defeat controls passed (`mode-regression.txt`, exit 0). A separate Unity process restored level 7 / XP 327 / 5 points / 6 sessions / 3 wins, while its fresh-save control started level 1 / XP 0 / 0 points / 0 sessions (`mode-reload.txt`, exit 0).

Rendered images were actually inspected: `idle-model.png`, `hover.png`, `forward-flight.png`, `normal-jog.png`, `panic-20.png`, `panic-60.png`, `punch-impact.png`, `death.png`, `cop-shoot.png`, `populated-city.png`. Hover is upright with relaxed knees and lowered/outward arms; forward flight is nearly horizontal with extended arms and straight trailing legs. Panic has visibly raised/flailing arms and a deeper lean than the normal jog control. Smoothness is measured over **uncaptured live frames**, since synchronous screenshot readback stalls the editor and would contaminate frame-step timing; the first capture-instrumented run is retained, not presented as smooth-frame evidence.

**Performance regressed:** final 39.78 FPS is **20.8% below 50.25 FPS**, and 74.3% below the historical gray-city 155 FPS. Earlier runs measured **33.48 and 33.39 FPS** (`initial-pass.txt`, `second-pass.txt`), so observed Editor throughput is about **33–40 FPS**, not a promised stable 40. All 40 Animators evaluate, including off-screen characters; no actor/detail/animation culling or LOD cut was made to improve the number. Same 1280×720 actual-camera setup and Radeon Pro 5300 as the city pass; shared-machine load and evolving visual pose vary between runs. No standalone performance or hands-on feel certification is claimed.

The **separate existing city/power stress regression also passed** (`city-regression.txt`, exit 0): naturally exhausting three punch charges, cooldown/recharge, seventh-definition projectile physics, Telekinesis grab/hurl, Ice freeze/expiry, destruction raising Heat, cops **4 → 10**, pursuing Hero, civilian panic, Heat decay, side objectives and XP all remain functional. Its later legacy-sandbox performance sample was **26.41 FPS / p95 51.10ms** with 24 civilians and 10 cops. This is lower than the fresh mode benchmark above and **83.0% below that harness's historical 155.01 FPS**; it is retained, not excluded from the reported performance envelope. Across these different test workloads the measured range is therefore **26–40 FPS**. That legacy harness does not collect draw calls; the fresh populated-mode sample records 2,220 median / 2,222 max. This is an unresolved performance limitation.

That power regression's own separate-process reload also passed (`city-reload.txt`, exit 0): level 5 / XP 155 / 1 point / Villain, Strength tier 1 (1,890 N·s), Telekinesis unlocked; a fresh save again started level 1 / 0 points.

Final targeted `HumanoidVerification.DeathControl` also exited 0 (`death-interruption.txt`): dying during a paid punch's windup cancels the force and plays Death; actual respawn clears pending presentation state, and subsequent real damage immediately plays Hit again. This prevents a canceled punch from leaving future hit reactions deferred forever.

`dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` passes **0 warnings / 0 errors** (`build.txt`). An initial punch timing check correctly failed at 135.46ms / 11 frames because Unity's fixed-time start offset scaled with state speed; switching to a normalized clip offset corrected it (`initial-timing-failure.txt` retained). This is a measured marker/force alignment, not a claim of sub-frame GPU/physics simultaneity. The pre-existing Editor Search indexing exception remains unrelated to gameplay. Legacy capsule tests are not claimed as current-model verification; current ownership and limits are explicit above.

## Stylized city art pass (previous milestone)

Camera prerequisite was completed and committed first as **16fd426**. This pass keeps that scene-camera flow, the existing `CityLayout` seed/placement algorithm, movement/abilities, mode rules and progression.

### Added

- **Four coherent building archetypes:** brick warehouses, sandstone terraces, teal offices and slate towers. They vary footprint proportions and upper-storey setbacks inside the existing building slots. Original seeded heights and the three taller landmarks remain authoritative. Floors have geometric trim/banding, repeated dark/amber windows, actual recessed ground-floor entrance pockets, shop canopies and abstract sign glyphs. No building interiors or new layout algorithm.
- **Rooftop traversal layer:** every building has a roof deck, low physical parapets, HVAC unit, vent and roof-access structure. Seeded rules add water tanks on legs, antennae and occasional billboards. The center stays available for landing and existing rooftop XP discoveries; real collision geometry supports the decks/terraces. Roof equipment is physical and mostly destructible; structural decks/parapets/access structures are not. Rooftop access doors are visible but do not open.
- **Street level:** cream curbs, zebra crossings and amber lane dashes; matched lamps with shape-based street signs, benches, trash cans, hydrants, bus shelters, newspaper boxes, blocky planted shrubs and parked cars with cabins, wheels, lights and bumpers. Crime-event throwable cars reuse the same car model; crates/barrels remain simple matching wooden primitives. No real-word signage, traffic/bus simulation or texture assets.
- **One material palette:** `Assets/Resources/CityPalette.asset` is the sole source for generated material colors. A named-swatch Inspector exposes Road, Pavement, Cream, Brick, Sand, Teal, Slate, Roof, Glass, Amber, Metal, Wood, Leaf, Red, Blue, Cyan and Fire. The palette uses warm masonry, cool teal/navy, cream trim, dark roads and amber accents. Materials are shared and updated live when a swatch changes; no per-object material instances. The existing player/NPCs, crime markers, projectile visuals and debris also use these slots so the new city does not clash with the old prototype. Projectile definitions expose a palette slot for future data-driven color selection. Existing legacy color fields remain serialized but no longer control generated material colors.
- **Authoring data:** `Assets/Resources/CityArtSettings.asset` holds styles, facade/roof dimensions, prop sizes/masses/destructibility, normalized placement anchors, probabilities, jitter and mesh combining. Rules use a separate deterministic PRNG derived from the existing city seed; they do not perturb `CityLayout`. `Overpowered → Bake current art placements to editable data` records kind, base position, yaw and rooftop flag, then enables authored placement mode. Those records override generation, so manual edits survive subsequent runs. Roof placement receives a 0.1m starting clearance over its recorded base. If building heights/layout change after baking, re-bake or move those anchors yourself.
- **Destruction reused:** every new destructible prop has one Rigidbody and the existing `BreakableProp`. Compound visual parts are children of one proxy BoxCollider; the old hit/health/force/shard path handles breaking. Shards inherit the parent palette material. No second destruction/health/reward system. Structural building geometry remains indestructible.

### Rendering decisions, limits and explicit omissions

Low-poly primitive geometry with opaque, low-smoothness Standard materials, not realism. Window/sign detail is geometry, not texture maps. Meshes are combined per building/material and per prop/material **from the outset**; this is an Inspector option, not a post-benchmark content cut. Uncombined geometry can be inspected by disabling `Combine Meshes`. A ProBuilder conversion/export tool is **not implemented**; baked placement data is the supported hand-authoring boundary. Facade composition and prop silhouette ratios are code-defined recipes; placement rules, scale, mass, colors and building proportions are data.

Furniture uses simple bounding-box collision, including bus shelters (not walk-in interiors) and water tanks. Lamp heads/windows use palette colors, not extra point lights or a night-light simulation. No LOD/culling authoring, occlusion bake, art-quality lighting replacement, standalone build benchmark, or human traversal/feel acceptance is claimed. The existing directional sun (1.2 intensity, 45/-35 orientation), ambient color and Built-in rendering pipeline remain unchanged. There was **no active post-processing Volume** to preserve; none was introduced. Graphics/Quality project settings were not edited.

**Performance regression is significant.** Initial measurements were 41.91 / 52.04 FPS for the two seeds, with 2,046 / 2,080 draw calls, versus the recorded 155 FPS baseline. That first harness used an extra capture camera; its complete output is preserved in `Verification/Art/initial-two-camera-run.txt`. A matched-camera repeat (the existing camera, follow temporarily disabled, as in the historical harness) measured 36.25 / 47.12 FPS and 2,054 / 2,088 draw calls (`first-matched-camera-run.txt`). No detail was removed or runtime performance tuning applied in response. Final shipping-content measurements follow below. This remains a substantial performance cost to address in a dedicated profiling pass, not a claimed 155-FPS visual upgrade.

### Art verification

`CityArtVerification.Run` builds both seeds in real Hero sessions, renders street/roof/whole-city captures, tests shared palette changes/restoration, compares deterministic and authored placement controls, punches an actual generated newspaper box, checks lighting, and samples FPS plus Unity's actual draw-call/SetPass counters with civilians/cops active. Saves are isolated. Captures and raw outputs are under `Verification/Art/`. FPS uses a 1280×720 RenderTexture, wall-clock timings and the historical camera position (-65,60,-80), looking at the origin; the player is held airborne. This is Editor throughput, not a standalone-player or hands-on combat guarantee. The historical baseline had 24 civilians; these mode runs have 26 (including two encounter civilians) and 10 active/on-NavMesh cops. Hardware remains Intel i9-10910 / Radeon Pro 5300. Current and historical runs are not a controlled dedicated-machine GPU benchmark.

Final Unity run exited 0. Seed **2409** produced **247 new props (159 rooftop / 88 street)** and 9 of each building style. Seed **3226** produced **253 (163 rooftop / 90 street)**: 10 sandstone, 9 teal, 8 slate and 9 brick. The second seed had 9 rather than 7 bus stops, 27 rather than 24 antennae, and 13 rather than 12 billboards; heights, style assignments and street jitter also changed. Both had 36 HVAC units, 36 vents, 36 roof access structures, 15 water towers and 18 parked cars. Placement JSON for each seed is retained, with same-seed exact-repeat and authored-placement controls.

Selected **real output** (complete output: `Verification/Art/results.txt`):

```text
PASS Layout CONTROL: original CityLayout placements/heights/reward records are unchanged by art generation.
PASS ALL live renderers use shared materials from CityPalette; unregistered material count=0.
PASS Palette CONTROL: changed ONE Amber swatch RGBA(1.000, 0.700, 0.260, 1.000) -> RGBA(0.310, 0.860, 0.800, 1.000); 115 renderers sharing it updated (windows, lamps, vehicle lights, actors).
PASS Palette restore CONTROL returns all shared Amber surfaces to the original swatch.
PASS New newspaper box uses the EXISTING BreakableProp and one real Rigidbody, no parallel damage implementation.
PASS New prop physics: mass=45kg, punch=1350 N·s, displacement=8.978m, velocity=23.008m/s.
PASS Existing sun intensity=1.2, rotation=(45,-35,0), ambient=(.45,.5,.6) unchanged.
PASS Existing Built-in render pipeline retained; no active post-processing Volume was present/added.
MEASURED seed=2409: 1280x720 Editor actual renders, frames=252, seconds=5.015, FPS=50.25, p95=30.45ms, draw calls median/max=2074/2074, SetPass median=88; vs recorded 155 FPS=-67.6%; GPU=AMD Radeon Pro 5300.
MEASURED seed=3226: 1280x720 Editor actual renders, frames=268, seconds=5.011, FPS=53.48, p95=24.53ms, draw calls median/max=2108/2108, SetPass median=80; vs recorded 155 FPS=-65.5%; GPU=AMD Radeon Pro 5300.
PASS Camera repair retained through both seeded mode runs and return Home.
```

**Final performance is still 65.5–67.6% below the recorded baseline.** Both initial and matched-camera runs are preserved; the last run also includes the matching crime-event car models. No LOD/content cut was made. The force test relocates one generated newspaper box above a clear road to isolate it, then measures motion only after a real charged punch; final destruction goes through existing damage/shard code. Rendered street, rooftop and city images were visually inspected. `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` is clean: **0 warnings, 0 errors** (`Verification/Art/build.txt`). The editor's pre-existing Search indexing exception still appears independently of gameplay.

The full mode regression after the art changes passed (Unity exit 0): Home → Hero → Results → Home, then Villain → Results → Home, plus a definition-only third mode and failure/timeout/defeat controls (`Verification/Art/mode-regression.txt`). This pass did not run a standalone build or certify hands-on balance. The recorded high draw-call cost remains an explicit unresolved limitation, not a hidden cut.

An initial regression attempt was interrupted by a delayed editor assembly reload during Play, which cleared runtime singleton references and caused `ThirdPersonCamera.LateUpdate` null-reference errors. It was stopped and is **not** counted as a passing run (`Verification/Art/interrupted-regression.txt`). The camera now safely handles a temporarily unavailable world, and the mode verifier locks assembly reloads until test completion. Full live recompilation/restoration of the generated world is not supported by this change: restart Play after code recompilation. Unrelated animation assets added to the workspace during this task are not part of the art implementation or commit.

## Camera diagnosis and repair

The saved Home, Prototype and Results scenes had **zero GameObjects and zero Cameras**. The mode refactor relied on `GameFlow.SceneReady` to create menu cameras and `PrototypeBootstrap.BuildCity` to create the player camera at runtime. No disabled scene camera was found. The `Prototype` heading is a scene container, not a newly introduced parent GameObject; the saved scene contained no such parent. A Project search result is not evidence of a Camera instance in the loaded scene.

Read the actual open editor's `Logs/Editor.log`: it contains earlier `CS1061` errors (`WorldSession.Mode` / `SpawnEncounter` missing) from the incomplete refactor, followed by successful assembly reloads. The clean baseline renders Home and both gameplay modes successfully; **a runtime camera-factory failure was not reproduced**. The reproducible “No cameras rendering” condition is the camera-less edit-time scene. The baseline's error check also caught the existing `ArgumentOutOfRangeException` in `UnityEditor.Search.SearchDatabase.EnumerateAll`, not in camera/gameplay code. The verifier records this specific editor exception separately, without hiding other errors. No claim is made that a disabled Project-search hit was repaired or that the Search package exception was fixed.

Repair: each of the three scenes now saves one enabled, top-level **Main Camera**, tagged MainCamera, with an AudioListener and `GameCamera` component. Runtime setup reuses that camera, explicitly restores Display 1/full viewport/enabled state, configures menus to clear their background, and attaches the existing third-person follow logic in Hero/Villain. It no longer needs to invent an invisible-to-the-editor camera from scratch. Newly created menu scenes also receive a camera. `Overpowered → Repair scene cameras` is an explicit repair utility, not an automatic scene rewrite. The city still generates only after Play/mode selection; a camera alone does not generate an edit-time city preview.

Verification uses `CameraVerification.Baseline`, `Verify`, and `DirectPrototype` in an isolated Unity project. The disabled-camera control checks that a disabled camera drops out of Unity's rendering camera list. Pixel captures render the **actual scene camera**, not an extra verification camera, at 640×360, then restore the Display target. Menu captures show the camera background, not IMGUI; batch rendering does not certify mouse interaction or Game View repaint cadence. The historical baseline output is retained even though its blanket error check failed on the unrelated Search exception.

Fixed flow and direct-Prototype startup both passed (exit 0): Home, Hero, Hero pause/results/Home, Villain, Villain pause/results/Home each had exactly one active enabled camera on Display 1. All captures overwrote **230,400 / 230,400** pixels; gameplay images contained many colors and the camera followed the current hero. Each saved scene now reports `roots=1, cameras=1, enabled=1` before Play. The disabled-camera controls passed. Full output and actual-camera captures are in `Verification/Cameras/`. `dotnet build` passed with **0 warnings, 0 errors**.

## Home and game-mode sessions (current)

Built a mode wrapper around the existing city, controller, five powers, progression/save, Heat/police, HUD and procedural presentation. No replacement controller, combat system, city generator or animation system was introduced.

- **Startup and flow:** `Home` is the first build scene. OVERPOWERED lists Hero and Villain as playable; Free Play and Endless Fight say COMING SOON and reject selection. Selecting a definition loads `Prototype`; a completed/failed/timed-out session loads `Results`, with score, XP earned, outcomes and counts. Results returns to Home. Starting Play directly in Prototype also redirects to Home unless an isolated legacy verification explicitly requests the sandbox.
- **Mode architecture:** `GameModeDefinition` assets supply side, reusable `ModeRules`, encounter pool, spawn cadence/population, win/fail/defeat/time limits, rewards and HUD flags. The menu discovers assets; no mode-ID registry/switch was added. `GameModeSession` owns the session lifecycle; the existing Hero/Villain crime entry points route into `CrimeEncounter` and the selected rules. A new mode can reuse rules or provide its own rule subclass/asset without editing abilities, city, save or menu code.
- **Hero:** neutral police pursue/suppress robbers. Stop every robber by capture (held R) or combat, physically displace/destroy blockades, hold R to rescue cyan civilians, and extinguish fire nodes in arson events. Resolving just the marker or one actor is insufficient. Civilian death or any escaped robber fails the event.
- **Villain:** the same city and encounter population; cops attack the player through existing Heat AI. Steal gold loot, wreck at least three encounter props, interact with sabotage/fire nodes where present, then escape beyond 22m. Robbers are allies. Destruction earns existing XP and Heat; Heat-driven reinforcements and the pursuing Hero remain intact. Vertical flight escape is intentionally allowed.
- **Set-pieces:** bank break-out, street ambush and arson definitions. Each spawns three robbers with different exit routes, two endangered civilians, two responding cops, two throwable cars, four supply crates, two rescue blockades and two loot nodes. Arson adds two fire nodes. Cars/crates use real Rigidbodies and the existing force/damage/shard system. Encounters appear at separated street intersections; actors use the existing NavMesh. Scenario objects are removed when the event ends or the city unloads; temporary physical shards retain their existing lifetime.
- **Feedback/UI:** live objective sub-counts, distances/bearings, world markers, deadlines, success/failure feedback, session score/XP/counts. Escape is a true pause with resume, results or return-home actions; Tab remains the live power-upgrade menu. H cannot change sides mid-session. Mode HUD flags control health, powers, progression, Heat and objectives.
- **Save extension:** earned XP and purchased powers remain shared across modes. Added session/win counts, best score, last mode and last-session XP to the existing atomic JSON save. Old version-1 files remain compatible with zero-initialized session fields. Rewards save immediately; session outcomes save once. Transient city state, health, Heat, resources and active encounters still reset between sessions.

### Session choices and consequences

Both shipping modes are **objective-count based: five completed encounters wins**, not an enforced 10–15-minute survival session. A **900s / 15-minute cap** produces timeout results. **Three failed encounters or three player defeats loses**; the first two defeats use the existing respawn behavior. These limits can be disabled with zero or edited per mode in the Inspector.

One event starts immediately; another can spawn every **50s**, up to **two active**. Each has a **210s deadline**. Robbers scatter locally, then try their city exits after **45s**. Unrescued civilians begin taking **1 damage/s after 90s**. Hero failure on escape/death/timeout adds **0.75 Heat**, removes **25 score** (floor zero), and counts toward the loss limit; it does not remove earned XP. Success grants **100 score / 60 XP**, plus existing combat rewards; civilian rescue gives **20 score**. Hero completion lowers Heat by **1**. Villain completion adds **1 Heat**, wrecked encounter props give **5 score** each, and existing destruction XP is retained. Villain deadline failure also adds 0.75 Heat and counts against the session. Physics collateral can kill a civilian: rescue requires care with blast direction, not indiscriminate area attacks.

All of those numbers and population counts live in the mode/encounter assets. Powers, props, movement and animation retain their existing authoritative tuning assets.

### Explicit cuts / prototype limits

- **Not implemented by design:** Free Play and Endless Fight gameplay. Their disabled catalog definitions are present; no fake playable buttons.
- **No art pass:** primitive actors/cars/crates and basic IMGUI screens. Bank/ambush share the common encounter choreography; they are not authored bank interiors or vehicle-driving missions. Arson has extra interactive fire nodes, not a spreading-fire simulation. Civilians are immobilized by a gameplay blockade condition until rescued, not physically pinned/ragdolled. Cop support uses navigation/suppression/damage, not firearms.
- **Not claimed:** human keyboard/mouse feel acceptance, a 15-minute hands-on balance run, native-resolution player-build FPS, or a standalone distribution build. Automated completion controls position the player and supply hold/damage inputs directly to gameplay methods; they do not prove navigation/input ergonomics or difficulty. The physics rescue check uses an actual charged punch.
- UI button callbacks and real scene transitions are exercised via the same flow methods in batch Play Mode, not automated mouse clicks. World rendering is captured separately; no batch screenshot of IMGUI menus is claimed.
- The original expansion notes below are historical. Their H-switch, single-step crimes, repeating Villain chaos goal, and non-pausing Escape behavior are superseded by these modes. Legacy sandbox paths remain only for isolated regression checks.

### Mode verification

Reproducible checks: `ModeVerification.Run` followed by `ModeVerification.Reload` in a **separate Unity process**, using a temporary project copy and isolated save paths. The first run temporarily creates a third definition reusing Hero rules/encounters, with a one-encounter goal; the runtime discovers it without new system code. The definition is removed after testing; a copy is retained as evidence under `Verification/Modes/`.

Actual final Unity Play Mode output (`Verification/Modes/results.txt`; process exit 0):

```text
PASS Startup HOME, no city or player spawned.
PASS free-play COMING SOON CONTROL refuses launch.
PASS endless-fight COMING SOON CONTROL refuses launch.
PASS Populated event: 3 robbers, 2 trapped civilians, 2 responders, 8 real Rigidbody props (2 cars, 4 supplies, 2 blockades), 2 loot nodes.
PASS Untouched marker CONTROL does not resolve encounter.
PASS Blocked civilian CONTROL refuses rescue before blockade is moved/broken.
PASS Rescue physics: actual punch AddExplosionForce(1350 N·s, Impulse), mass=45kg; displacement=10.000m.
PASS Hero police CONTROL: neutral cop in attack range does not damage player.
PASS Pause freezes session clock and encounter simulation.
PASS Ignored Hero event: failures=1, Heat 1.75 -> 2.50 (+0.75).
PASS Hero PLAY -> RESULTS: Won, success=5, failure=1, score=700, XP=825.
PASS Hero RESULTS -> HOME; city unloaded.
PASS Hero/Villain regenerated the SAME seeded city layout.
PASS Mode-switch persistence: level=5, XP=162, points=4, wins=1.
PASS Earned points purchase strength upgrade; tier=1 must survive restart.
PASS Villain live cop AI attacked: health 100 -> 76.
PASS Existing Heat escalation retained: cops 4 -> 10 at Heat 3.00.
PASS Configured spawn-clock interval adds second multi-part encounter.
PASS Population-cap CONTROL refuses a third simultaneous encounter.
PASS Villain escape CONTROL: loot/destruction alone cannot resolve while inside scene.
PASS Villain PLAY -> RESULTS: Won, success=5, score=700, XP=780.
PASS Villain RESULTS -> HOME; city unloaded.
PASS Third mode discovered/selected from a definition ONLY; same rule and encounter assets, goal=1, no registry changes.
PASS Data-only third mode loaded, ran, and won at its configured single-encounter goal.
PASS Failure-limit CONTROL: third failed encounter produces LOST results.
PASS Timeout CONTROL: configured 900 seconds produces TIMED OUT results.
PASS Defeat CONTROL: two respawns allowed; actual third player death produces LOST results.
MEASURED populated events: civilians=28, cops=12, events=2; 1280x720 rendered Editor Play Mode; frames=1237, seconds=5.007, FPS=247.08, p95=6.48ms; Intel(R) Core(TM) i9-10910 CPU @ 3.60GHz; AMD Radeon Pro 5300.
```

The FPS sample renders a dedicated top-down 1280×720 camera every sampled frame while live world/NavMesh/NPC updates continue, with two active encounters, six robbers, 28 civilians and 12 cops. The player is held airborne for test safety. It measures wall-clock Editor throughput, **not a standalone or hands-on combat FPS guarantee**. The captured frame is `Verification/Modes/populated-event.png`. Police neutrality is tested away from hostile robbers to avoid attributing their attacks to the cop. Timers/interaction durations are advanced through runtime methods; the test does not wait 15 real minutes. `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` passes with **0 warnings / 0 errors** (`Verification/Modes/build.txt`). Unity's existing `UnityEditor.Search` startup indexing exception appeared but did not prevent the completed tests.

Separate-process reload (`Verification/Modes/reload.txt`; exit 0):

```text
PASS SECOND UNITY PROCESS exact reload: level=7, XP=327, points=5, sessions=6, wins=3, best score=700; all power tiers/rooftops retained.
PASS Fresh-save CONTROL in second process: level=1, XP=0, points=0, sessions=0.
```

The saved Strength upgrade was tier 1; the comparison covers the full progression JSON, not just the printed fields. The three wins are Hero, Villain and the temporary third mode; the other three outcomes cover failed encounters, timeout and defeats.

Existing city/power and procedural-animation regressions also exited 0 after this change. Their current output is retained separately as `Verification/Modes/city-regression.txt` and `animation-regression.txt`, without overwriting the earlier milestone's evidence. The city regression rechecked flight draining **6 → 4 → 0**, grounded recharge, normal punch/cooldown/zero-charge/recharge controls, seventh-power physics, Heat/police, Telekinesis, Ice and progression. The 20 animation checks include the actual controller fall and recovery. Those tests deliberately use the legacy isolated sandbox; the mode suite above independently exercises the new shipping flow.

## City and progression expansion (previous milestone)

Implemented all six systems from the attached expansion brief, extending the existing controller and retaining procedural animation:

- **City:** seeded 3×3 blocks, 36 solid buildings with varied heights and three tall landmarks, sidewalks/roads, five one-time rooftop XP discoveries, and crates, barrels, benches, trash cans, streetlights, and parked cars with Rigidbody physics and breakable shards. Buildings do not break. `CityLayout` can capture generated placements into editable data for later authored/ProBuilder layout work.
- **Powers:** Flight and Super Strength now use Inspector-editable PowerDefinition assets and the generic charge/cooldown/upgrade pipeline. Added Telekinesis (spring-force hold + impulse hurl), Fire Blast (colliding explosive projectile), and Ice (timed NPC/rigidbody freeze). Resources, force, damage, range, duration, costs, and upgrade tiers are data. Reusable effect assets dispatch behavior without a central power switch or hardcoded catalog. Existing gameplay constants remain in their original file as defaults.
- **Progression:** deterministic XP thresholds grant spendable points; powers are never randomly granted or automatically upgraded. Enemy defeats, appropriate crime/chaos actions, and rooftop exploration award XP. JSON saves persist XP, level, points, side, unlocks/tiers, and rooftop claims. Atomic replacement and a backup are used because explicit versioned JSON is inspectable and expandable; normal saves are separate from verification saves.
- **Sides/objectives:** H switches the same player between Hero and Villain with a 10s default cooldown. Mugging, robbery, and fire events spawn with world markers. Nearby interaction or defeating criminals resolves Hero crimes; Villains can assist events, destroy props for repeating chaos objectives, and gain XP from cops/civilians. Cops are neutral to Heroes and hostile to Villains. Switching retains powers and upgrades.
- **Heat/AI:** 0–5-star Heat increases from property destruction, assaults, and hostile actions; it decays after a quiet delay. Additional and tougher cops spawn by tier, with a pursuing Hero NPC at high Heat. Runtime-built Unity NavMesh supports wandering/fleeing civilians and enemies that pathfind, attack, receive damage/freeze, and die. Player death respawns with progression intact.
- **HUD:** health, energy, selected power charges/cooldown, flight fuel, XP bar/level, unspent points, Heat stars, side, event markers, and scrolling power selection/unlock/upgrade buttons. Tab opens the menu; H changes side; R interacts; E punches; LMB uses the selected power. See README for all controls.

### Configuration and judgments

`GameTuning.asset` groups each system's settings in the Inspector. Each power's authoritative values and upgrade tiers live in its own asset. Animation settings stay separate. The five named shipping powers start with Flight and Strength unlocked. The brief's “seventh power” check uses two temporary definitions reusing the projectile effect, not extra shipping abilities.

This is a primitive blockout, not an art pass: parked cars and benches have simple box silhouettes, streetlights are physics poles, and NPCs are colored capsules. Crime scenarios use timed/contextual interactions and criminal NPCs rather than cinematics or detailed rescue simulations. Fire interaction takes 2s; it is not a propagating fire simulation. Enemies use ground NavMesh paths and cannot fly after a player above rooftops; escaping vertically is possible. Police are neutral to the Hero even when property damage raises Heat. Layout generation happens at Play startup; restart Play after changing the seed. Menus do not pause combat.

Transient health, Heat, charges, destroyed objects, and active events are intentionally not saved. There is no structural building destruction. Human keyboard/mouse playtesting and a standalone-player performance benchmark remain unperformed. No required system is left as an architecture-only stub; the limitations above describe the implemented prototype depth.

### Expansion verification

Actual Unity Play Mode output, separate-process reload results, a data-only seventh-power artifact, and the rendered district capture are recorded under `Verification/City/`. Verification ran in a project copy with isolated save files. `dotnet build Overpowered.Build.csproj` completed with **0 warnings, 0 errors**. Unity gameplay, separate-process reload, and animation regression all exited successfully. The editor still reports its pre-existing Search indexing exception at startup; it did not prevent these tests from running.

Measured output from the final run:

```text
PASS Seventh power discovered from DATA only; exact same projectile effect asset as Fire Blast.
PASS Seventh projectile hit real Rigidbody: impulse setting=200 N·s; displacement=12.992m; velocity=32.672m/s.
PASS Unspent CONTROL: level=2, points=1, force unchanged=1350.
PASS Upgraded strength: force=1890, max charges=4, points=0.
PASS Zero-point upgrade CONTROL rejected.
PASS Real prop destruction: Heat=3.50; police 4 -> 10; high-tier max HP=137.
PASS High Heat spawns one pursuing Hero NPC.
PASS Lay-low decay: 3.50 -> 2.80 after configured delay plus 10s.
PASS Hero fire outcome: Heat delta=-1.00, XP awarded=60.
PASS Same fire event as Villain: Heat delta=1.00; opposite Hero outcome; same character=Overpowered Hero.
PASS Hostile cop attacked through live AI: player HP 100 -> 72.
PASS Defeating cop as Villain awards XP and disables dead NPC navigation.
PASS Separate Unity process restored exact save: level=5, XP=155, points=1, side=Villain.
PASS Separate process retained strength upgrade tier=1; force=1890
PASS Unlocked Telekinesis persisted.
PASS Fresh-save CONTROL: level=1, points=0.
MEASURED 1280x720 rendered Editor Play Mode: frames=776; seconds=5.006; FPS=155.01; p95 frame ms=10.33.
```

The benchmark had 24 civilians and 10 enabled, on-NavMesh cops, on an Intel i9-10910 / AMD Radeon Pro 5300. A 1280×720 RenderTexture was actually rendered each sampled frame; Stopwatch wall time measured throughput while world/AI updates ran. The camera viewed the populated district from above, with the player airborne; this is not a hands-on combat or standalone-player benchmark. The much lighter 4kg verification target reached 444.154m/s on a 1800 N·s telekinetic hurl; shipping prop masses are 45–400kg, so this test is a force-path check, not a claim that the test object's speed is balanced.

Other passed controls include real flight resource consumption (6 → 4 → 0s, then 2.5s after one second grounded), punch cooldown and empty-charge rejection/recovery, physical Telekinesis grab/hurl, timed Ice freeze/unfreeze, civilian fleeing, repeat-side-switch rejection, and a complete NavMesh route. The retained 20-check procedural-animation regression passed in the city: one actual 5m landing at 13.340m/s, minimum visual height scale 0.817, recovered scale 1.000, and unchanged 1.800m controller height.

## Original arena milestone (historical; superseded by city above)

- Third-person controller: WASD movement, Left Shift run, Space jump.
- Flight: hold `F` while airborne. Flight has 6.0 seconds of fuel and refills at 2.5 fuel/second while grounded.
- Super-strength punch: left mouse or `E`. It has 3 charges, a 0.45-second cooldown, and recovers a charge every 1.25 seconds.
- Tiny physics arena generated at play time with crates, barrels, and stacked objects. Punches use `Rigidbody.AddForce` / `AddExplosionForce`; props retain physics and can break apart from sufficiently hard impacts.
- HUD provides live numbers for fuel, charges, cooldown, force applied, and last punch outcome.

## Original arena verification controls (historical)

Enter Play Mode, then press `V` to run the built-in deterministic validation sequence. It prints concrete telemetry to the Console and shows it in the HUD: flight fuel is sampled at 6.000, 4.000, and 0.000 seconds; it then demonstrates ground recharge. The punch test records three successful charged punches, a rejected zero-charge punch, a rejected cooldown attempt, then a recovered successful punch. The force test prints the exact configured force (1350 N) and affected rigidbody count.

The verifier drives the same resource and punch methods used by gameplay; it is deliberately available in the running prototype so the values can be checked alongside the actual physics response.

The original V demo is no longer attached by the city bootstrap. Use the city verification described above; the old demo assigned some state directly and should not be read as independent measurement of all gameplay behavior.

## Procedural animation pass

- Added collider-free arms and contrasting fists to the existing capsule. A successful punch snaps the right shoulder forward and pitches/twists the body, then eases to rest. Rejected cooldown/empty-charge attempts do not animate.
- Moving flight pitches forward; hovering or ascending without horizontal travel levels out. Ground movement leans in the actual local travel direction, with a small distance-driven vertical bob that fades at rest.
- A landing event reports downward impact speed from the actual CharacterController movement. Falls above the minimum impact threshold produce a quick foot-pivot squash, slight widening, backward tilt, and recovery. Sub-skin-width resting contact transitions are filtered from the event; light impacts also do not trigger the animation.
- All animation timing, angle, intensity, speed thresholds, and blend rates are Inspector-editable in `Assets/Resources/ProceduralAnimationTuning.asset`. Select that asset in the Project window; edits take effect during Play Mode. Asset edits persist, so undo unwanted experiments. Defaults: punch attack/recovery 0.065/0.240s, arm rotation -100 degrees; flight lean 48 degrees; run lean 10 degrees and bob 0.045m; landing compression/recovery 0.055/0.240s and maximum squash 22%.
- Existing gameplay constants remain in `PrototypeTuning.cs`. Animation configuration is a ScriptableObject rather than C# constants because constants cannot be edited in the Inspector.
- `ProceduralHeroAnimation` only writes visual children. The controller publishes `PresentationState`, `PunchStarted`, and `Landed`; a future Animator adapter can subscribe to those same signals without changing ability logic. Disable/remove the procedural component when replacing it; disabling restores the resting pose. No animation clips or Animator controller were introduced.
- Scope: this pass implements the four requested placeholder animations. The attached broader city/powers/progression brief was treated as background, not implemented in this animation pass. Existing package edits are not part of this commit.

### Animation verification

`Assets/Editor/ProceduralAnimationVerification.cs` is a reproducible Unity batch Play Mode check. Run Unity with `-batchmode -projectPath <project> -executeMethod ProceduralAnimationVerification.Run -logFile <log>` (omit `-quit`; the check exits with its result). It samples controlled animation states, checks successful/rejected real punch calls, and then lets the real controller fall onto the arena. Output is written to `Verification/Animation/results.txt`; with graphics enabled it also renders six pose samples. This check is separate from the original `V` resource demo above.

Actual verification on Unity 6000.6.0f1, in a temporary project copy to preserve the open editor session: 20 checks passed, process exit 0. Recorded results and six inspected rendered poses are in `Verification/Animation/`.

- Successful punch: 100.000-degree arm rotation at 0.065s; recovered to rest. Cooldown and zero-charge controls emitted no additional punch event.
- Moving flight: 48.000-degree pitch; hover/vertical-ascent control: level. Diagonal run: pitch/roll +7.071/-7.071 degrees; bob over 120 steps: 0.0000–0.0450m; idle bob faded to zero.
- Controlled full-strength landing: scale (1.100, 0.780, 1.100), -13.000-degree backward tilt; weak-contact control remained unsquashed.
- Actual 5m controller fall: exactly one landing event, 13.117m/s impact, minimum visual Y scale 0.820, recovery to 1.000. Controller height remained 1.800m. All controlled poses preserved physics-root position, rotation, and scale. Disabling the component restored the resting pose.

Limits: deterministic pose samples and an actual automated fall were verified; no human keyboard/mouse feel session was performed. Unity compiled the scripts successfully, but the editor log included existing deprecated object-lookup warnings and a UnityEditor.Search startup indexing exception; these did not prevent the Play Mode checks from passing. No city/progression work or gameplay-resource re-verification is claimed by this pass.

## macOS Intel architecture investigation — 2026-09-25

### Findings

The reported Apple Silicon binary could not be reproduced in the Unity output recorded by this checkout. Unity's last two successful Build History entries say:

```text
Build 2026-09-26T01:59:54.4219670Z: Succeeded, StandaloneOSX, OutputPath=/Users/melaniehernquist/Documents/ChatGPT/op/overpowered.app, BuildProfilePath="", errors=0.
Build 2026-09-26T02:00:30.6208430Z: Succeeded, StandaloneOSX, OutputPath=/Users/melaniehernquist/Documents/ChatGPT/op/overpowered.app, BuildProfilePath="", errors=0.
```

These are not two different output locations: both target the same in-project `overpowered.app`. Their Unity `BuildLog.jsonl` entries explicitly report `Intel (x86_64) architecture support for macOS is deprecated and will be removed in a future version of Unity.` The second build ended at 22:00:37 local time; the executable at that exact output path has the matching 22:00:37 modification time.

Direct inspection of the executable named by that app's `Info.plist`:

```text
CFBundleExecutable: Overpowered Prototype
file overpowered.app/Contents/MacOS/Overpowered Prototype:
  Mach-O 64-bit executable x86_64
lipo -archs overpowered.app/Contents/MacOS/Overpowered Prototype:
  x86_64
```

So the app this Unity project just built is Intel 64-bit. Finder's reported Apple Silicon label does not describe this executable. Unity's build reports do not include the path Finder's Get Info panel was displaying, so I cannot identify a different app's origin from this evidence. The mismatch is between the Finder observation and the app path/output recorded by Unity; it is not an architecture failure in the produced executable. No architecture code/settings change was appropriate because the verified Build already produces Intel.

### Serialized settings and other build routes

- `ProjectSettings/ProjectSettings.asset` has `platformArchitecture: {}`; there is no serialized global macOS architecture override. Its current working-tree diff only changes the Standalone bundle identifier and WebGL texture compression. Those pre-existing user edits were preserved.
- There is no `Assets/Settings` directory or tracked BuildProfile asset in this checkout. Unity's generated local profiles are under `Library/BuildProfiles/`. The only macOS profile is `PlatformProfile.0d2129357eac403d8b359c2dcbf82502.asset`: `m_BuildTarget: 2`, type `OSXStandaloneBuildProfile`, `m_Architecture: 1`. Its output is independently confirmed as x86_64 above. The only other platform profile is WebGL (`m_BuildTarget: 20`, `WebGLPlatformSettings`); `SharedProfile.asset` is the shared settings object, not another macOS profile.
- Searched project C#, shell/command files, GitHub workflows and build-related docs for `BuildPipeline.BuildPlayer`, `BuildPlayerOptions`, `StandaloneOSX`, `OSXIntel` and `OSXARM64`: no custom macOS build automation exists. The reports identify normal `StandaloneOSX` Player builds.
- Build History's `BuildProfilePath` is empty on these two builds, while the generated macOS platform profile stores architecture 1. I do not infer a profile asset path from that blank report field; the decisive architecture evidence is the Unity Intel warning plus the executable's Mach-O slices.
- `Editor.log`'s `Architecture: x86_64` describes the Unity Editor host, not its player. It was not used as evidence for the app. The player architecture was checked with `file` and `lipo`.

Conclusion: no reproducible build defect remains in the confirmed Unity output, and no architecture fix was needed. If the Finder item was meant to be this build, its selected path/metadata conflicts with the verified binary. If it was a different output, that app was not one of the paths in Unity's two latest build reports. `.DS_Store`, the local backup folder and the existing ProjectSettings edits were left untouched. No commit was made for this diagnostic pass.

## Toggleable first-person camera — 2026-09-24 (appended)

### Scope and delivery

Implemented on `codex/first-person` in `/private/tmp/op-first-person`, based on merged main `aea8bd0` (Hero Forge, expanded modes, combat, HUD Phase 1). Git was checked before edits: the Hero Forge merge was complete, but unrelated HUD Phase 2 changes were underway, including PlayerProgression. Those shared-checkout changes were left untouched. This feature is committed separately, not merged over the ongoing HUD work; that later integration is not claimed tested here.

- **B** toggles first-/third-person through the existing ThirdPersonCamera; third-person remains the default for fresh and old saves. B was unused (the historical V verifier still reserves V). Menu, pause, defeat and ended-session states reject the toggle. Existing third-person orbit/collision logic remains available.
- Stable capsule-relative **1.62m eye**, not animated head tracking: flight lean, landing squash and backflip bones do not drag/roll the view. The player's existing visual-root renderers become **ShadowsOnly**, leaving Animator, physics, shadows and NPCs intact. Original renderer modes and near clip restore on return/disable. No first-person arms or new body mesh.
- Eye sweep from capsule center prevents overhead clipping; its radius encloses near-plane corners, including aspect/FOV changes. The CharacterController still handles locomotion/environment collision. This does not promise recovery from arbitrary teleports into solid walls.
- **All camera tuning stays in GameTuning.asset > Camera**: toggle key, eye height, 0.03m near clip, 0.15m eye sphere, -85..85 degree pitch, +5 degree forward-flight FOV at 8m/s, 5/s response. Hover has no FOV boost. Synergy FOV kick is additive through the same camera writer rather than competing with flight. No movement/collider dimensions were changed.
- PowerUser uses the camera origin in first-person and retains shoulder-to-crosshair convergence in third-person. Fire/Ice/Telekinesis share the same viewport-center target. Trigger-only colliders no longer steal aiming. Fire caches aim BEFORE creating its projectile (avoids querying its own new collider), sweeps its radius along the muzzle offset, and refuses a cast from an obstructed origin before payment. This also fixes close-wall projectile spawning in third-person: the old 1.7m offset could skip a nearer wall. Damage, force, resources and loadout gates are unchanged.
- **ProgressSave.FirstPerson** is one additive boolean in the existing version-1 atomic JSON save. Toggle saves immediately through PlayerProgression; scene/startup loads use the normal profile. No PlayerPrefs or separate settings framework.

Files: camera integration in PrototypeBootstrap.cs; settings in GameTuning.cs / GameTuning.asset; preference in PlayerProgression.cs; aiming in PowerUser.cs / FireBlastEffect.cs; additive FOV handoff in Forge/SynergyRunner.cs. Added FirstPersonVerification.cs and FirstPersonVerificationRunner.cs (with metadata), docs/first-person.md and Verification/FirstPerson evidence. README, AGENTS and test-save ignore rules updated. No scene or manual Inspector setup required.

### Real verification and controls

Unity **6000.6.0f1 batch compile clean**, no C# warnings/errors in the final run. `FirstPersonVerification.Run` exited 0 with **194 PASS / 0 FAIL**; a separate-process `Reload` verifies the same final save with **5 PASS / 0 FAIL**. Supplemental dotnet build also has **0 warnings / 0 errors**. Existing camera flow regression: **45 PASS**, including Home, Hero/Villain, pause, Results, Home and disabled-camera controls. Existing city/power regression: **53 PASS** (including paid/empty-charge melee, real projectile/ice/telekinesis, progression and live AI). All exited 0. This is not a rerun of every historical suite.

Commands used against isolated project `/private/tmp/op-fp-verify-jVg9ok` (no -quit; each runner exits itself):

```sh
Unity -batchmode -projectPath /private/tmp/op-fp-verify-jVg9ok -executeMethod FirstPersonVerification.Run -logFile /private/tmp/op-fp-matched.log
Unity -batchmode -projectPath /private/tmp/op-fp-verify-jVg9ok -executeMethod FirstPersonVerification.Reload -logFile /private/tmp/op-fp-matched-reload.log
Unity -batchmode -projectPath /private/tmp/op-fp-verify-jVg9ok -executeMethod CameraVerification.Verify -logFile /private/tmp/op-fp-camera-regression.log
Unity -batchmode -projectPath /private/tmp/op-fp-verify-jVg9ok -executeMethod CityVerification.Run -logFile /private/tmp/op-fp-city-regression.log
```

Both modes exercised Fire+Ice and Fire+Telekinesis loadouts in BOTH views, using real resource-gated casts. Physics targets sit on the actual camera-center ray; an off-axis target is the negative control. First-person ray error **0.1051 pixels**, third-person **0.0066 pixels** at 1280x720. Fire's measured collision-contact error was **0.0846m first-person / 0.0745m third-person**, smaller than its 0.175m sphere radius. Ice freezes only the aimed body; Telekinesis holds exactly the aimed body. A wall inside the old muzzle offset gets the actual fire collision, and blocks Ice/Telekinesis from the target behind it.

Verbatim selected final output (complete output in Verification/FirstPerson/results.txt):

```text
PASS hero third -> first toggle.
PASS hero-first-fire aim error=0.1051px.
PASS hero-first-fire physical projectile hits crosshair target; off-axis CONTROL untouched.
MEASURED hero-first-fire contact error=0.0846m, target speed=0.548m/s.
PASS hero-first-ice aimed target frozen; off-axis CONTROL not frozen.
PASS hero-first-telekinesis aimed target held, not off-axis control.
PASS villain third -> first toggle.
PASS villain-first-fire aim error=0.1051px.
PASS villain-first-ice aimed target frozen; off-axis CONTROL not frozen.
PASS villain-first-telekinesis aimed target held, not off-axis control.
PASS First-person hides own body; Animator and shadows retained.
PASS Paused toggle CONTROL refused.
PASS Tight corner eye volume clear at pitch -85
PASS Tight corner eye volume clear at pitch 0
PASS Tight corner eye volume clear at pitch 85
PASS Low-overhang positive control compresses eye below requested 1.62m height.
PASS Compressed eye remains outside overhang/walls.
PASS Projectile spawn cannot skip wall inside original 1.7m muzzle offset.
PASS Close-wall projectile actually impacts intervening wall.
PASS Wall CONTROL blocks ice target behind it.
PASS Wall CONTROL blocks telekinesis target behind it.
PASS villain first -> third restores view.
PASS Body visible again after reverse toggle.
MEASURED forward flight FOV=69.997, additive=4.997 degrees; base=65.
PASS Actual forward motion drives bounded flight FOV.
PASS Synergy FOV adds to flight instead of fighting it.
PASS Non-flight recovery restores baseline FOV after flight/kick.
```

Flight verification moves the real CharacterController through MoveAbility while consuming flight fuel. Hover, forward and non-flight recovery exercise camera response; this is not a human F-key feel test or a separate measured landing impact.

The tight-space fixture has a close front wall, two side walls and low ceiling, inside the actual gameplay scene. Screenshots composite the existing HUD/crosshair and real gameplay camera. **Inspected** tight-space-first.png and tight-space-look-down.png: solid corner/ceiling/floor remain visible; no inside-head/body polygons or see-through wall gaps. The eye-volume checks additionally cover ±85 degree look and a deliberately lowered overhang. City-first.png / city-third.png show the populated city from each view; per-mode/power captures show the actual crosshair centered on the physical target. No assertion that every possible city crevice is glitch-free.

Separate-process output (Verification/FirstPerson/reload.txt):

```text
PASS SECOND PROCESS restores first-person preference in existing save.
PASS Existing loadout/progression preserved.
PASS Fresh save CONTROL defaults to third-person.
PASS Old save without camera field CONTROL keeps progression and defaults third-person.
PASS SECOND PROCESS gameplay camera actually starts first-person.
```

### FPS and limits

Final **matched world look angle** comparison: same city, player position (-20,8.12,-40), heading and world pitch 13.412 degrees; 26 civilians + 10 cops remain active. Player is held 8m above the spawn for benchmark safety; not a ground-level hands-on firefight. Existing HUD active, one enabled 1280x720 camera renders once per frame. Four five-second ABBA samples, after warmup:

```text
MEASURED third-person phase=0: FPS=59.80, draw calls=453.8.
MEASURED first-person phase=1: FPS=57.47, draw calls=433.7.
MEASURED first-person phase=2: FPS=54.64, draw calls=434.6.
MEASURED third-person phase=3: FPS=51.73, draw calls=459.0.
PASS Benchmark remained alive with populated city simulation running.
PAIRED same-scene means: third-person=55.77 FPS; first-person=56.06 FPS; delta=0.52%. Perspective naturally changes visible geometry; no content or population cut.
```

This is effectively within measurement noise, not proof of an optimization or a standalone FPS guarantee. Even this run drifted 59.80 -> 51.73 FPS between third-person phases. Earlier unmatched-view samples were more variable (36.67 third / 47.22 first; retained in initial-results.txt); they are not substituted for the matched comparison. No geometry/actor cuts, pipeline changes or per-instance meshes were introduced.

No hardware B-key/controller injection or human motion-sickness/feel session. No first-person hand animation. Console is **not** wholly error-free: UnityEditor.Search.SearchDatabase's pre-existing startup ArgumentOutOfRangeException remains; no game-code errors occurred in the successful runs. An initial test-only compile error attempted to set private Energy; the test now recharges through PowerUser.Tick and compiles cleanly. Tested feature scripts/assets were compared to the delivery checkout; unrelated shared HUD Phase 2 work is excluded.

## Gameplay Phase 1: synergies available on equip, long cooldowns — 2026-09-26 (appended; branch feat/gameplay-balance)

**Step 0 (merged base 66937b9, before any edit):** Feel 142, Camera 45, HUD P1 318, HeroForge 132 + Reload 3, City 53 + Reload 5
all exit 0. FirstPerson.Run FAILED at "hero-third-fire contact within projectile radius tolerance" (0.3083 m); its Reload failed
as a consequence. Root cause = **test assumption, not a merge bug**: the fixture put targets 9 m from the CAMERA; the feel camera
(0, 2.8, −7.6 / look 2.5) now puts that point 1.25 m in front of the hero, inside Fire's 1.7 m muzzle offset. Targets now sit
9 m of reach beyond the hero (the `near` rule AimDirection uses; first person unchanged): contact error 0.0951 m third /
0.0846 m first; Run 210 PASS, Reload 5 PASS (commit d0f5ae8). **FP eye vs Feel impulse:** the real Feel hooks (heavy impact from
4 sides and from below, heavy incoming hit), sampled at 1 ms over the whole impulse at pitch −85/0/85 in the tight corner and the
low overhang: 0 of 3,960 samples put the 0.044 m near-plane corner sphere into geometry; closest 0.127 m (low overhang, +0.123 m
upward swing). No Feel change needed. One rerun was lost to macOS sleep (pmset: sleep 02:10 → 04:03; the runner's 600 s deadline
fired on wake); runs now use `caffeinate -dims`.

**Finding (verified):** synergies never had an unlock step; `SynergyRunner` fires whenever the resolved pair is equipped. The
"UNLOCK … / 1 POINT" button was POWER OWNERSHIP (Fire/Ice/Telekinesis had `InitiallyUnlocked: 0`), and the Forge slots list only
owned powers, so the pair (and its synergy) was point-gated.

**Change (data first):** `InitiallyUnlocked: 1` on fire/ice/telekinesis (and `ProjectDataSetup` defaults). The loader already
grants InitiallyUnlocked powers to old saves. PowerUser/PlayerProgression equip + ownership gates are unchanged; `UnlockCost`
stays as unused legacy data; `Buy` still exists and now only buys TIERS for owned powers. UI: Forge UNLOCK buttons removed and a
status line added ("READY WHEN EQUIPPED · 30 S COOLDOWN · C TO USE", key from ForgeCatalog); Tab menu shows "Not owned" instead of
an Unlock purchase; Results upgrade picks list owned powers only, equipped pair first ("UPGRADE …"). HUD synergy radial reads
`runner.Cooldown / Synergy.Cooldown` and needed no change. `HeroForgeSetup` creation defaults carry the same cooldowns.

**Cooldowns — the balancing lever** (normal powers: 0.45–0.6 s + charges/energy, ~30 sustained DPS; "potential" = damage × up to
4 targets in radius, from the effect code):

| Synergy | Damage | Radius | Force | Duration / control | Old | New | Reasoning |
|---|---|---|---|---|---|---|---|
| Frostwake | 12 per pass | 3 m along a ~28 m dash | 2600 | 1.1 s dash, 2 s freeze | 10 | 25 | low damage (~48), CC + escape; also burns flight fuel |
| Glacier Fist | ×1.65 punch force, +0 dmg | punch 3.3 m | 2600 | 6 s buff, 1.5 s freeze per punch | 10 | 30 | ~6 punches of AoE freeze in the window; still pays charges |
| Orbit Throw | 35 per prop | ≤4 props in 9 m | 1800 | 2 s orbit | 10 | 30 | ≤140, needs props nearby |
| Cryo Crush | 35 slam | 4 m | 2000 | 2 s freeze + 1.1 s lift | 10 | 30 | one enemy removed + small AoE (≤140) |
| Thermal Shock | 25 (+17.5 on frozen/burning) | 4 m | 1500 | 0.6 s freeze | 10 | 30 | ranged, instant; 100–170 |
| Sonic Slam | 40 | 7 m | 2600 | self dive, NPC displacement | 10 | 35 | clears the whole 4.5 m wait ring (160+) |
| Meteor Slam | 50 | 7 m | 2800 | 1.2 s lift | 10 | 35 | 200 potential, needs a target |
| Phoenix Dive | 55 | 7 m | 1800 | 4 s burn (enables Thermal bonus) | 10 | 40 | 220 at a chosen spot 24 m away |
| Meteor Punch | 75 | 4 m | 3400 | 4 s burn | 10 | 40 | highest single hit (300 potential), reliable |
| Inferno Orbit | 40 per prop + 40 blast (3 m) each | ≤4 props | 1600 | 4 s burn | 10 | 45 | highest ceiling (≤320+) |

**Verification — `SynergyAvailabilityVerification.Run` 36 PASS, separate-process `Reload` 9 PASS, exit 0** (`Verification/Synergy/`):
fresh profile (level 1, 0 points) owns all five powers; the Forge has no UNLOCK buttons; VECTOR equips Fire + Ice with 0 points
spent; the Forge line reads "SYNERGY / Thermal Shock — READY WHEN EQUIPPED · 30 S COOLDOWN" (and 40 S for Meteor Punch); in Play
Mode Thermal Shock fires immediately and deals 25 (= data) to the aimed actor, CONTROL actor untouched; HUD radial 1.000 then
0.503 at 14.98/30 s; a mid-cooldown use is REFUSED ("Synergy cooling down"), with health, impacts and VFX emissions unchanged and
the cooldown not reset (29.69 → 29.19 s after 0.5 s); ready again after 30.01 s and fires again (CONTROL). Refusals: a pair with no
synergy (catalog changed IN MEMORY ONLY, restored, never dirtied) → "No synergy for this pair", no cooldown/impact, no HUD slot;
Ice owned but unequipped → session synergy is Meteor Punch and Ice is refused by the equip gate. Tiers: 0-point purchase refused;
an earned point buys Fire tier 0→1; the next purchase is refused. **Old-save migration (second process):** a save written without
Fire/Ice/Telekinesis and without a Loadout loads with all three owned at tier 0, level 3 / 17 XP / 2 points / Flight tier 1 /
rooftop / sessions preserved, equips Fire + Ice for 0 points, writes the ownership back, and fires Thermal Shock in a session.

Historical suites retargeted (kept what they prove): HeroForge "Locked-power CONTROL" → fresh profile owns all at 0 points, no
UNLOCK buttons, and an ownership CONTROL (Fire removed from the save in memory → Forge and SetLoadout refuse; restored). City
"Earned points unlock Telekinesis and Ice" → both owned at tier 0 with the 3 earned points unspent. Regressions, all exit 0:
HeroForge 131 + Reload 3, City 53 + Reload 5, MenuPresentation 42, HUD P1 316, Mode 79 + Reload 2. (HeroForge 132→131 and HUD
318→316 are exactly the removed "Existing progression unlock …" purchases, which have nothing to buy now.) dotnet 0/0.

**Limits:** cooldown lengths are a design judgment from the effect numbers, not human-playtested. The IMGUI Tab menu is never
drawn in batch mode, so its "Not owned"/"Upgrade" labels are compile-checked only. Activation goes through
`SynergyRunner.TryActivate` (what the C key calls); no hardware input.

## Gameplay Phase 2: police and Heat rebalanced per side from measured numbers — 2026-09-26 (appended)

**Diagnosis first (code + data, then measured).** Roster: Criminal = Rusher, Cop = Gunner (ranged 22 m), PursuingHero =
Brute. Criminals are hostile only to a Hero, police only to a Villain. Detection range 100 m (the whole city). The police
count was ONE rule for both sides: 2 "friendly patrol" + 2 per star (2→12 Gunners), + a Brute at ≥ 4 stars, + 2 responders
per encounter. Gunner shot = 8 + 2/star, cycle 0.7 s windup + 1.8 s cooldown. Player: 100 HP with **no regeneration** outside
respawn. Heat: destruction +0.35 (both sides), assault on non-criminals +0.25, defeat +0.5, success Hero −1 / Villain +1,
decay 0.07/s after 10 s calm. The attack-token budget (2) only caps concurrent WINDUPS (the token is released at the
shot), so a ranged crowd's shot RATE grows with its size.

**Measured asymmetry** (`BalanceVerification.Before`: fresh session per cell, idle player at the live encounter site, Heat held,
40 s cap; plus "objective" rows where the harness stays by the nearest robber / loot and holds R, 25 s cap):
- Villain at **0 stars** already faced 4 hostile Gunners (the 2 "friendly" patrols + 2 heist responders) and died idle in
  **9.2 s** (13 hits); 1★ 6.3 s … 5★ 4.2 s (15 hostiles). Looting 2/2 at 0★ still died at 10.3 s.
- Hero took **0 damage at every Heat level**, idle or while capturing all 3 robbers: police are never hostile to a hero,
  and the robbers' 1.6 m trigger is inside the 3 m capture radius, so capturing was risk-free. Hero Heat only added
  4 → 14 neutral cops (clutter, no consequence).

**Changes (all data; per-side values keyed by `PlayerSide`, no mode-ID switch).** New `GameTuning.Heat.HeroPolice` /
`VillainPolice` (`SidePoliceSettings`: patrol count, cops per star, hostile-from-stars, responders hostile, police damage
multiplier). `WorldSession.ReconcilePolice` and `CityNpc.Hostile` / `ContactDamage` read them; director waves (AlwaysAggro, i.e.
Endless) and encounter responders follow `RespondersHostile`; Endless explicit stats are untouched. The legacy
`FriendlyPatrolCount` / `CopsPerStar` fields stay in the asset, marked superseded. New archetype `Enemies/Robber.asset`
(Rusher body) for `EnemyRoster.Criminal`; Endless still uses Rusher from its own composition.

| Value | Before | After | Why |
|---|---|---|---|
| Villain patrol cops per star | 2 | 1 | shot rate scales with gunner count (budget caps windups only) |
| Villain patrols hostile from | 0 stars | 1 star | the "friendly patrol" hunted a 0-Heat villain city-wide; heist responders stay hostile |
| Villain police damage | ×1 | ×0.5 | no player regen; 0.6 left 5★ idle TTD at 4.9 s (tried, recorded) |
| Hero patrol cops per star | 2 | 1 | 14 neutral cops at 5★ were clutter only |
| Hero police hostile | never | never (99) | unchanged by design; Heat still escalates the count |
| City/encounter criminal | Rusher ×0.6 HP ×0.75 dmg, trigger 1.6 m | Robber ×1 HP ×1 dmg, trigger 3.2 m, reach 1.8, radius 1.5 | reach now covers the 3 m capture radius, so robbers fight back |

**Measured after** (same scenario code, `BalanceVerification.After`, exit 0, 8 PASS):

| Side | Stars | Cops alive before→after | Hostiles before→after | Incoming DPS before→after | Time to death before→after |
|---|---|---|---|---|---|
| Villain | 0 | 4 → 4 | 4 → 2 | 10.9 → 2.0 | 9.2 s → survived 40 s (80 dmg) |
| Villain | 1 | 6 → 5 | 6 → 5 | 15.8 → 7.4 | 6.3 → 13.5 s |
| Villain | 2 | 8 → 6 | 8 → 6 | 17.9 → 9.7 | 5.6 → 10.3 s |
| Villain | 3 | 10 → 7 | 10 → 7 | 26.3 → 11.8 | 3.8 → 8.5 s |
| Villain | 4 | 12 → 8 | 13 → 9 | 22.0 → 12.9 | 4.5 → 7.8 s |
| Villain | 5 | 14 → 9 | 15 → 10 | 24.0 → 16.8 | 4.2 → 5.9 s |
| Hero idle | 0–5 | 4..14 → 4..9 | 3 → 3 | 0 → 0–3.3 (0★ 24 dmg; 2★ died 30.2 s; 3★ died 39.4 s; 1/4/5★ 0) | never → sometimes |
| Hero capturing 3 robbers | 0 / 3 | — | 3 | 0 → 8 HP / 14 HP taken | survived |
| Villain looting 2/2 | 0 / 3 | — | 4→2 / 10→7 | 9.5 → 2.4 / 24.0 → 12.7 | 10.3 → survived / 4.2 → 7.9 s |

CONTROLS (asserted): Hero mode has real threat (captures cost 8 / 14 HP); Villain still escalates with Heat (DPS 2.0 → 16.8,
hostiles 2 → 10, never dropping star to star) and an idle 5★ villain still dies. The Hero idle rows vary between seeded runs
because whether fleeing robbers pass within 3.2 m depends on their exit routes (before: 0 in all cells).
Regressions, all exit 0: Combat 170, Mode 79 + Reload 2, ModeExpansion 111 + Reload 10, City 53 + Reload 5 (Combat's roster
check and ModeExpansion's police-count checks now read the Robber / per-side data). dotnet 0/0.

**Not claimed:** balance FEEL is not human-playtested; samples are an idle (or R-holding) player, one seeded run per cell, not
averages. Player health regeneration was left out (a new mechanic outside this pass's file scope); the villain's high-Heat
pressure still comes from gunner count × shot rate and would change most with regen or a ranged token that covers the
cooldown — both are recommendations, not done.

## Gameplay Phase 3: Ice made visible — 2026-09-26 (appended)

**What Ice is meant to do** (IceEffect + ice.asset): select the crosshair target within 20 m; an NPC is frozen for
`Duration` 4 s (CityNpc.Freeze → agent stopped, attack cancelled) and takes `Damage` 5; a non-kinematic rigidbody gets
`FreezeAll` constraints for 4 s. 2 charges, 0.6 s cooldown, 10 energy.

**Evidence BEFORE the fix** (`IceVerification.Before`, live city, real `PowerUser.Use`, `Verification/Ice/results-before.txt`):
the mechanism WORKED. A live AI Rusher charging at 7.00 m/s dropped to 0.00 m/s for exactly 4.00 s (third and first person),
then moved again; 5 damage; Frozen flag set; a falling 45 kg crate went 11.0 → 0.00 m/s (0.000 m drift) for 4.03 s and fell
again; off-axis and out-of-range (27.3 m vs 20 m: "No valid target", no charge spent) CONTROLs held. Targeting, range and
the NPC path were fine. **Root cause of "doesn't do shit": nothing showed it.** The cast emitted no effect of any kind; the
frozen NPC kept its colours and kept animating (0 of 2 body renderers changed, Animator.speed 1) so a stopped enemy just
looked idle; a frozen prop looked like any resting prop; and `ice.asset` used the Fire palette colour (orange HUD slot).
So it is a presentation bug, not an effect/targeting bug, and not a numbers problem — damage stays 5 (no blind buff).

**Fix:** `IceEffect` now (1) shows a `FrozenLook` for exactly as long as the freeze holds — every body renderer swaps to ONE
shared palette material (`CityMaterials.Get(PaletteColor)`, no per-instance material) and an NPC's Animator holds its pose
(speed 0); both restore exactly on thaw, death or destruction — and (2) emits two bursts (hand, impact) from the existing
fixed Feel particle pool. `ice.asset` PaletteColor Fire → Cyan (also recolours its HUD slot/projectile colour data).

**After** (`IceVerification.After` 34 PASS, exit 0): NPC 7.00 → 0.00 m/s for 4.00 s (third person) / 4.01 s (first person),
resumes after thaw (1.74 / 1.28 m/s mean over 1.5 s), 5 damage, 2/2 renderers on the shared Cyan material with Animator.speed 0
while frozen, 0 cyan and speed 1 after; 2 pooled cast bursts; crate 11.04 → 0.00 → 8.16 m/s, Cyan while frozen, back to
shared Wood after; control crate untouched; off-axis NPC never frozen or damaged; out-of-range refused. Images:
`before-*-npc-frozen.png` vs `after-*-npc-frozen.png` (third and first person).
Regressions exit 0: FirstPerson 207 + Reload 5 (includes Ice in B view), City 53, Humanoid 54, HUD P1 316, Feel 139,
HeroForge 131, Combat 170, Audio 127 + Reload 49. (FirstPerson 210→207 and Feel 142→139 are the removed conditional "unlock"
purchases from Phase 1.) Two Phase 2 consequences were caught here and fixed in 1f9ad49: Humanoid and Audio waited for a shot
from a patrol cop at 0 Heat; they now assert the 1-star rule and raise Heat first.

**Limits:** the frozen look applies to the Ice POWER only; synergy freezes (Frostwake, Thermal Shock, Glacier Fist, Cryo Crush)
still stop NPCs without the look. Frozen Cyan is close to the Teal cop body colour. Ice damage (5) is unchanged and may still
feel weak — a design call left to a playtest. No human has judged readability.

## Gameplay balance branch: final regression sweep — 2026-09-26 (appended)

On the final code (ff1e5f3 + evidence), all exit 0 unless noted: HUD Phase 2 482 + Reload 31, HUD Phase 3 560 + Reload 11,
BackflipHurricane 63, MenuPresentation 42, SynergyAvailability 36 + Reload 9, Mode 79, ModeExpansion 111, City 53 + Reload 5
(the first City Reload in the sweep ran without its paired Run and failed the exact-save match; the paired Run + Reload rerun
passes). Earlier in this pass on the same final code: FirstPerson 207 + 5, Humanoid 54, HUD P1 316, Feel 139, HeroForge 131,
Combat 170, Audio 127 + 49, Ice 34, Balance after 8.
**KNOWN FAILURE, not fixed here: CityArtVerification.Run** fails its benchmark precondition "Populated benchmark: civilians=26,
on-NavMesh cops=7" (needs ≥ 10). It sets 3 stars in a Hero session; Hero police are now 2 + 1/star (+ 2 responders) = 7,
was 2 + 2/star + 2 = 10. This is a direct consequence of the Phase 2 cop count, not a crash. The CityArt runner belongs to the
WORLD agent's area, so it was left for the lead: either raise its Heat to 5 stars or relax the precondition to the new count.
PASS-count changes, explained: FirstPerson 210→207, Feel 142→139, HUD P1 318→316 and HeroForge 132→(−4 +3)=131 lost ONLY their
conditional setup lines "Existing progression unlock <power>" / "Unlock <power> through progression". Those lines ran
`if(!Owns(p)) Check(Buy(p))`; every power is now owned from the start, so no purchase happens. The powers are still equipped
and used through the real paths, and ownership-from-start plus the tier purchase and zero-point CONTROLs are asserted in
SynergyAvailabilityVerification.

## World expansion: four districts on one island — 2026-09-26 (feat/world-districts, appended)

**What changed.** The boxed 3x3 grid (130 x 130 m, 36 buildings) is now a 576 x 368 m island of four districts, all
generated from data by one seeded planner (`CityLayout.Plan`, no district-ID branches):

| District (region, NavMesh tile-aligned) | Content (seed 2409) | Silhouette |
|---|---|---|
| Downtown 192 x 256 m | 37 buildings on 3x4 blocks of 36–42 m, 14 m streets, intersection plazas, landmark plaza | 20–84 m with a core boost toward the spire; tallest 106 m |
| Park 192 x 256 m | lawn, loop + cross paths with benches/lamps (breakable props), pond with footbridge, 130+ trees | open, low; lookout tower |
| Residential 192 x 256 m (incl. canal) | 20 townhouses/terraces 6–22 m on 40–46 m blocks, 12 m streets, street trees, canal with the Grand Canal bridge + footbridge | low, wide spacing |
| Docks 576 x 192 m | 6 harbour sheds 7–18 m, container yard (stacks 1–3), 4 gantry cranes, 3 piers, east basin, quay bridge | open, low, water edge; Harbour Light |

Boulevards (24 m, planted medians) separate the districts; canal bridges carry the East Boulevard traffic into
Residential. Landmarks: Meridian Spire (154 m, downtown plaza), Park Lookout Tower (~104 m), Harbour Light (53 m, on a
pier). The edge is one sea plane (palette `Water`), fog (palette `Haze`, exponential 0.0016), a ring of distant
backdrop silhouettes on low shores and a haze skirt — no extra playable geometry. Invisible boundary walls 90 m off
shore; a seabed collider at −1.3 m lets the hero wade and jump out of canal/pond/sea (not driven-tested, see limits).
Palette gained three swatches at the END (`Water`, `Haze`, `Lawn`); layers 8–11 named `CityDetail`, `CityProps`,
`Actors`, `Backdrop`. Styles 4–6 and eight structure recipes were appended to `CityArtSettings.asset`
(`WorldSetup.Apply`, idempotent).

**Contracts kept.** `WorldSession.City` is still `CityDistrict` (`Spawn`, `Sidewalks`, `Buildings`,
`NearestSidewalk`). Rooftop pickups: the first N buildings are the reward ones and keep ids `seed:roof:index`
(3 downtown, 1 residential, 1 docks); a separate-process reload keeps a claimed roof. BreakableProp is the only
destruction path; encounter props unchanged; police still spawn via NavMesh at `NearestSidewalk`. Set-pieces use
`CityDistrict.EncounterSites` with a round-robin district rule (`CityLayout.EncounterSites`); Endless arena =
spawn-district site nearest spawn. Civilians wander within 70 m (`RandomSidewalkNear`) instead of across the island.

**Rendering / static geometry decision (measured).** New `StaticGeometryMode.BuildingMeshes` (shipped): pieces are
written straight into one mesh per building (and per 64 m chunk for ground/streets/structures) per material and layer
— no per-piece GameObjects — then `StaticBatchingUtility.Combine` once per district root. Per-piece static batching
(the previous default) was measured on the same world: 16,093 renderers, 1,035 draws at street, **62.5 FPS and 1,002 ms
build vs 80.0 FPS and 451 ms** — rejected. Props stay shared-mesh + instanced on the prop layer (0 of 516 breakable
renderers in a static batch, asserted). Window/band/kerb detail never casts shadows. Cull distances (layer, m):
detail 220, props 140, actors 170; far clip 2,300.

**NavMesh.** Per district, but NOT as separate NavMeshData: measured first, separate per-district datas did not stitch
(spawn→Park path ended at the seam). Shipped: one NavMeshData over the island, `UpdateNavMeshData` once per district as
each district's sources are added. Build per district: Downtown 69, Park 67, Residential 57, Docks 79 ms (mean of warm
generations); total 272 ms vs 61 ms for the old city.

**NPC LOD** (`NpcLod`, tuning `CityLayout.NpcLod`): within 60 m (±8 m hysteresis) full AI/Animator/presentation;
beyond: AI every 0.25 s, Animator off and advanced manually every 0.2 s, presentation overlay off, skinning only when
visible, no obstacle avoidance. Unseen civilians beyond 150 m are moved to unseen sidewalks 45–110 m from the hero.
Verified: at spawn 10 full / 23 cheap; a hostile criminal 120 m away ran 8 AI ticks in 342 frames with the animator
off; CONTROL — moved next to the hero it was promoted within 2 frames and attacked (windup→release→hit, clip impact
marker on the release frame, AI every frame).

**FPS — FINAL comparison, after merging main 84c4129 (gameplay balance: fewer hero-side police), so both sides use the
same police rules.** Interleaved A/B x3, same harness (`WorldProfile`), Free Play (24 civilians) + 3 Heat stars (now
5 patrol cops), gameplay camera single-render, no other batch Unity during any sample. Baseline = 84c4129 + the
timing/harness patch of 4ee45ed (`Verification/World/ab2/`, `baseline-harness.patch`):

| | Baseline 3x3 (84c4129) | District world (merged e8237d1) |
|---|---|---|
| Densest street FPS (9 samples each) | **89.1** (85.0–91.6) | **94.0** (88.4–101.3) |
| Flight view FPS (views differ) | 77.9 | 140.4 |
| Street draws / static-batched / instanced draws | 347 / 603 / 200 | 269 / 327 / 174 |
| Street triangles | 0.46 M | 1.22 M |
| City build, warm (ms) | 175 | 420 |
| Build incl. session (ms) | 340 | 596 |
| NavMesh (ms) | 54 (whole city) | 63 / 62 / 54 / 73 (Downtown/Park/Residential/Docks) |

Same-process control (merged): street LOD on 94.4, **LOD off 82.7 (−12%)**, i.e. without NPC LOD the district world
would be ~7% below this baseline at street level. Flight LOD off 85 vs on 138. Street profile: skinning 0.72 vs
0.90 ms, scripts 0.46 vs 0.27 ms, opaque rendering 1.27 vs 1.25 ms. Generation +245 ms (city) / +256 ms (incl.
session). Flight samples drift (138 → 175 FPS within the controls run), so flight numbers are ±15%.

*Historical (before the merge; baseline 4ee45ed = 66937b9 + timing, old police rules, 8 cops):* interleaved
A/B/S x3 (`Verification/World/ab/`):

| | Baseline 3x3 | District world (shipped) | District world, per-piece static batching |
|---|---|---|---|
| Densest street FPS (9 samples each) | **81.0** (71.9–87.8) | **80.0** (71.0–85.2) | 62.5 (58.9–66.7) |
| Flight view FPS (views differ: 45 m over old city / 70 m over downtown) | 70.0 | 130.0 | 77.6 |
| Street draws / static-batched / instanced draws | 351 / 603 / 200 | 269 / 327 / 174 | 1,035 / 5,881 / 174 |
| Street triangles | 0.56 M | 1.22 M | 1.26 M |
| Renderers (static-batched) | 2,483 (2,129) | 1,691 (1,032) | 16,093 (15,434) |
| City build, warm (ms) | 192 | 451 | 1,002 |
| Build incl. hero/session/NPC spawn (ms) | 367 | 633 | 1,184 |

Stage split, shipped (merged, ms): plan 3, ground/water/decks 2, buildings 50, streets 1, structures+backdrop 12, static
finalize 42, props 55, NavMesh 253, session 177. **Generation is +245–266 ms over baseline (under the 1.5 s flag).**

**Said plainly: the bigger world is NOT free at street level — NPC LOD pays for it.** Historical same-process control at the
street view: LOD on 76.8 FPS, LOD off (all 32 NPCs full) 65.8 FPS (−14%). So without LOD the district world would be
~2 ms/frame slower than the old city; the profiler attributes the visible part to +0.2 ms scripts (523 props vs 247,
NpcLod) and a larger batch-mode editor overhead (+0.8 ms), rendering itself is flat (Render.OpaqueGeometry 1.3 vs
1.24 ms); the rest is not attributed. LOD halves skinning (0.95 vs 2.0 ms). Streaming control: disabling every
non-spawn district's static geometry changed street FPS 80.0→76.9 and flight 125→121.7 (noise) — district
streaming would save nothing measurable, so none was added.

**Travel (NavMesh path / RunSpeed 9 m/s; flight = straight line at RunSpeed x FlightForwardBoost = 72 m/s, one 6 s /
432 m tank):** spawn → Downtown far site 104 m 11.6 s run; → Park 113–228 m 12.5–25.3 s; → Residential 257–262 m
28.6–29.1 s; → Docks 190–310 m 21.2–34.5 s; every district is within one flight tank (≤ 4 s). Encounter deadline 210 s;
two seeds each spread 8 set-pieces over all 4 districts. HUD waypoint verified at 261 m (label "261 M", edge arrow when
facing away).

**Verification.** `WorldVerification.Run` / `.Reload` (new): two seeds valid and different, same seed twice identical
(fingerprint CONTROL), 0 overlapping buildings (overlap-test CONTROL), spawn on NavMesh, every district reachable from
spawn (sea CONTROL unreachable), crimes in 4 districts, landmark line-of-sight from downtown rooftops, rooftop save
contract across processes, NPC LOD tiers + promotion CONTROL. Captures `Verification/World/verify/`. Tests that
hard-coded the 3x3 grid were retargeted to the city's own sites/sidewalks without weakening assertions (Combat lane,
HUD2 alert/waypoint spots, HUD3 escape sidewalk, Mode villain-cop start, City/Menus building counts vs the plan).

**Not verified / limits (explicit).**
- No human playtest: traversal feel, whether crossing feels tedious or too short, rooftop-hopping downtown, district
  readability. Travel times are computed from paths and movement numbers, not driven with input (batch mode).
- Flight time assumes run-speed flight (Shift held); walking flight is 41.6 m/s.
- Wading/jump-out of canal, pond and sea (bed 1.3–1.44 m below the bank, jump apex 1.5 m) was not exercised.
- Seeds vary block size, lots, heights, plazas, styles, trees, containers and props; district regions, canal, bridges
  and landmark positions are authored data and do not move with the seed.
- Civilian recycling concentrates the 24 civilians around the player by design; far districts are empty of civilians.
- Before the merge, `FirstPersonVerification.Run` failed `hero-third-fire contact within projectile radius tolerance`
  (0.3083 m vs 0.3 m) identically on the untouched 4ee45ed baseline clone; on the merged branch it passes (207).
- `HudPhase2Verification` "briefing still up after 5.5 s" failed once under another agent's concurrent Unity load, on
  the baseline clone as well; it passes on a quiet machine. One pre-merge `HudPhase3` run failed a 1.7 px
  top-centre/banner overlap in `popup-endless-crowd`; it passed on the runs before and after (not reproduced).
- The Mode villain-cop check now waits up to 4 s for the responder's first hit (it landed at 0.73 s); a first retarget
  that stood 25 m away beforehand made the cop strike early and then sit in cooldown, and was replaced.
- The edge: from altitude the sky reads as uniform haze (overcast) and the backdrop silhouettes are plain boxes; the
  haze-skirt corners are faintly visible at frame edges in the 120 m north view.

**Final regressions on the merged branch (e8237d1 + Mode retarget), all exit 0:** World 57 + Reload 3, City 53 +
Reload 5, CityArt 31, Humanoid 54, Combat 170, Mode 79 + Reload 2, ModeExpansion 111 + Reload 10, HUD P1 316,
P2 483 + Reload 31, P3 562 + Reload 11, Audio 127, MenuPresentation 42, FirstPerson 207. Run in an APFS clone of the
branch (`scratchpad/world-reg`) except World, which writes its evidence in the branch.
- All FPS is Editor batch-mode throughput (includes ~5–7 ms batch overhead), not a player build.

## Ice + Feel verification fixtures retargeted to the district map — 2026-09-26 (feat/world-districts, appended)

The final sweep found two runners whose FIXTURES assumed the old 3x3 grid; product code is unchanged, assertions unchanged.
- `IceVerificationRunner`: the live-NPC target was picked from `CityPlan.Sidewalks` 13–19 m from spawn, but on the
  district map the nearest sidewalk points (block corners) are ~21.3 m away (`FAIL No clear sidewalk 13-19 m`). Target,
  off-axis CONTROL and out-of-range CONTROL now come from NavMesh points fanned out around the hero (15° steps, band
  middle first) with a clear sight line where needed: target 13–19 m (inside Ice's 20 m range) → (-28,0.1,20), 16.0 m;
  CONTROL 8–16 m and 60–120° off the lane → 11.9 m, 75°; out-of-range NPC range+4..range+12 m → 28.0 m. The out-of-range
  check now also asserts `distance > range` explicitly. A `FIXTURE` line logs the chosen points.
- `FeelVerificationRunner` particles check: a real street `BreakableProp` inside the crate row's blast reach broke too
  (13 bursts, the extra one exactly `BreakParticles`). For that exact-count check only, non-test BreakableProps within
  Strength radius + 4 m (7.3 m) of the crates are deactivated and re-activated afterwards (both logged: City Planter,
  City Car, Barrel, City Newspaper; 4/4 restored). The assertion stays `== 12`.
- Rest of both runners checked on the new map: no other failure. Feel's `15m-yaw30` aim row hits a parked `City Car`
  (shoulder-line parallax) exactly as the committed old-map baseline did — a real case, not a fixture failure.

**Results (twice each, exit 0, identical PASS sets; only timing numbers differ).** `IceVerification.After` 34 PASS / 0 FAIL
(baseline 34): approach 7.00 m/s → 0.00 during freeze → 2.72/2.66 (3rd person) and 2.71/2.64 m/s (1st person) after
thaw; freeze 4.00–4.01 s; damage 5; prop 11.04 → 0.00 m/s (drift 0.000 m) → 7.20 m/s; out-of-range NPC 28.0 m refused.
`FeelVerification.Run` 139 PASS / 0 FAIL (baseline 139): hit pause 61.1 / 60.8 ms; particles bursts 23 → 35 (= 12);
Fire aim MISS 0.001–0.004 m at 5/8/15 m, 0.102 m at 22 m, 0.705 m at 30 m (beyond Fire's 20 m range, as before);
particles A/B ON 338.4 vs OFF 347.7 FPS (−2.7%) and 342.7 vs 356.5 FPS (−3.9%), Editor batch-mode.
`IceVerification.Before` shares the placement code and also runs clean (exit 0, 27 PASS + 7 "OBSERVED yes"); its
output was NOT committed so the historical pre-fix `results-before.txt` / `before-*.png` evidence stays as recorded.

## Ice shatter + directed Telekinesis throw — 2026-09-26 (appended)

Started from clean `main` at `a94d967`, after reading AGENTS, STATUS and the merged history. No world, rebalance,
Fire effect, Flight, Forge/synergy source or existing power resource numbers were changed.

**Ice:** a punch or Hurricane Kick against an already frozen NPC/prop consumes the freeze, restores the visible pose/material
and prop constraints immediately, adds **30 damage** to the same damage call, emits **28 Cyan ice fragments** through the
existing fixed Feel particle pool, and applies **1,800 N.s additional AddExplosionForce** (lift 0.65). The normal melee
force still applies. Tuning lives on `Assets/Resources/Effects/Ice.asset`, linked by `Powers/ice.asset`; these are flat
additive values, not another upgrade tree. Damage is awarded once, preserving the existing defeat/XP/Heat path.
The only controller edits label the two existing punch/kick Blast calls `melee:true`; timing, charge payment, cooldown,
normal damage/force and Strength upgrades are unchanged. Non-melee Blast callers default to false.

NPCs use the existing `SynergySuspension` physics/navigation handoff (80 kg capsule, not a ragdoll). A lethal hit keeps
that capsule dynamic through the existing death-presentation lifetime, rather than letting navigation recovery stop
the corpse next frame. Existing dead/defeat checks still apply. `FreezeStartedFrame` prevents a melee attack from
consuming a freeze it just established itself: Glacier Fist's first hit still freezes, and a later follow-up can shatter.
No synergy effect, cooldown or rule was edited. An expired/consumed freeze cannot produce another bonus.

**Telekinesis:** LMB grabs, LMB again throws the already-paid hold (release still works with no charges or during cooldown).
The old second-click path already existed but launched parallel to the shoulder's ray from the prop's different position;
its NPC collision handler only dealt flat damage. `PowerUser.AimPoint` now exposes the exact existing Fire crosshair query
(hero-relative range and behind-hero exclusion retained), and `AimDirection` delegates to it. A held body is excluded from
its own aim query. Throw converges from the body's actual centre of mass to that point, compensates existing velocity
and gravity, and calls `AddForce(..., ForceMode.Impulse)` once. There is no teleport, homing, new aiming system or change
to Fire's origin/wall sweep.

Throw tuning lives on `Assets/Resources/Effects/Telekinesis.asset`: launch speed = power Force / body mass, clamped to
**22–40 m/s** so a 400 kg car can reach the crosshair instead of dropping at 4.5 m/s. This deliberately means heavy-object
launch impulses exceed the old flat 1,800 N.s. Damage = power Damage × sqrt(mass / **45 kg**), clamped to **0.5–3×**.
Actual collision momentum transfers **0.65×**, capped at **2,400 N.s**, with **0.3 lift**, into the existing NPC physics
handoff. The struck actor hosts the handoff coroutine so destroying the projectile cannot cancel it. The initiating
projectile/actor collision is ignored for **0.2 s** and restored; without this separation, the original contact was
mistaken for a landing and canceled the launch (caught by the live test). Damage remains one-shot in `ThrownProp`.
Legacy synergy callers of `ThrownProp.Initialize` retain their original damage/force behavior via optional settings.

While Orbit Throw (or Inferno Orbit, which uses the same effect type) is holding props, the same Telekinesis LMB input
calls its existing `RequestRelease`. The existing coroutine owns every sequential release, interval and cooldown;
neither it nor its launch algorithm was duplicated or modified. The original C-key release remains available.

**Verification:** `PowerPayoffVerification.Run` in an isolated APFS project copy, isolated temporary save, Unity 6000.6.0f1
with graphics enabled: **91 PASS, exit 0**. Evidence: `Verification/Payoff/results.txt`.

```text
PUNCH unfrozen: 35.00 damage, 0.000 m/s NPC speed; frozen: 65.00 damage, 31.425 m/s.
KICK unfrozen: 59.50 damage, 0.000 m/s NPC speed; frozen: 89.50 damage, 37.768 m/s.
Base impulse unchanged: punch 1350 / kick 2025 N.s; shatter adds 1800 N.s.
Normal hit 14 pooled particles; frozen hit 42 = 14 normal + 28 ice.
Non-melee CONTROL: exactly 7 damage, still frozen, no shatter.
Consumed/thawed CONTROL: next punch has only its original 35 damage.
Frozen prop constraints restored: launch speed 52.76 m/s.
Lethal shatter of a normal 65HP enemy: dead body still moving at 32.12 m/s.
```

Throws at an NPC ~18 m from the hero (about 14 m of prop travel), stationary clear-lane fixtures in the actual city scene:

| View / mass | Crosshair-to-contact miss | Actual AddForce impulse | Impact damage | NPC impulse / launch speed |
|---|---:|---:|---:|---:|
| Third / 45 kg | 0.0261 m | 1,922.4 N.s | 35.00 | 1,176.3 N.s / 15.416 m/s |
| Third / 400 kg | 0.0467 m | 10,468.3 N.s | 104.35 | 2,400 N.s / 30.727 m/s |
| First / 45 kg | 0.0369 m | 1,924.7 N.s | 35.00 | 1,177.4 N.s / 15.425 m/s |
| First / 400 kg | 0.0573 m | 10,396.3 N.s | 104.35 | 2,400 N.s / 30.724 m/s |

Gravity was enabled on the thrown body before grabbing and restored on release. Off-axis NPCs took **zero damage**;
the wall control recorded an actual wall collision and the target behind it stayed unharmed. Repeated collision did
not pay damage twice. A lethal 400 kg hit still launched its dead target. Orbit captured three real bodies and released
all three through the existing path at **0.152 / 0.151 s** intervals (configured 0.150); cooldown remained active.

Limits: tests call the same entry points as input and advance real PhysX frames; no human feel acceptance or hardware
input injection. Accuracy is measured against stationary NPCs; throws do not predict moving enemies or steer around
obstacles. Physics fixtures are lifted above the city to isolate force/aim controls. Living NPC recovery retains the
existing suspension's NavMesh fallback; no ragdoll or new recovery system. Shatter is geometric pooled ice debris,
not a new particle asset. Inspector balance values are design decisions that still need playtesting.

**Combat FPS, actual before/after:** the identical `PowerPayoffBenchmark.Run` harness was run on two isolated project
copies, one with the nine changed shipping files restored to `a94d967`, one with the finished feature. Sequential runs
(no concurrent Unity), same seed, one enabled 1280x720 gameplay camera, **26 civilians + 7 cops**, ambient AI active,
15 repeated Ice/punch/Telekinesis cycles over 30 seconds per run. Only test-player health is restored to sustain combat;
the close combo target is a controlled fixture, and successful casts are counted. Throws completed 15/15 per baseline
run and 15/15 then 14/15 after (one live obstruction prevented a grab). Draws use UnityStats, as in WorldProfile.

| Run | Three 10-second FPS samples | Mean FPS | Draw-call range |
|---|---|---:|---:|
| Before 1 | 94.54 / 96.25 / 85.74 | 92.18 | 256.4–259.6 |
| After 1 | 79.84 / 81.58 / 84.00 | 81.81 | 257.0–262.1 |
| After 2 | 83.27 / 86.20 / 82.93 | 84.13 | 256.6–260.8 |
| Before 2 | 74.01 / 73.76 / 77.37 | 75.05 | 252.9–256.6 |

Pooled means **83.61 before → 82.97 after (−0.77%, about +0.09 ms/frame)**. No measurable regression at this run's
noise level; **this is not proof of zero cost**. Baseline drift (92.18 → 75.05) is much larger than that pooled delta,
so the first pair's apparent 11% loss and the second pair's apparent gain cannot establish a stable effect. Per-run
data is retained in `Verification/Payoff/benchmark-{before,after}-{1,2}.txt`; the table pairs by version, while actual
execution was After 1 → Before 1 → After 2 → Before 2. An earlier exploratory baseline (81.75/83.85/93.11) used an
unsupported ProfilerRecorder counter (zero draws) and overlapped a brief dotnet compile; it is retained as
`benchmark-before-initial.txt`, explicitly excluded from the comparison. No detail, population or VFX was cut.
All figures are Editor batch throughput, not standalone player FPS or a controlled hardware laboratory result.

**Regression fixture correction:** CityVerification originally failed its Ice-prop assertion after the new capped-speed
throw. Its `Destroy(held)` is deferred until end of frame, but it immediately creates and casts at the next fixture.
Actual diagnostic output: `aimed old thrown body=True, aimed Ice body=False; old position=(-28.00, 1.12, 8.74),
new position=(-28.00, 1.12, 10.00)`. The old 4 kg / 1,800 N.s throw had already flown past; the 40 m/s prop was still
blocking the ray. The test now disables its retired fixture before querying Ice and adds an explicit target-selection
control. The original Ice freeze/expiry assertions are unchanged; the final City run passes **54** (previous 53 + the
new fixture control). Original failure and final output are both retained in `Verification/Payoff/regression/`.

**Final regression gate, all exit 0:** HeroForge **131**, BackflipHurricane **63**, FirstPerson **207**, Ice **34**,
Feel **139**, City **54**, Combat **170**. Full text output is retained under `Verification/Payoff/regression/`.
FirstPerson's Fire contacts remain **0.0951 m third / 0.0846 m first**, in both Hero and Villain sessions; Feel also
rechecks the hero-relative range fix. Forge exercises all ten synergies, original C-key Orbit release, Glacier freeze,
Strength charge refusal and original force, and flight fuel. The historical abilities runner freezes its NPC fixtures
to hold them still, so its recorded melee damages now correctly include shatter; the new suite separately proves
the unchanged **unfrozen** 35 / 59.5 controls. No existing assertions were weakened.

Unity **6000.6.0f1 batch compile passed with no C# errors/warnings**; the pre-existing Editor Search indexing
`ArgumentOutOfRangeException` still appears at startup, separately from gameplay. No gameplay errors occurred in the
completed payoff run. Run commands (on an isolated copy, omit `-quit`):
`-executeMethod PowerPayoffVerification.Run` and `-executeMethod PowerPayoffBenchmark.Run`.
Supplemental dotnet build: **0 warnings / 0 errors** (`Verification/Payoff/build.txt`). All 18 changed/new asset,
source and metadata files byte-match the final tested copy. `git diff --check` passes.

## Local agent Phase 0: ground truth, packages, DOTween — 2026-09-27 (appended)

Verified in an isolated clone (the user's Editor was open on the real tree at the start); real tree only received the
verified files. Evidence: session scratchpad `phase0/` (logs, reflection dumps, tween test, Sidekick renders).

- **Imported, now committed:** Synty Sidekick (was already in `ef63fdb`), DOTween 1.3.030 (`Assets/Plugins/Demigiant`),
  Cartoon FX Remaster FREE (`Assets/JMO Assets`). Cartoon FX IS present.
- **Baseline compile of the tree as the user left it FAILED** (1 error, third-party):
  `Synty/.../Editor/ModularCharacterWindow.cs(25,13): error CS0234: 'VisualScripting' does not exist in 'Unity'`. Because
  Sidekick's editor assembly failed, Assembly-CSharp-Editor (all our verifiers) could not rebuild. Fixed by adding
  `com.unity.visualscripting 1.9.12` (ships with the editor; Sidekick's asmdef already references Unity.VisualScripting.Core).
- **Package audit:** 0 references in our code/asmdefs AND 0 of 5,309 package GUIDs in 420,013 serialized project GUIDs for
  Entities, Unity Physics, Multiplayer Services, Transport, QoS, Wire, Deployment. Removed `com.unity.physics` then
  `com.unity.services.multiplayer`, recompiling after each (0 errors, no new warnings; 67 → 61 → 53 packages). Kept:
  characters-animation (Rigging/Cinemachine/Timeline/FBX, needed later), shadergraph (Sidekick shader), ai.* (App UI
  settings referenced), probuilder/collab/multiplayer.center (unreferenced but harmless — left by default).
- **DOTween setup** run by its real API (the Utility Panel's "Setup DOTween" chain, found by reflecting DOTweenEditor.dll):
  created `Assets/Resources/DOTweenSettings.asset`, deleted DOTweenUpgradeManager files, `DOTWEEN` scripting define added to
  every build target (DOTween's own post-processor). Proven: an unscaled (`SetUpdate(true)`) tween advances at timeScale 0
  (0.333 at 1.001 s real, completes to 1.000); CONTROL scaled tween stays 0.000, then moves once timeScale is 1.
- **Compile gate:** `Overpowered.Build.csproj` now references `DOTween.dll` so gameplay code may use DOTween; 0/0.
  Unity batch compile of the final clone: 0 errors. Third-party warnings only (CFXR WelcomeScreen CS0618, Synty ToolDownloader
  CS0618); our code 0 CS warnings.
- **Smoke regressions on the final package set:** City 54 + Reload PASS, HUD P1 316, Humanoid 54 — all exit 0.
- **Sidekick facts (Phase 1 input):** 8 prefabs (Starter_01–04, HumanSpecies_01–04), each 1 Animator with a valid Humanoid
  avatar (55 mapped bones, a superset of our Mixamo 52), Unreal-style bone names (irrelevant: HumanoidPresentation uses
  HumanBodyBones). SharedHumanoid.controller plays Idle on Starter_01. Sidekick_ShaderGraph has a Built-in target and renders
  non-pink on Built-in (0.00% magenta; error-shader control 100%). Colours come from a 32x32 point-filtered `_ColorMap`
  (2x2 swatches) + masks — so HumanoidPresentation's current "replace every material with a palette material" would flatten
  Sidekick characters to one colour; Phase 1 must recolour the colour map instead.

### OVERNIGHT DECISIONS
- Added `com.unity.visualscripting` (not in the brief): the only non-invasive fix for Sidekick's compile error; patching vendor
  code was the alternative.
- Removed only the two direct manifest entries the brief named (their dependents went with them). Left unreferenced
  probuilder / collab-proxy / multiplayer.center: the brief said "if unsure, leave it", and they cost nothing at runtime.
- Committed Cartoon FX + DOTween vendor files to git (same as Synty was), so the cloud agent and fresh clones compile.
- `Side_Kick_Data.db` (tracked) is rewritten by Sidekick's Character Creator window when play mode toggles in an Editor where
  it auto-opened (`file_exists` flips on 58 rows). Left tracked; verification runs restore it. If it keeps showing up as a
  diff, set the EditorPref `syntySkAutoOpenState` false (per-machine, not in the repo).
- `SyntyPackageHelper` may prompt in the Editor to add `com.unity.formats.fbx` as a direct dependency (it's already present
  transitively) — safe to decline or accept.


## CLOUD FEEDBACK — local integration #1 of `cloud/gameplay-depth` (2026-09-27, local agent, Unity 6000.6.0f1 macOS Intel)

**Integrated state.** Branch `integrate/cloud-1` in a scratch worktree:
- `b789375`: merge of cloud `f315f31`.
- `fc7d254`: RosterSetup assets.
- `9535325`: merge of cloud `4c6f51d` (the Vault heist, MissionSetup, MissionVerification and STATUS commits pushed while this run was going on).
- `6b44a08`: MissionSetup assets.

The base was local main `540379e`. After `fc7d254`, only new files and STATUS changed, so the sweep results below, taken at `fc7d254`, still hold for `6b44a08`.

**Compile and setup: all clean.**
- The dotnet gate reported 0 warnings and 0 errors after both merges.
- The Unity batch compile reported 0 CS errors and 0 CS warnings in our code. The only warnings were the two analyzer warnings that already existed (UAC0005 in AudioSetup.cs:46, UAC1001 in HumanoidPresentation.cs:34).
- `RosterSetup.Batch` created 40 files. `MissionSetup.Batch` created 17 files.
- Both are idempotent. A second run of each changed no file: 2679 and 2704 files hash-identical, and `git status` identical.
- `MissionSetup.BatchUseInModes` was **not** run. The cloud STATUS says to run it only after the full sweep passes, and the sweep did not pass.

**Result: NOT ready to merge.** Every failure below comes from test harness bugs or one mission logic bug. With the harness bug patched in memory only (never committed), every new power, all three synergies and the loadout reload pass.

### Failures to fix on the cloud branch

1. **Roster, Melee and HeroStats: the harness never runs `PowerUser.Tick`.** Classification: cloud **test bug**.
   - `RosterVerification.Run`: exit 1, 41 PASS. `FAIL System.Exception: Restart after cooldown.` at `RosterVerificationRunner.cs:165`. The suite aborted inside Laser Eyes, so Lightning, Force Field, Speed, Poison and the synergy sections never ran.
   - `RosterVerification.Reload`: exit 1. `FAIL System.Exception: SECOND PROCESS restores the new-power loadout: vector laser-eyes + strength.` This is a cascade: Run aborted before it saved the nova laser-eyes + poison loadout.
   - `MeleeVerification.Run`: exit 1, 4 PASS. `FAIL System.Exception: Tap 2 inside the window = stage 2 (punch).` at `MeleeVerificationRunner.cs:30`.
   - `HeroStatsVerification.Run`: exit 1, 40 PASS. `FAIL System.Exception: Second Ice cast (energy for the regen sample).` at `HeroStatsVerificationRunner.cs:89`.
   - **Cause:** `SessionVerificationRunner.PlaceHero` sets `W.Hero.enabled = keepEnabled` (false by default, `SessionVerificationRunner.cs:78`). `Isolate()` calls it at `:72`. The only per-frame caller of `PowerUser.Tick` is `SuperHeroController.Update` (`SuperHeroController.cs:71`). With the controller disabled, no cooldown or recharge ever counts down, so `WaitForSeconds(stats.Cooldown)` waits forever in game terms.
   - **Evidence from the diagnostic run.** I ran with a patch that decrements `PowerRuntime.Cooldown` by `Time.deltaTime` while the hero is disabled, plus non-throwing checks:
     - Roster: exit 0, **194 PASS, 0 failures**. Roster Reload: 3 PASS.
     - Melee: **43 PASS, 0 failures**.
     - HeroStats: 82 PASS, 1 failure (item 2 below).
   - **Hint:** follow the existing local verifiers. After disabling the hero they call `W.Powers.Tick(dt, true)` themselves (for example `IceVerificationRunner.cs:149` and `FirstPersonVerificationRunner.cs:119`). Either tick in the shared waits in `SessionVerificationRunner`, or pass `keepEnabled: true` wherever time must pass.

2. **HeroStats: the Ice measurement is occluded.** Classification: cloud **test bug**. It is hidden behind item 1 until that is fixed.
   - Diagnostic failure: `PowerDamage x1.5 (in-memory hero): Ice 7.50 vs VECTOR 0.00.` at `HeroStatsVerificationRunner.cs:48`.
   - In `Measure()` (`:80`) the melee actor at (0,150,2.2) stands on the crosshair line to `far` at (0,150,12), so the Ice cast hits the near actor.
   - As a result, `MEASURED` Ice damage is 0.00 for VECTOR, TITAN and NOVA, although the data value is 5. The "same Ice damage" check at `:46` passes vacuously (0 == 0).
   - The value 7.50 is correct (5 × 1.5), so `PowerDamage` itself works.
   - **Hint:** move the melee actor off the aim line, or remove it before the Ice casts. Assert that the measured Ice damage equals the data value.

3. **SynergyAvailability was not retargeted to 13 synergies.** Classification: **test not retargeted**. The runner is local code, but the cloud's data change broke it.
   - `SynergyAvailabilityVerification.Run`: exit 1, 0 PASS. `FAIL System.Exception: All ten synergy cooldowns are long (25-45 s): sonic-slam=35, phoenix-dive=40, frostwake=25, orbit-throw=30, thermal-shock=30, meteor-punch=40, inferno-orbit=45, glacier-fist=30, cryo-crush=30, meteor-slam=35, solar-flare=40, void-grasp=40, eclipse-beam=45` at `SynergyAvailabilityVerificationRunner.cs:59` (`F.Synergies.Length==10`).
   - The same `Length==10` assumption is at `:124`: `Catalog restored to its 10 shipping synergies; never dirtied or saved.`
   - With non-throwing checks, Run gave 33 PASS and exactly 3 failures (`:59`, `:62`, `:124`). Reload passed 9.
   - `SynergyAvailabilityVerification.Reload`: exit 1. `FAIL System.Exception: SECOND PROCESS: old save without Fire/Ice/Telekinesis now owns them at tier 0 (loader grants InitiallyUnlocked).` This is a cascade: Run aborted before writing `Verification/Synergy/saves/old-save-path.txt`. See local issue L3.
   - **Hint:** compare against the catalog's real count, or the capped set plus the legacy set, instead of the literal 10.

4. **Design question: the synergy-to-power cooldown ratio.** For the cloud agent or the user to decide.
   - Diagnostic failure: `Shortest synergy cooldown 25 s is >= 40x the longest normal power cooldown 1 s.` at `SynergyAvailabilityVerificationRunner.cs:62`.
   - Force Field has a 1.0 s cooldown, so the rule needs 40 s. Laser Eyes and Lightning (0.8 s) would need 32 s. The shortest synergy is Frostwake at 25 s.
   - Pick one:
     - lower the new powers' `Cooldown` to 0.625 s or less;
     - scope the 40× rule to instant offensive powers (Force Field's real gate is its 12 s charge recharge);
     - raise Frostwake.
   - Do not simply delete the assertion.

5. **Mission: the robbery getaway car never drives.** Classification: cloud **logic bug** (mission physics).
   - `MissionVerification.Run`: exit 1, 12 PASS. `FAIL System.Exception: The getaway car really drives: 0.0 m in 1.5 s.` at `MissionVerificationRunner.cs:74`.
   - Diagnostic state logging, six samples over 1.25 s after "Getaway car 1 departs with 1":
     - position stayed at (20.48, 0.12, 23.31) and velocity at about 0;
     - isKinematic False, not sleeping, constraints None, mass 400, `RobberyState` enabled, timeScale 1;
     - the route has 4 corners. Corner 0 is the car's own position, so the corner index jumped to 1 at once; corner 1 is (-16.17, 0.20, 20.67), about 37 m away.
   - `FixedUpdate` adds `ClampMagnitude(dir*CarSpeed - v, CarAcceleration*fixedDt)` as VelocityChange: at most 0.18 m/s per step (9 m/s²). That is barely above ground friction on a 400 kg box (μg ≈ 5.9 m/s² at default friction), and the car is parked by `NearestSidewalk`, possibly against a curb.
   - Because the car never moves, the fail path ends as "A robber escaped on foot." instead of "The getaway car got away…".
   - **Hint:** log the contact and friction state. Consider lifting the car slightly, using a low-friction physic material, `MovePosition` along the route, or a stronger drive force, and assert the actual displacement.

6. **Mission: robbery cuff completion.** Undetermined whether cloud logic or the test; only seen with non-throwing checks.
   - Diagnostic failure: `Cuffing him (hold R on a frozen robber) completes the mission: SUCCESS.` at `MissionVerificationRunner.cs:97`.
   - The steps before it passed: Ice stalls the car, the robber bails out, the CONTROL cuff of an un-frozen robber is refused, and the bailed robber freezes.
   - The check does not log `last.Captured`, `Ended(e)` or `Result`.
   - **Hint:** include those three values in the check text, and assert the cuff's `InteractableNear` range from the standing point you use.
   - Hostage, Building fire and Vault heist **passed every check** in the same diagnostic run: 78 PASS in total, 2 failures, both in Robbery.

### AGENTS.md review of the new code

Items that pass:
- Shared palette materials only. No runtime `new Material` in any cloud code; every line, trail, ring and flame uses `CityMaterials.Get(...)` through `sharedMaterial`.
- Power VFX is pooled. There is one `PowerVfx` pool of 16 lines plus 1 beam, and particles come from the Feel `ImpactParticlePool`. Per-actor helpers (`PlayerShield`, `DashTrail`, `RootedLook`, `Poisoned`, `BeamState`) are added once per actor and reused, not created per cast.
- No mode-ID switches. `MissionSetup`'s "mode switch" is a data edit of `Modes/hero|villain.asset` Encounters.
- Equip and ownership gates are respected. Channeled start goes through `Use` → `IsEquipped` / `Owns`, and `Channel()` ends the channel on unequip or reselect. Number keys go through `PowerForSlot` → `Select` gates.
- The physics root is not animated. The dash uses `CharacterController.Move`.

Items that need attention:
- **Violation: synergy cap.** `ForgeCatalog` ships **13** synergies against the "5 named only" cap: Sonic Slam, Thermal Shock, Solar Flare, Void Grasp and Eclipse Beam, plus 8 legacy ones (Phoenix Dive, Frostwake, Orbit Throw, Meteor Punch, Inferno Orbit, Glacier Fist, Cryo Crush, Meteor Slam). The cloud reported this knowingly and did not delete them. Removing them needs a decision, because `HeroForgeVerification.AllSynergies` still exercises all 10 legacy synergies and currently passes (177).
- **Minor: fire flames are not pooled.** `FireScenario` spots each add their own `ParticleSystem`, 4 per fire encounter, on the shared Fire material. This is set-piece geometry like encounter nodes, not a per-cast effect, but it is outside the pool.
- **Minor: small per-cast allocations.** Lightning allocates a `HashSet` per cast and Poison a `List` per spread.
- **Presentation contract change.** A tap-E punch now fires on key release, up to 0.2 s later than before. Payment is still immediate at the call. The cloud flagged this for playtest.
- **Visual observation, not a failure.** In the diagnostic captures, the Laser Eyes beam is a thin dark line that is hard to read. The Void Grasp capture is dominated by large dark navy screen-crossing rings.

### Suites that passed on the merged branch

All exited 0. Counts are PASS lines.
- **City and powers:** City 54 + Reload 5, CityArt 31, Humanoid 54, Combat 170.
- **Modes:** Mode 79 + Reload 2, ModeExpansion 111 + Reload 10.
- **Powers and forge:** HeroForge 177 + Reload 3, Feel 139, Ice.After 34, Balance.After 8, BackflipHurricane 63, PowerPayoff 91.
- **Camera, menus and world:** FirstPerson 207 + Reload 5, Camera.Verify 45, MenuPresentation 42, World 57 + Reload 3.
- **HUD:** HUD P1 316. HUD P3 562 + Reload 11 on its rerun (see L4).

### Lead decisions for the cloud agent (OVERNIGHT DECISIONS, local agent)
- **Synergy cap: remove the 8 legacy synergies.** The user's instruction is explicit ("synergies are capped at exactly 5
  named ones — Sonic Slam, Thermal Shock, Solar Flare, Void Grasp, Eclipse Beam — not full pair coverage"). Remove Phoenix
  Dive, Frostwake, Orbit Throw, Meteor Punch, Inferno Orbit, Glacier Fist, Cryo Crush, Meteor Slam from `ForgeCatalog`
  (keep their effect code only if a capped synergy reuses it) and retarget `HeroForgeVerification.AllSynergies` and
  `SynergyAvailabilityVerification` to the capped five (assert the exact five IDs, not a count).
- **Cooldown-ratio rule (item 4): scope it, don't delete it.** Apply "shortest synergy >= 40x longest power cooldown" to
  instant offensive powers only; Force Field's real gate is its charge recharge, and channeled powers (Laser Eyes) are gated
  by drain. Assert the scoped rule explicitly and list which powers are excluded and why.
- **Missions stay out of the shipping modes** until the robbery car drives and the full sweep passes: do not run
  `MissionSetup.BatchUseInModes` on your side; the local agent runs it after the next green integration.
- **Laser Eyes beam readability** is local presentation work (Phase 9 / addendum) — don't restyle it on the cloud side.

### Local issues found during this integration (handled by the local agent, listed for transparency)
- Audio music-DSP check fails on local main since Phase 0 (not cloud) — being bisected locally.
- HUD Phase 2 briefing CONTROL is timing-sensitive under parallel Unity load (fails on untouched main too) — local test fix.
- A Reload whose Run aborted could fall back to the player's real save (read-only, file verified unchanged) — local harness
  guard added in `PlayerProgression.Initialize` (batch mode never touches the real save).
- `MenuPresentationSetup.Create` re-serializes the original five power assets with the cloud's new fields at defaults
  (`Activation: 0`, `DrainPerSecond: 0`) — harmless; will be committed with the next merge.

## Local issues L1–L3 fixed: real-save guard, music DSP race, briefing clock (2026-09-27, branch `fix/local-issues`, appended)
These are the local issues from CLOUD FEEDBACK #1. All runs were on this Mac (Unity 6000.6.0f1). The user's real save
`overpowered-progression.json` was checked with md5 before and after every run. It never changed (`9047e6ee…`).

- **L3: a Reload could load the user's real save (commit `5e833b6`).**
  - **Fix:** in batch mode, `PlayerProgression.Initialize` no longer falls back to `persistentDataPath` when no path is set.
    It uses a throwaway `temporaryCachePath` file and logs a warning.
  - **Negative control:** I ran `SynergyAvailabilityVerification.Reload` with no `old-save-path.txt`. The log shows the
    FileNotFoundException and then the warning. The run fails cleanly on its assertion "Old progression preserved exactly"
    (exit 1). The real save was unchanged.
  - **Control:** a normal Run passed 36 and its Reload passed 9, with no warning.
- **L1: "Music DSP sample cursor advances" was a test race, not silenced audio (commit `cc78bd1`).**
  - **Cause:** the check read `timeSamples` after `Scene(Home)` plus a 1.1 s realtime wait that starts in play-mode
    frame 1. `AudioDirector` first calls `Play()` on the music in frame 2. When frame 1 alone took more than 1.1 s, the check
    ran in the same frame as `Play()`, before any mixer block, and read 0.
  - **Frame timing, measured with non-perturbing logs:**

    | State | Frame 1 → 2 | When the check ran | Result |
    |---|---|---|---|
    | HEAD | 1.53 s / 1.50 s | same frame as the music `Play()`, ts=0 | FAIL |
    | `ada0a2e` | 0.68 s / 0.66 s | frames 61 / 3, ts=18815 / 21638 | PASS |

  - **Not a Phase 0 commit regression:**
    - Unmodified `ada0a2e` failed the same way under load at 04:48. It passed at 04:27, 05:05 and 05:09, and also passed
      2 more runs with timing logs added.
    - The check already flaked on 09-24, before Phase 0.
    - Phase 0 made frame 1 longer: editor windows are created in it, and `DOTweenSettings.asset` is imported and refreshed.
      That turned an occasional flake into a near-constant failure.
    - With Sidekick's auto-open skipped, frame 1 still took 1.11–1.43 s under load 17–34. So no single component is proven
      to cause the longer frame.
  - **Fix:** the check now uses the audio clock.
    - It waits for 2 s of `AudioSettings.dspTime`. The wait is bounded in real time, so a dead output device still fails.
    - The cursor must then advance by 50–125 % of dspTime × clip frequency. This is stronger than `ts > 0`.
  - **New CONTROL:** at pitch 0 the source still reports `isPlaying`, but its cursor stalls. The same measurement rejects it
    (990 samples in 0.51 s, below 50 % of 22579).
- **L2: HUD P2 briefing CONTROL (commits `61a3c04` + `e744053`).**
  - **Mechanism:** the test's realtime stamp starts after the 1920×1080 composite. The HUD's timeout clock
    (`BriefingElapsed`, unscaled) already includes that capture.
  - **The actual trigger was an editor stall.** In every HUD P2 run, one frame of 5.47–5.75 s landed near the villain
    briefing.
    - A PlayerLoop and editor-update probe placed the 5.3 s inside the editor's `HostView.SendUpdate`, right after
      `Synty…ModularCharacterWindow.AnimationUpdate`. This is Sidekick's Character Creator window, which
      `MenuBootstrapController` auto-opens even in batch mode. It is editor-only, not a game hitch.
    - When the stall landed inside the capture, the old test failed. When it landed after the capture, the old test passed
      by luck: the check ran in the stall frame, before the HUD's LateUpdate counted it.
  - **Fixes:**
    - `Assets/Editor/BatchModeSidekickQuiet.cs` sets Sidekick's own per-session flag (`SessionState` "FirstInitDone") in
      batch mode only. This changes no vendor file and no EditorPrefs.
    - The test watches the HUD clock every frame. The card must be seen up in [5.5, 7) s. It must stay up while the clock is
      below 7 s. It must close by timeout on the first update past 7 s. The HUD clock must advance with real time, within one
      frame.
  - **Result:** the longest frame across the briefing window is now 0.02 s. As a side effect, `Side_Kick_Data.db` was no
    longer rewritten by these runs.

**Final regression sweep on `e744053`** (runs back to back, wt-sidekick's Unity sometimes busy in parallel, load 10–13):

| Suite | Exit | PASS | Reload exit | Reload PASS |
|---|---|---|---|---|
| AudioVerification | 0 | 128 | 0 | 50 |
| HudPhase2Verification | 0 | 483 | 0 | 31 |
| SynergyAvailabilityVerification | 0 | 36 | 0 | 9 |
| CityVerification | 0 | 54 | 0 | 5 |
| HudVerification (P1) | 0 | 316 | — | — |
| HudPhase3Verification (extra) | 0 | 561 | 0 | 11 |

- **Earlier HUD P2 proof runs** (while wt-sidekick was profiling): 484 + Reload 31, then 483 + Reload 31.
- **PASS counts that vary by one:**
  - HUD P2 has 483 or 484 PASS lines, and HUD P3 has 561 or 562.
  - The difference is always the same thing: a prompt card that is or is not on screen in one capture, which adds or drops
    one "composite contains hud-prompt" line. This variation already existed.
- **dotnet gate:** 0 warnings, 0 errors.
- **Limits:**
  - The audio check proves the DSP cursor advances. It does not prove the sound is audible on a speaker.
  - If some other editor stall longer than 1.5 s ever lands inside the briefing window, the HUD P2 CONTROL will fail. The
    failure message states the longest frame.
  - I did not re-run L4 (HUD P3 timing) in isolation. Its failure signature matches the same editor stall.

## Local agent Phase 1: Synty Sidekick heroes — 2026-09-27 (appended; branch feat/sidekick)

The three Hero Forge heroes are now Synty Sidekick characters inside the existing Forge architecture (same
HeroDefinition/ForgeCatalog/PlayerProgression/HumanoidPresentation path, no parallel character system). NPCs stay the
Mixamo mannequin (measured below). Evidence: `Verification/Sidekick/**`.

### Load-bearing check first: the shared Mixamo clips on the Sidekick rig — PLAY CORRECTLY
`SidekickClipCheck.Run` (edit mode) evaluates every SharedHumanoid clip through the Humanoid retarget path on the
mannequin and on all 8 Sidekick prefabs at the same normalized times (15 clips, 232 Sidekick samples), captures
mannequin | Starter_02 | HumanSpecies_01 front-3/4 + side (`clips/<clip>-<n>.png`) and measures pose metrics
(`clips/results.txt`). Looked at walk, run, punch, hurricane kick, backflip, cast, death (+ the rest):
- **Walk / run / jog / back**: same gait phase and arm swing, feet planted; no broken wrists, spine not twisted.
- **Punch**: at the impact marker (0.567 s) the left fist is forward: 0.60–0.68 m ahead of the chest on the 8 Sidekicks vs
  0.648 m on the mannequin (1.27 vs 1.15 of each rig's own arm length — Sidekick arms are shorter, hands reach further).
- **Hurricane kick / backflip / jump**: same shapes (airborne spin kick, inverted tuck) but Sidekick bodies travel lower:
  kick lowest foot 0.25–0.31 m vs 0.41 m; backflip mid-flip lowest vertex 0.48–0.72 m vs 1.00 m.
- **Cast**: same crouched two-hand stance. **Death**: lies on its back; Starter_02's tail/backpack (and any bulky back
  attachment) pass up to 0.70 m through the ground.
- Proportions: at the same 1.8 m fit Sidekick hips ride lower (idle hips 0.88 m vs 1.03 m).
- **Feet**: without foot IK, Sidekick feet sat 3–9 cm lower than the mannequin's; worst: run at 25% had Starter_03 and
  HumanSpecies_03 feet 9.4–9.5 cm BELOW the ground while the mannequin's were 6 cm above. With IK on feet that sample is
  −1.4…+1.1 cm. **Foot IK is now ON for every state of the SHARED controller** (see decisions).

### Heroes and recolour
| Hero (width) | Sidekick prefab | Look | Vertices |
|---|---|---|---|
| VECTOR (1.0) | Starter_03 | hooded plate armour | 19,510 |
| TITAN (1.2) | Starter_01 | bearded knight, plumed helmet, back weapon | 26,338 |
| NOVA (0.92) | Starter_02 | fox-mask sci-fi samurai, tail + backpack | 26,575 |

`SidekickSetup.CreateHeroes` (menu **Overpowered > Forge > Sidekick heroes**, idempotent) assigns the prefabs and builds
one `SidekickSuit` per hero (`Resources/Forge/Sidekick/*-suit.asset`): a readable copy of the authored 32×32 colour map
plus the role of every swatch the mesh's UVs use, read from Sidekick's own colour table (`sk_color_property` via
`/usr/bin/sqlite3 -readonly`, edit time only). Keep = Species (skin, hair, eyes, mouth, nails, brows), Elements, unmapped,
glow/glass/screen/gem; Trim (authored luminance < 0.30) = CityPalette Metal; Outfits group = loadout Primary;
Attachments + material parts = loadout Secondary (TITAN 16/9/9/29 swatches Primary/Secondary/Trim/Keep, NOVA 12/10/16/33,
VECTOR 15/5/9/26). `CityMaterials.Suit` builds ONE material + colour map per (suit, primary, secondary), shares it, rewrites
it when the palette changes and destroys it with its CityMaterials (city teardown / Forge preview). No Sidekick runtime
API, database or SQLite at runtime (Sidekick's runtime API needs its DB — rejected). The suit material uses the Standard
shader with the suit colour map (+ Sidekick's emission map); `SidekickSuit.AuthoredShader` restores Sidekick_ShaderGraph
(`shader-compare.png`: the two look near-identical). The Forge preview now frames by posed skinned vertices.

**Optimized hero bodies.** Sidekick's combined prefab mesh lists one skeleton copy per part in `bones[]` (VECTOR 2,992
entries, TITAN 3,176, NOVA 2,793, vs the mannequin's 64). The setup writes `<hero>-body.asset` with duplicate (bone,
bind pose) entries merged (88 / 96 / 130 left), weights remapped and Sidekick's 84 editor blend shapes dropped (all weights
were 0); `SidekickSuit.ApplyBody` swaps it in at spawn and in the preview. Same bone transforms, so Animator,
HumanoidPresentation bones and first-person hiding are untouched. CONTROL: both meshes skinned in the same non-trivial
pose differ by at most 0.001 mm (setup fails above 1 mm). Assets: 3.7–5.0 MB each.

### Verification (real Play Mode, controls)
- `SidekickVerification.Run` **exit 0, 99 PASS** / `.Reload` (separate process) **exit 0, 7 PASS**: all 3 heroes × 2
  colour pairs through the Forge UI (`heroes-recolour.png`: top = defaults, bottom = VECTOR Red/Cream, TITAN
  UiPurple/Cyan, NOVA Amber/Teal) — every used swatch asserted exactly (e.g. NOVA 71 swatches: 12 Primary, 10 Secondary,
  16 Trim, 33 Keep); **CONTROL skin**: swatch (0,5) identical for both pairs and equal to the authored map (VECTOR/NOVA
  191,144,98; TITAN 213,165,123); vendor materials untouched. Per hero: Forge → SAVE & BACK → Hero session spawns that
  prefab (same mesh + avatar, shared controller, no root motion, Animator not on the physics root), ONE cached suit material,
  60 frames later none created (live suit materials = 1); Ice FrozenLook shows the shared Cyan material and thaw restores
  the same suit material + map; first person → ShadowsOnly → back to On; returning Home destroyed the material and map
  (`session-heroes.png`). Reload: NOVA Amber/Teal restored in a new process, swatches re-asserted; fresh-save CONTROL = hero
  defaults (`reload-session.png`).
- `SidekickClipCheck.Run` exit 0 (gross gate 0/232; see decisions for the tight counts).

### FPS: NPC bodies A/B, then the hero — NPCs stay mannequins, hero cost removed
`SidekickNpcProfile.Run`: fresh Free Play sessions cycling A/B/C/D × 4 rounds, Heat topped to 3 stars, hero parked on the
sidewalk of the densest crossing of the island (Downtown, 722 m of building height within 60 m), real ThirdPersonCamera
placement, single render per frame at 1280×720, 5 s samples; a sample is retaken when another batch Unity ran or other
processes used > 60% CPU (4 of 20 retaken). Final run `npc-fps/results-run5-final.txt` (Radeon Pro 5300, i9-10910):

| Variant | FPS per session | median | vs A |
|---|---|---|---|
| A mannequin NPCs + Sidekick hero (shipping) | 91.42, 86.23, 84.39, 81.39 | 85.31 | — |
| B Sidekick NPCs (HumanSpecies_01–04) + Sidekick hero | 111.08*, 55.74, 56.94, 55.51 | 56.34 | **−34.0% (+6.0 ms)** |
| C mannequin NPCs + mannequin hero | 100.17, 102.07, 97.93, 86.88 | 99.05 | +16.1% (−1.6 ms) |
| D = A with the hero on Sidekick_ShaderGraph | 87.29, 87.98, 85.22, 80.32 | 86.25 | +1.1% (none) |

*B r1 was taken after a 107 s hygiene wait with only 15 NPC bodies visible; the median ignores it. B had 29 NPC skinned
renderers / 208k vertices vs A's 58 / 823k, yet skinning cost 3.2–3.3 ms vs 1.0–1.2 ms and the frame +6 ms (the NPC looks
are unoptimized prefab meshes with thousands of bone entries — see below). NpcLod still works on both (far NPC: Animator
disabled and stepped manually 4–5×/s, presentation off, skinning only when visible; near NPC fully on).

Run 5 also showed **the Sidekick hero itself cost ~1.6 ms/frame (−14%) vs the mannequin hero** (the suit shader made no
difference). One bounded A/B on cheap fixes (`results-run6-hero-knobs.txt`, noisy): SkinQuality.Bone2 and zeroed blend
shapes gave nothing; `updateWhenOffscreen=false` gave ~+10%, pointing at per-bone work — the renderer had 2,992 bone
entries. After the optimized body (`results-run7-body-final.txt`, 4 rounds, same method):

| Variant | FPS per session | median | vs A |
|---|---|---|---|
| A Sidekick hero with optimized body (shipping) | 106.17, 94.57, 96.20, 95.98 | 96.09 | — |
| C mannequin hero | 91.70, 96.69, 96.64, 96.26 | 96.45 | +0.4% |
| H Sidekick hero, unoptimized prefab mesh | 85.59, 95.08, 86.56, 87.71 | 87.13 | −9.3% (+1.07 ms) |
| G = A + `updateWhenOffscreen=false` (not adopted) | 171.00*, 98.37, 109.11, 98.47 | 103.79 | +8.0% |

Skinning (UpdateAllSkinnedMeshes) A 0.66–0.84 ms, C 0.80–0.85, H 1.01–1.17. **Remaining hero cost vs the mannequin: none
measurable (+0.4% for the mannequin, inside run-to-run noise of about ±5%).** G is not adopted: its gain rests on one
171-FPS sample after a 218 s wait, and fixed bounds risk culling/shadow errors in flight poses and first person.
*Other agents' Unity instances (wt-cloud, wt-local) ran during these runs; 17 samples were retaken by the hygiene rule.
Earlier runs are kept: run 1 A/B 74.79 vs 47.85 (−36%); run 3 suggested the ShaderGraph cost 1.9 ms but ran at load
average 9–12; run 4 was contaminated (load 12–23, 12 FPS outliers) and is not used.

### Regressions (exit codes; evidence `Verification/Sidekick/regression/<suite>/`)
| Suite | Result |
|---|---|
| Humanoid (VECTOR) / TITAN / NOVA (`-overpoweredHero`) | exit 0 — 54 / 55 / 55 PASS |
| Humanoid DeathControl | exit 0 — 5 PASS |
| HeroForge + Reload | exit 0 — 131 PASS + 3 PASS |
| SynergyAvailability + Reload | exit 0 — 36 + 9 PASS |
| FirstPerson + Reload | exit 0 — 207 + 5 PASS |
| Ice.After | exit 0 — 34 PASS |
| Feel | exit 0 — 139 PASS |
| City + Reload | exit 0 — 54 + 5 PASS |
| HUD P1 | exit 0 — 316 PASS |
| MenuPresentation | exit 0 — 42 PASS |
| BackflipHurricane | **first run exit 1** (20 PASS, then FAIL "Backflip returns the presentation to locomotion once the dash ends"; the cloud agent's Unity was running); 3 re-runs exit 0, 63 PASS each |
| Combat (mannequin NPCs, foot IK on) | exit 0 — 170 PASS |
| CityArt | exit 0 — 31 PASS |

Humanoid's populated-city benchmark read 44.14 / 13.99 / 43.31 FPS for VECTOR / TITAN / NOVA; the TITAN figure ran while
another Unity was busy and is not a hero difference (that harness has no contention check). Compile gate
`dotnet build Overpowered.Build.csproj` 0 warnings / 0 errors; Unity: only the pre-existing analyzer warnings.
Mannequin-specific test assumption retargeted: HumanoidVerification's "two skinned meshes" now means "the selected hero
model's own count" (1 for Sidekick) and additionally requires no MeshRenderer under the visual root. The CityArt palette
check now accepts the palette-coloured suit materials owned by CityMaterials (`CityMaterials.Owned`).

### OVERNIGHT DECISIONS
- **Clip-check gate changed after seeing results.** Tolerances fixed before the first run (max bone-direction change 20°,
  mean 6°, foot within 5 cm of the mannequin, punch reach within 0.10 arm lengths) flagged **133/232** samples without foot
  IK and **138/232** with it — mostly mean bone differences of 6–15°, feet a few cm lower and reach +0.12, which the captures
  show are proportion effects, not broken poses. The committed exit gate is gross breakage (a chain moving > 35° differently,
  or a grounded foot > 0.15 m off); the tight counts are still printed. That gate caught 2/232 without foot IK (feet 9.4–9.5 cm
  below ground) → next item.
- **Foot IK ON for all 15 states of the SHARED controller** (`HumanoidSetup.FootIK`, applied in place by
  `HumanoidSetup.ApplyFootIK`, also used by future rebuilds). It changes the mannequin NPCs too; measured mannequin foot
  heights are essentially unchanged (run 25%: 0.061 vs 0.060 m) and Humanoid (all 3 heroes), DeathControl and Combat pass
  with mannequin NPCs. Its per-animator CPU cost was not isolated.
- **NPCs stay mannequins**: Sidekick NPC bodies cost −34% FPS in the densest district, and the light Sidekick variants
  (HumanSpecies) are underwear bodies that don't read as civilians/cops (`npc-fps/street-B-sidekick-npcs-run5.png`). The
  data path is kept but off (`HumanoidAnimationTuning.SidekickNpcs=false`, 4 NpcLooks suits generated).
- **Heroes = Starter_01/02/03 as shipped**; no new meshes were assembled from Sidekick parts (that needs Sidekick's DB-backed
  runtime at edit time). HumanSpecies (underwear) and Starter_04 (pumpkin head, underwear) were not used for heroes.
- **Suit roles by Sidekick colour group** (cloth → Primary, armour/attachments → Secondary, dark → Metal trim) instead of
  "largest colour cluster → Primary": the first attempt made Primary a small dark accent. Roles are editable data.
- **Suit shader = Standard** (+ Sidekick emission map), not Sidekick_ShaderGraph: taken after a noisy run suggested 1.9 ms;
  the clean run shows no difference, so it stands on consistency with palette materials and the near-identical look.
  Toggle `SidekickSuit.AuthoredShader` to go back.
- **Optimized hero bodies** (lead request: one bounded A/B on the hero's 14% cost): new generated mesh assets instead of
  the vendor prefab mesh at runtime; Sidekick's blend shapes (body/face sliders of its editor) are not available in game.
  The four NPC-look suits did not get optimized bodies (NPCs are off).
- The existing hero fit (reference-pose vertices → 1.8 m) was kept; plumes/back items count toward it, so TITAN's body is
  shorter than VECTOR's (posed heights 1.63 m vs 1.84 m, NOVA 1.68 m). VisualScale.y kept at 1 (docs rule).
- `Side_Kick_Data.db` restored with `git checkout --` after runs / before every commit.

### Human playtest list
1. Each hero running, sprinting, jumping, punching, kicking, backflipping in the city: feet contact (foot IK now on), hands
   at punch reach, whether the lower airborne arcs of kick/backflip read well.
2. TITAN looks shorter than the others (plume/back weapon in the height fit) — acceptable, or fit by skeleton instead?
3. Recolour taste per hero across the 8 suit colours (cloth = Primary, armour = Secondary, dark parts = Metal). NOVA's large
   dark areas become Metal; TITAN's armour follows Secondary.
4. Forge preview framing and HUD with the new heroes; first-person body hiding while flying/punching.
5. Death: bulky attachments (NOVA's tail/backpack) clip through the ground while lying.
6. Mannequin NPC walking/running with foot IK now on (knees/feet on slopes, kerbs).
7. Frame rate feel with the Sidekick hero (measured equal to the mannequin hero after the body optimization; Editor batch
   figures only, no standalone build measured).

## Codex continuation: Sidekick final body checkpoint — 2026-09-27
Recovered the previous session's uncommitted optimized bodies and completed `reg2-summary.txt` in the original scratchpad. All 21 Run/Reload entries exited 0 after the body replacement, including all three hero Humanoid runs, Combat, FirstPerson, Ice, Forge, and BackflipHurricane. Earlier failed/noisy evidence and the pre-body regression are retained. These are recovered prior-session measurements, not newly run Codex playtests.

Codex independently rebuilt this exact source with the bundled Unity dotnet SDK: **0 warnings, 0 errors**. Removed the stale suit inspector claim of a proven 1.9 ms shader cost; the clean comparison did not establish that cost. No gameplay or verification thresholds were changed during this continuation.

### OVERNIGHT DECISIONS
- Preserve previous screenshots that the interrupted evidence cleanup had deleted.
- Retain the optimized hero meshes and disabled Sidekick NPC setting; the prior controls support both choices.
- Resume the queue in order. Cloud integration still requires fixes and fresh regression evidence before shipping missions.

## Phase 1 merged verification — Codex, 2026-09-27
Sidekick + the local L1–L3 fixes are merged on main at `f3040c7`. Codex reran `SidekickVerification.Run` (exit 0) and `SidekickVerification.Reload` in a separate Unity process (exit 0) on the merged source in wt-sidekick. Evidence: `Verification/Continuation/Sidekick/`. Original package/Sidekick-database working changes on main were preserved.

Push of main failed: `fatal: could not read Username for 'https://github.com': Device not configured`. Local commits remain intact; no credentials were changed.
