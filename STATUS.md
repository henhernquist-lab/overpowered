# Prototype Status

## City and progression expansion (current)

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
