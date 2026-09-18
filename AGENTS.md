# Overpowered

Unity 6000.6 C# superhero prototype: home screen, data-driven Hero/Villain sessions, running, jumping, limited flight, physics powers, procedural animation, and a seeded city with persistent progression and Heat-driven police.

Tune gameplay in `Assets/Resources/GameTuning.asset`, power definitions in `Assets/Resources/Powers`, and visuals in `ProceduralAnimationTuning.asset`. Keep legacy `PrototypeTuning` constants in place. Read `STATUS.md` for implemented scope and verification limits; preserve existing movement and presentation event contracts when extending abilities.

Modes live in `Resources/Modes`, reusable rules in `ModeRules`, and multi-part set-pieces in `Encounters`. Add definitions/rules rather than mode-ID switches. Start at `Assets/Scenes/Home.unity`. Verify with positive and negative controls and separate-process save reloads; document real output and untested feel/performance limits.
