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
