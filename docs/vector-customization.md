# VECTOR modular appearance slice

Based on integrated `fef5fac` (Synergies 2.0, Battle Lab, Skirmisher/Bulwark and parkour). Feature branch: `codex/vector-modular-customization`. No push or merge. Start `Assets/Scenes/Home.unity`, open HERO FORGE, select VECTOR, then APPEARANCE → SUIT → **Starter armor · light fittings**. The original suit remains the default for existing saves. Choose **Hood** or **Hood + face cover**, and PRIMARY / SECONDARY / ACCENT. Selections save immediately. SAVE & BACK closes Forge; enter any existing game mode or BATTLE LAB to use the saved outfit.

## Delivered artwork and its limits

This is existing Sidekick starter armor with lighter fittings, not new superhero artwork. The reusable system, fitted suit, two headgear packages, three colors, preview and persistence work. The two requested chest emblems are **unavailable**: there are no suitable authored chest emblems in the installed starter set. Forge shows disabled Chevron/Bolt entries with an art-needed explanation. No geometric placeholder, purchase, download, vendor material edit or generated superhero model is involved. Human art approval remains outstanding.

The base and masks are editor-baked subsets of the **already fitted** optimized VECTOR mesh. Every original part range is verified against its FBX vertex count and UV sequence, and no triangle may cross a retained/removed boundary. Posed extraction controls compare source and extracted vertices. Thus the build preserves body-shape fitting and original bind poses without loading Sidekick's database or calling vendor APIs at runtime.

| Stable ID | Actual art | Vertices / bone entries |
| --- | --- | --- |
| `vector-legacy` | Original combined Starter_03 appearance, unchanged | 19,510 / 88 |
| `vector-starter-modular` | Starter_03 body, without attachment slots 22–34 | 14,402 / 65 |
| `vector-hood` | `SK_SCFI_CIVL_10_22AHED_HU01` | 872 / 1 |
| `vector-hood-respirator` | Same hood + `SK_SCFI_CIVL_09_23AFAC_HU01` | 1,963 / 3 |
| `vector-no-emblem` | No chest attachment | 0 |
| `vector-emblem-chevron`, `vector-emblem-bolt` | No mesh supplied; unavailable | 0 |

Source recipe: `Assets/Synty/SidekickCharacters/Characters/Starter/Starter_03/Starter_03.sk`. Source outfit FBXs: `Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits/Starter`. The retained torso/upper arms/left forearm/hips/legs use `SK_FANT_KNGT_17`; right forearm/hands/feet use `SK_SCFI_CIVL_09`; the original human head, eyebrows, eyes, ears, nose, teeth and tongue remain. `Verification/VectorCustomization/bake.txt` lists every exact asset. All parts retain VECTOR's baked body proportions; there is no overlapping second helmet or second set of gloves/boots. The hood-only variant exposes the existing face; the other adds its authored face cover.

## Architecture and save compatibility

- `Assets/Scripts/Forge/CosmeticCatalog.cs`: stable IDs, slot, hero compatibility, compatible suits, availability/reason, shared baked mesh, ordered bone paths, safe legacy defaults, and detached `HeroAppearanceSave` records. Definitions are serialized in `Assets/Resources/CosmeticCatalog.asset`; new meshes are in `Assets/Resources/Forge/Cosmetics`.
- `HeroAppearanceRenderer.cs`: same bounded assembler in preview and gameplay. It changes the existing body renderer, binds to existing skeleton transforms, and reuses at most one mask and one emblem renderer. It owns no Animator or gameplay components. There is no per-frame reconstruction or runtime mesh combination.
- `HeroForgeScreen.cs`: extends the existing code-built UI Toolkit form. Existing hero/power/signature controls and the 400×500 RenderTexture remain. Color/slot changes reuse the model, camera, Animator, material and map. Unity's skinning buffers refresh after mesh/bone reassignment; rendering is queued for the following frame, with rapid edits coalesced into one capture. The existing orbit reveal remains.
- `HumanoidPresentation.cs`: fits the original canonical body **before** applying cosmetics, then installs the assembler before the camera's Start renderer cache. Physics capsule, eye height, visual scale, skeleton, Avatar, shared controller, root motion and presentation events retain their existing ownership.
- `CityPalette.cs` / `CityMaterials`: adds accent to the shared material cache key. The old three-argument material APIs keep the authored Trim default. Preview owns one mutable material/map instead of caching every color combination. Existing `SidekickSuit.Write` protects Keep swatches (skin, eyes, hair and unrelated materials); vendor maps/materials are untouched. Cleanup destroys owned resources.
- `PlayerProgression.cs`: additive `ProgressSave.HeroAppearances` list, **outside** HeroLoadout. Outer save Version remains 1. `Appearance(hero)` returns a detached saved selection or legacy defaults without eagerly rewriting old saves. `SetAppearance` saves through the existing atomic progression writer and refuses active-session mutation. Unknown/unavailable IDs remain in saved records; catalog resolution only changes the rendered fallback. Explicit color edits are palette-validated. Legacy Loadout primary/secondary colors mirror the selected appearance, and legacy `SetLoadout` callers update those two channels while retaining slot IDs/accent. Power/loadout reconstruction does not discard appearances. Switching heroes restores each hero's stored colors/outfit.

No gameplay statistics, damage, power ownership/equip gates, signature mechanics, city layout, hitboxes, mode/parkour rules or rendering pipeline were changed. TITAN and NOVA keep their original optimized meshes and avatars; this slice does not add their modular wardrobes.

## Missing-art handoff

1. **Chevron chest emblem**: authored mesh fitted to VECTOR's existing chest at rest and during spine/shoulder motion; consistent outward thickness, clean depth separation, palette-compatible UVs and normals. Bind to `spine_03` (or suitable weighted spine bones), export in the existing model space with correct bind poses. Use existing VECTOR Trim swatches in the 32×32 color map for the selectable accent channel; preserve the protected skin/eye cells. Assign the existing reserved ID only after fit/render validation.
2. **Bolt chest emblem**: same requirements, visibly distinct silhouette and geometry. There is no approved mesh yet.
3. **Polished superhero suit/masks (optional replacement art)**: current choices are hood/face-cover starter armor. A sleek superhero aesthetic needs artist-created or approved compatible art in Blender or another DCC tool. Flexible suit/hood sections need skinning and deformation checks; rigid decorations may be weighted to a single attachment bone. Do not advertise the present starter armor as final superhero art.

No cape, cloth simulation, glow customization, full glove/boot wardrobe or TITAN/NOVA modular wardrobe is included. Authored protected glow/emission remains as before.

## Verification and performance scope

The Unity entry points are `VectorAssetInspection.Run`, `VectorCosmeticBake.Run`, and `VectorCustomizationVerification.Run` / `.Reload`. Use `tools/verify-sequential.py` to run serial Editor processes with isolated profiles and personal-save/database hash checks. The new runner tests the real Home Forge controls, compatible/unavailable options, rendered mask differences, lower-body pixel coverage after skinning refresh, all color/protected swatches, old and unknown-ID saves, power edits, per-hero return, exact separate-process reload, canonical dimensions, bounded renderers/resources, Battle Lab and normal-city spawn, first-person hiding/restoration, shared-controller states and real movement/combat/signature events.

The first logic-only run missed incomplete immediate preview captures; those captures are retained under `gates-1/captures`. `gates-3` contains the explicit failed lower-body pixel control that exposed same-frame skinning refresh timing. The final implementation queues/coalesces renders after buffer refresh. Later screenshot review also caught the expanded form shrinking/overflowing; explicit viewport sizing/clipping and row shrink constraints now have non-overlap, clipping and end-of-scroll reachability controls. `ui-final` retains a failed test compilation using unsupported `IResolvedStyle.overflow`; the fixture now uses `IStyle.overflow.value`. Do not use the initial logic-only passes as visual acceptance evidence.

The modular hood/face outfit has **16,365 vertices, 68 bone entries, two skinned renderers, one Animator and one shared suit material**, versus the original 19,510 vertices / 88 entries / one skinned renderer. The extra renderer is a draw/submission cost; fewer vertices/bones do not prove higher FPS. Shared assets are reused; no persistent runtime meshes are generated. The preview warm-update check exercises 96 explicit edits and checks no growth in live Mesh/Material/Texture2D counts, alongside exact identity checks for its camera, Animator, model, RenderTexture and material. This is a bounded lifetime test, not a sustained standalone performance benchmark.

**UNVERIFIED:** human visual approval, hands-on feel, all day/night/first-person angles, long-session memory behavior, standalone CPU/GPU frame-time comparisons and production draw-call profiling. Existing NPC mannequin policy and historical performance measurements remain unchanged.

## Human acceptance checklist

- Open Home → HERO FORGE → VECTOR. Choose the light-fitting suit, both hood variants and contrasting three-color combinations. Check face/hood/neck, wrists, chest, knees and boots while orbiting; approve the actual starter-art appearance or request new art.
- Rapidly change colors and masks. Confirm a complete preview refreshes on the next frame without persistent blank geometry, changed camera framing or lost power controls. Confirm unavailable emblems cannot be selected.
- Save, quit the application, reopen, then enter Hero, Villain and Battle Lab. Confirm the chosen outfit/colors; change powers and return to Forge; verify no appearance reset. Compare TITAN/NOVA to their existing looks.
- Run, jump/double-jump, backflip, climb/mantle/release, land, punch, combo kick, Hurricane Kick and cast signatures. Inspect hood/chest seams in extreme poses; automated finite-vertex tests cannot approve deformation aesthetics.
- Toggle B, look fully up/down near walls, cast aimed powers, and toggle back. Confirm no headgear/body blocks the view and shadows return correctly.
- Profile a representative busy city and repeated Forge visits in a standalone build. Check skinning/draw costs and memory teardown before promising an FPS improvement.

## Extending the catalog

For TITAN/NOVA, keep their legacy definitions/defaults and original visual scale. Prepare shared meshes against each hero's actual baked fit and full skeleton (their accessory bones differ from VECTOR). Never assume matching Humanoid avatars mean identical skinning arrays. Add definitions with that hero's ID and compatible suit IDs; specify existing ordered bone paths matching mesh bind poses. Flexible sections need correct weights; rigid attachments can be a one-bone skinned mesh under this assembler. Validate pose/UV/material slots, finite bone counts, bounds and first-person startup capture before marking Available. Maintain one Animator and fit before attachments. Preserve old IDs; deprecate via availability/fallback rather than deleting saved values. Run the same preview, save/reload, animation, first-person and resource controls for each new fit.

`VectorCosmeticBake.Run` is an editor-only reproducible bake for the current VECTOR source recipe. It fails if source part boundaries change and must not be used to overwrite a future artist-authored catalog without review. It does not modify vendor sources or the Sidekick database.


## Implementation checkpoints

- `115c3a4`: fitted shared meshes, catalog, material cache and additive per-hero save records.
- `d0467c9`: shared Forge/gameplay appearance integration.
- `68bd2e9`: deferred skinning refresh and visual/save/gameplay controls.
- `80eddb6`: expanded form layout and scrolling controls. The final evidence commit also corrects the test-only style getter noted above.

Final native counts, selected process manifests, preservation proof and the final evidence commit are recorded in the appended STATUS entry and handoff. No merge or push was performed.


## Final selected evidence — 2026-10-23

**679 PASS / 0 FAIL in nine serial Unity 6000.6.0f1 processes**, plus **39 PASS** in the native asset-bake process. Exact selection: `Verification/VectorCustomization/selected-evidence.json`.

| Suite | Run | Separate Reload |
| --- | ---: | ---: |
| VECTOR appearance, UI layout, save and gameplay | 132 | 4 |
| Existing Hero Forge (`VerifyExisting`) | 136 | 3 |
| Existing Sidekick | 102 | 7 |
| Existing first-person | 211 | 5 |
| Existing Humanoid presentation | 79 | — |

The final affected UI/appearance suites are in `ui-final-2`; first-person and Humanoid gates are selected from `regressions-1` (their presentation/physics code was unchanged by the subsequent scroll-layout correction). Every selected process exited 0 and kept the personal save and Sidekick database byte-identical. No unresolved failure remains in this selected evidence. Historical failed visual/fixture attempts are retained and described above.

Warm preview resources remained **18 live Mesh / 67 Material / 285 Texture2D** across 96 explicit edits, with the same model, Animator, camera, 400×500 RenderTexture and suit material. Runtime body/mask vertex/bone budgets stay below the optimized original, while renderer count rises from one to two; standalone performance remains UNVERIFIED.

`preservation.txt` confirms **1,002 pre-existing modified/untracked files** match task-start hashes and **524 pre-existing regression files** were restored byte-exact. **66 fresh changed/new regression artifacts** are retained separately under `regression-artifacts`; test profiles and raw Editor logs remain local/ignored. Current appearance captures: `vector-forge-menu.png`, `vector-hood.png`, `vector-hood-face-cover.png`, `vector-gameplay-city.png`. These are automated captures, not human art approval.
