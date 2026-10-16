# Nine-clip Mixamo integration — 2026-10-16

Branch `codex/mixamo-animation-integration`; source checkpoints `1289053`, `eae1e72`, `992fc8c`. Unity 6000.6.0f1, Built-in RP. This is an Editor source/runtime verification report, not a motion-quality sign-off or a finished-game claim. No shipping mission pool was changed.

## Clip inventory and use

All nine supplied FBXs are under `Assets/Animations/`. `Verification/AnimationIntegration/clip-audit.txt` records each source Humanoid Avatar, Sidekick retarget, duration, loop flag, joint samples and sampled hip/root travel. `Verification/AnimationIntegration/Preview/` contains five **baked posed-skin frames per clip**. These stills show actual evaluated Sidekick poses; they cannot establish transition quality at game speed.

| Exact asset path | Imported clip | Integrated use |
| --- | --- | --- |
| `Assets/Animations/X Bot@Head Hit.fbx` | 0.933 s, one-shot | Brief light NPC/player hit reaction. Incoming player damage never locks controls. |
| `Assets/Animations/X Bot@Taunt.fbx` | 2.833 s, one-shot | Low-priority hero idle flourish after 12 seconds; movement/input/cast cancels it. |
| `Assets/Animations/X Bot@Martelo 2.fbx` | 1.300 s, one-shot | The existing Hurricane Kick Animator state for both the E punch→kick finisher and direct RMB Hurricane Kick. |
| `Assets/Animations/X Bot@Falling.fbx` | 3.000 s trimmed looping segment | Downward airborne pose below -1.2 m/s, after higher-priority actions release. The original 11.367 s take includes a late collapse and was unsuitable as a whole fall loop; its importer trims to the first 3 seconds. |
| `Assets/Animations/X Bot@Terrified.fbx` | 20.000 s, one-shot | First 1.1 seconds when a civilian first flees, then the existing moving Panic gait. Does not steal an active forced-hit reaction. |
| `Assets/Animations/X Bot@Shoved Reaction With Spin.fbx` | 4.533 s, one-shot | Strong NPC hit reaction selected from real impact impulse (at least 1000), played at 4×. Does not add knockback/damage. |
| `Assets/Animations/X Bot@Jumping Down.fbx` | 2.667 s, one-shot | Wall-climb release into downward traversal, beginning at clip time 1.5 s and playing at 2×; ordinary jumps keep their existing pose. |
| `Assets/Animations/X Bot@Climbing Ladder.fbx` | 0.767 s, loop | Universal wall-climb pose. Actual CharacterController climbing drives its speed; near-zero climb speed holds the pose. The same loop serves sideways movement. |
| `Assets/Animations/X Bot@Running Forward Flip.fbx` | 1.100 s, one-shot | Second jump only, at 2.2×; first jump and Q Backflip retain their own animations and code-driven movement. |

No clip was rejected. The full Falling take was rejected as a *loop* and trimmed in import settings; Terrified and Jumping Down use only useful sections. All nine imported source Avatars and the selected Sidekick Avatar are valid Humanoids, and all sampled retargeted bones are finite. The FBX source bytes were not edited. No duplicate gameplay rigs, Animator controllers or runtime clip clones were created.

## Animator and gameplay ownership

`Assets/Resources/SharedHumanoid.controller` gained `Double Jump`, `Climb`, `Fall`, `Jump Down`, `Head Hit`, `Shoved Spin`, `Terrified`, and `Taunt`; the existing `Hurricane Kick` state now uses Martelo. `ClimbRate` is the sole new Animator parameter. Existing `ActionRate`, `GaitRate`, `Speed` and `Blend` are reused. `Assets/Resources/HumanoidAnimationTuning.asset` holds clip references and rates. The old `Martelo Kick` duplicate state was removed.

`HumanoidPresentation` still only translates gameplay events into poses. `Animator.applyRootMotion=false`; physics roots, CharacterController, NavMesh, damage, charge/cooldown and hit detection remain authoritative. The audit sampled **0 m model-root translation and 0° model-root rotation** for each clip, but some takes animate their hips substantially: sampled maximum from the first pose is 0.692 m for Martelo, 2.131 m for Shoved, 2.115 m for Jump Down, and 3.853 m for Running Forward Flip. The presentation holds visual hips at the neutral capsule-relative position for these moving takes while retaining joint rotations. These are sampled pose values, not measured world displacement in live play.

Climb latch/idle/up/down/sideways/release/top-out, second-jump/no-third-jump, fall and wall release are checked through the existing movement verifiers. The actual measured vertical climb was 5.000 m/s; sideways 4.000 m/s. The first jump apex was 2.0067 m, air-jump rise 1.4692 m, combined peak 3.4758 m; these remain code-driven. The first-person camera remains capsule-relative, not attached to the animated head, and only player renderers are hidden in first person. FirstPerson and Camera regression suites passed (below), although a human should assess moving-camera comfort.

The existing E combo window remains **0.9 s**. Tap 1 is punch; tap 2 inside the window is the Martelo kick through the existing melee path; an expired/refused tap restarts or refuses without advancing the combo. Direct RMB Hurricane Kick is a separate action using the same clip and its existing cost/cooldown. The audited left-foot contact is source frame 15 at **0.500 s**, normalized **0.3846**. Playback begins at 0.050 s at 1.5×, so the original **0.300 s gameplay windup** is retained. Clip impact and authoritative force aligned within two rendered frames. One accepted tap causes one authoritative impact; ordinary player damage does not erase a Backflip or kick pose/marker. Death still has priority.

At tier 0 Strength, the normal punch remains **35 damage / 1350 N·s**; the E combo finisher is **83.30 / 3240 N·s**; direct Hurricane is **59.50 / 2025 N·s**. Without Strength, the basic punch is **8 damage / 160 N·s**, the combo finisher **19.04 / 384 N·s**, and standalone basic Hurricane **13.6 / 240 N·s**. The finisher multipliers and existing kick multipliers produce these values; no damage, range, resource, hitbox, physics or cooldown balance data was changed to fit Martelo.

## Verification and limits

Each manifest records process exit, PASS/FAIL counts, source commit, elapsed time, and unchanged personal-save/Sidekick-database hashes. `tools/verify-sequential.py` launched one Unity process at a time. All entries below exited 0 with 0 FAIL and unchanged hashes.

| Source | Unity suite | PASS/FAIL |
| --- | --- | ---: |
| `992fc8c` | Humanoid; Jump; Climbing fixture; real-city Climbing; Melee; Backflip/Hurricane | 77/0; 128/0; 57/0; 29/0; 40/0; 66/0 |
| `992fc8c` | Spline routes; separate-process Spline Reload; Combat; dense Phase Shift baseline | 72/0; 8/0; 176/0; 22/0 |
| `eae1e72` | Combat; FirstPerson; separate-process FirstPerson Reload; Camera; Mode; separate-process Mode Reload | 176/0; 211/0; 5/0; 45/0; 79/0; 2/0 |

Evidence: `Verification/AnimationIntegration/final-focused-20261016/manifest.json`, `final-broad-20261016/manifest.json`, and `broad-r1/manifest.json` in that same directory. The final `992fc8c` priority change was focused on presentation and is covered by the final Humanoid, Backflip/Hurricane, Spline and Combat reruns; FirstPerson/Camera/Mode are from the immediately preceding gameplay source `eae1e72`. Unity's final nine-clip audit exited 0; its 45 posed previews were inspected. Unity SDK `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` completed with **0 warnings / 0 errors**.

Failed attempts are retained in separate manifests and **not counted as passing**: the first Jump run stopped 119 PASS/1 FAIL on an obsolete two-combat-power fixture; the first Climbing run 19/1 exposed a stale wall-release presentation flag; three early Melee runs stopped 0/1 while their loadout setup still requested unavailable/wrong-role powers. The corrected fixtures use the real progression/equip gates; the final reruns above passed. The earlier initial Humanoid run also had an obsolete expectation of immediate Panic rather than the new brief Terrified onset. No gameplay control was weakened to turn these green.

The final 18-enemy, 1280×720 batch Editor dense fixture measured **85.60 FPS / 11.683 ms mean**, 0 whole-frame GC B/frame over 685 frames; 1888 renderers, 1019 meshes, 109 materials, 3739 objects and 22 particles. The same fixture's earlier pre-integration source sample was **73.93 FPS / 13.526 ms**, 0 B/frame over 592 frames, with the same reported resource counts. Those samples came from different Editor processes and dates; the +11.67 FPS/-1.843 ms observation is **not a causal improvement claim or a controlled before/after benchmark**. The separate Humanoid city fixture measured 59.46 FPS over 298 frames with 37 active animators and 594 median draw calls; it is a different scene and cannot be compared directly with the dense fixture. No standalone player build, long session memory profile, or human motion-quality sign-off was run.

Henry should playtest at normal third-person and first-person camera distances: forward-flip speed on the second jump; climb rate and whether a ladder animation reads on a flat wall, including sideways/paused climb; jump→fall→land and wall-drop transitions; Head Hit versus Shoved intensity; whether civilian Terrified delays escape visually; Martelo contact timing for both E combo and RMB; whether the 12-second Taunt fits the hero; and first-person camera comfort/body hiding while all of these play. The next animation task is to tune only the clips/transitions that this in-motion review shows to be weak, then capture a controlled same-host old/new performance comparison if an animation cost claim is needed.
