# Power Expansion 2.0 — architecture and phased enablement

Base: d70ccc1 (complete three-hero artwork plus verified two-mode/Pulsebreaker integration). Isolated branch feature/power-expansion-2. Original checkout is concurrently on feature/health-on-kill; its uncommitted work is not imported or edited. No merge/push.

## Existing extension points verified

- PowerDefinition/PowerEffect and PowerUser.Use own activation, ownership/equip gates, charge/energy payment, upgraded stats, difficulty and attribution. New abilities must use that path. Melee E/RMB uses the retained legacy Strength runtime only when actually equipped; otherwise existing universal basic attacks remain available.
- PowerUser.Credit scopes deferred damage; CityNpc.Damage is the authoritative HP/guard/difficulty/kill-event boundary. No replacement damage/healing pipeline is needed. Enemy control can be extended through CityNpc's existing partial class and finite NavMesh motion instead of permanently suspending bodies.
- SignatureCatalog already maps one OwnerPrimary to one effect, and SynergyRunner supplies payment, cooldown, credited coroutines, cancellation and motion ownership. Its completeness gate currently hard-codes eight primary powers. It must count the selectable roster and an explicit expected count, while retaining the current eight until the new ten are ready.
- ForgeCatalog and HeroDefinition.AvailablePowers are the shared selection gates for all heroes. HeroForgeScreen is the existing code-built UI Toolkit screen with independent appearance records. PlayerProgression.ValidateLoadout reconstructs HeroLoadout; migration must run before validation and retain old ownership/progression records.
- Existing IDs are strength and laser-eyes; signatures seismic-slam and prism-barrage. Intended aliases are combat-morphing, plasma-blades, apex-mutation and blade-cyclone. Duplicate migrated slots must deterministically keep two distinct valid powers. Outer ProgressSave remains Version 1.
- SuperHeroController.MoveAbility is CharacterController collision-aware; it currently reserves driving motion to SynergyRunner. Grapple needs a narrow additional motion owner without replacing the controller. Jump/double-jump/climb/backflip events and the first-person camera remain authoritative.
- CityNpc commits melee/ranged shapes against the hero. Mirage requires a temporary target hook covering both movement/aim commitment and actual release damage, not merely attracting navigation. A decoy is not a CityNpc and cannot award kill XP/healing or progress missions.
- HumanoidPresentation owns the shared Animator. New weapon meshes attach to existing forearm bones and use presentation-only action hooks. They must be removed/hidden on power change, death, menus and first-person view as appropriate. A frozen-pose hologram can reuse shared skinned meshes and cloned bone transforms without an Animator or gameplay/profile components.

## Baseline tuning observed (tier 0)

Fire27 damage, Ice10, Darkness6.5, Lightning44.064, Poison12.5 per tick, legacy Strength35, Laser42 channel mean. These values are not rebalanced. All new attacks must roll through PowerUser.RollDamage and preserve source/credit through delayed hits.

## Implementation sequence

1. Add explicit selection/completeness gates; new definitions remain hidden while incomplete.
2. Wind Blast and Sonic Burst: distinct directional geometry, moderate damage, finite displacement vs short interruption with resistance/refractory windows; native positive/negative tests and commit.
3. Three intentional morph forms and bounded plasma combo/charged strike, shared fitted weapon meshes and existing animation actions; preserve E/RMB; native tests and commit.
4. Collision-aware grapple/release and finite one-decoy targeting, authoritative hit handling and cleanup; native tests and commit.
5. Four finite signatures via existing SynergyEffect/SynergyRunner; test each and commit.
6. Atomically author the 10-primary/6-secondary/10-signature catalog and all hero arrays, enable the complete roster, migrate aliases/tiers/signatures additively, verify all 45 unordered pairs, UI/cosmetics and separate-process reload.
7. Execute relevant existing-power, movement, enemy/boss, mode, first-person and bounded-resource regressions. Append exact results and limits to STATUS; preserve older evidence.

Proposed additional configurable controls: V cycles morph form; hold/release G commits the charged plasma strike. Existing code has no V/G bindings; 1/2 selects primaries, LMB activates, F secondary, C signature, E melee combo, RMB Hurricane, B view, Q backflip, Space jumps. Verify actual HUD and input behavior before final enablement.

Specialized art is functional stylized starter work, not final artist-approved polish. Native small weapon/effect geometry may be authored, but existing body meshes/Avatars/skeletons/cosmetic assets stay intact. Use fixed shared meshes, palette/VFX materials, finite pools/scans and no per-frame appearance reconstruction. Built-in Render Pipeline remains.
