# Overpowered

A Unity/C# superhero blockout: a compact city, physical combat, five upgradeable powers, Hero/Villain objectives, and escalating police response. Procedural character animation is retained; no animation clips or Animator controller are required.

## Play

Open this folder with **Unity 6000.6.0f1**, open `Assets/Scenes/Home.unity`, and press Play. Choose Hero Mode or Villain Mode. Home is first in Build Settings; pressing Play directly in Prototype also redirects to Home. The existing district, NavMesh, player, and NPCs generate when a mode is selected. Free Play and Endless Fight are visible but disabled.

- WASD move, Left Shift run, Space jump.
- Hold F while airborne to fly; Space ascends, release directional input to hover. Fuel recharges on the ground.
- E always punches. Left mouse uses the selected power. Number keys match the slots listed in the power menu.
- Tab opens the power/unlock/upgrade menu (world continues). Escape truly pauses and offers resume, results, or return home.
- Telekinesis: aim at a prop, click to grab, click again to hurl. Changing powers or reaching the hold timeout releases it.
- Hero: stop all fleeing robbers (capture with held R or defeat), punch/throw the brown blockades away from cyan civilians, then hold R nearby to rescue. Hold R at orange fire markers to extinguish arson.
- Villain: hold R at gold loot and orange sabotage markers, destroy at least three encounter props, then move/fly more than 22m from the site. Police attack and escalate with Heat.
- Side is locked by the selected mode; choose another at Home. Find cyan rooftop discoveries for one-time XP rewards.

Complete five encounters to win. Three failed encounters or three defeats loses; 15 minutes produces a timeout. Events spawn every 50s (up to two), with a 210s deadline. Hero failure also raises Heat by 0.75; rescue success lowers it by 1. Robbers try to escape after 45s and unattended civilians take damage after 90s. Results show score and XP; earned progression survives every outcome.

Flight and Super Strength start unlocked. Earn XP and explicitly spend Power Points to unlock Telekinesis, Fire Blast, or Ice, or upgrade an owned power. The first two deaths respawn the player and retain progression.

## Tune in the Inspector

Select these Project assets; no code edits are needed for numeric gameplay tuning:

- `Assets/Resources/GameTuning.asset`: movement, city seed/dimensions, NPCs, progression, Heat, crime timing, prop physics, and camera settings, grouped by system.
- `Assets/Resources/Powers/*.asset`: each power's costs, charges, cooldown, resource use, force/damage, range/duration, and upgrade tiers.
- `Assets/Resources/ProceduralAnimationTuning.asset`: punch, flight, run, and landing visuals.
- `Assets/Resources/Modes/*.asset`: mode catalog, side, rules, encounter pools, HUD flags, session limits, population, score/XP/Heat rewards.
- `Assets/Resources/Encounters/*.asset`: actor/prop counts, escape/rescue/destruction requirements, timings and interaction distances. `ModeRules/` holds the reusable Hero/Villain rule assets.
- `Assets/Resources/CityLayout.asset`: optional authored building placements. **Overpowered → Bake generated buildings into layout data** captures the seeded placements for editing. Change the seed and restart Play Mode to regenerate; disable `Use Authored Buildings` to return to seeded generation. Building positions/sizes remain separate from generation logic for a later hand-built/ProBuilder environment.

Existing constants remain in `PrototypeTuning.cs` as legacy defaults. Runtime balance now comes from the Inspector assets. Avoid editing definitions' stable `Id` values after players have saved progression.

To add another power using existing behavior, create/duplicate a PowerDefinition asset under `Resources/Powers`, give it a unique ID, assign one of the existing effect assets, and tune it. Catalog discovery, selection UI, cooldowns, upgrades, and persistence are generic. A genuinely new behavior requires one new PowerEffect subclass and its asset, not changes to registries or switches.

To add a mode, duplicate a `GameModeDefinition` under `Resources/Modes`, give it a stable unique ID, assign rules and encounter definitions, and set Playable. Menu discovery, city loading, pause/results, population, limits and save integration need no registry edits. New objective behavior belongs in a new `ModeRules` subclass/asset. Existing movement, powers and animation do not depend on mode IDs.

## Save data

Versioned JSON is stored in `Application.persistentDataPath/overpowered-progression.json`. It saves level, XP, points, side, owned powers/tiers, claimed rooftop discoveries, session/win counts, best score and last session's mode/XP. The new fields are additive: older version-1 saves start with zero session statistics. Saves use temporary-file replacement and keep a `.bak`. Health, Heat, transient charge state, and world damage reset between sessions. The HUD reports save failures.

## Verify

On this Mac, the bundled .NET SDK can compile all project C# independently of Play Mode:

```sh
/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet build Overpowered.Build.csproj
```

With a .NET SDK on PATH, `dotnet build` is sufficient. Override `UnityEditorPath` (or `UnityManaged`) if your editor is installed elsewhere. This compile check does not replace Unity physics/rendering tests.

Run these Unity commands **against a temporary copy of the project**, with the editor closed for that copy; omit `-quit` because each verification exits itself:

```sh
Unity -batchmode -projectPath /path/to/copy -executeMethod CityVerification.Run -logFile /path/to/city.log
Unity -batchmode -projectPath /path/to/copy -executeMethod CityVerification.Reload -logFile /path/to/reload.log
Unity -batchmode -projectPath /path/to/copy -executeMethod ProceduralAnimationVerification.Run -logFile /path/to/animation.log
Unity -batchmode -projectPath /path/to/copy -executeMethod ModeVerification.Run -logFile /path/to/modes.log
Unity -batchmode -projectPath /path/to/copy -executeMethod ModeVerification.Reload -logFile /path/to/modes-reload.log
```

The city test creates two temporary data-only projectile variants, exercises the seventh in Play Mode, then removes those assets. It isolates progression saves from normal play. The reload command must follow the first command in the same copy; it launches a fresh editor process and compares the saved values. Rendering must remain enabled for the FPS measurement and screenshots.

The mode run creates a temporary third mode definition using the shipping Hero rules, runs real scene transitions and controlled interactions/damage, then removes the definition. Run its Reload command in a separate Unity process. Outputs are in `Verification/Modes/`; no normal player saves are touched. These are automated functional checks, not a substitute for keyboard/mouse feel testing.

See `STATUS.md`, `Verification/Modes/` and `Verification/City/` for measured output, controls, and prototype limitations.
