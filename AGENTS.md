# Overpowered

Unity 6000.6 C# superhero prototype: home screen, data-driven Hero/Villain sessions, running, jumping, limited flight, physics powers, procedural animation, and a seeded city with persistent progression and Heat-driven police.

Tune gameplay in `Assets/Resources/GameTuning.asset`, power definitions in `Assets/Resources/Powers`, and visuals in `ProceduralAnimationTuning.asset`. Keep legacy `PrototypeTuning` constants in place. Read `STATUS.md` for implemented scope and verification limits; preserve existing movement and presentation event contracts when extending abilities.

Modes live in `Resources/Modes`, reusable rules in `ModeRules`, and multi-part set-pieces in `Encounters`. Add definitions/rules rather than mode-ID switches. Start at `Assets/Scenes/Home.unity`. Verify with positive and negative controls and separate-process save reloads; document real output and untested feel/performance limits.

City art uses `Resources/CityPalette.asset` for every generated material and `CityArtSettings.asset` for styles, dimensions and seeded placement rules. Keep `CityLayout` as the layout authority. Use shared palette materials and the existing `BreakableProp`; do not add one-off materials or a second destruction system. Preserve measured performance regressions in STATUS rather than hiding detail cuts.

Humanoids share `Resources/SharedHumanoid.controller` and `HumanoidAnimationTuning.asset`. Animator owns clips; `HumanoidPresentation` owns weighted flight/panic bones and visual lean; the old procedural component owns landing squash only. Never animate the physics root or enable root motion. Punch resource payment is immediate, force follows the configured windup; tests must wait for impact. Use `HumanoidVerification.Run` for the current presentation, not the historical capsule-hierarchy verifier.

Hero Forge uses `Resources/ForgeCatalog.asset`, hero definitions and unordered pair/effect assets under `Resources/Forge`. Persist selection only through `PlayerProgression`; gameplay caches its two equipped powers for the session. Do not bypass PowerUser's equip/ownership gates. Basic E/RMB melee remains weaker without Strength; equipped Strength retains its charges. Extend SynergyEffect assets instead of hero/pair ID switches. Effects use the fixed pool, shared palette materials and existing combat/destruction. See `docs/hero-forge.md`; verify with `HeroForgeVerification.Run` and a separate-process `Reload`. Historical all-powers-at-once verifiers need explicit loadout setup now.

The existing ThirdPersonCamera also owns first-person view (B), eye collision and additive flight/synergy FOV. Tune GameTuning.Camera; persist only ProgressSave.FirstPerson. Hide only player visual renderers, never physics/Animator. Aimed powers use PowerUser's view-aware origin; Fire's spawn sweep must not bypass nearby walls. See docs/first-person.md and FirstPersonVerification.Run/Reload controls.
