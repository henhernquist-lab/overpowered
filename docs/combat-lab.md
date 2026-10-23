# Combat Lab and enemy expansion

Current integration (2026-10-23): the full enemy expansion is now combined with the playable game's parkour and Home **BATTLE LAB** route on `codex/synergies-2-final-four`. Launch from Home; both new enemies are selectable in the lab and join both Endless variants at waves 3 and 5. [Combined-game verification and integration notes](combat-integration.md). The dated feature-delivery notes below describe the earlier isolated branch and its historical evidence; their no-merge status is superseded by the developer's integration request.

Baseline: `2ed79a9` (complete Synergies 2.0). Work is isolated on `codex/combat-lab-enemy-expansion`; no merge or push.

The original checkout contained two untracked Power Mechanics verification scripts and their metadata, plus metadata for the already tracked PoisonProjectilePool. The two test scripts are not runtime dependencies and remain untouched in the original. The stable PoisonProjectilePool metadata is copied byte-for-byte into this branch so import does not assign a different GUID. No original checkout files are edited.

## Launch and practice safety

Open `Assets/Scenes/CombatLab.unity` and press Play, or use **Overpowered > Combat > Open Combat Lab**. The scene is in Build Settings for arena resets. GameFlow has one early scene dispatch; the normal Home/Prototype/Results flow is unchanged. The current game also exposes this scene through Home’s BATTLE LAB button.

The lab copies the current saved profile read-only, deep clones it and initializes PlayerProgression with a null SavePath. Profile repair, XP, usage stats, side/view/difficulty edits and quit saves cannot write to the personal profile. WorldSession practice skips Heat, rewards, police, crimes and mode creation. Its CityDistrict is an empty metadata holder, never a generated city. Baked arena navigation is removed on scene exit.

F2 or Esc opens/closes the control panel and pauses combat. Choose an archetype, Spawn one or Spawn group, Clear lab enemies, Restore health, Restore energy, select difficulty, Reset arena + cooldowns, or Return Home. Standard equipped movement/power/signature controls remain active when the panel is closed. Change loadout in Hero Forge before reopening the lab.

Reset reloads this scene with the starting in-memory snapshot; it clears all effects, props, corpses and cooldowns through their existing lifecycle. Difficulty selection uses SetDifficulty on the transient profile and restarts so SessionDifficulty remains an immutable snapshot. There is no separate cooldown manager.

Tuning: Resources/CombatLabSettings.asset; 72 m arena, 13 m spawn ring, 6 m minimum player distance, group 3, cap 12. Active actors are capped; old corpses are deactivated and scheduled for removal before a slot is reused. Clear removes only lab-owned actors. Cover is static; throwable crates use BreakableProp. All generated meshes/materials are shared.

## Verification log

The initial handoff used static compilation while the exclusive Unity slot was unavailable. The developer subsequently reserved Unity for Codex combat. This worktree has now been imported and verified with strictly serial Unity 6000.6.0f1 batch processes. Current results and retained failed attempts appear below; human feel and standalone performance remain unverified.

## Skirmisher

Evasive melee is opt-in data on EnemyArchetype, not a role or power-ID switch. Skirmisher uses 7.8 m/s approach, 0.75x base health, 0.6x attack damage, 0.38 s first windup, two strikes with 0.26 s followup windup, 1.4 s final recovery. The existing token stays held across the bounded burst.

Dodge/dash: 4.5 s cooldown, 0.22 s visor tell, at most 2.2 m lateral / 3 m forward, 0.32 s travel and 0.6 s stationary recovery; 1.8 m post-burst retreat. Destination, straight NavMesh lane, capsule clearance and complete path are checked before moving and again after the tell. NavMeshAgent owns movement; no warp, invulnerability or disabled colliders. Ice/death/pause/suspension cancel movement and pending burst; Darkness root cancels movement while preserving the existing rooted-attack contract. A shared mannequin rig plus shared helmet/visor/shoulder pieces supplies the silhouette.

`CombatLabVerification.Run` now contains runtime controls for original/new spawns, resolved event/death cardinality, spawn cap, clear ownership, transient saves, restoration, reset and finite vulnerable evasion. These controls executed successfully; the selected Run has 369 passing checks, including actual Skirmisher damage to player health.

## Bulwark

Bulwark uses 2.7 m/s movement, 1.8x base health (Brute remains 3x), 1.5x base damage, a 1.05 s locked bash windup and 2.5 s unguarded recovery. Its shield faces the player at up to 85 degrees/s while approaching; the windup uses the existing committed attack shape and animation. Shared plate/stripe/armor pieces make the guard direction visible. Shield drops/tilts during windup, recovery, disable/suspension, freeze/root and guard break; absorbed hits flash cream and broken guard shows red.

Defense is opt-in EnemyArchetype.Guard data. The central CityNpc.Damage operation resolves difficulty once, then a 110-degree forward cone absorbs 65% of damage (at least 10% can always get through even if configured higher). Sides/rear/overhead remain exposed. Absorbed damage spends 75 integrity; depleting it makes that blow fully effective and breaks guard for 1.8 s. An impact >=900 breaks guard before applying damage. Freeze/root use the same generic disruption lease, with no power-ID exemptions. Repeated control can extend the lease but it expires after control stops.

Damage accepts an optional world hit origin; the shared CombatImpact area path supplies its blast origin. Existing direct/deferred callers fall back to the attacker's current capsule centre. This deliberately avoids changing each power. Poison/beam ticks still do damage, spend guard integrity and use normal resolved events; no categorical immunity. Indirect callers without an explicit origin use the fallback (not a full ballistic hit-normal model). NpcDamageResult adds DamageBeforeGuard and GuardAbsorbed; ResolvedDamage/HealthLost, usage, health bars and floating numbers share the final reduced value.

Guard runtime controls cover front/side/rear, resolved-event count, heavy impulse, finite rearm, root/freeze, telegraphed bash, recovery and death. Native guard controls pass, including real health loss and once-only difficulty composition in Normal, Hard and Extreme.

## Waves

Only Endless Fight Hero and Endless Fight Villain have a round director. Both share EndlessWaves. Their smooth weighted round-robin table is Rusher from 1 (weight 3), Gunner from 2 (2), Brute from 3 (1), Skirmisher from 3 (2), Bulwark from 5 (1). Existing size, 12-alive cap, reinforcement, spawn validity, faction selection, kill accounting and health/damage escalation formulas are unchanged. The new definitions participate in the existing data table; no new round code, mode switches or miniboss/event enabling.

Hero/Villain campaign modes remain encounter-based. Free Play still has a null director, no automatic waves, and unchanged manual/debug spawning. CombatLabVerification checks eligibility, both side roles, once-only stat composition, unchanged non-round modes and capped targets. ModeExpansionVerification's live-wave roster assertion now expects five definitions; its existing real kill/reinforcement/wave advancement controls remain in place.

## Final verification and delivery (2026-10-21 UTC)

- Branch: `codex/combat-lab-enemy-expansion`.
- Worktree: `/Users/melaniehernquist/.codex/worktrees/combat-lab-enemy-expansion/op-overnight-recovery`.
- Baseline: `2ed79a9`; phases: `c03745b` lab foundation, `39666d2` Skirmisher, `a6bb246` Bulwark, `2bc60ef` Endless integration. `d31ad6c` records the initial static handoff, `d0e9cca` fixes native initialization/evasion, and `948a695` fixes native UI layout and matrix fixtures. The final verification commit records the stronger controls and completed native evidence; use `git log -1` for its hash.
- No merge, push, rooftop feature changes, main-city generation changes, universal movement changes, power/signature retuning or vendor material edits.
- Full created/modified file inventory: `Verification/CombatLab/delivery-files.txt` (including metadata). Production input hashes and baseline comparisons: `Verification/CombatLab/delivery-evidence.json`.

<!-- NATIVE-EVIDENCE-START -->
Selected native evidence: **6,161 PASS / 0 FAIL across 12 serial Unity 6000.6.0f1 processes**, all exit 0, all personal-save and Sidekick database hashes unchanged. No merge or push. Selection and exact source hashes: `Verification/CombatLab/selected-evidence.json`; full process records, checks and observations remain in the named attempt folders. Raw `.log` files remain local and ignored.

| Entry | PASS / FAIL | Seconds | Evidence folder |
| --- | ---: | ---: | --- |
| `CombatLabVerification.Run` | 369 / 0 | 82.97 | `final-gates-01` |
| `CombatLabVerification.Reload` | 4 / 0 | 41.25 | `final-gates-01` |
| `CombatLabVerification.Matrix` | 747 / 0 | 255.45 | `presentation-attempt-02` |
| `CombatLabPresentationVerification.Run` | 65 / 0 | 54.0 | `presentation-final` |
| `ModeExpansionVerification.Run` | 179 / 0 | 108.62 | `final-gates-01` |
| `ModeExpansionVerification.Reload` | 10 / 0 | 47.42 | `final-gates-01` |
| `CombatVerification.Run` | 179 / 0 | 168.51 | `regressions-02` |
| `CombatBalanceVerification.Run` | 2346 / 0 | 247.46 | `regressions-02` |
| `HeroForgeVerification.Run` | 136 / 0 | 96.66 | `regressions-02` |
| `HeroForgeVerification.Reload` | 3 / 0 | 43.21 | `regressions-02` |
| `FinalSignaturesVerification.Matrix` | 708 / 0 | 346.09 | `regressions-02` |
| `DifficultyVerification.Run` | 1415 / 0 | 134.72 | `regressions-02` |

Static recheck: **2 successful assemblies, 31 executable guard-policy checks, 80 asset/data/baseline checks; 0 failures**. The three existing analyzer warnings are RobberyState.Car serialization, nullable HeroPresentationState verification serialization, and AudioSetup AppDomain.GetAssemblies. Compiler/reference and topology outputs are under `Verification/CombatLab/static`.

Run verifies real attack/death, finite hittable evasion and recovery, flank/rear/front guard, finite break/rearm, ordinary root/freeze, single resolved events, actual HP/difficulty, cap/owned clear, source isolation and reset. Reload uses a fresh process. Presentation submits the actual UI buttons/dropdowns, renders 720p/1080p, checks live front-guard damage in all three difficulties, shared attachment mesh and ten clean arena resets. Captures are in `Verification/CombatLab/captures`.

Matrix: eight primaries and eight selected signatures x two new enemies x three difficulties (48 sessions), plus **four currently selectable Forge secondaries**: Phase Shift, Speed, Force Field and Teleport. The legacy Flight asset still exists but no current hero permits it; its equip refusal is verified. No ownership/loadout gate was bypassed. Matrix targets are stationary, non-guarding compatibility fixtures; active evasion/guard are checked by Run. This does not establish equal power effectiveness or balance.

Mode/Combat verify real round clear/reinforcement, both factions, the full eligible mix, kill progression and unchanged Free Play. Balance verifies actual damage/health rolls, impacts, bars/numbers and DoT spread. Forge Run/Reload, final signatures and difficulty are fresh branch regressions. Fresh output from 29 generated historical verification paths is archived separately under `Verification/CombatLab/regression-artifacts`; the historical files were then restored byte-for-byte, indexed with hashes.

### Retained failures and fixes

- Initial Run failed in the NavMeshPath field initializer; creation now occurs in arena initialization. Native testing also found post-burst retreats could keep postponing the independent dodge cooldown; retreat now holds only its own movement/recovery lease.
- The fresh-worktree Editor Search exception was addressed with the local preference described below.
- Initial matrix failed three fixture assertions (unselectable legacy Flight and blocked/ungrounded Teleport); corrected through real Forge eligibility and a grounded clear lane. All 48 primary/signature sessions had passed in that failed attempt.
- Initial presentation failed 16 layout checks; corrected full-screen root anchoring, button/dropdown styling and practice HUD flags. The actual controls and reset path already worked.
- Legacy Mode/Combat benchmarks hit wall-clock/watchdog failures; frame loops now use 120 warm / 240 measured / 180 profiled frames with the same scene, camera and elapsed-time measurements. Combat's watchdog now uses a monotonic 900-second clock; Mode's allowance is 1800 Editor seconds.
- The old combat fixture expected three archetypes; it now derives the eligible count from wave data. Failed process manifests/checks and original failed UI captures are retained; none are counted in selected passing evidence.

### Measured performance and limits

Single explicit 1280x720 camera render per Editor frame, 180 measured lab frames, Intel i9-10910 / Radeon Pro 5300. Accepted lab sample (final-gates-01), idle render CPU median/p95 **0.716/0.764 ms**, whole frame **2.672/3.078 ms**, GC median **1338 B/frame**. Twelve live mixed enemies: render **1.271/1.439 ms**, whole frame **8.718/10.252 ms**, GC **1355 B/frame**. Earlier successful sample: idle **2.479/3.194 ms**, mixed **9.964/22.436 ms** whole-frame median/p95. Earlier pilot idle **2.441/2.771**, mixed **9.577/13.237 ms** is retained. These short runs show variability; they do not establish sustained FPS, first-use cost, a like-for-like prefeature delta or standalone performance.

City wave sample (240 measured frames, hovering hero kept alive): wave 1 / 3 enemies **181.22 FPS**, mean/p95 **5.52/5.87 ms**; wave 5 / 11 enemies **83.10 FPS**, **12.03/16.29 ms**; wave 6 / 12 enemies **78.27 FPS**, **12.78/16.78 ms**. Skinning **0.201/0.557/0.602 ms**; batch-mode overhead **0.994/3.124/3.548 ms**. Separate grounded wave-five combat sample: **92.68 FPS**, mean/p95 **10.79/14.80 ms**, 10 telegraphed attacks, max two simultaneous windups, skinning **0.545 ms**. Existing Forge benchmark idle/FX1/FX2/idle reported **50.96/50.37/45.16/40.65 FPS**, draws **307.1/314.3/315.0/306.0**, pool 6. All observations remain visible; these different fixtures cannot be compared as a controlled feature regression. Historical STATUS performance data is preserved.
<!-- NATIVE-EVIDENCE-END -->

Latest presentation observations (`Verification/CombatLab/presentation-final`):

```text
MEASURED lab idle: one explicit 1280x720 camera render per frame, 180 frames; render CPU median/p95 0.587/0.653 ms; whole-Editor frame median/p95 2.279/2.563 ms; whole-frame GC median 1338 B; live NPCs 0. Not standalone FPS or a sustained combat benchmark.
MEASURED lab 12 mixed enemies: one explicit 1280x720 camera render per frame, 180 frames; render CPU median/p95 1.111/1.471 ms; whole-Editor frame median/p95 7.424/9.656 ms; whole-frame GC median 1355 B; live NPCs 12. Not standalone FPS or a sustained combat benchmark.
```


### Reproduce verification

Reserve the single Unity slot across all worktrees first. From this branch run:

```sh
python3 tools/verify-sequential.py --out Verification/CombatLab/new-run --combat-roll 0.5 \
  CombatLabVerification.Run CombatLabVerification.Reload CombatLabVerification.Matrix \
  CombatLabPresentationVerification.Run ModeExpansionVerification.Run ModeExpansionVerification.Reload \
  CombatVerification.Run CombatBalanceVerification.Run \
  HeroForgeVerification.Run HeroForgeVerification.Reload \
  FinalSignaturesVerification.Matrix DifficultyVerification.Run
```

The driver refuses an already-running Editor, stops on the first failed process/check, records fresh output and guards personal-save/Sidekick database hashes. A Reload requires its matching Run in the same invocation. Keep other agents out of Unity for the whole sequence. Restore Side_Kick_Data.db if Play Mode changes it; no completed process in this delivery changed its hash.

The fresh worktree exposed an Editor Search startup indexation exception before gameplay. Only this worktree's ignored `UserSettings/Search.settings` now has `indexOnEditorStartup = false` and `version = 1`; the prior `{}` is backed up at `/private/tmp/op-combat-search-settings-before`. No global or original-project preference changed, and no runtime error was filtered to pass a check. The workaround is local and uncommitted.

Static checks (do not launch Unity):

```sh
python3 tools/verify-combat-static.py --reference-project /Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery
python3 tools/verify-combat-data.py
```

### Known limits and remaining work

The requested four features are implemented and native automated verification passes as recorded above. Human playtesting and standalone performance acceptance remain before integration; no balance or polish claim is made. The dedicated scene is intentionally minimal: arena geometry, lighting, NavMesh and actors are constructed by CombatLab at runtime, with no main-city generator. Entry is via the scene/Editor menu, not a new Home button. A separate instant per-ability cooldown button is omitted; Reset Arena uses existing initialization to refresh every cooldown safely.

Hit origins: CombatImpact area attacks provide their true origin. Other existing callers use the attacker's current centre unless they supply the new optional origin. A moving attacker can therefore change the guard relation of an old DoT/indirect hit; this is an explicit limitation of the existing metadata, not a new per-power exception. Continuous damage still spends finite guard integrity. Unknown/environmental hits without a source or origin bypass the directional reduction.

Navigation/evasion destinations are validated against both the NavMesh and a bounded capsule sweep before travel, including a second check after the tell. Dynamic obstacles can still move into the route during travel; the existing NavMeshAgent owns avoidance. Human testing must check fast movement near throwable props and tight cover. No enemy collider is disabled to dodge, and no navigation Warp/physics handoff was added for evasion.

Short rendered Editor samples are recorded below; sustained standalone performance has **not** been measured. New attachments reuse Unity's shared cube mesh and palette materials; no new mesh/material per enemy or hit, and no new VFX pool is required for guard feedback. Evasion holds one NavMeshPath only on evasive actors, uses a fixed 24-hit buffer and a finite cooldown. Lab enemies cap at 12 (configurable 1–24); each burst caps at 3 via data (shipping 2). Existing humanoid skinning/fitting, damage queries and signatures retain their costs; all historical STATUS measurements remain intact. The native presentation suite samples 12 live mixed enemies and verifies ten clean resets. Longer combat with varied powers and a standalone profile still require human acceptance.

### Exact human playtest checklist

1. With Gemini/other Editors closed, open this worktree in Unity 6000.6.0f1. Wait for import; confirm no new Console errors or USS/UXML warnings. Open CombatLab and Play. Confirm bright flat floor, cover, crates, selected hero, current difficulty and standard HUD.
2. F2/Esc: open/close controls. Confirm combat pauses while controls are open and resumes afterward. Select each of five types; Spawn one and group. Reach the cap and confirm additional spawns refuse. Clear; confirm only lab-owned enemies disappear.
3. Take damage/spend energy; restore each separately. Reset during a DoT/signature, then during a channel and held prop. Confirm clean arena, fresh vitals/charges/cooldowns, no old attacks, status markers, projectiles, held objects or pooled effects. Fall/fly beyond the arena; confirm normal respawn to the floor.
4. Fight one Skirmisher at normal speed. Observe a visible tell, short lateral dodge/occasional approach dash, two readable strikes, retreat and recovery. Land precise shots and timed melee during travel/recovery. Freeze/root it and ensure movement stops, expires and resumes without sliding through cover. No permanent dodge/invulnerability loop.
5. Fight one Bulwark. Compare repeated equal hits from front, flank and rear using actual numbers and HP. Bait its slow bash, leave the committed disc, then punish unguarded recovery. Watch cream shield hits, red break and finite rearm. Break it with heavy impact, Ice and Darkness. Try sustained Laser/Poison and overhead/nearby AoE; none should be categorically ineffective.
6. Repeat with eight primaries, all four currently selectable secondaries and all eight signatures using valid saved Forge builds. Test first- and third-person view. Check health bars, resolved damage numbers, death, collision, status cleanup and no duplicated hits. Do not judge effectiveness from the stationary matrix alone.
7. Select Hard and Extreme in the lab; confirm a full arena restart, the new label, expected max health/enemy HP/resource/cooldown changes and unchanged faction relations. Return Home/restart the application; personal XP, points, records, hints, difficulty, side, view and Forge loadout must match the pre-lab save.
8. Play both Endless sides through rounds 1–6. Originals only before 3; Skirmisher appears from 3, Bulwark from 5; later mix includes originals. Kill both new types and confirm LEFT/score/next wave/reinforcements and cap. Check Hero police remain friendly and Villain responders hostile. Check campaign encounters and Free Play manual spawning; Free Play must never auto-start waves.
9. Profile 12 mixed live enemies, movement around cover, repeated guard hits, continuous beams/DoT, multiple signatures and 10 arena resets. Compare CPU, allocation, renderers/materials/meshes and pools before/after. Check a standalone build separately. The human developer decides actual feel and tuning changes.

### Merge notes

Integrate the combat branch after reviewing the Unity gates; it has not been merged or pushed. Preserve the standalone CombatLab scene GUID/build-settings entry and all new metadata. The shared integration points are CityNpc, EnemyArchetype, CombatImpact, PlayerProgression, PowerUser, WorldSession, a one-line GameFlow.SceneReady dispatch, and GameHud choosing Health/Powers flags in practice. Practice hides Heat/XP HUD elements; other modes retain their flags. The two legacy render benchmark fixtures use bounded frame samples, and combat/mode verification expectations now derive from the expanded roster. Reconcile GameFlow/build-settings additions if Gemini also registers an entry, and append/reconcile both STATUS sections. CityDistrict, CityLayout, universal movement and rooftop systems are unchanged. Do not copy the original checkout's unrelated verification output, untracked PowerMechanics scripts or vendor metadata changes into this feature. The PoisonProjectilePool metadata here is the exact pre-existing GUID from the original checkout.
