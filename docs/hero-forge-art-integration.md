# Hero Forge 2.0 — actual art integration

Date: **2026-10-25, America/New_York**. Branch: **feature/hero-forge-art-integration**.
Worktree: /Users/melaniehernquist/Documents/ChatGPT/op-hero-forge-art-integration

No push or merge into main/the original checkout. This report supersedes earlier missing-art/registration handoffs **only for this integration branch**.

## Repository audit and consolidation

Original checkout: codex/vector-modular-customization at eb5290c, with 1,003 pre-existing modified/untracked files recorded by hash. It is not used as scratch space.

The committed baseline is **3301e39**, descended from eb5290c. It includes the eight Synergies 2.0 signatures, parkour, Home Battle Lab, Skirmisher/Bulwark and committed Pulsebreaker. No unfinished source edits were copied from Pulsebreaker's worktree; it had only a generated downloader flag untracked.

NOVA **8534740** and TITAN **6c69511** were inspected and merged here as **74ea992 / 71677d6**. Only STATUS append conflicts occurred; the baseline history and both complete character sections were retained. The VECTOR catalog/renderer/preview/save foundation remains the single architecture.

The previous gaps were real: VECTOR's emblems and NOVA/TITAN's four target masks lacked meshes, and TITAN retained fantasy chest detailing. This branch produces new native derivative meshes and registers them in the normal catalog.

## Working wardrobe

| Hero | Suit | Two actual headgear choices | Two actual emblems |
| --- | --- | --- | --- |
| VECTOR | Tactical Weave | Aegis Visor; Sentinel Cowl | Chevron; Bolt |
| TITAN | Guardian plated suit | Guardian Helmet; Vanguard Mask | Shield Crest; Power Core |
| NOVA | Energy Weave | Spectrum Visor; Phantom Helmet | Nova Star; Energy Core |

Unmasked is an additional choice and is **not** counted toward the two rendered masks. Original legacy appearances and old VECTOR starter/hood options remain compatible historical choices, not replacements for the new suits.

Stable IDs:

- VECTOR: vector_suit_tactical; vector_mask_aegis; vector_mask_sentinel; vector_mask_unmasked; vector-emblem-chevron; vector-emblem-bolt.
- TITAN: titan_suit_guardian; titan_mask_guardian; titan_mask_vanguard; titan_mask_unmasked; titan_emblem_shield; titan_emblem_core.
- NOVA: nova_suit_energyweave; nova_mask_spectrum; nova_mask_phantom; nova_mask_unmasked; nova_emblem_star; nova_emblem_core.

Every target choice references a real imported native Mesh and correct bone paths. The two options in each slot are different assets and produce different rendered pixels. No missing-art dropdown is counted as a feature.

## Source assets and artwork production

This is **original low-poly derivative starter art**, authored/fitted through Unity Mesh APIs. Human approval of final art quality remains pending. A Blender executable was not on PATH and no Blender app was found in /Applications. No pack purchase/install, model download, image-as-model substitution or destructive vendor edit was performed.

Full suits use actual modeled/skinned Synty parts. Headgear is purpose-modeled attachment geometry fitted from the actual neutral head cross-sections, with distinct visor bands/rims, face coverage/cutouts and crown profiles. These are new 3D meshes, not a claim that those designs existed in the installed pack. Emblems are deliberately authored small surface-fitted geometry. Entire heroes are not assembled from primitives.

Preserved playable prefabs and source recipes:

- Assets/Resources/Presentation/vector-rigged.prefab — Starter_03; masculine, skinny26.6 fitting.
- Assets/Resources/Presentation/titan-rigged.prefab — Starter_01; masculine, neutral fitting; existing 1.2/1/1.2 visual proportions.
- Assets/Resources/Presentation/nova-rigged.prefab — Starter_02; feminine100, skinny14.5 fitting; existing .92/1/.92 proportions.
- Recipes: Assets/Synty/SidekickCharacters/Characters/Starter/<Starter>/<Starter>.sk.
- Fitted original source: Assets/Resources/Forge/Sidekick/<hero>-body.asset.

Facial source names use SK_HUMN_BASE_01_ plus codes 01HEAD,03EBRL,04EBRR,05EYEL,06EYER,07EARL,08EARR,35NOSE,36TETH,37TONG and suffix _HU01. Original hair and integrated headgear are excluded from the new suits.

New garment parts under Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/ use SK_HUMN_BASE_01_ plus 10TORS,11AUPL,12AUPR,13ALWL,14ALWR,15HNDL,16HNDR,17HIPS,18LEGL,19LEGR and suffix _HU01. VECTOR/NOVA use these ten parts and their fitted original boots. NOVA boots are SK_SCFI_CIVL_09_20FOTL_HU01 / 21FOTR; VECTOR boots are the two 20FOTL/21FOTR parts listed in its exact recipe inventory.

TITAN retains fitted SK_FANT_KNGT_17_ parts 11AUPL,12AUPR,13ALWL,14ALWR,15HNDL,16HNDR,20FOTL,21FOTR with _HU01 suffix. Its laced torso/skirt are replaced with modeled human torso/hip/leg geometry. The rigged torso is reshaped into a wider reinforced chest; no duplicate torso is layered underneath. Retained gauntlets/shoulders/boots still have source-design cues and require human style approval.

**Exact full part names, morphs, measured pivot/bind differences, counts and badge-clearance samples:** Verification/HeroForgeArt/native-bake.txt.

## Rigging and assembly

Full Sidekick skeletons, existing Humanoid Avatars, canonical fitting, shared controller and one Animator remain. No physics-root animation, root motion, Mixamo clothing skeleton or costume Animator.

Names alone are not treated as bind-pose proof. The baker measures imported axis differences, uses source skin weights to fit actual anatomical pivots and calculates target bindposes. Retained source ranges are UV-verified. TITAN's installed recipe has a net three-vertex drift in excluded shoulder attachments; the entire exact retained head UV sequence is recovered uniquely.

One shared skinned body; one existing head bone per mask; one spine_03 bone per emblem. At runtime there are three bounded SkinnedMeshRenderers, one Animator and one cached suit material. Mask swaps replace the same renderer. New pieces have no colliders or gameplay components.

Bodies: VECTOR **5,065 vertices / 65 weighted entries**; TITAN **7,336 / 65**; NOVA **5,065 / 65**. Rigid mask/emblem each add one renderer bone entry: **67 total**, below legacy body budgets. Full original transform hierarchies remain; lower upload entries are not a claim that Animator hierarchy cost disappears.

Pose review found tiny crown/skull intersections in the first enclosed-helmet iteration. That iteration is preserved as rejected evidence. Crown rings now cover the full skull profile before tapering above it. Final counts and post-correction evidence are recorded below.

## Color, preview and normal gameplay

Existing HeroDefinition.Suit, SidekickSuit and CityMaterials.SuitMaterial supply Primary/Secondary/Accent via palette color maps. Only derivative costume UVs are remapped; original face/eye swatches remain protected. New materials are not allocated every frame and vendor materials are not modified.

Bright accents are palette colors. Bespoke emission masks/glow, bloom and a new shader pipeline are not included. Built-in Render Pipeline remains.

**Assets/Resources/CosmeticCatalog.asset is persistently registered.** Normal Home in this worktree can use the new wardrobe immediately; no temporary registration scope or special test scene is required. Compatible Available entries populate the existing Forge controls. Missing-art option buttons are omitted; unknown saved IDs retain a fallback explanation. Power/secondary/signature selection and the scroll/footer layout remain.

The existing 400x500 preview preserves its character, camera, Animator, RenderTexture and mutable material across simple edits. HeroAppearanceRenderer is shared between preview and gameplay. Assembly follows canonical fitting and precedes the first-person renderer cache, so body/mask/emblem all hide as ShadowsOnly and restore normally.

The verifier enters all three heroes through **Hero, Villain, Free Play, both Endless sides and Combat Lab: 18 ordinary routes**. It alternates both mask variants and checks actual selected IDs, materials, stats, capsule and camera behavior. Combat controls exercise running, jumping, double jump, backflip, climb, punch, Martelo combo, Hurricane, Fire, Phase Shift and Solar Flare. Flight is outside this loadout and is not claimed tested.

## Save compatibility

PlayerProgression, ProgressSave and the outer Version 1 format are unchanged. Additive per-hero appearance records remain outside reconstructed HeroLoadout. Missing records retain legacy defaults/colors. Unknown IDs survive with safe render fallback.

Protected-JSON controls preserve noncosmetic data during appearance changes, including XP/levels, a purchased upgrade, powers/secondary/signature, difficulty and seeded mode/parkour records. A separate process compares exact final isolated profile JSON and reconstructs all three outfits in actual Hero Mode. This does not establish every historical/corrupt/interrupted-save scenario.

## Files and commit phases

- Gameplay baseline: 3301e39; character consolidation: 74ea992 / 71677d6.
- Real art/catalog assets and native authoring: ba620f4.
- Forge missing-option behavior: 77e88d4.
- Integrated tests: 21aa9ca.
- First crown-ring correction: cf4537e / 9eae402; rejected after a residual overlap audit.
- Final quantitative skull audit and independent diagnostic camera: 1ec6da0.
- Final native helmet clearance geometry: 94cc580.
- Final report/evidence commit is the branch HEAD; consult git log for its hash.

Art: Assets/Resources/Forge/Cosmetics/{Vector,Titan,Nova}/ and central Assets/Resources/CosmeticCatalog.asset.
Editor authoring/audit: Assets/Editor/HeroForgeArt/HeroForgeArtBake.cs, ForgeHeadgearAuthoring.cs, HeroForgeArtPreview.cs, HeroForgeArtFitAudit.cs.
Editor harness: HeroForgeArtVerification.cs. Test-only runner parts: Assets/Scripts/Forge/ArtVerification/.
Only product UI edit: Assets/Scripts/Forge/HeroForgeScreen.cs removes missing-art option buttons.

No new art change to movement, powers, stats, save reader, camera implementation, city batching, controller, Avatar, shared scenes or vendor files. Pulsebreaker changes come from its committed gameplay baseline, not this art work.

Do not run the historical VectorCosmeticBake.Run against the integrated catalog: it reconstructs the old partial catalog. Current authoring entry point is HeroForgeArtBake.Run. Future artist-authored replacements must be preserved/reviewed before rebaking.

## Verification and limits

Initial native bake: **183/0**; six native art renders: **6/0**. Preliminary integrated suites: **481/0 + Reload41/0**, then teardown extension **499/0 + Reload44/0**. They precede crown correction and are retained separately. First authoring compile failed on a local variable collision, corrected before successful creation; it is not counted as a pass.

Final selected post-correction manifests, exact process counts/hashes, inventory, regressions and measured render observations are recorded below. Pre-existing Metal tessellation errors, obsolete API warnings, old DOTween importer metadata and a Cinemachine sample asmref warning mean the entire Console is not claimed clean. New runtime Script errors/Exceptions/Asserts fail the integrated verifier.

### Selected final evidence

**1,738** checks passed across **14** separate serial Unity 6000.6.0f1 processes; zero failed assertions, every process exit 0. Real personal-save and Sidekick database hashes were unchanged in every selected process. These totals sum assertion lines, not unique player actions.

| Native entry point | PASS | FAIL | Source commit |
| --- | ---: | ---: | --- |
| HeroForgeArtBake.Run | 183 | 0 | 1ec6da0 |
| HeroForgeArtFitAudit.Run | 4 | 0 | 1ec6da0 |
| HeroForgeArtPreview.Run | 6 | 0 | 94cc580 |
| HeroForgeArtVerification.Run | 499 | 0 | 94cc580 |
| HeroForgeArtVerification.Reload | 44 | 0 | 94cc580 |
| HeroForgeVerification.VerifyExisting | 136 | 0 | 21aa9ca |
| HeroForgeVerification.Reload | 3 | 0 | 21aa9ca |
| FirstPersonVerification.Run | 211 | 0 | 21aa9ca |
| FirstPersonVerification.Reload | 5 | 0 | 21aa9ca |
| HumanoidVerification.Run | 79 | 0 | 21aa9ca |
| PulsebreakerVerification.Run | 451 | 0 | 21aa9ca |
| PulsebreakerVerification.Reload | 8 | 0 | 21aa9ca |
| SidekickVerification.Run | 102 | 0 | 94cc580 |
| SidekickVerification.Reload | 7 | 0 | 94cc580 |

Selected records: Verification/HeroForgeArt/selected-evidence.json. Full process metadata are in fit-final/manifest.json, final/manifest.json, regressions/manifest.json and sidekick-final/manifest.json under that folder. Regression suites target preserved shared/legacy behavior at their recorded source revision; they do not individually test every new outfit. The final integrated Run/Reload use the committed final meshes at **94cc580**. Bake/audit ran **1ec6da0** with generated geometry subsequently committed as 94cc580. No source or asset edit followed final gameplay verification; subsequent work is evidence and append-only documentation.

The initial authoring compile failure and rejected crown runs are retained under attempts and their original manifests. The first crown-ring correction still failed actual skull audit: Sentinel minimum -1.494 mm. The final larger shell passes all four tests: 23 actual neutral crown vertices each, no missing shell rays, minimum radial clearance **Sentinel 1.077 mm; Guardian 4.023 mm; Vanguard 1.078 mm; Phantom 1.395 mm**. This samples the rigid skull/crown, not every face/neck surface or continuous animation frame. Exact values: crown-fit-audit.txt. Final six pose sheets cover 66 pose/mask snapshots with independent camera framing. No obvious crown breakthrough was seen in the reviewed final sheets; artist clearance review remains.

Final headgear vertices/triangles: Aegis **192/96**, Sentinel **737/368**, Guardian **817/408**, Vanguard **697/348**, Spectrum **240/120**, Phantom **817/408**. The **15 native meshes** (three bodies, six masks, six emblems) have exact paths, GUIDs, counts and SHA256 in asset-inventory.json. No IDs substitute for absent meshes.

The final integrated suite verifies 96 edits per hero without persistent Mesh/Material/Texture2D growth or rebuilding the preview model/camera/Animator/RenderTexture/material. It checks preview RenderTexture count returns to zero outside Home on all 18 routes and three reload spawns. Existing shared content, appearance isolation, unknown-ID retention, legacy fallback, animation bindings, first-person hiding and stat/root/capsule invariants use positive and negative controls.

Historical verification outputs were not silently replaced. Thirty-six earlier regression artifacts were restored byte-exact and fresh captures archived in regression-artifacts; the Sidekick run separately restores all 317 pre-existing files including its older sandbox fixtures. Personal saves were never used as test fixtures. source-preservation.json verifies **6,302 protected baseline files** and the original checkout's **1,003 pre-existing local files**; no unexpected change. Existing baseline differences are limited to central cosmetic catalog, missing-art UI behavior and appended STATUS. changed-files.txt enumerates the entire branch delta from 3301e39, including the two imported modules and their earlier evidence.

### Measured rendering observations

Same idle Battle Lab player/camera, no NPCs, 60 warm frames then 120 measured frames for each sequential legacy/modern condition. One explicit 1280x720 Camera.Render per measured frame. Render CPU uses Stopwatch; whole-Editor frame uses unscaled frame time; whole-frame GC uses ProfilerRecorder. All six whole-frame GC medians were **1,468 bytes**. The Editor/test harness contributes to these figures.

| Hero | Legacy render CPU median/p95 ms | Modern render CPU median/p95 ms | Legacy whole Editor frame median/p95 ms | Modern whole Editor frame median/p95 ms |
| --- | --- | --- | --- | --- |
| VECTOR | .452 / .533 | .474 / .554 | 1.834 / 2.287 | 1.607 / 1.988 |
| TITAN | .463 / .523 | .472 / .589 | 2.089 / 2.346 | 1.663 / 2.045 |
| NOVA | .453 / .503 | .461 / .521 | 1.983 / 2.246 | 1.524 / 1.918 |

The explicit render CPU observations are slightly higher with the modern outfits; those results are retained. Lower whole-frame observations do not establish a causal improvement. This does **not** establish GPU skinning cost, sustained FPS, standalone performance, city/crowd behavior or long-session memory stability. No city detail or batching change was made.

### Remaining artwork and visual limits

The required initial three suits, six masks and six emblems exist and render in ordinary gameplay. No mandatory option remains a missing-art entry. These starter derivatives still need human visual approval: especially TITAN's retained gauntlet/boot source cues, low-poly helmet silhouettes, garment panel colors, badge float through extreme bends, and gameplay-distance identity. A professional art pass may replace assets while retaining stable IDs. Additional suit families, independently swappable gloves/boots/capes, custom animations and authored emission textures are later art work, not supplied here. Body/neck/joint snapshots and sampled badge/crown clearance do not certify all motion free of intersections. Flight with the new outfit loadout, sustained standalone/crowd performance and subjective combat feel remain **UNVERIFIED**.

Human checklist:

1. Start this worktree's Assets/Scenes/Home.unity. Choose each named new suit, both masks, both emblems and all three colors. Legacy defaults intentionally remain for older profiles.
2. Approve/reject front/side/back silhouettes, visor width, face coverage, crown/jaw/neck clearance, armor and superhero identity. These are functional starter meshes, not human-approved polished art.
3. Review shoulders/elbows/wrists, waist/crotch/knees/boots and full animation timing. Snapshots are samples, not continuous no-clipping proof.
4. Inspect badge float/intersection through extreme spine bends and oblique views.
5. Compare Forge/city/night and combat-distance readability; accent color is not emission.
6. Toggle B near walls throughout movement/powers; check all restored shadows.
7. Cycle heroes, save/quit/reopen and inspect independent outfits plus progression. Hero selection still restores combat defaults by existing design.
8. Profile a standalone build, busy crowds, repeated visits and long sessions. Narrow idle Editor render/whole-frame/GC measurements do not prove sustained FPS, GPU cost or a causal improvement.

## Launch and later integration

The catalog is already registered here; no additional registration menu is needed. Open this worktree, import, start Home and select the new suit in Hero Forge.

No automatic main/original-checkout merge or GitHub push. When separately authorized, integrate the branch as a whole into the chosen destination, retain STATUS history, review catalog references and rerun the suites there while preserving local work. Do not take only selectors/IDs and omit native Mesh assets, their .meta GUIDs or the shared VECTOR foundation.
