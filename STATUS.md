# Prototype Status

## Camera diagnosis and repair

The saved Home, Prototype and Results scenes had **zero GameObjects and zero Cameras**. The mode refactor relied on `GameFlow.SceneReady` to create menu cameras and `PrototypeBootstrap.BuildCity` to create the player camera at runtime. No disabled scene camera was found. The `Prototype` heading is a scene container, not a newly introduced parent GameObject; the saved scene contained no such parent. A Project search result is not evidence of a Camera instance in the loaded scene.

Read the actual open editor's `Logs/Editor.log`: it contains earlier `CS1061` errors (`WorldSession.Mode` / `SpawnEncounter` missing) from the incomplete refactor, followed by successful assembly reloads. The clean baseline renders Home and both gameplay modes successfully; **a runtime camera-factory failure was not reproduced**. The reproducible “No cameras rendering” condition is the camera-less edit-time scene. The baseline's error check also caught the existing `ArgumentOutOfRangeException` in `UnityEditor.Search.SearchDatabase.EnumerateAll`, not in camera/gameplay code. The verifier records this specific editor exception separately, without hiding other errors. No claim is made that a disabled Project-search hit was repaired or that the Search package exception was fixed.

Repair: each of the three scenes now saves one enabled, top-level **Main Camera**, tagged MainCamera, with an AudioListener and `GameCamera` component. Runtime setup reuses that camera, explicitly restores Display 1/full viewport/enabled state, configures menus to clear their background, and attaches the existing third-person follow logic in Hero/Villain. It no longer needs to invent an invisible-to-the-editor camera from scratch. Newly created menu scenes also receive a camera. `Overpowered → Repair scene cameras` is an explicit repair utility, not an automatic scene rewrite. The city still generates only after Play/mode selection; a camera alone does not generate an edit-time city preview.

Verification uses `CameraVerification.Baseline`, `Verify`, and `DirectPrototype` in an isolated Unity project. The disabled-camera control checks that a disabled camera drops out of Unity's rendering camera list. Pixel captures render the **actual scene camera**, not an extra verification camera, at 640×360, then restore the Display target. Menu captures show the camera background, not IMGUI; batch rendering does not certify mouse interaction or Game View repaint cadence. The historical baseline output is retained even though its blanket error check failed on the unrelated Search exception.

Fixed flow and direct-Prototype startup both passed (exit 0): Home, Hero, Hero pause/results/Home, Villain, Villain pause/results/Home each had exactly one active enabled camera on Display 1. All captures overwrote **230,400 / 230,400** pixels; gameplay images contained many colors and the camera followed the current hero. Each saved scene now reports `roots=1, cameras=1, enabled=1` before Play. The disabled-camera controls passed. Full output and actual-camera captures are in `Verification/Cameras/`. `dotnet build` passed with **0 warnings, 0 errors**.

## Home and game-mode sessions (current)

Built a mode wrapper around the existing city, controller, five powers, progression/save, Heat/police, HUD and procedural presentation. No replacement controller, combat system, city generator or animation system was introduced.

- **Startup and flow:** `Home` is the first build scene. OVERPOWERED lists Hero and Villain as playable; Free Play and Endless Fight say COMING SOON and reject selection. Selecting a definition loads `Prototype`; a completed/failed/timed-out session loads `Results`, with score, XP earned, outcomes and counts. Results returns to Home. Starting Play directly in Prototype also redirects to Home unless an isolated legacy verification explicitly requests the sandbox.
- **Mode architecture:** `GameModeDefinition` assets supply side, reusable `ModeRules`, encounter pool, spawn cadence/population, win/fail/defeat/time limits, rewards and HUD flags. The menu discovers assets; no mode-ID registry/switch was added. `GameModeSession` owns the session lifecycle; the existing Hero/Villain crime entry points route into `CrimeEncounter` and the selected rules. A new mode can reuse rules or provide its own rule subclass/asset without editing abilities, city, save or menu code.
- **Hero:** neutral police pursue/suppress robbers. Stop every robber by capture (held R) or combat, physically displace/destroy blockades, hold R to rescue cyan civilians, and extinguish fire nodes in arson events. Resolving just the marker or one actor is insufficient. Civilian death or any escaped robber fails the event.
- **Villain:** the same city and encounter population; cops attack the player through existing Heat AI. Steal gold loot, wreck at least three encounter props, interact with sabotage/fire nodes where present, then escape beyond 22m. Robbers are allies. Destruction earns existing XP and Heat; Heat-driven reinforcements and the pursuing Hero remain intact. Vertical flight escape is intentionally allowed.
- **Set-pieces:** bank break-out, street ambush and arson definitions. Each spawns three robbers with different exit routes, two endangered civilians, two responding cops, two throwable cars, four supply crates, two rescue blockades and two loot nodes. Arson adds two fire nodes. Cars/crates use real Rigidbodies and the existing force/damage/shard system. Encounters appear at separated street intersections; actors use the existing NavMesh. Scenario objects are removed when the event ends or the city unloads; temporary physical shards retain their existing lifetime.
- **Feedback/UI:** live objective sub-counts, distances/bearings, world markers, deadlines, success/failure feedback, session score/XP/counts. Escape is a true pause with resume, results or return-home actions; Tab remains the live power-upgrade menu. H cannot change sides mid-session. Mode HUD flags control health, powers, progression, Heat and objectives.
- **Save extension:** earned XP and purchased powers remain shared across modes. Added session/win counts, best score, last mode and last-session XP to the existing atomic JSON save. Old version-1 files remain compatible with zero-initialized session fields. Rewards save immediately; session outcomes save once. Transient city state, health, Heat, resources and active encounters still reset between sessions.

### Session choices and consequences

Both shipping modes are **objective-count based: five completed encounters wins**, not an enforced 10–15-minute survival session. A **900s / 15-minute cap** produces timeout results. **Three failed encounters or three player defeats loses**; the first two defeats use the existing respawn behavior. These limits can be disabled with zero or edited per mode in the Inspector.

One event starts immediately; another can spawn every **50s**, up to **two active**. Each has a **210s deadline**. Robbers scatter locally, then try their city exits after **45s**. Unrescued civilians begin taking **1 damage/s after 90s**. Hero failure on escape/death/timeout adds **0.75 Heat**, removes **25 score** (floor zero), and counts toward the loss limit; it does not remove earned XP. Success grants **100 score / 60 XP**, plus existing combat rewards; civilian rescue gives **20 score**. Hero completion lowers Heat by **1**. Villain completion adds **1 Heat**, wrecked encounter props give **5 score** each, and existing destruction XP is retained. Villain deadline failure also adds 0.75 Heat and counts against the session. Physics collateral can kill a civilian: rescue requires care with blast direction, not indiscriminate area attacks.

All of those numbers and population counts live in the mode/encounter assets. Powers, props, movement and animation retain their existing authoritative tuning assets.

### Explicit cuts / prototype limits

- **Not implemented by design:** Free Play and Endless Fight gameplay. Their disabled catalog definitions are present; no fake playable buttons.
- **No art pass:** primitive actors/cars/crates and basic IMGUI screens. Bank/ambush share the common encounter choreography; they are not authored bank interiors or vehicle-driving missions. Arson has extra interactive fire nodes, not a spreading-fire simulation. Civilians are immobilized by a gameplay blockade condition until rescued, not physically pinned/ragdolled. Cop support uses navigation/suppression/damage, not firearms.
- **Not claimed:** human keyboard/mouse feel acceptance, a 15-minute hands-on balance run, native-resolution player-build FPS, or a standalone distribution build. Automated completion controls position the player and supply hold/damage inputs directly to gameplay methods; they do not prove navigation/input ergonomics or difficulty. The physics rescue check uses an actual charged punch.
- UI button callbacks and real scene transitions are exercised via the same flow methods in batch Play Mode, not automated mouse clicks. World rendering is captured separately; no batch screenshot of IMGUI menus is claimed.
- The original expansion notes below are historical. Their H-switch, single-step crimes, repeating Villain chaos goal, and non-pausing Escape behavior are superseded by these modes. Legacy sandbox paths remain only for isolated regression checks.

### Mode verification

Reproducible checks: `ModeVerification.Run` followed by `ModeVerification.Reload` in a **separate Unity process**, using a temporary project copy and isolated save paths. The first run temporarily creates a third definition reusing Hero rules/encounters, with a one-encounter goal; the runtime discovers it without new system code. The definition is removed after testing; a copy is retained as evidence under `Verification/Modes/`.

Actual final Unity Play Mode output (`Verification/Modes/results.txt`; process exit 0):

```text
PASS Startup HOME, no city or player spawned.
PASS free-play COMING SOON CONTROL refuses launch.
PASS endless-fight COMING SOON CONTROL refuses launch.
PASS Populated event: 3 robbers, 2 trapped civilians, 2 responders, 8 real Rigidbody props (2 cars, 4 supplies, 2 blockades), 2 loot nodes.
PASS Untouched marker CONTROL does not resolve encounter.
PASS Blocked civilian CONTROL refuses rescue before blockade is moved/broken.
PASS Rescue physics: actual punch AddExplosionForce(1350 N·s, Impulse), mass=45kg; displacement=10.000m.
PASS Hero police CONTROL: neutral cop in attack range does not damage player.
PASS Pause freezes session clock and encounter simulation.
PASS Ignored Hero event: failures=1, Heat 1.75 -> 2.50 (+0.75).
PASS Hero PLAY -> RESULTS: Won, success=5, failure=1, score=700, XP=825.
PASS Hero RESULTS -> HOME; city unloaded.
PASS Hero/Villain regenerated the SAME seeded city layout.
PASS Mode-switch persistence: level=5, XP=162, points=4, wins=1.
PASS Earned points purchase strength upgrade; tier=1 must survive restart.
PASS Villain live cop AI attacked: health 100 -> 76.
PASS Existing Heat escalation retained: cops 4 -> 10 at Heat 3.00.
PASS Configured spawn-clock interval adds second multi-part encounter.
PASS Population-cap CONTROL refuses a third simultaneous encounter.
PASS Villain escape CONTROL: loot/destruction alone cannot resolve while inside scene.
PASS Villain PLAY -> RESULTS: Won, success=5, score=700, XP=780.
PASS Villain RESULTS -> HOME; city unloaded.
PASS Third mode discovered/selected from a definition ONLY; same rule and encounter assets, goal=1, no registry changes.
PASS Data-only third mode loaded, ran, and won at its configured single-encounter goal.
PASS Failure-limit CONTROL: third failed encounter produces LOST results.
PASS Timeout CONTROL: configured 900 seconds produces TIMED OUT results.
PASS Defeat CONTROL: two respawns allowed; actual third player death produces LOST results.
MEASURED populated events: civilians=28, cops=12, events=2; 1280x720 rendered Editor Play Mode; frames=1237, seconds=5.007, FPS=247.08, p95=6.48ms; Intel(R) Core(TM) i9-10910 CPU @ 3.60GHz; AMD Radeon Pro 5300.
```

The FPS sample renders a dedicated top-down 1280×720 camera every sampled frame while live world/NavMesh/NPC updates continue, with two active encounters, six robbers, 28 civilians and 12 cops. The player is held airborne for test safety. It measures wall-clock Editor throughput, **not a standalone or hands-on combat FPS guarantee**. The captured frame is `Verification/Modes/populated-event.png`. Police neutrality is tested away from hostile robbers to avoid attributing their attacks to the cop. Timers/interaction durations are advanced through runtime methods; the test does not wait 15 real minutes. `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` passes with **0 warnings / 0 errors** (`Verification/Modes/build.txt`). Unity's existing `UnityEditor.Search` startup indexing exception appeared but did not prevent the completed tests.

Separate-process reload (`Verification/Modes/reload.txt`; exit 0):

```text
PASS SECOND UNITY PROCESS exact reload: level=7, XP=327, points=5, sessions=6, wins=3, best score=700; all power tiers/rooftops retained.
PASS Fresh-save CONTROL in second process: level=1, XP=0, points=0, sessions=0.
```

The saved Strength upgrade was tier 1; the comparison covers the full progression JSON, not just the printed fields. The three wins are Hero, Villain and the temporary third mode; the other three outcomes cover failed encounters, timeout and defeats.

Existing city/power and procedural-animation regressions also exited 0 after this change. Their current output is retained separately as `Verification/Modes/city-regression.txt` and `animation-regression.txt`, without overwriting the earlier milestone's evidence. The city regression rechecked flight draining **6 → 4 → 0**, grounded recharge, normal punch/cooldown/zero-charge/recharge controls, seventh-power physics, Heat/police, Telekinesis, Ice and progression. The 20 animation checks include the actual controller fall and recovery. Those tests deliberately use the legacy isolated sandbox; the mode suite above independently exercises the new shipping flow.

## City and progression expansion (previous milestone)

Implemented all six systems from the attached expansion brief, extending the existing controller and retaining procedural animation:

- **City:** seeded 3×3 blocks, 36 solid buildings with varied heights and three tall landmarks, sidewalks/roads, five one-time rooftop XP discoveries, and crates, barrels, benches, trash cans, streetlights, and parked cars with Rigidbody physics and breakable shards. Buildings do not break. `CityLayout` can capture generated placements into editable data for later authored/ProBuilder layout work.
- **Powers:** Flight and Super Strength now use Inspector-editable PowerDefinition assets and the generic charge/cooldown/upgrade pipeline. Added Telekinesis (spring-force hold + impulse hurl), Fire Blast (colliding explosive projectile), and Ice (timed NPC/rigidbody freeze). Resources, force, damage, range, duration, costs, and upgrade tiers are data. Reusable effect assets dispatch behavior without a central power switch or hardcoded catalog. Existing gameplay constants remain in their original file as defaults.
- **Progression:** deterministic XP thresholds grant spendable points; powers are never randomly granted or automatically upgraded. Enemy defeats, appropriate crime/chaos actions, and rooftop exploration award XP. JSON saves persist XP, level, points, side, unlocks/tiers, and rooftop claims. Atomic replacement and a backup are used because explicit versioned JSON is inspectable and expandable; normal saves are separate from verification saves.
- **Sides/objectives:** H switches the same player between Hero and Villain with a 10s default cooldown. Mugging, robbery, and fire events spawn with world markers. Nearby interaction or defeating criminals resolves Hero crimes; Villains can assist events, destroy props for repeating chaos objectives, and gain XP from cops/civilians. Cops are neutral to Heroes and hostile to Villains. Switching retains powers and upgrades.
- **Heat/AI:** 0–5-star Heat increases from property destruction, assaults, and hostile actions; it decays after a quiet delay. Additional and tougher cops spawn by tier, with a pursuing Hero NPC at high Heat. Runtime-built Unity NavMesh supports wandering/fleeing civilians and enemies that pathfind, attack, receive damage/freeze, and die. Player death respawns with progression intact.
- **HUD:** health, energy, selected power charges/cooldown, flight fuel, XP bar/level, unspent points, Heat stars, side, event markers, and scrolling power selection/unlock/upgrade buttons. Tab opens the menu; H changes side; R interacts; E punches; LMB uses the selected power. See README for all controls.

### Configuration and judgments

`GameTuning.asset` groups each system's settings in the Inspector. Each power's authoritative values and upgrade tiers live in its own asset. Animation settings stay separate. The five named shipping powers start with Flight and Strength unlocked. The brief's “seventh power” check uses two temporary definitions reusing the projectile effect, not extra shipping abilities.

This is a primitive blockout, not an art pass: parked cars and benches have simple box silhouettes, streetlights are physics poles, and NPCs are colored capsules. Crime scenarios use timed/contextual interactions and criminal NPCs rather than cinematics or detailed rescue simulations. Fire interaction takes 2s; it is not a propagating fire simulation. Enemies use ground NavMesh paths and cannot fly after a player above rooftops; escaping vertically is possible. Police are neutral to the Hero even when property damage raises Heat. Layout generation happens at Play startup; restart Play after changing the seed. Menus do not pause combat.

Transient health, Heat, charges, destroyed objects, and active events are intentionally not saved. There is no structural building destruction. Human keyboard/mouse playtesting and a standalone-player performance benchmark remain unperformed. No required system is left as an architecture-only stub; the limitations above describe the implemented prototype depth.

### Expansion verification

Actual Unity Play Mode output, separate-process reload results, a data-only seventh-power artifact, and the rendered district capture are recorded under `Verification/City/`. Verification ran in a project copy with isolated save files. `dotnet build Overpowered.Build.csproj` completed with **0 warnings, 0 errors**. Unity gameplay, separate-process reload, and animation regression all exited successfully. The editor still reports its pre-existing Search indexing exception at startup; it did not prevent these tests from running.

Measured output from the final run:

```text
PASS Seventh power discovered from DATA only; exact same projectile effect asset as Fire Blast.
PASS Seventh projectile hit real Rigidbody: impulse setting=200 N·s; displacement=12.992m; velocity=32.672m/s.
PASS Unspent CONTROL: level=2, points=1, force unchanged=1350.
PASS Upgraded strength: force=1890, max charges=4, points=0.
PASS Zero-point upgrade CONTROL rejected.
PASS Real prop destruction: Heat=3.50; police 4 -> 10; high-tier max HP=137.
PASS High Heat spawns one pursuing Hero NPC.
PASS Lay-low decay: 3.50 -> 2.80 after configured delay plus 10s.
PASS Hero fire outcome: Heat delta=-1.00, XP awarded=60.
PASS Same fire event as Villain: Heat delta=1.00; opposite Hero outcome; same character=Overpowered Hero.
PASS Hostile cop attacked through live AI: player HP 100 -> 72.
PASS Defeating cop as Villain awards XP and disables dead NPC navigation.
PASS Separate Unity process restored exact save: level=5, XP=155, points=1, side=Villain.
PASS Separate process retained strength upgrade tier=1; force=1890
PASS Unlocked Telekinesis persisted.
PASS Fresh-save CONTROL: level=1, points=0.
MEASURED 1280x720 rendered Editor Play Mode: frames=776; seconds=5.006; FPS=155.01; p95 frame ms=10.33.
```

The benchmark had 24 civilians and 10 enabled, on-NavMesh cops, on an Intel i9-10910 / AMD Radeon Pro 5300. A 1280×720 RenderTexture was actually rendered each sampled frame; Stopwatch wall time measured throughput while world/AI updates ran. The camera viewed the populated district from above, with the player airborne; this is not a hands-on combat or standalone-player benchmark. The much lighter 4kg verification target reached 444.154m/s on a 1800 N·s telekinetic hurl; shipping prop masses are 45–400kg, so this test is a force-path check, not a claim that the test object's speed is balanced.

Other passed controls include real flight resource consumption (6 → 4 → 0s, then 2.5s after one second grounded), punch cooldown and empty-charge rejection/recovery, physical Telekinesis grab/hurl, timed Ice freeze/unfreeze, civilian fleeing, repeat-side-switch rejection, and a complete NavMesh route. The retained 20-check procedural-animation regression passed in the city: one actual 5m landing at 13.340m/s, minimum visual height scale 0.817, recovered scale 1.000, and unchanged 1.800m controller height.

## Original arena milestone (historical; superseded by city above)

- Third-person controller: WASD movement, Left Shift run, Space jump.
- Flight: hold `F` while airborne. Flight has 6.0 seconds of fuel and refills at 2.5 fuel/second while grounded.
- Super-strength punch: left mouse or `E`. It has 3 charges, a 0.45-second cooldown, and recovers a charge every 1.25 seconds.
- Tiny physics arena generated at play time with crates, barrels, and stacked objects. Punches use `Rigidbody.AddForce` / `AddExplosionForce`; props retain physics and can break apart from sufficiently hard impacts.
- HUD provides live numbers for fuel, charges, cooldown, force applied, and last punch outcome.

## Original arena verification controls (historical)

Enter Play Mode, then press `V` to run the built-in deterministic validation sequence. It prints concrete telemetry to the Console and shows it in the HUD: flight fuel is sampled at 6.000, 4.000, and 0.000 seconds; it then demonstrates ground recharge. The punch test records three successful charged punches, a rejected zero-charge punch, a rejected cooldown attempt, then a recovered successful punch. The force test prints the exact configured force (1350 N) and affected rigidbody count.

The verifier drives the same resource and punch methods used by gameplay; it is deliberately available in the running prototype so the values can be checked alongside the actual physics response.

The original V demo is no longer attached by the city bootstrap. Use the city verification described above; the old demo assigned some state directly and should not be read as independent measurement of all gameplay behavior.

## Procedural animation pass

- Added collider-free arms and contrasting fists to the existing capsule. A successful punch snaps the right shoulder forward and pitches/twists the body, then eases to rest. Rejected cooldown/empty-charge attempts do not animate.
- Moving flight pitches forward; hovering or ascending without horizontal travel levels out. Ground movement leans in the actual local travel direction, with a small distance-driven vertical bob that fades at rest.
- A landing event reports downward impact speed from the actual CharacterController movement. Falls above the minimum impact threshold produce a quick foot-pivot squash, slight widening, backward tilt, and recovery. Sub-skin-width resting contact transitions are filtered from the event; light impacts also do not trigger the animation.
- All animation timing, angle, intensity, speed thresholds, and blend rates are Inspector-editable in `Assets/Resources/ProceduralAnimationTuning.asset`. Select that asset in the Project window; edits take effect during Play Mode. Asset edits persist, so undo unwanted experiments. Defaults: punch attack/recovery 0.065/0.240s, arm rotation -100 degrees; flight lean 48 degrees; run lean 10 degrees and bob 0.045m; landing compression/recovery 0.055/0.240s and maximum squash 22%.
- Existing gameplay constants remain in `PrototypeTuning.cs`. Animation configuration is a ScriptableObject rather than C# constants because constants cannot be edited in the Inspector.
- `ProceduralHeroAnimation` only writes visual children. The controller publishes `PresentationState`, `PunchStarted`, and `Landed`; a future Animator adapter can subscribe to those same signals without changing ability logic. Disable/remove the procedural component when replacing it; disabling restores the resting pose. No animation clips or Animator controller were introduced.
- Scope: this pass implements the four requested placeholder animations. The attached broader city/powers/progression brief was treated as background, not implemented in this animation pass. Existing package edits are not part of this commit.

### Animation verification

`Assets/Editor/ProceduralAnimationVerification.cs` is a reproducible Unity batch Play Mode check. Run Unity with `-batchmode -projectPath <project> -executeMethod ProceduralAnimationVerification.Run -logFile <log>` (omit `-quit`; the check exits with its result). It samples controlled animation states, checks successful/rejected real punch calls, and then lets the real controller fall onto the arena. Output is written to `Verification/Animation/results.txt`; with graphics enabled it also renders six pose samples. This check is separate from the original `V` resource demo above.

Actual verification on Unity 6000.6.0f1, in a temporary project copy to preserve the open editor session: 20 checks passed, process exit 0. Recorded results and six inspected rendered poses are in `Verification/Animation/`.

- Successful punch: 100.000-degree arm rotation at 0.065s; recovered to rest. Cooldown and zero-charge controls emitted no additional punch event.
- Moving flight: 48.000-degree pitch; hover/vertical-ascent control: level. Diagonal run: pitch/roll +7.071/-7.071 degrees; bob over 120 steps: 0.0000–0.0450m; idle bob faded to zero.
- Controlled full-strength landing: scale (1.100, 0.780, 1.100), -13.000-degree backward tilt; weak-contact control remained unsquashed.
- Actual 5m controller fall: exactly one landing event, 13.117m/s impact, minimum visual Y scale 0.820, recovery to 1.000. Controller height remained 1.800m. All controlled poses preserved physics-root position, rotation, and scale. Disabling the component restored the resting pose.

Limits: deterministic pose samples and an actual automated fall were verified; no human keyboard/mouse feel session was performed. Unity compiled the scripts successfully, but the editor log included existing deprecated object-lookup warnings and a UnityEditor.Search startup indexing exception; these did not prevent the Play Mode checks from passing. No city/progression work or gameplay-resource re-verification is claimed by this pass.
