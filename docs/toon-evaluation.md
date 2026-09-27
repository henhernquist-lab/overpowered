# Built-in toon evaluation — 2026-09-27

Decision: retain Standard city and hero materials. `CityPalette.ToonCity` and `ToonHeroes` default OFF. The installed Unity Toon Shader remains available for an opt-in experiment; this is not an adopted visual improvement.

Setup uses Package Manager Client.Add, package com.unity.toonshader 0.15.1-preview. No render pipeline migration. `ToonSetup.Enable` opts into both surfaces; `ToonSetup.RetainStandard` restores both. The same shared CityMaterials instances and Sidekick generated colour maps are reused. Vendor shaders are respected. Built-in requires UTS_RP_BUILTIN: shader.isSupported alone did not detect the initially pink output. Sidekick captures now reject rendered magenta pixels as well as verifying swatches.

## Evidence and costs

`Verification/Toon/profile-incomplete/street-S.png`, `street-T.png`, `street-U.png` use the same seed, dense Downtown crossing, third-person camera, 1280x720 target and Ultra quality. S = Standard city/hero, T = Standard city/toon hero, U = toon city/hero. Mannequin NPCs stay enabled and Sidekick NPCs stay OFF. There are 24 civilians plus five police at three stars, optimized VECTOR body.

| Retained sample | FPS | p95 frame ms | Median draw calls | Opaque render ms |
| --- | ---: | ---: | ---: | ---: |
| T, round 1 attempt 2 | 95.14 | 11.57 | 267 | 1.29 |
| U, round 1 attempt 1 | 80.62 | 13.31 | 716 | 2.28 |
| S, round 2 attempt 2 | 86.42 | 12.52 | 266 | 1.25 |
| T, round 2 attempt 1 | 83.28 | 13.69 | 272 | 1.34 |

S/T round 2 gives a 3.63% FPS reduction for hero-only toon. City-wide draw calls are approximately 2.7x Standard, and opaque work is substantially higher. Do not treat cross-round FPS as a paired mean: the Standard round-1 samples were contaminated. Full raw attempts, CPU/load, NPC tiers and frame profiler counters are preserved. The scheduled three-round test was stopped during the round-2 U retry because external CPU activity kept its quiet-load guard waiting; no complete-profile PASS is claimed. The guard remains strict, with future waits capped at two minutes per attempt. This editor measurement is not a standalone player/GPU benchmark.

Forge comparisons are `forge-toon/forge-menu.png` and `forge-standard/forge-menu.png`. Toon recolouring is correct but its heavy outlines obscure small suit details. No visual improvement is claimed. The corrected hero recolouring and gameplay captures are also retained under checks. Historic pre-toon evidence is preserved in its original directories.

Verification: corrected Toon Sidekick Run + separate-process Reload, CityArt, Humanoid, FirstPerson Run + separate-process Reload passed. HeroForge is additionally run on both material configurations. The initial pink-render attempt is not counted as a visual pass. Final restored-Standard compile/check results are recorded in STATUS.

Future investigation: tune outlines in the actual Forge preview, then repeat paired measurements under quiet load before enabling either switch. Verify shader inclusion in a standalone build before shipping the opt-in configuration.
