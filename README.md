# Overpowered

A Unity 6000.6 C# superhero prototype: a stylized compact city, physical combat, five upgradeable powers, Hero/Villain sessions, escalating police response, and shared Mixamo Humanoid animation with procedural flight/panic. Everything on this page was re-verified against the code on 2026-09-21; unverified items are marked as such.

## Requirements

- **Unity 6000.6.0f1** (Built-in Render Pipeline — see "Render pipeline" below). No URP, HDRP or render-pipeline asset is used or needed.
- **No .NET SDK on PATH required.** A standalone `dotnet` is not installed on the development Mac and is not on PATH. Unity's own batch-mode compile is the current build gate. Two compile-check options, in order of authority:
  1. Unity batch mode (the gate used for verification):
     ```sh
     /Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity \
       -batchmode -quit -projectPath "$(pwd)" -logFile "$(pwd)/build.log"
     ```
     Compilation errors appear in the log; a clean run compiles all scripts on startup.
  2. **Historical:** `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` was the former gate. A `dotnet` binary ships *inside* the Unity editor at `Contents/Resources/Scripting/DotNetSdk/dotnet` and invoking it by full path still compiles the project cleanly today (0 errors; 16 pre-existing CS0618 warnings). Because it is not on PATH, it is not the documented gate; re-adding `dotnet` to PATH would restore the old workflow.
- The `Overpowered →` editor menu items referenced below exist under `Assets/Editor/` and are only usable in the Unity Editor.

## Play

Open this folder with Unity 6000.6.0f1, open `Assets/Scenes/Home.unity`, and press Play. Choose Hero Mode or Villain Mode. Home is first in Build Settings (`Home`, `Prototype`, `Results`, in that order); pressing Play directly in Prototype also redirects to Home. The district, NavMesh, player, and NPCs generate when a mode is selected. Free Play and Endless Fight are visible but disabled (COMING SOON; their `Playable` flag is false and selection is rejected).

- WASD move, Left Shift run, Space jump.
- **B toggles first-/third-person**, saved with progression. First-person hides the player's model (shadow retained), uses the same crosshair and powers, and adds a small forward-flight FOV increase. Tune the toggle, eye height, near clip, pitch limits and flight FOV under `GameTuning.asset > Camera`.
- Hold F while airborne to fly; Space ascends, release directional input to hover. Fuel recharges on the ground.
- E always punches. Left mouse uses the selected power. Number keys select the powers listed in the power menu.
- **Q backflips:** a short backward dash/hop with its own 2.5 s cooldown, grounded only. It is repositioning only — there are no invincibility frames and no separate dodge state.
- **Right mouse throws a Hurricane Kick:** a heavier, wider melee sweep than the punch, paid for from the *same* Super Strength charges, cooldown and energy as `E`. It is not a separate power and does not appear in the power menu.
- Tab opens the power/unlock/upgrade menu (world continues). Escape truly pauses and offers resume, results, or return home.
- Telekinesis: aim at a prop, click to grab, click again to hurl. Changing powers or reaching the hold timeout releases it.
- Hero: stop all fleeing robbers (capture with held R or defeat), punch/throw the brown blockades away from cyan civilians, then hold R nearby to rescue. Hold R at orange fire markers to extinguish arson.
- Villain: hold R at gold loot and orange sabotage markers, destroy at least the configured number of encounter props (3 in the shipping set), then get more than the configured distance from the site (20–24 m depending on encounter; 22 m in the original set). Police attack and escalate with Heat.
- Side is locked by the selected mode; choose another at Home. Find cyan rooftop discoveries for one-time XP rewards.

Complete five encounters to win (mode asset `SuccessGoal`). Three failed encounters or three defeats loses; 15 minutes (900 s) produces a timeout. In the shipping Hero/Villain modes a second event can spawn every 50 s, up to two active, each with a 210 s deadline from its own definition (new encounters range 180–240 s). Hero failure also raises Heat by 0.75; Hero success lowers it by 1. Robbers try their city exits after 30–55 s depending on the encounter (45 s in the original set), and unattended civilians take damage after 75–100 s (90 s in the original set). Results show score and XP; earned progression survives every outcome.

Flight and Super Strength start unlocked (`InitiallyUnlocked: 1` on `strength` and `flight`). Earn XP and explicitly spend Power Points to unlock Telekinesis, Fire Blast, or Ice, or upgrade an owned power. The first two deaths respawn the player and retain progression.

## Tune in the Inspector

Select these Project assets; no code edits are needed for numeric gameplay tuning:

- `Assets/Resources/GameTuning.asset`: movement, city seed/dimensions, NPCs, progression, Heat, crime timing, prop physics, and camera settings, grouped by system.
- `Assets/Resources/Powers/*.asset`: each power's costs, charges, cooldown, resource use, force/damage, range/duration, and upgrade tiers. Effect behavior lives in `Assets/Resources/Effects/*.asset`.
- `Assets/Resources/HumanoidAnimationTuning.asset`: shared clips, locomotion, punch/backflip/hurricane-kick timing, flight and panic. `ProceduralAnimationTuning.asset` retains landing squash and legacy placeholder settings. Force/damage/radius for the punch stay in `Assets/Resources/Powers/strength.asset`; the kick's multipliers, radius and the backflip's distance/cooldown are tunable constants in `Assets/Scripts/HeroAbilityTuning.cs`.
- `Assets/Resources/CityPalette.asset`: the single named color palette for all generated materials (including existing actors, markers, projectiles and shards). Swatch changes propagate live during Play.
- `Assets/Resources/CityArtSettings.asset`: building archetypes, facade/roof dimensions, prop dimensions/masses, normalized placement anchors/chances/jitter, and mesh combining. Art stays inside existing seeded building bounds; `CityLayout` remains the layout authority.
- `Assets/Resources/Modes/*.asset`: mode catalog, side, rules, encounter pools, HUD flags, session limits, population, score/XP/Heat rewards.
- `Assets/Resources/ModeRules/Hero.asset` and `Villain.asset`: the reusable win/loss/failed rule assets referenced by the mode definitions.
- `Assets/Resources/Encounters/*.asset`: actor/prop counts, escape/rescue/destruction requirements, timings and interaction distances.
- `Assets/Resources/CityLayout.asset`: optional authored building placements. **Overpowered → Bake generated buildings into layout data** captures the seeded placements for editing. Change the seed and restart Play Mode to regenerate; disable `Use Authored Buildings` to return to seeded generation. Building positions/sizes remain separate from generation logic for a later hand-built/ProBuilder environment.

Existing constants remain in `PrototypeTuning.cs` as legacy defaults. Runtime balance now comes from the Inspector assets. Avoid editing definitions' stable `Id` values after players have saved progression.

**Overpowered → Bake current art placements to editable data** records the current seed's art placement list and enables authored placements. Edit each record's kind, world position and yaw in that asset; future runs use those exact placements instead of regenerating them. If you later change the building layout, rebake or adjust rooftop heights yourself. `Combine Meshes` can be disabled for individually inspectable geometry; a ProBuilder mesh-conversion/export workflow is not included. Legacy color fields in GameTuning, PowerDefinition and EncounterDefinition remain serialized for compatibility but are superseded visually by CityPalette.

### Adding content without code

- **A new encounter:** duplicate any asset under `Assets/Resources/Encounters/` (they are `EncounterDefinition` ScriptableObjects; all fields are counts, timings, distances and rewards — no code), then add it to the `Encounters` list of one or more mode definitions under `Assets/Resources/Modes/`. The session cycles the pool in list order and reuses it.
- **Another power using existing behavior:** duplicate a `PowerDefinition` asset under `Resources/Powers`, give it a unique ID, assign one of the existing effect assets, and tune it. Catalog discovery, selection UI, cooldowns, upgrades, and persistence are generic. A genuinely new behavior requires one new `PowerEffect` subclass and its asset, not changes to registries or switches.
- **A new mode:** duplicate a `GameModeDefinition` under `Resources/Modes`, give it a stable unique ID, assign rules and encounter definitions, and set `Playable`. Menu discovery, city loading, pause/results, population, limits and save integration need no registry edits. New objective behavior belongs in a new `ModeRules` subclass/asset. Existing movement, powers and animation do not depend on mode IDs.

## Render pipeline

This project uses the **Built-in Render Pipeline** and only Unity's Standard shader:

- `Packages/manifest.json` contains no URP/HDRP package.
- `ProjectSettings/GraphicsSettings.asset` has `m_CustomRenderPipeline: {fileID: 0}` — no render pipeline asset is assigned, and there are no Renderer Features to configure.
- `Assets/` contains no `.shader` files; generated materials use `Shader.Find("Standard")` with palette colors.

There is no outline pass, post-processing volume, or pipeline toggle to flip for performance. Any suggestion that rendering cost is controlled by a pipeline asset is stale.

## Performance

Measured Editor throughput across the current test workloads is roughly **26–40 FPS** (1280×720, AMD Radeon Pro 5300 / Intel i9-10910), and it is **under active investigation** — see the performance diagnosis at the top of `STATUS.md`, including its note that the benchmark harnesses rendered every sampled frame twice, so absolute numbers are understated and comparisons are more trustworthy than absolutes.

The frequently quoted **155 FPS** figure is historical: it was measured on a much simpler gray-box city before the art and humanoid passes and is not representative of current content. Recent populated runs record ~2,200 draw calls; the diagnosis attributes the dominant cost to per-renderer CPU work (~1,140 renderers) rather than batching. This is Editor throughput only — no standalone-player performance has been measured.

## Save data

Versioned JSON is stored in `Application.persistentDataPath/overpowered-progression.json` (`SaveFilename` in `GameTuning.asset` → Progression). It saves level, XP, points, side, owned powers/tiers, claimed rooftop discoveries, session/win counts, best score and last session's mode/XP. Saves use temporary-file replacement and keep a `.bak`. Health, Heat, transient charge state, and world damage reset between sessions. The HUD reports save failures.

## Verify

Unity batch mode is the build gate (see Requirements). Run the functional checks **against a temporary copy of the project**, with the editor closed for that copy; omit `-quit` because each verification exits itself:

```sh
Unity -batchmode -projectPath /path/to/copy -executeMethod CityVerification.Run -logFile /path/to/city.log
Unity -batchmode -projectPath /path/to/copy -executeMethod CityVerification.Reload -logFile /path/to/reload.log
Unity -batchmode -projectPath /path/to/copy -executeMethod HumanoidVerification.Run -logFile /path/to/animation.log
Unity -batchmode -projectPath /path/to/copy -executeMethod BackflipHurricaneVerification.Run -logFile /path/to/abilities.log
Unity -batchmode -projectPath /path/to/copy -executeMethod BackflipHurricaneVerification.Sample -logFile /path/to/abilities-sample.log
Unity -batchmode -projectPath /path/to/copy -executeMethod ModeVerification.Run -logFile /path/to/modes.log
Unity -batchmode -projectPath /path/to/copy -executeMethod ModeVerification.Reload -logFile /path/to/modes-reload.log
Unity -batchmode -projectPath /path/to/copy -executeMethod CityArtVerification.Run -logFile /path/to/art.log
Unity -batchmode -projectPath /path/to/copy -executeMethod MenuPresentationVerification.Run -logFile /path/to/menus.log
Unity -batchmode -projectPath /path/to/copy -executeMethod PerformanceProfile.Run -logFile /path/to/perf.log
```

The city test creates two temporary data-only projectile variants, exercises the seventh in Play Mode, then removes those assets. It isolates progression saves from normal play. The reload command must follow the first command in the same copy; it launches a fresh editor process and compares the saved values. Rendering must remain enabled for the FPS measurement and screenshots.

The mode run creates a temporary third mode definition using the shipping Hero rules, runs real scene transitions and controlled interactions/damage, then removes the definition. Run its Reload command in a separate Unity process. Outputs are in `Verification/Modes/`; no normal player saves are touched. These are automated functional checks, not a substitute for keyboard/mouse feel testing.

The menu-presentation run checks Home → Play → Results → Home for both modes plus a real upgrade purchase (`Verification/Menus/`), and the performance pass re-measures the city with a revert-everything diagnostic harness (`Verification/Performance/results.txt`).

See `STATUS.md`, `Verification/` and `docs/architecture.md` for measured output, system ownership, controls, and prototype limitations. No human playtest has been performed on this prototype; nothing here claims balance or feel.
