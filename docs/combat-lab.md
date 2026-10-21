# Combat Lab and enemy expansion

Baseline: `2ed79a9` (complete Synergies 2.0). Work is isolated on `codex/combat-lab-enemy-expansion`; no merge or push.

The original checkout contained two untracked Power Mechanics verification scripts and their metadata, plus metadata for the already tracked PoisonProjectilePool. The two test scripts are not runtime dependencies and remain untouched in the original. The stable PoisonProjectilePool metadata is copied byte-for-byte into this branch so import does not assign a different GUID. No original checkout files are edited.

## Launch and practice safety

Open `Assets/Scenes/CombatLab.unity` and press Play, or use **Overpowered > Combat > Open Combat Lab**. The scene is in Build Settings for arena resets. GameFlow has one early scene dispatch; the normal Home/Prototype/Results flow is unchanged. No Home menu integration is required.

The lab copies the current saved profile read-only, deep clones it and initializes PlayerProgression with a null SavePath. Profile repair, XP, usage stats, side/view/difficulty edits and quit saves cannot write to the personal profile. WorldSession practice skips Heat, rewards, police, crimes and mode creation. Its CityDistrict is an empty metadata holder, never a generated city. Baked arena navigation is removed on scene exit.

F2 or Esc opens/closes the control panel and pauses combat. Choose an archetype, Spawn one or Spawn group, Clear lab enemies, Restore health, Restore energy, select difficulty, Reset arena + cooldowns, or Return Home. Standard equipped movement/power/signature controls remain active when the panel is closed. Change loadout in Hero Forge before reopening the lab.

Reset reloads this scene with the starting in-memory snapshot; it clears all effects, props, corpses and cooldowns through their existing lifecycle. Difficulty selection uses SetDifficulty on the transient profile and restarts so SessionDifficulty remains an immutable snapshot. There is no separate cooldown manager.

Tuning: Resources/CombatLabSettings.asset; 72 m arena, 13 m spawn ring, 6 m minimum player distance, group 3, cap 12. Active actors are capped; old corpses are deactivated and scheduled for removal before a slot is reused. Clear removes only lab-owned actors. Cover is static; throwable crates use BreakableProp. All generated meshes/materials are shared.

## Verification log

Foundation: both runtime and Editor C# assemblies compiled with the installed Unity Roslyn compiler, using references from the original checkout's existing Library. No Editor process was launched for this check. Three existing serialization/API analyzer warnings remain. Import, UI rendering, gameplay and human feel are not established by a static compilation. Unity execution was deferred because the shared slot with Gemini was not confirmed.

## Skirmisher

Evasive melee is opt-in data on EnemyArchetype, not a role or power-ID switch. Skirmisher uses 7.8 m/s approach, 0.75x base health, 0.6x attack damage, 0.38 s first windup, two strikes with 0.26 s followup windup, 1.4 s final recovery. The existing token stays held across the bounded burst.

Dodge/dash: 4.5 s cooldown, 0.22 s visor tell, at most 2.2 m lateral / 3 m forward, 0.32 s travel and 0.6 s stationary recovery; 1.8 m post-burst retreat. Destination, straight NavMesh lane, capsule clearance and complete path are checked before moving and again after the tell. NavMeshAgent owns movement; no warp, invulnerability or disabled colliders. Ice/death/pause/suspension cancel movement and pending burst; Darkness root cancels movement while preserving the existing rooted-attack contract. A shared mannequin rig plus shared helmet/visor/shoulder pieces supplies the silhouette.

`CombatLabVerification.Run` now contains runtime controls for original/new spawns, resolved event/death cardinality, spawn cap, clear ownership, transient saves, restoration, reset and finite vulnerable evasion. These controls are prepared and compile; they have not been executed in Unity.

## Bulwark

Bulwark uses 2.7 m/s movement, 1.8x base health (Brute remains 3x), 1.5x base damage, a 1.05 s locked bash windup and 2.5 s unguarded recovery. Its shield faces the player at up to 85 degrees/s while approaching; the windup uses the existing committed attack shape and animation. Shared plate/stripe/armor pieces make the guard direction visible. Shield drops/tilts during windup, recovery, disable/suspension, freeze/root and guard break; absorbed hits flash cream and broken guard shows red.

Defense is opt-in EnemyArchetype.Guard data. The central CityNpc.Damage operation resolves difficulty once, then a 110-degree forward cone absorbs 65% of damage (at least 10% can always get through even if configured higher). Sides/rear/overhead remain exposed. Absorbed damage spends 75 integrity; depleting it makes that blow fully effective and breaks guard for 1.8 s. An impact >=900 breaks guard before applying damage. Freeze/root use the same generic disruption lease, with no power-ID exemptions. Repeated control can extend the lease but it expires after control stops.

Damage accepts an optional world hit origin; the shared CombatImpact area path supplies its blast origin. Existing direct/deferred callers fall back to the attacker's current capsule centre. This deliberately avoids changing each power. Poison/beam ticks still do damage, spend guard integrity and use normal resolved events; no categorical immunity. Indirect callers without an explicit origin use the fallback (not a full ballistic hit-normal model). NpcDamageResult adds DamageBeforeGuard and GuardAbsorbed; ResolvedDamage/HealthLost, usage, health bars and floating numbers share the final reduced value.

Guard runtime controls cover front/side/rear, resolved-event count, heavy impulse, finite rearm, root/freeze, telegraphed bash, recovery and death. Static runtime/Editor assembly compilation passes; Unity execution is UNVERIFIED; the shared exclusive slot was not confirmed.

## Waves

Only Endless Fight Hero and Endless Fight Villain have a round director. Both share EndlessWaves. Their smooth weighted round-robin table is Rusher from 1 (weight 3), Gunner from 2 (2), Brute from 3 (1), Skirmisher from 3 (2), Bulwark from 5 (1). Existing size, 12-alive cap, reinforcement, spawn validity, faction selection, kill accounting and health/damage escalation formulas are unchanged. The new definitions participate in the existing data table; no new round code, mode switches or miniboss/event enabling.

Hero/Villain campaign modes remain encounter-based. Free Play still has a null director, no automatic waves, and unchanged manual/debug spawning. CombatLabVerification checks eligibility, both side roles, once-only stat composition, unchanged non-round modes and capped targets. ModeExpansionVerification's live-wave roster assertion now expects five definitions; its existing real kill/reinforcement/wave advancement controls remain in place.

## Final verification and delivery (2026-10-21 UTC)

- Branch: `codex/combat-lab-enemy-expansion`.
- Worktree: `/Users/melaniehernquist/.codex/worktrees/combat-lab-enemy-expansion/op-overnight-recovery`.
- Baseline: `2ed79a9`; phases: `c03745b` lab foundation, `39666d2` Skirmisher, `a6bb246` Bulwark, `2bc60ef` Endless integration. The final delivery commit contains the static checks, hardening and this handoff; use `git log -1` for its hash.
- No merge, push, rooftop feature changes, main-city generation changes, universal movement changes, power/signature retuning or vendor material edits.
- Full created/modified file inventory: `Verification/CombatLab/delivery-files.txt` (including metadata). Production input hashes and baseline comparisons: `Verification/CombatLab/delivery-evidence.json`.

Executed checks: **2 successful C# assembly compilations, 31 executable guard-policy checks, 80 static asset/data/baseline checks; 0 failures**. Three pre-existing analyzer warnings concern RobberyState.Car serialization, nullable HeroPresentationState verification serialization and AppDomain.GetAssemblies in AudioSetup. C# compilation used the installed Unity compiler plus imported references from the original checkout. This did **not** launch a Unity Editor or import this worktree.

Evidence: `Verification/CombatLab/static/results.json`, both `*.compile.txt` outputs, `guard-policy.txt`, `asset-data-checks.txt`, and `asset-data-results.json`. No old gameplay screenshots or logs are presented as results for this branch.

**Unity import, Play Mode, rendered UI, live AI/navigation, actual damage numbers/bars, wave clear/reinforcement, power/signature/secondary interactions, separate-process reload, standalone build and performance are UNVERIFIED.** No new Unity process was started: the current brief requires a single coordinated slot across Codex/Gemini/user and no reply reserved it. Process checks found no Editor, but do not establish that the other agent will refrain from launching one. Do not open this worktree while Gemini or the developer is using Unity.

### Prepared Unity controls (not executed)

`CombatLabVerification.Run`: all five spawns, real health variance/difficulty formula, shared Animator, single damage/death result, clear ownership, cap, restoration, source-save isolation, faction policy, evasion/burst/retreat/control, frontal/flank/rear guard, heavy impact break/rearm, bash windup/recovery/death, wave eligibility and clean arena reset.

`CombatLabVerification.Reload`: fresh-process reload of Run's isolated source snapshot, proving practice XP/hints/view do not persist and the source remains unchanged. Must follow a successful Run in the same serial driver invocation.

`CombatLabVerification.Matrix`: eight equipped primaries and eight selected signatures x both new enemies x Normal/Hard/Extreme (48 sessions), plus every secondary defined in the repository. Repository truth is **five** secondaries (Flight, Speed, Force Field, Phase Shift, Teleport), although the brief says four. Ownership/loadout/signature choices use the actual Forge APIs on a temporary profile. Matrix targets are stationary, non-guarding vulnerability fixtures for deterministic compatibility; movement and active defense are tested separately by Run. The matrix checks primary damage or Ice control, resolved metadata/HUD path, signature completion/cancellation and navigation, then scene/pool cleanup. It is not evidence that every power is equally effective against an actively guarding Bulwark.

Once the human reserves the Unity slot, from this worktree run serially:

```sh
python3 tools/verify-sequential.py --out Verification/CombatLab/unity-gates --combat-roll 0.5 \
  CombatLabVerification.Run CombatLabVerification.Reload CombatLabVerification.Matrix \
  ModeExpansionVerification.Run CombatVerification.Run CombatBalanceVerification.Run \
  HeroForgeVerification.Run HeroForgeVerification.Reload \
  FinalSignaturesVerification.Matrix
```

The driver refuses an already-running Editor, stops on first failed process/check, records fresh output and guards personal-save/Sidekick database hashes. Keep other agents out of Unity throughout the whole sequence. Restore `Side_Kick_Data.db` in this worktree after any Play Mode run if Sidekick modifies it, as AGENTS requires. Do not import an old Library by copying it into this worktree; the static compiler only reads its references.

Reproduce the checks already executed without Unity:

```sh
python3 tools/verify-combat-static.py --reference-project /Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery
python3 tools/verify-combat-data.py
```

### Known limits and remaining work

The requested four features are implemented and statically compile. Unity verification and human playtesting remain before treating them as ready to integrate. There is no claim of balance/polish or measured performance. The dedicated scene is intentionally minimal: arena geometry, lighting, NavMesh and actors are constructed by CombatLab at runtime, with no main-city generator. Entry is via the scene/Editor menu, not a new Home button. A separate instant per-ability cooldown button is omitted; Reset Arena uses existing initialization to refresh every cooldown safely.

Hit origins: CombatImpact area attacks provide their true origin. Other existing callers use the attacker's current centre unless they supply the new optional origin. A moving attacker can therefore change the guard relation of an old DoT/indirect hit; this is an explicit limitation of the existing metadata, not a new per-power exception. Continuous damage still spends finite guard integrity. Unknown/environmental hits without a source or origin bypass the directional reduction.

Navigation/evasion destinations are validated against both the NavMesh and a bounded capsule sweep before travel, including a second check after the tell. Dynamic obstacles can still move into the route during travel; the existing NavMeshAgent owns avoidance. Human testing must check fast movement near throwable props and tight cover. No enemy collider is disabled to dodge, and no navigation Warp/physics handoff was added for evasion.

Performance has **not been measured**. New attachments reuse Unity's shared cube mesh and palette materials; no new mesh/material per enemy or hit, and no new VFX pool is required for guard feedback. Evasion holds one NavMeshPath only on evasive actors, uses a fixed 24-hit buffer and a finite cooldown. Lab enemies cap at 12 (configurable 1–24); each burst caps at 3 via data (shipping 2). Existing humanoid skinning/fitting, damage queries and signatures retain their costs; all historical STATUS measurements remain intact. Profile 12 live mixed enemies and repeated spawn/reset cycles in a single rendered view before accepting performance.

### Exact human playtest checklist

1. With Gemini/other Editors closed, open this worktree in Unity 6000.6.0f1. Wait for import; confirm no new Console errors or USS/UXML warnings. Open CombatLab and Play. Confirm bright flat floor, cover, crates, selected hero, current difficulty and standard HUD.
2. F2/Esc: open/close controls. Confirm combat pauses while controls are open and resumes afterward. Select each of five types; Spawn one and group. Reach the cap and confirm additional spawns refuse. Clear; confirm only lab-owned enemies disappear.
3. Take damage/spend energy; restore each separately. Reset during a DoT/signature, then during a channel and held prop. Confirm clean arena, fresh vitals/charges/cooldowns, no old attacks, status markers, projectiles, held objects or pooled effects. Fall/fly beyond the arena; confirm normal respawn to the floor.
4. Fight one Skirmisher at normal speed. Observe a visible tell, short lateral dodge/occasional approach dash, two readable strikes, retreat and recovery. Land precise shots and timed melee during travel/recovery. Freeze/root it and ensure movement stops, expires and resumes without sliding through cover. No permanent dodge/invulnerability loop.
5. Fight one Bulwark. Compare repeated equal hits from front, flank and rear using actual numbers and HP. Bait its slow bash, leave the committed disc, then punish unguarded recovery. Watch cream shield hits, red break and finite rearm. Break it with heavy impact, Ice and Darkness. Try sustained Laser/Poison and overhead/nearby AoE; none should be categorically ineffective.
6. Repeat with eight primaries, all five available secondaries and all eight signatures using valid saved Forge builds. Test first- and third-person view. Check health bars, resolved damage numbers, death, collision, status cleanup and no duplicated hits. Do not judge effectiveness from the stationary matrix alone.
7. Select Hard and Extreme in the lab; confirm a full arena restart, the new label, expected max health/enemy HP/resource/cooldown changes and unchanged faction relations. Return Home/restart the application; personal XP, points, records, hints, difficulty, side, view and Forge loadout must match the pre-lab save.
8. Play both Endless sides through rounds 1–6. Originals only before 3; Skirmisher appears from 3, Bulwark from 5; later mix includes originals. Kill both new types and confirm LEFT/score/next wave/reinforcements and cap. Check Hero police remain friendly and Villain responders hostile. Check campaign encounters and Free Play manual spawning; Free Play must never auto-start waves.
9. Profile 12 mixed live enemies, movement around cover, repeated guard hits, continuous beams/DoT, multiple signatures and 10 arena resets. Compare CPU, allocation, renderers/materials/meshes and pools before/after. Check a standalone build separately. The human developer decides actual feel and tuning changes.

### Merge notes

Integrate the combat branch after reviewing the Unity gates; it has not been merged or pushed. Preserve the standalone CombatLab scene GUID/build-settings entry and all new metadata. The small shared integration points are CityNpc, EnemyArchetype, CombatImpact, PlayerProgression, PowerUser, WorldSession and a one-line GameFlow.SceneReady dispatch. Reconcile GameFlow/build-settings additions if Gemini also registers an entry, and append/reconcile both STATUS sections. CityDistrict, CityLayout, universal movement and rooftop systems are unchanged. Do not copy the original checkout's unrelated verification output, untracked PowerMechanics scripts or vendor metadata changes into this feature. The PoisonProjectilePool metadata here is the exact pre-existing GUID from the original checkout.
