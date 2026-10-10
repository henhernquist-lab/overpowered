# Darkness, Lightning, Poison and Force Field visual pass

**State, 2026-10-10:** Implemented and runtime-verified on
`codex/power-polish-dark-lightning-poison-shield`. Human visual acceptance remains
open. Unity 6000.6.0f1, Built-in render pipeline. No shipping mission pool,
power damage/range, progression/save, Sidekick vendor material or database change.

## Exact source files

- `Assets/Scripts/Powers/StatusPowerVfx.cs` (+ `.meta`): fixed Darkness and Poison status line pools.
- `Assets/Scripts/Powers/DarknessEffect.cs`: target-local restraints and brief aimed activation snap.
- `Assets/Scripts/Powers/LightningEffect.cs`: ordered primary/chain core, shared afterglow and impact pops.
- `Assets/Scripts/Powers/PoisonEffect.cs`: tick-synced target cue and real-recipient spread wisps.
- `Assets/Scripts/Powers/PowerVfx.cs`: prewarm shared VFX swatches and reset reused line ordering.
- `Assets/Scripts/Powers/PlayerShield.cs`, `Assets/Scripts/CityNpc.cs`: coherent shield, localized real-hit cue, view-specific presentation.
- `Assets/Scripts/Powers/RosterVerificationRunner.cs`: gameplay-linked visual controls.
- `Assets/Editor/PowerPolishVerification.cs` (+ `.meta`), `Assets/Scripts/Powers/PowerPolishVerificationRunner.cs` (+ `.meta`): isolated-save captures, pool stress and city-night frame sample.
- `Assets/Scripts/HudVerificationRunner.cs`: current Phase Shift default has no Fire + Strength synergy; assert three real slots and refusal of a phantom synergy action while retaining synergy-present checks.
- `STATUS.md`, this report and selected `Verification/PowerPolish/` evidence.

## Before and after

| Power | Historical before | Current result |
| --- | --- | --- |
| Darkness | Four faint, long dark lines that could read as debug geometry against the street. | A brief target-directed snap followed by four close helical bands with lighter edges, leased only while the target is rooted. The city-night capture shows the restraint among dark buildings, though the target is small at normal camera distance. |
| Lightning | Similar-width dull arcs across the whole chain. | The actual first hit gets a brighter, wider cream core and blue residual path; later true hits use narrower ordered lines and compact pops. First-person bolt geometry starts farther ahead of the eye so the target remains visible. |
| Poison | Tiny tick particles and a thin lit spread line gave little persistent status. | A bounded organic ring follows each poisoned target, pulses on real DoT ticks, and short wisps connect each actual death-spread recipient. Ice's solid freeze and Darkness's restraint keep separate silhouettes. |
| Force Field | Three arbitrary circles with no hit-side reaction and an intrusive near-eye risk. | Five composed great/latitude circles form a shell. Real absorbed damage makes one source-side ripple; break pops immediately, expiry tapers. First person shows only short peripheral edges and a brief side flash. |

The four archived Roster captures were inspected for the initial audit; they are
not matched day/night or first-person before captures. See STATUS Phase 0. Fresh
captures are under [`Verification/PowerPolish/`](../Verification/PowerPolish/):
`{darkness,lightning,poison}-{day,night}-{third,first}.png`,
`shield-{day,night}-{third,first}.png`,
`shield-hit-{day,night}-{third,first}.png`, and
`darkness-night-city-third.png`. The final run overwrote these names with its
own current images; its manifest is
[`captures-final-20261010-r2/manifest.json`](../Verification/PowerPolish/captures-final-20261010-r2/manifest.json).
The isolated captures are geometry/readability fixtures, not a simulated
urban playtest. The city-night image supplies the actual dark-building context.

## Pooling and resources

`StatusPowerVfx` owns eight 4-line Darkness slots and 32 Poison rings, built
once per session. Existing `PowerVfx` stays at 16 transient lines; a maximum
six-target Lightning chain uses 12 (core plus afterglow). Particles use the
existing fixed impact pool. All swatches are cached `CityMaterials.GetVfx`
materials; no per-cast Material, mesh or permanent GameObject is created.
The status pool reports misses instead of expanding. More than eight
simultaneous roots or 32 poisons can lose a cue until a lease frees; gameplay
continues. The tested repeated-cast fixture recorded no new pool misses.

Each power received four view/light captures and 12 additional accepted casts.
Warm-to-stress counts from the final focused run:

| Power | Materials | Meshes | GameObjects | Renderers | New pool misses |
| --- | ---: | ---: | ---: | ---: | ---: |
| Darkness | 113→113 | 1018→1018 | 4188→4188 | 1968→1968 | 0 |
| Lightning | 113→113 | 1018→1018 | 4188→4188 | 1968→1968 | 0 |
| Poison | 113→113 | 1018→1018 | 4188→4188 | 1968→1968 | 0 |
| Force Field | 111→111 | 1018→1018 | 4100→4100 | 1889→1889 | n/a |

## Verification

Every row below is a fresh Unity process; reloads ran in separate processes.
Counts and hash checks live in the linked manifests under
[`Verification/PowerPolish/`](../Verification/PowerPolish/).

| Suite | Result | Evidence |
| --- | --- | --- |
| Roster VerifyExisting / Reload after final source | 212/0, 3/0 PASS/FAIL | `roster-final-20261010/manifest.json` |
| Focused PowerPolish.Run final | 134/0 | `captures-final-20261010-r2/manifest.json` |
| Combat | 176/0 | `regression-a-20261010/manifest.json` |
| FirstPerson / Reload | 211/0, 5/0 | `regression-a-20261010/manifest.json` |
| Camera.Verify, Feel | 45/0, 143/0 | `regression-a-20261010/manifest.json` |
| HeroForge VerifyExisting / Reload | 136/0, 3/0 | `regression-b-20261010/manifest.json` |
| HUD final | 308/0 | `hud-rerun-20261010/manifest.json` |
| World city / Reload | 57/0, 3/0 | `world-20261010/manifest.json` |

The first focused stress attempt stopped at Darkness repeat cast 3 (28 PASS,
1 FAIL) because its fixture did not refill energy; it was repaired through the
existing power clock. A later optional frame-profile attempt stopped at a
disabled-camera lookup (131 PASS, 1 FAIL); retaining the camera reference
fixed it. Initial HUD stopped after 62 PASS, 1 FAIL on a historical four-slot
assumption despite the current default resolving no synergy; the revised
control checks both absent and real synergies. These failures were not hidden
or counted as final passes. All final sequential manifests report unchanged
personal save and Sidekick database hashes.

## Performance and first-person limits

The fixed-camera **same-city, same-night** sample in the final focused run
measured 76.30 FPS / 13.106 ms idle before Darkness, and 73.96 FPS /
13.520 ms with one real root active. Active renderers changed 1750→1754,
particle systems 8→8, and loaded materials 109→112 from first-use cached
swatches. This is a short active-vs-idle sample on the final tree, not a
pre-change code benchmark. The tagged populated-street World profile measured
83.55, 83.57 and 84.78 FPS (median 83.57); mean frame times 11.97, 11.97
and 11.80 ms (median 11.97); p95 12.75, 13.28 and 12.54 ms; whole-frame GC
1658, 1669 and 1654 B/frame. It was an idle hero in batch Editor Play Mode,
not a standalone build or active four-power battle. Evidence is
[`Verification/World/power-polish-final-20261010/results.txt`](../Verification/World/power-polish-final-20261010/results.txt).
Historical runs used different revisions and host load; no matched old-code
FPS delta is available or claimed.

First-person captures show the Darkness restraint remains on its target rather
than the lens, Lightning's latest near-eye path leaves the target visible,
Poison's target cue remains bounded, and the Force Field shell gives way to
peripheral lines. Programmatic view-shape and hit-response checks passed.
These static captures cannot establish comfort during rapid camera motion.

## Human playtest still needed

Henry should judge Darkness density and target silhouette against city night,
Lightning primary/chain thickness and afterglow timing, Poison tick salience
versus annoyance and whether spreads are legible, and Force Field shape,
localized hit feedback, expiry/break distinction and first-person distraction.
No claim that the visuals “look good” or that the entire game is complete is
made from batch captures.
