# Combat Lab and enemy expansion

Baseline: `2ed79a9` (complete Synergies 2.0). Work is isolated on `codex/combat-lab-enemy-expansion`; no merge or push.

The original checkout contained two untracked Power Mechanics verification scripts and their metadata, plus metadata for the already tracked PoisonProjectilePool. The two test scripts are not runtime dependencies and remain untouched in the original. The stable PoisonProjectilePool metadata is copied byte-for-byte into this branch so import does not assign a different GUID. No original checkout files are edited.

## Launch and practice safety

Open `Assets/Scenes/CombatLab.unity` and press Play, or use **Overpowered > Combat > Open Combat Lab**. The scene is in Build Settings for arena resets. GameFlow has one early scene dispatch; the normal Home/Prototype/Results flow is unchanged. No Home menu integration is required.

The lab copies the current saved profile read-only, deep clones it and initializes PlayerProgression with a null SavePath. Profile repair, XP, usage stats, side/view/difficulty edits and quit saves cannot write to the personal profile. WorldSession practice skips Heat, rewards, police, crimes and mode creation. Its CityDistrict is an empty metadata holder, never a generated city. Baked arena navigation is removed on scene exit.

F2 or Esc opens/closes the control panel and pauses combat. Choose an archetype, Spawn one or Spawn group, Clear lab enemies, Restore health, Restore energy, select difficulty, Reset arena + cooldowns, or Return Home. Standard equipped movement/power/signature controls remain active when the panel is closed. Change loadout in Hero Forge before reopening the lab.

Reset reloads this scene with the starting in-memory snapshot; it clears all effects, props, corpses and cooldowns through their existing lifecycle. Difficulty selection uses SetDifficulty on the transient profile and restarts so SessionDifficulty remains an immutable snapshot. There is no separate cooldown manager.

Tuning: Resources/CombatLabSettings.asset; 72 m arena, 13 m spawn ring, 6 m minimum player distance, group 3, cap 12. The cap includes actors waiting for destruction; Clear removes only lab-owned actors. Cover is static; throwable crates use BreakableProp. All generated meshes/materials are shared.

## Verification log

Foundation: both runtime and Editor C# assemblies compiled with the installed Unity Roslyn compiler, using references from the original checkout's existing Library. No Editor process was launched for this check. Three existing serialization/API analyzer warnings remain. Import, UI rendering, gameplay and human feel are not established by a static compilation. Unity slot coordination is pending.

## Skirmisher

Evasive melee is opt-in data on EnemyArchetype, not a role or power-ID switch. Skirmisher uses 7.8 m/s approach, 0.75x base health, 0.6x attack damage, 0.38 s first windup, two strikes with 0.26 s followup windup, 1.4 s final recovery. The existing token stays held across the bounded burst.

Dodge/dash: 4.5 s cooldown, 0.22 s visor tell, at most 2.2 m lateral / 3 m forward, 0.32 s travel and 0.6 s stationary recovery; 1.8 m post-burst retreat. Destination, straight NavMesh lane, capsule clearance and complete path are checked before moving and again after the tell. NavMeshAgent owns movement; no warp, invulnerability or disabled colliders. Ice/death/pause/suspension cancel movement and pending burst; Darkness root cancels movement while preserving the existing rooted-attack contract. A shared mannequin rig plus shared helmet/visor/shoulder pieces supplies the silhouette.

`CombatLabVerification.Run` now contains runtime controls for original/new spawns, resolved event/death cardinality, spawn cap, clear ownership, transient saves, restoration, reset and finite vulnerable evasion. These controls are prepared and compile; they have not yet been executed in Unity.

## Bulwark

Bulwark uses 2.7 m/s movement, 1.8x base health (Brute remains 3x), 1.5x base damage, a 1.05 s locked bash windup and 2.5 s unguarded recovery. Its shield faces the player at up to 85 degrees/s while approaching; the windup uses the existing committed attack shape and animation. Shared plate/stripe/armor pieces make the guard direction visible. Shield drops/tilts during windup, recovery, disable/suspension, freeze/root and guard break; absorbed hits flash cream and broken guard shows red.

Defense is opt-in EnemyArchetype.Guard data. The central CityNpc.Damage operation resolves difficulty once, then a 110-degree forward cone absorbs 65% of damage (at least 10% can always get through even if configured higher). Sides/rear/overhead remain exposed. Absorbed damage spends 75 integrity; depleting it makes that blow fully effective and breaks guard for 1.8 s. An impact >=900 breaks guard before applying damage. Freeze/root use the same generic disruption lease, with no power-ID exemptions. Repeated control can extend the lease but it expires after control stops.

Damage accepts an optional world hit origin; the shared CombatImpact area path supplies its blast origin. Existing direct/deferred callers fall back to the attacker's current capsule centre. This deliberately avoids changing each power. Poison/beam ticks still do damage, spend guard integrity and use normal resolved events; no categorical immunity. Indirect callers without an explicit origin use the fallback (not a full ballistic hit-normal model). NpcDamageResult adds DamageBeforeGuard and GuardAbsorbed; ResolvedDamage/HealthLost, usage, health bars and floating numbers share the final reduced value.

Guard runtime controls cover front/side/rear, resolved-event count, heavy impulse, finite rearm, root/freeze, telegraphed bash, recovery and death. Static runtime/Editor assembly compilation passes; Unity execution is pending the exclusive slot.

## Waves

Only Endless Fight Hero and Endless Fight Villain have a round director. Both share EndlessWaves. Their smooth weighted round-robin table is Rusher from 1 (weight 3), Gunner from 2 (2), Brute from 3 (1), Skirmisher from 3 (2), Bulwark from 5 (1). Existing size, 12-alive cap, reinforcement, spawn validity, faction selection, kill accounting and health/damage escalation formulas are unchanged. The new definitions participate in the existing data table; no new round code, mode switches or miniboss/event enabling.

Hero/Villain campaign modes remain encounter-based. Free Play still has a null director, no automatic waves, and unchanged manual/debug spawning. CombatLabVerification checks eligibility, both side roles, once-only stat composition, unchanged non-round modes and capped targets. ModeExpansionVerification's live-wave roster assertion now expects five definitions; its existing real kill/reinforcement/wave advancement controls remain in place.
