# Hero Forge

Start at Home and choose **Hero Forge / Select Hero**. Previous/next buttons choose a
hero, two palette suit colors and two distinct unlocked powers. Changes save immediately
through PlayerProgression's existing atomic JSON save. Save & Back returns to the
unchanged mode cards. The same loadout is read by every mode that uses BuildCity.

## Controls and progression

- **C**, or legacy Unity **joystick button 5**, activates the equipped synergy.
  Both bindings are Inspector fields on Resources/ForgeCatalog.asset. Controller indices
  vary by device; hardware controller input has not been tested.
- Press the same button again during either orbit to release its volley early.
  Otherwise the volley releases automatically after its configured orbit duration.
- HUD shows the equipped pair, synergy, remaining cooldown and refusal feedback.
- Existing E punch, RMB Hurricane Kick, Q backflip, F flight and selected-power controls
  remain. Unequipped powers are rejected inside PowerUser, including direct calls and
  flight fuel consumption. E/RMB retain weaker basic melee when Strength is not equipped
  (160 N·s / 8 damage before kick multipliers, 0.65s shared basic cooldown).
  With Strength equipped, the existing charge/cooldown and upgrade pipeline is unchanged.
- Forge respects existing power ownership: fresh saves still start with Flight/Strength.
  Unlock buttons use PlayerProgression.Buy and existing XP-earned points. Selecting a hero
  never grants free powers or spends points; locked defaults fall back to two owned powers.
  Thus the three starting silhouettes share the starter pair until more powers are unlocked.
- Backflip has no invulnerability. Synergies do not make the player immune to damage either.

## Data and runtime ownership

| Asset / component | Responsibility |
|---|---|
| Resources/ForgeCatalog.asset | Roster, pair registry, palette choices, input, basic melee, VFX budget and FOV tuning |
| Resources/Forge/Heroes/*.asset | Hero ID/name, prefab, visual-only proportions, default/available powers, optional animation configuration |
| Existing Resources/Powers/*.asset | Original powers, costs, upgrade tiers, effect and icon references; not duplicated |
| Resources/Forge/Synergies/*.asset | Unordered pair, name/icon, effect reference, cooldown and effect parameters |
| Resources/Forge/Effects/*.asset | Reusable behaviour variants, such as targeted/vertical slam or icy/physical lift |
| ProgressSave.Loadout | Additive version-1-compatible hero/power IDs and palette roles; no second save file/framework |
| PowerUser | Cached immutable-for-session pair, actual ability gates, synergy reference |
| SynergyRunner | Validity, cooldown, bounded execution, cleanup, physics helper methods and short FOV kick |
| HumanoidPresentation | Existing shared Animator and bone/visual-root ownership; selected prefab replaces only the model |

Three original placeholder definitions are VECTOR, TITAN and NOVA. They reuse the
existing Mixamo mannequin at widths 1.0, 1.2 and 0.92 with different palette suits.
These are **not three newly authored meshes**. Height remains 1.8m; CharacterController
height/radius and movement stats are unchanged. No licensed character logos are added.

## Ten implemented pairs

| Pair | Synergy | Mechanic |
|---|---|---|
| Flight + Strength | Sonic Slam | Short rise, collision-driven downward slam, 2,600 N·s / 40 damage / 7m radial impact |
| Flight + Fire | Phoenix Dive | Rise and dive to crosshair ground hit; forward ground-probe fallback; fiery damage/impulse |
| Flight + Ice | Frostwake | 26m/s, 1.1s flight-fuel-consuming burst; nearby actors freeze; actor collision restored afterward |
| Flight + Telekinesis | Orbit Throw | Spring-force orbit of up to four movable props, then sequential physical launches |
| Fire + Ice | Thermal Shock | Targeted blast; burning/frozen actors receive 1.7× total damage and brief freeze/stagger |
| Fire + Strength | Meteor Punch | Charge pose, existing punch clip/windup, 3,400 N·s / 75 damage impact |
| Fire + Telekinesis | Inferno Orbit | Orbit/volley with fiery collision blasts |
| Ice + Strength | Glacier Fist | Six-second hand effects, 1.65× normal melee force and a short freeze; normal charges still apply |
| Ice + Telekinesis | Cryo Crush | Suspend an enemy with actual rigidbody forces, then drive it into a ground impact |
| Strength + Telekinesis | Meteor Slam | Lift a movable prop or enemy, then slam it with a physical shockwave |

All use the definition's own cooldown (10s defaults). Frostwake also consumes the existing
flight fuel. Meteor Punch requires a ready charged Strength runtime but uses the independent
synergy cooldown rather than spending a normal charge. Other synergies do not spend base-power
energy/charges. These are explicit prototype balance choices, not claimed final tuning.

Fire adds a timed *burning marker* for Thermal Shock eligibility, not new damage-over-time.
Ice reuses CityNpc.Freeze. Physical synergy impacts reuse CombatImpact and BreakableProp.
NPCs temporarily hand navigation to a reusable physics suspension; on contact/timeout they
return to a nearby NavMesh position, falling back to their prior valid position if necessary.
This is not a ragdoll system. No physics prop is teleported, marked static, or given new meshes.

## Rendering and performance limits

Built-in Particle System module is enabled in Packages/manifest.json. No URP pipeline,
Shader Graph, volume or renderer feature was added. Six scene-lifetime pooled effects reuse
palette materials; each has a 40-point ring and at most 28 particles. No per-cast meshes or
materials are created. Fixed-step yield instructions are reused and pair lookup is cached.
Existing CombatImpact allocations and first-use component attachment are retained; this
is not a claim of a measured allocation-free game.

Effects are intentionally geometric placeholders: sparse colored particles/rings rather
than custom trails, ice shaders or elaborate fire art. Existing Animator handles clips;
no synergy rotates physics roots to animate a pose. Synergy movement uses CharacterController
Move and reports the existing landing event for squash/audio. FOV kick is 3 degrees over
0.16s, with no long camera shake or global hit-stop.

Free Play and Endless Fight remain the existing **disabled Coming Soon definitions**.
Forge does not implement missing modes; they inherit loadouts when their existing city
bootstrap is made playable. Current Hero and Villain transitions are verified.

## Adding content / Inspector setup

The committed assets require no manual scene wiring. Open Home and press Play.
For a checkout missing Forge assets, run **Overpowered > Forge > Create missing assets**.
The command preserves existing catalog/tuning values and adds missing shipped pair definitions.

To add a hero:

1. Create an Overpowered/Forge/Hero asset and give it a stable unique ID.
2. Assign a Humanoid prefab with an Animator on its root and compatible humanoid bones.
3. Set palette roles, available/default power references and visual scale (keep Y=1 to
   preserve normalized collider height). Optional animation tuning must use the expected
   shared-controller states and clips.
4. Add the asset to ForgeCatalog.Heroes. The UI discovers it from that list; no gameplay switch.

To add a synergy:

1. Create a PowerSynergyDefinition with two different PowerDefinition references.
2. Assign a reusable effect asset, or implement SynergyEffect.CanBegin / Execute for a new rule.
   Optional Repeat handles a second press. Never put mutable per-player state on the effect asset.
3. Add it to ForgeCatalog.Synergies, ensuring exactly one entry per unordered pair.
4. Tune damage, force, radius, duration and cooldown in the definition. Add positive/negative
   controls to HeroForgeVerificationRunner and validate physics, not just dispatch.

Run HeroForgeVerification.Run, then HeroForgeVerification.Reload in a **separate Unity
process**, without -quit. Use an isolated project copy and its generated verification save.
Evidence is under Verification/Forge. Hardware input, long play sessions and human feel
remain unverified; the tests call the same gameplay/UI event entry points.

## File inventory

Created in `Assets/Scripts/Forge`: HeroDefinition, ForgeCatalog, PowerSynergyDefinition,
HeroForgeScreen (including ForgeChoice), SynergyRunner, SynergyVfx, SynergyPayload,
SynergySuspension, SonicSlamEffect, FrostwakeEffect, ThermalShockEffect, MeteorPunchEffect,
GlacierFistEffect, OrbitThrowEffect, LiftSlamEffect and HeroForgeVerificationRunner.
Each has its Unity metadata. Reusable effect variants cover all ten pairs without ten
separate dispatch branches.

Created in `Assets/Editor`: HeroForgeSetup and HeroForgeVerification, with metadata.
Created in `Assets/Resources`: ForgeCatalog.asset and `Forge/` containing three hero,
ten synergy and ten effect assets, with metadata. Created `Verification/Forge/` with
Sonic-first, progressive and final test output, separate-process reload/build output,
and menu/preview/impact captures. This document is the setup/extension guide.

Changed existing scripts: PlayerProgression (save/migration), PowerUser (equipped gates),
SuperHeroController (basic melee and collision-driven ability movement), PrototypeBootstrap
(model creation after saved build initialization), HumanoidPresentation (selected model/suit),
ModeScreens (Forge entry), PrototypeHUD (pair/cooldown), CityNpc (status markers),
CombatImpact (optional NPC physical displacement) and FireBlastEffect (burn marker).
Also changed Packages/manifest.json and packages-lock.json for the built-in particle module,
.gitignore for isolated test saves, AGENTS.md and STATUS.md. No scene wiring, layout,
city-generation, shared controller or destruction-system replacement was required.
