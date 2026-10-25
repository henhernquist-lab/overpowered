# TITAN modular customization handoff — 2026-10-25

Branch: `feature/hero-forge-titan`  
Worktree: `/Users/melaniehernquist/Documents/ChatGPT/op-hero-forge-titan`  
Baseline: `eb5290c` (VECTOR shared appearance foundation plus existing combat/parkour). Asset phase `f5ec986`, integration/test phase `e77774f`, signature-contract fixture correction `f52e0ab`, final garment UV zones `cdd58f3`, diagnostic lens `124ace8`. User explicitly assigned both TITAN and NOVA to Codex on separate branches and reserved the single Unity slot. No merge or push.

## Delivery and art acceptance

**Integration-ready functional module; incomplete target artwork.** Includes a fitted Guardian armor/body package, Unmasked option, Shield Crest and Power Core emblems, three color channels and a hero-local registration adapter. Native tests use temporary in-memory catalog registration. Ordinary Forge does not gain these entries until the explicit integration step below.

**FAILED visual target:** the installed knight breastplate still has fantasy-shaped details. The native render confirms that removing the plume, skirt and back gear does not by itself make this fully convincing modern superhero armor. It is delivered as reusable fitted armor/configuration, not certified final superhero art. A purpose-designed modern chest/shoulder kit needs an artist. Human approval of silhouette, quality and animation feel remains UNVERIFIED.

**Guardian Helmet and Vanguard Mask have no suitable installed meshes and are unavailable definitions, not completed variants.** Neither a plumed knight helmet nor a primitive substitute is presented as target art. Missing helmet positive controls remain UNVERIFIED / not satisfied. No purchased/installed packs, vendor edits, primitive superhero or new material pipeline.

## Actual model and sources

Playable prefab `Assets/Resources/Presentation/titan-rigged.prefab` retains its existing Humanoid Avatar, full Sidekick skeleton, shared controller, presentation root, foot IK and visual proportions `1.2/1/1.2`. Original optimized `Assets/Resources/Forge/Sidekick/titan-body.asset`: 26,338 vertices / 96 renderer bone entries. Original combined Starter mesh has 3,176 bone entries; source recipe has 36 parts.

Recipe: `Assets/Synty/SidekickCharacters/Characters/Starter/Starter_01/Starter_01.sk`. The baker retains exact already-fitted face/eyes/mouth/ears, torso/arm armor, gauntlets and boots from the optimized body. Fitted armor source names are `SK_FANT_KNGT_17_` plus codes `10TORS,11AUPL,12AUPR,13ALWL,14ALWR,15HNDL,16HNDR,20FOTL,21FOTR` plus `_HU01`.

Fitted head source names are `SK_HUMN_BASE_01_` with codes `01HEAD,03EBRL,04EBRR,05EYEL,06EYER,07EARL,08EARR,35NOSE,36TETH,37TONG`, suffix `_HU01`. Beard, plumed helmet, original hip skirt/trousers and attachment surfaces 22–34 are removed. No overlapping replacement helmet or dangling accessory geometry is left on the redesigned body; unused chains remain in the unchanged skeleton to preserve its Avatar.

New rigged trousers use actual `SK_HUMN_BASE_01_17HIPS_HU01.fbx`, `18LEGL` and `19LEGR` counterparts under `Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/`, neutral masculine shape. Garment-only UVs use existing palette swatches. These are skinned anatomical source meshes used as full-coverage costume fabric, not sculpted new trouser seams/detail. Exact source list and measured fitting are in `Verification/TitanCustomization/native-bake.txt`.

Imported part axes differ from the gameplay prefab (max matrix-element delta 2.65277) even though neutral anatomical pivots agree within 1.79e-7m. The baker computes bindposes against TITAN's existing bones and retains source skin weights; it never imports a Mixamo body skeleton or moves the actual hero bones.

The .sk recipe's removed shoulder attachment vertex counts differ from the combined prefab by a net three vertices. Early naive extraction FAILED and was stopped. The corrected baker verifies the full UV sequence of every retained range, recovers the full head sequence uniquely within a bounded search after excluded attachment drift, verifies total geometry accounting and refuses triangles crossing kept/removed boundaries. This targets the supplied source version; it is not a general asset importer.

## Data, rendering and save architecture

Local catalog `Assets/Resources/Forge/Cosmetics/Titan/TitanCosmetics.asset` uses existing `CosmeticCatalog` / `CosmeticDefinition` types and relative paths into the actual hero skeleton.

| Stable ID | Availability | Purpose |
| --- | --- | --- |
| `titan_suit_guardian` | Available fitted armor study | One native combined skinned body |
| `titan_mask_unmasked` | Available | No additional renderer |
| `titan_mask_guardian` | Missing art | Enclosed angular modern helmet |
| `titan_mask_vanguard` | Missing art | Lighter modern armored face mask |
| `titan_emblem_shield` | Available | Surface-fitted authored Shield Crest |
| `titan_emblem_core` | Available | Surface-fitted authored Power Core |

The small badge meshes bind to the unchanged `root/pelvis/spine_01/spine_02/spine_03`. They are intentionally authored symbol geometry, not a body built from primitives. Final shield: 51 vertices / 80 triangles; final core: 320 vertices / 512 triangles. Bind-pose triangle/edge samples clear the torso by at least 2mm after bounded additional stand-off of 2.293mm (shield) and 3.676mm (core), beyond the initial 6mm projection. Shield-equipped total: 10,313 vertices / 66 renderer bone entries; core-equipped total: 10,582 / 66. Both use two SkinnedMeshRenderers, one Animator and one cached suit material. No standalone performance improvement is claimed. Rigid chest weighting can still float/intersect at extreme poses; finite skinning or sampled bind-pose clearance does not prove continuous collision-free cloth.

Existing `HeroDefinition.Suit` / `CityMaterials.Suit` color-map behavior supplies Primary, Secondary, Trim/Accent, with protected face/eye cells untouched. Garment-only UVs are remapped so broad Metal 04 armor uses Primary, cloth/straps use Secondary, and Metal 01/02 regions use Accent; facial UVs are untouched. New trouser UVs reference costume roles. This corrects the legacy swatch assignment that made most of the chest follow Accent. No new shader/material copies per frame or palette replacements on vendor meshes. BIRP remains. The loud red/cyan/amber test palette demonstrates independent channels, not final art direction. No bespoke glow/emission work.

`TitanCosmeticContent` explicitly combines hero-local definitions into the existing catalog. It validates its stable ID namespace, uniqueness and hero identity, refuses foreign collisions and is idempotent. It creates no second manager. Local Defaults are empty; all legacy defaults remain.

Existing `HeroAppearanceSave`, `PlayerProgression`, `HeroAppearanceRenderer`, Forge UI and startup camera cache own all persistence/rendering. No new outer save version or subsystem. An old Version 1 save with no TITAN record keeps legacy armor. Invalid IDs remain stored while the common resolver draws safe fallback; cosmetics never alter HeroStats, powers, physics, health formulas or runtime loadout caching. Exact fixture JSON is checked in a separate process; every possible historical save, interrupted write and release build remain UNVERIFIED.

## Added files and ownership

- `Assets/Resources/Forge/Cosmetics/Titan/`: local definitions, one suit Mesh, two badge Meshes, metadata.
- `Assets/Scripts/Forge/Cosmetics/Titan/TitanCosmeticContent.cs`: explicit catalog adapter.
- `Assets/Editor/Titan/TitanBodysuitBake.cs`: editor-only source fitting and asset bake.
- `Assets/Editor/Titan/TitanCosmeticIntegration.cs`: bone/weight validation plus explicit registration menu.
- `Assets/Editor/Titan/TitanAssetPreview.cs`: native isolated asset render diagnostic.
- `Assets/Editor/Titan/TitanCustomizationVerification.cs` and `Assets/Scripts/Forge/Cosmetics/Titan/TitanCustomizationVerificationRunner.cs`: editor-only real Forge/gameplay/save checks and pose captures.
- `Verification/TitanCustomization/`: evidence, native captures, serial manifests and failed authoring attempts; temporary saves/logs ignored.
- This report and appended STATUS entry. New shared parent folder metadata is byte-identical to NOVA's branch.

No edits to central catalog, shared Forge/progression/presentation/camera/gameplay source, shared scenes, VECTOR/NOVA art, vendor files or project tuning. The existing main checkout and its dirty work remain separate.

## Verification meaning

Unity 6000.6.0f1 uses the user's exclusive reservation, one Editor process at a time; asset import workers belonging to that Editor are not independent verification Editors. Serial wrapper records actual exit codes, assertion counts, personal-save and Sidekick DB hash guards. Review the final evidence block below.

Controls cover legacy save fallback, disabled unavailable helmets, bad IDs, independent hero choices, exact palette-map protection, actual emblem pixel changes, 96 repeated preview edits with no persistent mesh/material/texture growth, body budgets, unchanged Avatar/controller/root/capsule, first-person visual hiding/restoration, eleven animation-state skinning checks, real run/jumps/secondary/punch/Hurricane/backflip/climb/Fire/signature, normal city reconstruction and separate-process reload. Other heroes are verified against their original optimized mesh and Avatar, without modifying their content.

Native pose captures sample one point per animation; extreme-time clipping, continuous body contact, helmet fit, every power variant, standalone GPU cost/FPS, long sessions and crowds remain UNVERIFIED. Existing vendor Metal tessellation import errors and obsolete API warnings are not reported as a clean whole Console.

Failed bake evidence is retained: first exact range check failed at removed shoulder source; second failed at the retained head, exposing three-vertex recipe drift; third failed badge clearance on the breastplate ridge; a passing but over-dense badge fit was replaced with a smaller bounded tessellation. The first runtime suite stopped at 139 PASS / 1 FAIL because Cast was not observed on a single subsequent frame. Its preserved save selected Solar Flare, not Seismic Slam. The exact observed intermediate state was not logged, so the cause is UNVERIFIED. The fixture now checks dispatch immediately and with a bounded wait, and supports the actual equipped effect's presentation contract. Gates-2 then passed 140/0 + separate-process Reload 6/0 before the final UV-zone bake; that run exercised Solar Flare/Cast, not Seismic Slam/Punch. These are recorded failures/history, not successful verification claims. Fixture explicitly restores Fire/Strength after hero cycling (Forge intentionally resets hero powers), then waits real payment cooldowns rather than bypassing them.

## Merge-time integration (not executed)

1. Integrate the branch commits into the intended branch containing the VECTOR framework when authorized. Retain both STATUS entries if the separate character branches conflict there. Do not replace shared appearance code or vendor assets.
2. After import, choose **Overpowered > Forge > Titan > Register content after integration**, equivalent to `TitanCosmeticIntegration.Register()`. Exact shared change: `Resources.Load<CosmeticCatalog>("CosmeticCatalog").Items = TitanCosmeticContent.Combined(target)`, mark/save that asset through the supplied menu. Defaults and other heroes survive.
3. Review and commit only the resulting intentional `Assets/Resources/CosmeticCatalog.asset` update in the integration branch. NOVA's registration menu may run before or after this one; neither removes the other's definitions.
4. Reserve the single Editor slot, then run `python3 tools/verify-sequential.py --out Verification/TitanCustomization/integrated TitanCustomizationVerification.Run TitanCustomizationVerification.Reload`. Manually launch Home → Hero Forge → TITAN, choose Guardian / Unmasked / either emblem, set colors, Done, play city, save/quit/reopen to validate ordinary registration outside temporary test scope.
5. Replace the armor study with approved modern superhero chest/shoulder art; fit actual Guardian/Vanguard meshes to this real skeleton and expose them only after fit/first-person acceptance. Do not enable unavailable definitions without geometry. No automatic runtime registration or default-hero replacement is included.

## Human acceptance checklist

- Art gate: eliminate remaining fantasy breastplate cues and approve modern heavy-superhero identity, front/side/back silhouette, face, gloves and boots.
- Inspect neck/shoulders/gauntlets, waist/crotch, knees and boot seams across full run, jump, double jump, landing, climb, backflip, combo and Hurricane playback.
- Examine both badges from oblique angles through extreme spine bends; evaluate stand-off around breastplate ridges.
- Compare Forge/city lighting and all three channels; preserve facial colors. Check B first-person near walls and all restored shadow modes.
- Cycle TITAN → VECTOR → NOVA → TITAN, save and restart; inspect cosmetics and progression. New helmet fit/occlusion checks remain blocked by missing artist meshes.
- Profile a standalone build and long sessions before accepting performance. No quantitative FPS improvement is claimed.

## Source preservation

`source-preservation.json` compares all 6,277 baseline-tracked files other than the append-only STATUS against their immutable Git blob hashes: no changed or missing files. All production changes are new character-local content/adapters. No personal-save or vendor database repair was needed in the passing serial runs.

## Final selected native evidence

`Verification/TitanCustomization/final-framed/manifest.json`: Run **140 PASS / 0 FAIL**, separate-process Reload **6 PASS / 0 FAIL**, both exit 0; tested source `124ace8`, including final `cdd58f3` UV zones. Personal-save and vendor database hashes are unchanged in both processes. Runtime logs report body 10,262 vertices/65 entries plus core 320 vertices/one entry.

Final Forge/menu, Shield/Core swap, city render and eleven-pose contact sheet were inspected. No gross skinning explosion, plume, skirt, backpack or old hanging attachment is visible in these samples. Fantasy breastplate detailing still fails the desired final modern-art target; the broad back panel also follows Accent because of its inherited Metal 01 region and needs art/UV review. High Climb framing crops the crown. Human visual approval, continuous clipping and target helmet fit remain UNVERIFIED. See `poses/contact-sheet.jpg`, `titan_emblem_shield.png`, `titan_emblem_core.png`, `titan-forge-menu.png` and `titan-gameplay-city.png`.

## Exact tested ability loadout / correction

Final saved fixture: **Fire + Strength, Phase Shift secondary, Solar Flare signature**. Phase Shift activation and its appearance interaction passed. Flight activation and the Flight-specific procedural pose were **UNVERIFIED** in these runs; the fixture contains a Flight branch but the selected loadout did not exercise it. This corrects earlier conversational references to a tested Flight activation.
