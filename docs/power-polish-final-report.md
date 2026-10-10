# Integrated power presentation pass — 2026-10-10

Branch: `codex/power-polish-final-pass`. Unity 6000.6.0f1, Built-in RP. This is a source and Editor-verification report, not a claim of final visual acceptance or a shipped game. Player save and Sidekick database hashes were unchanged in the cited isolated runs; no shipping mission pool was enabled.

## Actual roster and scope

The selectable primary powers are **Super Strength, Telekinesis, Fire Blast, Ice, Darkness, Laser Eyes, Lightning, and Poison**. The selectable secondary powers are **Speed, Teleport, Force Field, and Phase Shift**. Flight code and assets remain for legacy controls but are absent from the three shipping hero secondary lists. This pass did not visually polish Flight. Speed remains a 9 m / 0.18 s damaging dash, not a continuous sprint; Teleport and Phase Shift were already integrated when this branch began.

## Exact source changes

| Source | Change |
| --- | --- |
| `Assets/Scripts/TelekinesisEffect.cs`, `Assets/Scripts/PowerUser.cs`, `Assets/Resources/Powers/telekinesis.asset` | Five cached visual renderers follow the **actual held/thrown Rigidbody**: two object-local orbit rings, a narrow connection, a moving pulse, and a sampled throw trail. Real release and first collision produce bounded bursts. Existing Cyan palette swatch distinguishes this from Fire. No physics or combat value changed. |
| `Assets/Scripts/Powers/SpeedDashEffect.cs` | Replaced one generic broad trail with 12 cached, short longitudinal side streaks following measured controller displacement; start/stop particle snaps mark acceleration and halt. |
| `Assets/Scripts/Powers/PlayerPhaseShift.cs` | Slimmer front/side outline, three existing echoes, entry/exit ring, and two peripheral first-person cues. The hero stays visible; the camera center stays open. |
| `Assets/Scripts/Powers/TeleportEffect.cs` | Small departure and arrival pops at the actual successful move endpoints using existing pooled VFX. Refused teleports emit neither. |
| `Assets/Scripts/PowerPayoffVerificationRunner.cs`, `Assets/Scripts/Powers/RosterVerificationRunner.cs`, `Assets/Scripts/SecondaryVerificationRunner.cs`, `Assets/Scripts/Powers/PhaseShiftVerificationRunner.cs`, `Assets/Scripts/HumanoidVerificationRunner.cs` | Real-body/impact, displacement, no-move, view, pool and loadout controls plus current capture fixtures. The Humanoid fixture now asserts the unequipped Flight gate; scoped legacy Flight consumption remains covered by HeroForge. |
| `Assets/Scripts/VerificationHarness.cs`, `tools/verify-sequential.py` | `OP_VERIFY_SILENT=1` mutes only batch verification AudioListener output, so repeated Unity Play Mode checks no longer make sounds on the user's computer. Ordinary Editor/game audio is untouched. |

Existing visual work in this branch's parent history was audited rather than rebuilt: Strength physical impact, Fire comet/impact, Ice freeze/shatter, Darkness restraints, Laser continuous two-eye beam, Lightning ordered chain, Poison target ticks/spread, and Force Field shell/hit/break. The original implementation details and focused measurements for Darkness, Lightning, Poison and Force Field remain in [power-polish-report.md](power-polish-report.md).

## Visual inventory

All screenshot names below are under [`Verification/PowerPolishFinal/After/`](../Verification/PowerPolishFinal/After/). “Checked” means a static Editor capture or automated control, not a human motion/comfort verdict. Low intensity is for sustained status/ticks, medium for ordinary cast/arrival, high for physical or charged impact.

| Power | Activation → active/travel → payoff | First person | Representative capture |
| --- | --- | --- | --- |
| Strength | Paid windup → real force → high physical punch impact/debris; no sustained state | Existing camera/Feel controls; this run captured impact in third person only | `strength-impact-third.png` |
| Telekinesis | Paid grab → cyan orbit/connection around real suspended body → directional real-body throw and first-contact burst | Held, throw and impact captured; tether is offset below/right of center | `telekinesis-held-{third,first}.png`, `telekinesis-throw-{third,first}.png`, `telekinesis-impact-{third,first}.png` |
| Fire Blast | Comet launch → projectile travel → high flame/impact; clear wall sweep retained | Launch and close-wall controls pass | `fire-travel-third.png`, `fire-launch-first.png`, `fire-impact-third.png` |
| Ice | Cast → visible cyan frozen body → thaw/shatter; no separate traveling projectile for direct freeze | Frozen target captured in both views | `ice-frozen-{third,first}.png` |
| Darkness | Directed snap → target-local dark restraints → release at expiry; low active intensity | Target cue captured beyond eye | `darkness-active-{third,first}.png` |
| Laser Eyes | Two-eye channel start → precise continuous beam → contact on target; beam ends on release | FirstPerson and Feel controls pass; current dedicated screenshot is third person | `laser-beam-third.png`, `laser-beam-side-third.png` |
| Lightning | Cast core → angular ordered branch/afterglow → compact real-recipient impact pops | First-person bolt captured | `lightning-chain-third.png`, `lightning-active-first.png` |
| Poison | Cast → organic target ring/tick pulses → real-recipient death spread; low tick, medium spread | Bounded target cue captured | `poison-spread-third.png`, `poison-active-first.png` |
| Speed | Short directional snap → velocity-aligned longitudinal side streaks during real dash → short end snap; no teleport-like disappearance | Existing camera controls and dash geometry checked; no dedicated first-person dash frame | `speed-dash-third.png` |
| Teleport | Departure pop → actual absence/clear-path relocation → arrival pop; no connecting path | Secondary + FirstPerson suites pass; no dedicated first-person pop frame | `teleport-departure-arrival-third.png` |
| Force Field | Start → persistent composed shell → local hit ripple and immediate break/expiry; low persistent, medium hit | Peripheral hit cue captured | `force-field-shell-third.png`, `force-field-hit-first.png` |
| Phase Shift | Brief boundary pulse → ghosted outline and three echoes, normal-speed intangible traversal → clean rematerialization | Peripheral edge cue leaves crosshair clear | `phase-shift-{third,first}.png`, `phase-shift-dense.png` |

The table identifies the capture gaps: Strength, Laser, Speed and Teleport have no new dedicated first-person action image in this gallery, even though relevant camera/control suites passed. No isolated screenshot can prove instant recognition or comfort during live input.

## Before/after evidence and color hierarchy

The pre-edit audit inspected repository source and historical image archives, **not fresh matched before captures**. Telekinesis previously showed a bare physics cube in archived first-person shots (`Verification/Presentation/FirstPersonVerification.Run/hero-{third,first}-telekinesis.png`); its current images show a bounded cyan object-local orbit, connection and true-contact payoff. Speed previously had one generic orange TrailRenderer and step particles; the current mid-dash frame shows short displacement-aligned orange side streaks, and source/controls confirm a brief end snap. Phase Shift already had a thin front-plane outline and echoes; its old side angle could lose the silhouette and start/end had no boundary pulse. Current third/first-person captures show a side-visible purple silhouette and peripheral camera cue. Teleport already moved instantly through its clear-path gate but had only generic cast presentation; the current paired frame shows separate endpoint pops without a path. These are source-and-archived-reference comparisons, not matched same-camera A/B screenshots. The user's requested matched **old-code versus new-code** visual and performance baseline does not exist; no such delta is claimed.

Shape carries the identity: Strength uses a body hit, Telekinesis rings plus a tether, Fire a comet, Ice solid freeze, Darkness restraints, Laser a straight channel, Lightning angular branches, Poison organic pulses, Speed longitudinal motion, Teleport endpoint pops, Force Field a shell, and Phase Shift a ghosted silhouette. Palette colors are reused through `CityMaterials`; no global city/UI recolor or Sidekick material replacement occurred. High-intensity layers are concentrated at hits and release; sustained status shapes are restrained.

## Pooling, clutter and performance

`PowerVfx` retains its 16 transient line slots, Feel particles its eight pooled systems, and existing synergy/status pools stay bounded. Telekinesis has five cached renderer slots tied to the real body; Speed has 12 cached streaks; Phase Shift has two outline faces, one boundary, three echoes and two peripheral cues. No new per-frame GameObject or Material construction was introduced in these power paths. Phase Shift passed 20 repeat activations with stable material, mesh, renderer and object counts; current focused Darkness/Lightning/Poison/Force Field runs retained their warm-to-repeat counts, detailed in the earlier report. Some effects are bounded by pool capacity, so saturation can omit a cue without changing the actual gameplay effect.

The same **final-source** Phase Shift dense-combat fixture with 18 enemies measured 84.27 FPS / 11.866 ms mean while Phase was repeatedly used, and 73.93 FPS / 13.526 ms in a separate no-Phase baseline process. Both reported 0 whole-frame GC B/frame, 1888 renderers, 1019 meshes, 109 materials and 22 particle systems. This process-to-process variation cannot be attributed to Phase or interpreted as an old/new gain. The earlier fixed-camera Darkness same-city sample measured 76.30 FPS / 13.106 ms idle versus 73.96 FPS / 13.520 ms with one root active; this is an effect-active cost, not a pre/post code comparison. The final-tree populated-street World profile sampled 76.44, 80.02 and 78.89 FPS (median 78.89); mean frame time 13.08, 12.50 and 12.68 ms (median 12.68); whole-frame GC 1783, 1628 and 1629 B/frame. That profile had 29 NPCs and a parked hero, so it does not isolate power cost. These are Editor 1280×720 samples, not standalone build targets. See [`world-profile-20261010/manifest.json`](../Verification/PowerPolishFinal/world-profile-20261010/manifest.json).

The dense fixture verifies repeated Phase amid attacks, and HUD center-clear pixel controls pass at tested resolutions. It does not reproduce a player deliberately stacking all 12 powers in one chaotic battle; objective/telegraph visibility and first-person comfort under that load still need a human review.

## Fresh verification and failures

Each entry used one Unity Editor process at a time; Reload entries were separate processes. Counts are PASS/FAIL. Exact output, exit status and save/Sidekick hash controls are in the linked manifests.

| Suite | PASS/FAIL | Evidence |
| --- | ---: | --- |
| Roster / Reload, PowerPayoff | 215/0 + 3/0; 99/0 | [`final-captures-r2-20261010/manifest.json`](../Verification/PowerPolishFinal/final-captures-r2-20261010/manifest.json) |
| Phase Shift / Reload, Secondary / Reload | 142/0 + 3/0; 43/0 + 3/0 | [`phase-muted-20261010/manifest.json`](../Verification/PowerPolishFinal/phase-muted-20261010/manifest.json), [`visual-captures-20261010/manifest.json`](../Verification/PowerPolishFinal/visual-captures-20261010/manifest.json) |
| Combat, FirstPerson / Reload, Camera, Feel | 176/0; 211/0 + 5/0; 45/0; 143/0 | [`regression-a-20261010/manifest.json`](../Verification/PowerPolishFinal/regression-a-20261010/manifest.json) |
| HeroForge / Reload, HUD, Ice, Mode / Reload, focused four-power polish | 136/0 + 3/0; 308/0; 34/0; 79/0 + 2/0; 134/0 | [`regression-b-20261010/manifest.json`](../Verification/PowerPolishFinal/regression-b-20261010/manifest.json) |
| FireVisual, Humanoid rerun, Phase no-active control | 34/0; 71/0; 22/0 | [`gallery-and-profile-20261010/manifest.json`](../Verification/PowerPolishFinal/gallery-and-profile-20261010/manifest.json), [`humanoid-recheck-20261010/manifest.json`](../Verification/PowerPolishFinal/humanoid-recheck-20261010/manifest.json) |
| Populated World profile | 1/0 | [`world-profile-20261010/manifest.json`](../Verification/PowerPolishFinal/world-profile-20261010/manifest.json) |

The first final Roster capture attempt stopped at Darkness last-charge after **27 PASS / 1 FAIL**, in a fixture where live AI had moved actors before the aimed cast. Evidence did not capture the exact aim state, so root cause is not asserted. The revised fixture explicitly places actors in a clear line and proves `FindTarget` acquired the intended NPC before its positive/zero-charge controls; rerun passed. An earlier Telekinesis payoff run stopped at a stale Flight-secondary assumption (**96/1**); the fixture now uses the shipping Phase Shift loadout, preserving the no-synergy negative control. The first Humanoid run stopped at an obsolete Flight fuel assumption (**24/1**) while Flight was unequipped; the revised test checks refusal and ground recharge and passed **71/0**. Failures remain in their manifests and are not presented as passes.

## Human decisions and next improvements

Henry still needs to play in motion and decide which powers look cheap, whether any shape is too large, whether first-person motion is comfortable, whether Speed/Teleport/Phase Shift are instantly distinct, whether stacked combat obscures objectives/telegraphs/HUD, and whether impact timing feels strong enough. The five highest-value next visual improvements, subject to that review, are:

1. Capture and tune live first-person Speed, Teleport, Laser and Strength at normal camera motion, with direct crosshair and near-wall inspection.
2. Run a player-controlled mixed-power crowd encounter and shorten secondary layers where objectives or enemy telegraphs disappear.
3. Add a brief, clearly visible Laser contact treatment if human review finds the current precise beam payoff too subtle.
4. Tune the timing of Telekinesis collision burst against actual wall, floor and NPC impacts at normal play speed.
5. Build a reproducible same-scene, same-host old/new performance capture before any further VFX pass, so cost claims have a true baseline.
