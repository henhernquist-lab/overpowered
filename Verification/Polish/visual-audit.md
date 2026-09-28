# Visual audit: prototype smell (CLOUD, 2026-09-28)

**Read this first.** This audit was written by the CLOUD agent, which has **no Unity Editor**, so it could not run the game.
It is built from the **existing committed screenshots** of real local runs (2026-09-15 to 2026-09-27, mostly 1280×720),
inspected as images, and each finding is traced to the code or data that draws it.

- **No fresh BEFORE set was captured and no visual change was made.** `Verification/Polish/Before/` still has to be
  captured by LOCAL at 1920×1080 (list at the end).
- Several source shots predate the Sidekick heroes: the Forge and HUD captures still show the mannequin hero. Findings
  that depend on them are marked **(stale shot)**.
- **There is no night or dusk mode in the build** (no time-of-day code exists), so shot 11 cannot be captured as it stands.

## Evidence used (the 13 required views)

| # | View | Best existing shot | Date | Caveat |
|---|---|---|---|---|
| 1 | Home | `Verification/Menus/home.png`, `home-hover.png` | 09-26 | 1280×720 |
| 2 | Hero Forge | `Verification/Forge/forge-menu.png`, `forge-preview.png`, `Sidekick/session-heroes.png`, `Sidekick/reload-session.png` | 09-26 / 09-27 | Forge shots are **stale** (mannequin); Sidekick shots are the heroes in the street, not the Forge |
| 3 | Downtown street | `Sidekick/npc-fps/street-A-run7.png`, `World/ab2/controls/view-street.png` | 09-27 / 09-26 | current world |
| 4 | Downtown rooftop | `World/verify/downtown-rooftop-north-spire.png`, `World/ab2/controls/view-flight.png` | 09-26 | |
| 5 | Docks | `World/verify/waterfront-quay.png` | 09-26 | |
| 6 | Park | `World/verify/park-pond-and-tower.png` | 09-26 | |
| 7 | Residential | `World/verify/downtown-rooftop-east-residential.png` (seen from above) | 09-26 | no street-level residential shot exists |
| 8 | Hero combat | `Feel/final-hud/hero-1920x1080.png`, `Feel/particles-hits-and-breaks-1920x1080.png`, `Ice/after-third-person-npc-frozen.png` | 09-25 / 09-26 | mannequin hero **(stale shot)** |
| 9 | Villain combat | `Feel/final-hud/villain-1920x1080.png` | 09-25 | **(stale shot)** |
| 10 | Flight over city | `World/ab2/controls/view-flight.png` | 09-26 | |
| 11 | Night / dusk | — | — | **no night mode exists** |
| 12 | Endless Fight | `Combat/endless-wave-5-gameplay-camera.png` | 09-23 | |
| 13 | Results | `Menus/hero-results-level-up.png`, `ModeExpansion/endless-results-new-best.png` | 09-26 / 09-22 | |

Also inspected:
- `Combat/archetypes-side-by-side.png` and `Combat/windup-gunner.png`;
- `Sidekick/lineup.png`;
- `World/verify/edge-north-from-spire-level.png`.

**What already reads as a real game:**
- **The three Sidekick heroes (`Sidekick/session-heroes.png`).** Clean toon materials, strong silhouettes and suit
  colours; they are the best art in the build. The problem is that the world around them is far below their fidelity,
  which makes the mismatch obvious.
- **The HUD (`Feel/final-hud/*`).** Clear corner hierarchy, a readable objective and a quiet centre. Keep it.
- **The Home layout.** Clear hero / villain split. It needs finish, not structure.

## The ten strongest causes of prototype smell, ranked by visible impact ÷ cost

Scores: impact and cost are each 1–5; **rank = impact ÷ cost**.

### 1. No shadows anywhere, and flat, over-bright lighting (impact 5, cost 1) → **5.0**
**Seen:**
- Not one cast shadow in any world shot. Cars, NPCs, the hero, props and buildings all float on the ground (hero, villain,
  endless and street shots).
- Up-facing surfaces are blown out: sidewalks render near `#a8c8e8` and read as **snow or ice**, and the lawn in
  `park-pond-and-tower.png` is almost white-green. Building faces have little light / dark separation, so the forms
  read as flat cards.

**Cause:**
- `PrototypeBootstrap.cs:26` creates the Sun with `AddComponent<Light>()`, which defaults to **`LightShadows.None`**.
  No code sets `sun.shadows`.
- The sun is white at intensity 1.2, and a flat ambient `(.45,.5,.6)` sits on top of it (`PrototypeBootstrap.cs:27`).
- `QualitySettings.shadowDistance` is 15.
- Side note: the `04 SHADOWS off` control in `PerformanceProfileRunner` therefore measured nothing: the sun never cast
  shadows in the first place.

**Fix, all in code:**
- `sun.shadows = LightShadows.Soft`, `shadowStrength` about 0.55, a warm cream sun colour at intensity about 1.0.
- Gradient ambient (`AmbientMode.Trilight`: sky navy-blue, equator desaturated purple, ground warm grey) instead of flat.
- Shadow distance 60–80 m with 2 cascades.
- Keep the Haze fog.

**Perf:** must be measured, because every building becomes a shadow caster. Mitigations if needed:
- window / band detail pieces set to `ShadowCastingMode.Off`;
- the backdrop is already off;
- a shorter shadow distance.

This single change is expected to do more for "real game" than anything else on this list.

### 2. Horizon: tombstone cards and visible fog walls (impact 4, cost 1) → **4.0**
**Seen:**
- `edge-north-from-spire-level.png`: the mainland is a ring of plain tilted slabs on thin platforms, like gravestones.
- `park-pond-and-tower.png` shows a **dark flat quad across the top of the sky**, and `waterfront-quay.png` shows a blue
  triangle in the top of the sky. Both are the haze-skirt walls seen from inside.
- The water ends in the same pale haze.

**Cause:**
- `CityArt.Backdrop` (`CityArt.cs:292`) builds one or two cube silhouettes per cluster, plus a "shore" slab.
- `CityArt.Skirt` (`CityArt.cs:308`) builds four giant cube walls.
- Values: `CityLayout.Backdrop`.

**Fix:**
- Build backdrop silhouettes from the stepped / podium-tower vocabulary (item 4 kit), untilted, one or two tones darker
  than Haze.
- Make the skirt walls match the fog colour exactly and never show their top edge. Lower them below the camera's upward
  view, or replace them with a low horizon band.
- Consider a gradient skybox material (built-in `Skybox/Procedural` tinted to the palette) so the sky is not one
  flat haze.

### 3. Ground planes: pale sidewalks, chunky cream kerbs, flat roads (impact 4, cost 2) → **2.0**
**Seen:**
- Sidewalks are pale blue-white and cover a third of every street shot.
- The kerb is a thick cream stripe that outlines every block like a UI border.
- Roads have centre dashes and crosswalks (good) but no lane edge lines, stop bars, manholes, corner radii, medians or
  plaza treatment.

**Cause:**
- The `Pavement` palette colour `#72878e` is fine in isolation; it is lit to near-white by item 1.
- Kerbs: `CityArt.Streets` (`CityArt.cs:243`) uses `CityColor.Cream` at `CurbWidth`.

**Fix, after item 1:**
- Warm the concrete (towards `#8d8a86`).
- Darker concrete kerbs and a narrower kerb top.
- Shared-mesh sidewalk seam strips and corner caps.
- White edge lines plus stop bars at intersections.
- A plaza paving pattern at encounter sites.

All of these are thin shared-material strips; the existing `Part(..., detail:true)` path already batches them per
district.

### 4. "Box city": facades are extruded rectangles, and buildings don't meet the street (impact 5, cost 3) → **1.7**
**Seen:**
- At gameplay distance the nearest wall is a flat slab with three or four window quads. See `street-A-run7.png` (left half
  of the frame) and `hero-1920x1080.png` (left tan wall).
- Every tower is a stack of identical window bands. Most ground floors have no door, shopfront, awning or base.
- Silhouettes differ only in height and one setback. From the rooftop and flight shots the skyline is lots of towers
  with the same flat roof and parapet.

**Cause:** the entire kit is Unity `Cube` / `Cylinder` primitives composed by recipes:
- `CityArt.Part` / `Piece` (`CityArt.cs:50`, `:81`);
- the recipe pieces in `CityArtSettings.cs:161` (`RecipePiece P(PrimitiveType t, ...)`).

**Fix, no architecture rewrite:**
- Add an optional **shared `Mesh` slot to `RecipePiece`** next to `Type`, so recipes can place authored modules (built
  once with the installed **ProBuilder**, saved as mesh assets, one per module, palette materials only).
- Instancing and static batching stay exactly as they are: the same mesh plus the same palette material batches like a
  cube does today.
- Module vocabulary, Downtown first:
  - shopfront strip with awning;
  - recessed entrance;
  - base plinth;
  - cornice / parapet cap;
  - vertical window column;
  - corner trim;
  - stepped crown;
  - roof stair housing, HVAC block, water tank, antenna, billboard frame.
- Use controlled variation from the existing seeded style index (`CityArtSettings.StyleOf`), not new randomness.

### 5. Enemy roles are not readable at a glance (impact 4, cost 2) → **2.0**
**Seen:**
- `archetypes-side-by-side.png`: Rusher, Gunner and Brute are three red mannequins with a small chest-colour
  difference; the Brute is only slightly larger.
- In `endless-wave-5` the crowd is a uniform salmon-pink mass.
- The Gunner's only tell is the orange aim line (`windup-gunner.png`).
- Civilians are amber mannequins. Cops are Teal (`HumanoidPresentation.cs:69`), which sits close to the hero cyan / blue
  family.

**Cause:**
- NPCs are mannequins by design. `HumanoidAnimationTuning.SidekickNpcs` is off after measured cost; respect that.
- The per-role difference lives in `EnemyArchetype` data only.

**Fix within the mannequin budget:**
- Proportion scale per archetype:
  - Brute: ×1.35 width, ×1.2 height, heavy shoulder pads from one shared mesh;
  - Rusher: ×0.9, forward lean in the pose, a long orange stripe.
- Gunner: one shared low-poly rifle mesh on the right hand plus a raised-arm aim pose. It becomes the ranged silhouette.
- Palette regions: hostile red base with a role-specific second colour (Rusher orange, Gunner dark purple, Brute near
  black). Keep hero-side cops blue-grey rather than cyan.

### 6. Street props are raw primitives in the foreground (impact 3, cost 2) → **1.5**
**Seen:**
- `hero-1920x1080.png`: a giant brown cube (the breakable crate) and a brown cylinder (barrel) dominate the lower right.
- Green-on-sand stacked cubes, box cars made of two boxes, and cube debris (`particles-hits-and-breaks`).

**Cause:**
- `CityDistrict.MakeProp` creates `PrimitiveType.Cube` / `Cylinder` (`CityDistrict.cs:65-66`).
- `BreakableProp` shards are primitives (`BreakableProp.cs:37`).
- Car recipes in `CityArt.cs:402-432`.

**Fix:**
- Shared prop meshes, authored once: crate with a lid band, barrel with ribs, car body with a bevelled cabin and
  window band, dumpster, bench, planter, newspaper box.
- Same palette materials and the same `BreakableProp` (AGENTS.md: do not add a second destruction system).

### 7. Power VFX are thin or primitive, and one telegraph is an opaque slab (impact 4, cost 3) → **1.3**
**Seen:**
- The fireball is a flat orange sphere (`hero-1920x1080.png`, centre).
- A thin orange line spans the whole villain frame.
- Hit debris is little cubes.
- `endless-wave-5`: an **opaque solid orange disc** covers the ground around the player; it hides the play space and
  reads as a UI bug.

**Cause:**
- `FireBlastEffect.cs:16` (a `Sphere` primitive).
- `AttackTelegraph.cs:21` (a built-in `Cylinder` disc, opaque).
- `ImpactParticlePool`.

**Fix:**
- Telegraph: an outline ring plus a low-alpha fill, pulsing towards impact.
- Fire: a core plus a trailing ember ribbon.
- Impacts: a flash, a shock ring and a few chunky sparks, per power colour.

**Installed but unused:** Cartoon FX Remaster FREE (`Assets/JMO Assets/...`, 60 prefabs), for example
*Impact Glowing HDR (Blue)*, *Hit Ice B*, *Electrified 3*, *Poison Cloud*, *Ground Hit*, *Flash*.
**Henry decides** whether its own materials are allowed. AGENTS.md says effects use shared palette materials and the
fixed pool. If not allowed, copy only its textures into palette-tinted pooled particle systems.

### 8. Park and Residential are empty and blocky (impact 3, cost 3) → **1.0**
**Seen:**
- Park: trees are stacked cubes, with a blown-out lawn and a lone tower (`park-pond-and-tower.png`).
- Residential from the air: a flat grey slab with small cube houses on pale lots and no fences, drives or yards
  (`downtown-rooftop-east-residential.png`).

**Fix, after Downtown:**
- Two or three shared canopy meshes (round, cone, cluster) with trunk variety.
- Paths and hedges, plus a bench / lamp cluster.
- Residential lots with fence runs, a garage module, a driveway strip and gabled roof modules.

### 9. Docks: stick cranes, flat containers, pale ground (impact 3, cost 3) → **1.0**
**Seen (`waterfront-quay.png`):**
- Cranes are bare brown beams.
- Containers are boxes with dark flat faces.
- The quay is a huge pale plane with no edge, bollards or markings.

**Fix:**
- Ribbed container module with door ends (palette tints).
- Truss crane module in hazard yellow / red.
- Quay edge cap, bollards, loading-bay markings.

### 10. Menus: flat rectangles; the Forge reads as an inspector; the Results screen has no payoff (impact 3, cost 3) → **1.0**
**Seen:**
- **Home:**
  - large flat cards with faint oversized background icons;
  - a sparse "YOUR POWERS" strip with a lot of dead space;
  - a blurry low-res box-city background;
  - the Hero Forge entry is a small outlined button in the corner.
- **Forge (stale shot):** a list of `‹ value ›` rows beside a flat preview box, with a full-width cyan SAVE bar.
- **Results:** the title is strong, but the stats are a flat row of small numbers, and NEW BEST is just appended to the
  title text.

**Fix:**
- **Forge:** the hero stage as the focal point (a raised disc, rim light, a slow idle turn); power cards with icons
  instead of text rows.
- **Home:** bigger mode art, tighter card proportions, a Forge button with the hero portrait, and a sharper skyline
  capture.
- **Results:** make the primary outcome dominant, then give NEW BEST and LEVEL UP their own stamped badges with a short
  DOTween pop.

## Order of work (for LOCAL, Downtown as the quality bar)

1. **Lighting pass (item 1)** plus horizon (item 2). Capture Downtown street / rooftop / flight before and after, then
   measure FPS at the existing measured views. Current reference from STATUS: densest street ~80 FPS, flight ~125–130
   FPS on the Radeon Pro 5300. Gate: street stays ≥ 70 FPS, otherwise shorten the shadow distance or turn off detail
   shadow casting.
2. **Ground planes (item 3)** in Downtown.
3. **Downtown modular kit (items 4 + 6)**:
   - add the `RecipePiece` mesh slot;
   - author about 12 ProBuilder modules;
   - apply them to the Downtown styles only;
   - re-measure renderer, draw and SetPass counts against the batching numbers in STATUS.
4. **Power VFX (item 7)**: telegraph ring first (cheapest, most visible), then fire, impacts, and one screenshot per
   power.
5. **Enemy readability (item 5).**
6. **Menus (item 10).**
7. Docks, Park and Residential (items 8–9), reusing the kit.

## BEFORE captures LOCAL still needs (1920×1080 → `Verification/Polish/Before/`)

**Main set:**
- `01-home.png`
- `02-forge.png` (with a Sidekick hero selected)
- `03-downtown-street.png`
- `04-downtown-rooftop.png`
- `05-docks.png`
- `06-park.png`
- `07-residential-street.png`
- `08-hero-combat.png`
- `09-villain-combat.png`
- `10-flight.png`
- `11-night.png` (**not possible: no night mode**; record as N/A, or add a lighting preset later)
- `12-endless.png`
- `13-results.png`

**Plus one shot per power.** Only five powers exist on `main` (fire, flight, ice, strength, telekinesis). The other six
(darkness, laser, lightning, force field, speed, poison) exist only on the unmerged `cloud/*` branches, created by
`RosterSetup`. Capturing all eleven needs those branches merged first:
- `power-flight`, `power-strength`, `power-fire`, `power-ice`, `power-telekinesis`, `power-darkness`;
- `power-laser`, `power-lightning`, `power-forcefield`, `power-speed`, `power-poison`.

**With FPS / frame time / draw calls / SetPass / renderers** at street, flight and dense combat.
