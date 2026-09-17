# Overpowered

A Unity/C# superhero blockout: a compact city, physical combat, five upgradeable powers, Hero/Villain objectives, and escalating police response. Procedural character animation is retained; no animation clips or Animator controller are required.

## Play

Open this folder with **Unity 6000.6.0f1**, open `Assets/Scenes/Prototype.unity`, and press Play. The district, NavMesh, player, and NPCs are generated at startup. The scene is also included in Build Settings.

- WASD move, Left Shift run, Space jump.
- Hold F while airborne to fly; Space ascends, release directional input to hover. Fuel recharges on the ground.
- E always punches. Left mouse uses the selected power. Number keys match the slots listed in the power menu.
- Tab opens the power/unlock/upgrade menu. The world continues running while the menu is open. Escape releases the cursor.
- Telekinesis: aim at a prop, click to grab, click again to hurl. Changing powers or reaching the hold timeout releases it.
- R resolves nearby mugging/robbery events. Hold R at a fire. Heroes stop the event; Villains assist it. Defeating the criminal also resolves a crime for a Hero.
- H switches Hero/Villain, with a configurable 10-second cooldown. The same character and upgrades remain.
- Find cyan rooftop discoveries for a one-time XP reward. Villains also have repeating destruction objectives.

Flight and Super Strength start unlocked. Earn XP and explicitly spend Power Points to unlock Telekinesis, Fire Blast, or Ice, or upgrade an owned power. Death respawns the player and retains progression.

## Tune in the Inspector

Select these Project assets; no code edits are needed for numeric gameplay tuning:

- `Assets/Resources/GameTuning.asset`: movement, city seed/dimensions, NPCs, progression, Heat, crime timing, prop physics, and camera settings, grouped by system.
- `Assets/Resources/Powers/*.asset`: each power's costs, charges, cooldown, resource use, force/damage, range/duration, and upgrade tiers.
- `Assets/Resources/ProceduralAnimationTuning.asset`: punch, flight, run, and landing visuals.
- `Assets/Resources/CityLayout.asset`: optional authored building placements. **Overpowered → Bake generated buildings into layout data** captures the seeded placements for editing. Change the seed and restart Play Mode to regenerate; disable `Use Authored Buildings` to return to seeded generation. Building positions/sizes remain separate from generation logic for a later hand-built/ProBuilder environment.

Existing constants remain in `PrototypeTuning.cs` as legacy defaults. Runtime balance now comes from the Inspector assets. Avoid editing definitions' stable `Id` values after players have saved progression.

To add another power using existing behavior, create/duplicate a PowerDefinition asset under `Resources/Powers`, give it a unique ID, assign one of the existing effect assets, and tune it. Catalog discovery, selection UI, cooldowns, upgrades, and persistence are generic. A genuinely new behavior requires one new PowerEffect subclass and its asset, not changes to registries or switches.

## Save data

Versioned JSON is stored in `Application.persistentDataPath/overpowered-progression.json`. It saves level, XP, points, side, owned powers/tiers, and claimed rooftop discoveries. Saves use temporary-file replacement and keep a `.bak`. Health, Heat, transient charge state, and world damage reset between sessions. The HUD reports save failures.

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
```

The city test creates two temporary data-only projectile variants, exercises the seventh in Play Mode, then removes those assets. It isolates progression saves from normal play. The reload command must follow the first command in the same copy; it launches a fresh editor process and compares the saved values. Rendering must remain enabled for the FPS measurement and screenshots.

See `STATUS.md` and `Verification/City/` for measured output, controls, and prototype limitations.
