# Architecture map

Derived from reading the code on 2026-09-21 (branch `main` at `804ab6f`). Unity 6000.6, Built-in Render Pipeline, Standard shader only. One C# assembly (Assembly-CSharp); no package dependencies beyond Unity built-ins, ProBuilder, the test framework, and AI Assistant packages. Every gameplay number lives in a `Resources` asset; code reads assets, it does not hard-code balance.

## Flow

`GameFlow` (static, installed via `RuntimeInitializeOnLoadMethod`) owns scene flow: `Home` → `Prototype` → `Results`. Selecting a mode asset loads the city scene; `PrototypeBootstrap.BuildCity` generates the world on scene load. Playing directly in `Prototype` redirects to Home unless a verification sandbox flag is set. Scene names are constants in `GameFlow` (`HomeScene`/`CityScene`/`ResultsScene` = `Assets/Scenes/Home.unity`, `Prototype.unity`, `Results.unity`, all in Build Settings).

## Systems and owners

| Concern | Code | Data |
|---|---|---|
| Scene flow, session results | `GameFlow.cs` | Build Settings scene list |
| World/session state: Heat, health, spawning, police reconciliation | `WorldSession.cs` | `Resources/GameTuning.asset` |
| Session goals, score/XP/heat rewards, spawn clock, encounter pool cycling | `GameModeSession.cs` | `Resources/Modes/*.asset` (`GameModeDefinition`) |
| Win/lose/objective rules per side | `HeroModeRules.cs`, `VillainModeRules.cs` (abstract base `ModeRules` in `GameModeDefinition.cs`) | `Resources/ModeRules/Hero.asset`, `Villain.asset` |
| Multi-part set-piece lifecycle (actors, blockades, loot/fire nodes, hold-R interaction, timers) | `CrimeEncounter.cs` | `Resources/Encounters/*.asset` (`EncounterDefinition`) |
| Simple (non-mode) crime markers | `CrimeEvent.cs` | `GameTuning.asset` → Crime |
| Player movement, jump, punch (with 125 ms authoritative windup), input | `SuperHeroController.cs` | `GameTuning.asset` → Movement |
| Power selection, charges/cooldown/energy, telekinesis hold | `PowerUser.cs` | `Resources/Powers/*.asset` (`PowerDefinition`) |
| Power behavior dispatch | `PunchEffect.cs`, `FlightEffect.cs`, `TelekinesisEffect.cs`, `FireBlastEffect.cs`, `IceEffect.cs` (abstract base `PowerEffect` in `PowerDefinition.cs`) | `Resources/Effects/*.asset` |
| Progression: XP thresholds, points, purchases, side switch, JSON save | `PlayerProgression.cs` | `GameTuning.asset` → Progression; save file `Application.persistentDataPath/overpowered-progression.json` |
| Seeded city, sidewalks, NavMesh build, rooftop discoveries | `CityDistrict.cs` | `GameTuning.asset` → City; `Resources/CityLayout.asset` (authored placements) |
| City art generation, palette materials, mesh combining, props | `CityArt.cs`, `CityPalette.cs` | `Resources/CityArtSettings.asset`, `CityPalette.asset` |
| NPCs: roles, NavMesh AI, alarms, panic, damage | `CityNpc.cs` | `GameTuning.asset` → Npcs |
| Prop damage/shards | `BreakableProp.cs` | `GameTuning.asset` → Props |
| Humanoid presentation (animation, flight/panic bones) | `HumanoidPresentation.cs` | `Resources/SharedHumanoid.controller`, `HumanoidAnimationTuning.asset` |
| Landing squash only | `ProceduralHeroAnimation.cs` | `Resources/ProceduralAnimationTuning.asset` |
| Third-person camera | `GameCamera.cs` | `GameTuning.asset` → Camera |
| In-game HUD and pause/power menus (IMGUI) | `PrototypeHUD.cs` | `GameModeDefinition.Hud` flags |
| UI Toolkit home/results menus | `ModeScreens.cs`, `MenuSkyline.cs`, `MenuGlyph.cs`, `MenuPresentationTuning.cs` | `Resources/MenuPresentationTuning.asset`, `MenuTheme.tss` |

## Extension points (data, not switches)

- New encounter: add an `EncounterDefinition` asset and list it in a mode's `Encounters` pool. `GameModeSession.SpawnNext` cycles the pool in list order.
- New mode: add a `GameModeDefinition` asset; menus discover it via `Resources.LoadAll` sorted by `MenuOrder`. `Playable: 0` shows COMING SOON.
- New rules: subclass `ModeRules` (code) + an asset referencing it; a mode's `Rules` slot points at any `ModeRules` asset.
- New power behavior: subclass `PowerEffect` (code) + an effect asset; a `PowerDefinition` asset points at it.

## Verification harnesses

All under `Assets/Editor/` (entry points) + `Assets/Scripts/*VerificationRunner.cs` (Play Mode coroutines). Evidence lives in `Verification/<Area>/`. Each verifies against an isolated project copy and isolated save paths. `ModeVerification.Run` temporarily creates a third mode definition from data only, then deletes it; this is the check that data-only content loads and runs.

## Performance notes

Editor throughput is ~26–40 FPS across current workloads and is under investigation; see the performance diagnosis at the top of `STATUS.md` (mesh combining produces ~1:1 unique meshes, defeating batching; ~1,140 renderers dominate CPU; benchmark harnesses double-rendered frames). Content changes must not raise population counts; encounter/mode data costs essentially nothing to render.
