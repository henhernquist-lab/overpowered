# Overpowered overnight continuation

Latest user brief, received 2026-09-27. This supersedes the shorter recovered queue.

Current local checkpoints:
- Main: `49ffbed` (includes Sidekick and preserved FBX/vendor changes; main was pushed outside this command-line session).
- Phase 2: existing `codex/toon` in wt-sidekick. Toon renders correctly after setting UTS_RP_BUILTIN. Sidekick Run + Reload, CityArt, Humanoid, FirstPerson Run pass; remaining reload/profile underway. Adoption is not merged yet.
- `codex/cloud-repair-experiment`: diagnostic repairs of the old cloud merge only. Do not merge this experiment. Await the Cloud agent's corrected branch and perform the prescribed fresh sweep.
- Phase 3 onward remains pending.

CONTINUE THE OVERPOWERED OVERNIGHT BUILD.

Read `AGENTS.md`, `STATUS.md`, and `docs/overnight-queue.md` first. Inspect the actual current code and git state before making changes. Do not trust old README claims when STATUS/current code disagree.

CURRENT GROUND TRUTH:
- Main currently includes the recovered overnight queue and the explicit FBX package dependency.
- Sidekick heroes + optimized bodies are merged and freshly verified.
- Sidekick NPCs stay OFF. The measured cost was unacceptable.
- `cloud/gameplay-depth` is still being repaired by the Cloud agent. Do not merge its missions/new powers until the corrected branch is available and your integration sweep is green.
- Free Play and Endless Fight ALREADY EXIST and were previously verified. Phase 4 is a CURRENT-STATE AUDIT and regression pass, not permission to rebuild them from scratch.
- Push may fail because this machine's command-line Git lacks GitHub credentials. A push failure is NOT a reason to stop. Commit locally and continue.
- Preserve `Side_Kick_Data.db` and vendor state exactly as documented.

Work continuously. Do not stop to ask Henry questions. Make reasonable reversible decisions, record them under `OVERNIGHT DECISIONS`, and continue. Only stop for destructive/irreversible work.

=== NEXT: FINISH PHASE 2 ===

Resume the EXISTING `codex/toon` work. Do not restart the toon effort from scratch.

Goal: get the current Built-in RP toon/stylization experiment into a clean, measured state.

Requirements:
- Built-in Render Pipeline only. Do not migrate to URP/HDRP.
- Preserve Sidekick colour-map recolouring.
- Do not blindly replace working materials.
- Compile after setup.
- Capture representative gameplay and Forge comparison screenshots.
- Measure current gameplay FPS/draw calls against the pre-toon state using the same camera/scenario.
- If the toon solution causes unacceptable visual breakage, Sidekick incompatibility, or a meaningful performance regression, revert the runtime adoption while keeping useful setup/research documented.
- Do not claim a visual improvement without screenshots.
- Commit the finished Phase 2 independently.

=== PHASE 3: RIGGING + DOTWEEN + CARTOON FX ===

All three packages already exist. Actually USE them where they solve a current weakness.

ANIMATION RIGGING:
- Add restrained head/look-at behavior for the hero and nearby NPC awareness where appropriate.
- Telekinesis should visually sell the action: hand/arm aiming toward the held object without animating the physics root.
- Preserve SharedHumanoid.controller ownership and existing punch/kick/backflip timing.
- No root motion.
- Verify death, hit reactions, flight, punch, kick, backflip, first person, and all three Sidekick heroes.

DOTWEEN:
- Replace hand-written HUD/menu interpolation where DOTween materially simplifies it.
- Use unscaled tweens for UI that must continue during hit pause/pause exactly as current contracts require.
- Do NOT rewrite working gameplay timing into tweens.
- Re-run HUD layout/resolution/center-clear checks.

CARTOON FX:
- Integrate selectively for impacts/powers where it beats the placeholder effect.
- POOL runtime effects.
- Shared materials only.
- No effect spam.
- Preserve the existing Feel particle pool where it is still the cheaper/better effect.
- Measure performance with combat effects active.

Commit Phase 3 separately.

=== CLOUD INTEGRATION LOOP ===

When `cloud/gameplay-depth` receives its repair commits:

Integrate it in the isolated integration worktree FIRST.

The shipping synergy set must be EXACTLY:
1. Sonic Slam
2. Thermal Shock
3. Solar Flare
4. Void Grasp
5. Eclipse Beam

No Phoenix Dive, Frostwake, Orbit Throw, Meteor Punch, Inferno Orbit, Glacier Fist, Cryo Crush, or Meteor Slam in the shipping Forge catalog.

Run the setup scripts and verify they are idempotent.

Do NOT enable the new mission scenarios in Hero/Villain shipping pools until:
- robbery getaway car actually moves,
- robbery cuff completion passes,
- Roster passes,
- Roster Reload passes,
- Melee passes,
- HeroStats passes,
- Mission passes,
- SynergyAvailability passes,
- HeroForge passes,
- core mode/combat/camera/HUD regressions pass.

If green, merge the corrected cloud feature set, then run `MissionSetup.BatchUseInModes` locally and re-run mission/mode/HUD suites.

=== PHASE 4: FREE PLAY + ENDLESS CURRENT-STATE AUDIT ===

DO NOT REBUILD THEM.

Free Play already has:
- no objectives,
- no timer,
- no defeat limit,
- no encounters,
- Heat/police enabled,
- saved profile side,
- side switching,
- infinite respawn,
- direct return home.

Endless already has:
- escalating waves,
- reinforcement cap,
- explicit per-wave health/damage scaling independent of Heat,
- mixed Rusher/Gunner/Brute composition,
- score,
- best score/best wave persistence,
- Survival results,
- Hero and Villain variants.

Audit them AFTER all current Forge/cloud changes.

Verify:
Home -> Free Play -> side switch -> powers -> death/respawn -> Home.
Home -> Endless Hero -> multiple waves -> death -> Results -> Home.
Home -> Endless Villain -> multiple waves -> death -> Results -> Home.
Best score/wave survives a separate-process reload.
All 11 powers can be equipped where the final Forge catalog permits.
No objective HUD leaks into Free Play.
No encounters spawn in Free Play.
Endless stays below MaxAlive.
Wave composition still works with current enemy roster.
Measure FPS at a late representative wave.

Only fix real current regressions.

=== PHASE 5: ICE READABILITY, SHAPE NOT JUST COLOR ===

Ice already turns targets cyan and freezes animation. That is NOT enough.

Add a colorblind-safe visual indicator recognizable by SILHOUETTE/SHAPE:
- frost/snowflake icon above frozen NPCs and props, OR
- an unmistakable geometric pulse/halo, OR both if cheap.

Requirements:
- visible against Cyan AND Teal bodies,
- applies to direct Ice,
- applies to EVERY shipping synergy that can create a frozen state,
- one shared implementation so future freeze sources cannot silently forget presentation,
- restores cleanly on thaw/death/destruction,
- pooled/shared materials,
- no runtime material creation.

Verify direct Ice plus Thermal Shock and any other FINAL five synergy effect that freezes.

=== PHASE 6: WORLD LIGHTING ===

Build the day/dusk/night system.

Data-driven cycle:
- tunable cycle duration,
- tunable sun intensity/colors,
- Hero defaults daytime,
- Villain defaults dusk/night,
- Free Play cycles,
- Endless may use a stable readable preset unless cycling clearly works better.

Night MUST remain readable.

Add:
- streetlights,
- lit windows,
- emissive signs/landmarks,
- sensible ambient/fog adjustment.

POWER LIGHTS:
- Fire,
- Lightning,
- Laser Eyes.

Use capped pooled short-lived point lights. No light explosion from chain Lightning.

Measure:
day FPS,
dusk FPS,
night FPS,
night combat with maximum pooled power lights active.

Capture screenshots from identical viewpoints at day/dusk/night.

=== PHASE 7: CINEMACHINE + TIMELINE ===

FORGE:
Use Cinemachine for a short hero reveal/orbit when the player locks in a loadout. Do not make Forge slower to operate.

COMBAT CAMERA:
Only replace GameCamera with Cinemachine if it is objectively regression-free.

Required gates:
- third person,
- first person,
- camera collision,
- flight FOV,
- hit impulse,
- landing impulse,
- synergy FOV,
- Fire aim at 15m/22m,
- Ice at 15m/22m,
- Telekinesis at 15m/22m,
- Laser Eyes at 15m/22m,
- obstacles between CAMERA and HERO do not hijack aiming,
- nearby walls still block real projectiles correctly.

ZERO tolerance for aim regression. If it regresses and cannot be fixed cleanly, revert the combat-camera migration and document why.

TIMELINE:
Create a roughly 8-10 second first-launch intro:
city skyline -> hero entrance/landing -> home screen.

Requirements:
- skippable immediately,
- does not replay once seen,
- additive version-safe save field,
- old saves load,
- test skip and full playback,
- no hard dependency on one HeroDefinition.

=== PHASE 8: SPLINE PEDESTRIAN/PATROL ROUTES ===

Use Unity Splines to make ordinary movement intentional.

Create district-aware sidewalk/boulevard routes.
- civilians walk routes instead of point-to-point random wandering,
- cops patrol routes when not responding,
- combat/flee/encounter AI overrides the route immediately,
- NPC returns to an appropriate route afterward,
- preserve NpcLod near/far behavior,
- preserve far civilian recycling,
- no NPC teleports in camera view,
- do not break NavMesh response/combat movement.

Verify Downtown, Park, Waterfront/Docks, Residential/Industrial.
Profile the densest district.

=== PHASE 9: FINALIZE THE SIX CLOUD POWERS ===

Once integrated:
Darkness
Laser Eyes
Lightning
Force Field
Speed
Poison

For EVERY power:
- Forge selection works,
- save/reload works,
- equip gate works,
- unequipped control refuses use,
- death/session transitions clear transient state,
- resource/cooldown/charge behavior matches its data model,
- first-person works,
- third-person works.

LASER EYES:
Replace the placeholder thin line with a readable superhero beam.
Add an actual channel pose/upper-body presentation.
Beam start must follow the hero's eyes/head, but gameplay aiming remains authoritative from the existing camera/crosshair system.

FORCE FIELD:
Visible shield shell/barrier.
Block/brace pose.
Damage absorption must be visually obvious without changing its verified gameplay math.

LIGHTNING:
Readable chain arcs without allocating garbage per cast.
Confirm chain target uniqueness.

DARKNESS:
Root indicator that reads clearly against dark city/night lighting.

POISON:
Readable affected state and death-spread event.

SPEED:
Trail/pose that clearly distinguishes the combat dash from Flight.

Then run an 11-power real-combat performance scenario and capture evidence.

=== FINAL REGRESSION / MORNING REPORT ===

Run the broadest reasonable regression suite on the final code.

Do not hide failures to make the dashboard green.

Write a new top STATUS section containing:
- exact commits,
- phases complete,
- phases partial,
- failures and causes,
- performance before/after,
- setup scripts run,
- every suite + PASS count,
- what Henry should personally playtest,
- the exact next development task.

Commit everything phase-by-phase.

If push still fails, record the commit SHAs and continue instead of stopping.