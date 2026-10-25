# NOVA modular customization handoff — 2026-10-25

Branch: `feature/hero-forge-nova`  
Worktree: `/Users/melaniehernquist/Documents/ChatGPT/op-hero-forge-nova`  
Committed baseline: `eb5290c` (completed VECTOR shared appearance framework and existing combat/parkour). Asset phase: `375f2c5`; integration/test phase: `949f7c5`; diagnostic camera framing: `f19c02e`, `f6c43cd` (applied after Cinemachine lens evaluation); actual equipped-signature presentation fixture: `e528eae`. No merge or push.

## Delivery status

**Integration-ready character module.** Native Unity testing temporarily registers its definitions in memory. Ordinary Forge/gameplay will show the new wardrobe only after explicit central registration described below. No shared catalog, scene, UI, progression, combat, stats, movement, camera, other-hero asset or vendor asset is changed by this branch.

Available: fitted Energy Weave suit, safe Unmasked choice, Nova Star and Energy Core emblems, three palette channels. **Spectrum Visor and Phantom Helmet are NOT IMPLEMENTED artwork:** their stable definitions are unavailable, have explicit art requirements, and are disabled/refused by the existing UI/resolver. This is an incomplete wardrobe art delivery, not two finished mask variants. Optional glow is deferred; no suitable purpose-authored emission mask was supplied. Human visual approval remains UNVERIFIED.

## Actual model and source assets

Existing playable model: `Assets/Resources/Presentation/nova-rigged.prefab`; existing Humanoid Avatar, full skeleton, shared `Resources/SharedHumanoid.controller`, canonical presentation fitting and `.92/1/.92` visual scale are retained. Original optimized body: 26,575 vertices / 130 bone entries. Original 33-part Starter recipe is `Assets/Synty/SidekickCharacters/Characters/Starter/Starter_02/Starter_02.sk`.

The native baker verifies original part ranges and copies already-fitted face, eyebrows, eyes, ears, nose, teeth, tongue and boots from `Assets/Resources/Forge/Sidekick/nova-body.asset`. The head sources have the prefix `SK_HUMN_BASE_01_` with codes `01HEAD,03EBRL,04EBRR,05EYEL,06EYER,07EARL,08EARR,35NOSE,36TETH,37TONG` and suffix `_HU01`. Boots are `SK_SCFI_CIVL_09_20FOTL_HU01` and `SK_SCFI_CIVL_09_21FOTR_HU01`. See the exact full list in `Verification/NovaCustomization/native-bake.txt`.

New garment geometry uses legitimate rigged human anatomy FBXs under `Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/`, prefix `SK_HUMN_BASE_01_`, suffix `_HU01`, codes `10TORS,11AUPL,12AUPR,13ALWL,14ALWR,15HNDL,16HNDR,17HIPS,18LEGL,19LEGR`. The editor baker applies feminine100 / skinny14.5 shape offsets, fits vertices with their existing weights to NOVA's actual anatomical pivots, and assigns clothing-only palette UVs. This is a fitted full-coverage bodysuit made from skinned art, not newly sculpted designer armor or primitive geometry. Further panel/seam/detail work needs an artist.

Imported part axes differ from the playable rig (maximum matrix-element difference 2.65277); raw bind matrices are not interchangeable. Measured pivot delta is 0.02046545m. The baker applies weighted pivot offsets and computes bindposes against NOVA's unchanged bones. Actual controller poses were baked/rendered for review. This is specifically validated for the supplied model, not a universal Sidekick fitting algorithm.

Old fox mask, hair, robe/hip skirt, back and other attachment geometry are removed rather than overlaid. Unused skeleton chains remain to preserve the Avatar but do not deform rendered garment vertices. Existing neutral body mesh is replaced by one shared native suit asset; no model is generated per character at runtime.

## Definitions and visual assets

| Stable ID | Status | Mesh / binding |
| --- | --- | --- |
| `nova_suit_energyweave` | Available | 5,065 vertices / 65 weighted entries, existing bone paths |
| `nova_mask_unmasked` | Available | No additional geometry |
| `nova_mask_spectrum` | Missing art | Artist-fitted futuristic visor required |
| `nova_mask_phantom` | Missing art | Artist-fitted enclosed lightweight helmet required |
| `nova_emblem_star` | Available | 49 vertices / 64 triangles / existing spine_03 |
| `nova_emblem_core` | Available | 32 vertices / 32 triangles / existing spine_03 |

Catalog and three native Mesh assets live under `Assets/Resources/Forge/Cosmetics/Nova/`. Badges are deliberately authored small surface-fitted geometry, following the existing chest bone. Bind-pose samples measure minimum triangle/edge clearance 1.423mm (star) and 2.756mm (core). This is not proof of zero intersections throughout all animation times.

Colors use the existing Sidekick color-map architecture, `HeroDefinition.Suit` and `CityMaterials.Suit`. Garment UVs address existing Primary/Secondary/Trim cells. Accent drives Trim/badges. Face/eyes retain original UVs and protected colors. No vendor material or shader is modified; Built-in rendering remains in place. The vivid red/cyan/amber verification palette is a test of channel independence, not a proposed final art palette.

## Shared integration and saves

`NovaCosmeticContent` only combines NOVA definitions into existing `CosmeticCatalog.Items`; it validates unique hero-local stable IDs, refuses foreign collisions, replaces its own IDs idempotently and leaves legacy defaults/other entries intact. It is not another appearance manager. The module's Defaults array is empty.

Existing `HeroAppearanceSave`, `PlayerProgression.SetAppearance/Appearance`, `HeroAppearanceRenderer` and `HeroForgeScreen` perform rendering, UI and persistence. There is no new save subsystem or outer-version migration. Existing Version 1 profiles with no record retain `nova-legacy`; known selection creates/updates only the per-hero appearance list. Raw unknown suit/mask/emblem IDs survive save/reload while rendering safe fallback. Gameplay continues to cache its loadout; appearance cannot be mutated mid-session.

The fixture compares protected profile data and exact separate-process JSON including loadout/signature/difficulty, XP/levels and parkour records. Arbitrary production profiles, interrupted writes, every historical save and a release build remain UNVERIFIED. No progression fields were removed or reconstructed by this module.

## Files added

- `Assets/Resources/Forge/Cosmetics/Nova/`: local catalog, one suit Mesh, two badge Mesh assets, metadata.
- `Assets/Scripts/Forge/Cosmetics/Nova/NovaCosmeticContent.cs`: explicit local catalog adapter.
- `Assets/Editor/Nova/NovaBodysuitBake.cs`: reproducible editor-only native asset bake.
- `Assets/Editor/Nova/NovaCosmeticIntegration.cs`: binding validation and explicit registration menu.
- `Assets/Editor/Nova/NovaAssetPreview.cs`: isolated asset diagnostic.
- `Assets/Editor/Nova/NovaCustomizationVerification.cs` and `Assets/Scripts/Forge/Cosmetics/Nova/NovaCustomizationVerificationRunner.cs`: real Home/Forge/gameplay/save checks, editor-only.
- `Verification/NovaCustomization/`: native bake, serial manifests, actual PNGs, pose captures, preserved failed attempts; isolated saves/logs ignored.
- This report and an appended STATUS entry. Shared parent folder metadata has an identical GUID/text to TITAN's branch to simplify later integration.

## Verification and limits

See the final evidence entry below and the serial manifests. Unity 6000.6.0f1 processes run serially under the user's exclusive reservation. No simultaneous independent Editor is launched. The wrapper guards the real personal-save and Sidekick database hashes.

Verified controls include legacy fallback, disabled unavailable masks, invalid IDs, independent hero choices, exact color-map swatches, actual badge pixel changes, 96 preview edits without persistent mesh/material/texture growth, canonical scale/Avatar/controller, unchanged health formula, first-person ShadowsOnly/restoration, finite skinning in eleven states, real run/jump/double jump/Phase Shift/punch/Hurricane/backflip/climb/Fire/signature, ordinary city spawn and separate-process reload.

Runtime suit+core: 5,097 vertices / 66 renderer bone entries / two SkinnedMeshRenderers / one Animator / one shared cached costume material, below the original single-body vertex and bone budgets. Star instead totals 5,114 vertices / 66 entries. Added badge is one bounded renderer, not a claimed free draw call. No standalone FPS, GPU profile, long-session memory run, texture residency or crowd stress benchmark was performed. Existing vendor Metal tessellation import errors/obsolete API warnings are not represented as a clean whole-Console run.

Failed attempts remain visible: gates-1 stopped on an incorrect unselected-hero legacy-color expectation (3 PASS/1 FAIL); gates-2 stopped because hero cycling restored NOVA's default powers and the fixture's .6s pause was shorter than basic melee cooldown (127/1). The fixture now explicitly restores its paid combat loadout through Forge and waits the real cooldown; no gameplay gate was weakened. A later final-framed run stopped at 139/1 because Cast was not observed on the single subsequent frame. The preserved fixture save selected Solar Flare; the exact intermediate presentation state was not logged, so the failure cause is UNVERIFIED. The final fixture checks dispatch immediately and with a bounded wait and supports each eligible signature's actual presentation contract. A Seismic Slam failure must not be inferred from this output. Early strict matrix/pivot bake guards rejected incompatible assumptions before the measured weighted-fit correction. The first accessory-only suit retained a robe and was visually rejected; its image/inventory remain under `attempts/rejected-robe`.

## Merge-time integration (not executed here)

1. Integrate this branch's commits into the intended VECTOR-framework branch when authorized. Preserve existing dirty work and resolve STATUS append conflicts by retaining both entries. Do not replace shared appearance implementations.
2. Open the integrated project in Unity, wait for import, then use **Overpowered > Forge > Nova > Register content after integration** (equivalent to `NovaCosmeticIntegration.Register()`). This is the exact minimal shared change: combine local items into `Assets/Resources/CosmeticCatalog.asset`, then save that asset; legacy Defaults are preserved.
3. Review the resulting catalog diff. Both hero-local registration menus can be applied in either order; neither removes the other module's entries. Commit that central asset change in the integration branch.
4. Run `python3 tools/verify-sequential.py --out Verification/NovaCustomization/integrated NovaCustomizationVerification.Run NovaCustomizationVerification.Reload` with only one reserved Editor. Launch ordinary Home → Hero Forge → NOVA, choose Energy Weave / Unmasked / either emblem, set colors, Done, then enter Hero city. Verify the non-test catalog registration, save, quit and reopen.
5. Commission the two masks with correct fit/pivots/UVs or bone paths, preview them on this actual suit through extreme poses and first person, then fill their existing definitions and enable only after art acceptance. No automatic registration/startup patch or default-outfit change is included.

## Human playtest checklist

- Approve or reject the suit identity, face choice, boots, silhouette and channel placement in front/side/back views; it is not certified polished.
- Inspect shoulder, wrist, neck, hips/crotch, knees and boot seams through full animation playback, especially climb, double jump and both kicks; snapshots sample only one time per clip.
- Check badge float/intersection at extreme twists, landing and flight; chest-only weighting may need artist refinement.
- Check preview versus city lighting, all intended palettes and B first-person near walls; target helmets remain untestable until supplied.
- Switch VECTOR → NOVA → TITAN → NOVA; confirm outfits/colors, powers/signature and progression after quit/relaunch. Forge intentionally resets powers to hero defaults on hero selection; this module does not change that behavior.
- Profile a standalone build and long sessions before performance acceptance. Glow/emission art and target mask quality remain open work.

## Source preservation

`source-preservation.json` compares all 6,277 baseline-tracked files other than the append-only STATUS against their immutable Git blob hashes: no changed or missing files. All production changes are new character-local content/adapters. No personal-save or vendor database repair was needed in the passing serial runs.

## Final selected native evidence

`Verification/NovaCustomization/final-contract/manifest.json`: Run **140 PASS / 0 FAIL**, Reload **6 PASS / 0 FAIL**, both exit 0, tested source `e528eae`. Both processes preserve personal-save and vendor DB hashes. Final normal-city spawn reports 5,065 body vertices/65 entries plus the 32-vertex core/one entry. Earlier passing runs and failed attempts remain separately labeled.

Inspected final actual Forge/menu, Star/Core swap and city PNGs, plus the eleven-pose contact sheet. No gross body explosion, old robe, fox mask or hanging accessory is visible in these samples. The high Climb snapshot still crops the crown; it does not establish head/helmet quality across the full motion. Continuous clipping, designer polish and human playtest approval remain UNVERIFIED. See `poses/contact-sheet.jpg`, `nova_emblem_star.png`, `nova_emblem_core.png`, `nova-forge-menu.png` and `nova-gameplay-city.png`.

## Exact tested ability loadout / correction

Final saved fixture: **Fire + Strength, Phase Shift secondary, Solar Flare signature**. Phase Shift activation and its appearance interaction passed. Flight activation and the Flight-specific procedural pose were **UNVERIFIED** in these runs; the fixture contains a Flight branch but the selected loadout did not exercise it. This corrects earlier conversational references to a tested Flight activation.
