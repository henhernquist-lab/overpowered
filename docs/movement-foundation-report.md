Movement foundation is implemented on `codex/movement-foundation` in the persistent recovery project. Functional regression gates pass. Performance was measured twice after the change, but variability prevents a clean performance acceptance claim.

Source checkpoints: `37ffd12` diagnosis, `99f07cb` measured baseline, `a88ab57` camera, `f45319c` air jump, `78e2a89` mantle, `8f3b891` Flight, `558d610` corrected visible-roof route, `d320807` verified Flight handoff correction. `ef4de9d` preserves separate pre-movement recovery evidence.

| Setting / measurement | Before | After |
|---|---:|---:|
| Third-person input pitch | −10° to65° | −85° to65° |
| Third-person rendered pitch at limits | −7.591° to83.100° | −70.646° to83.100° |
| First-person pitch | −85° to85° | −89° to85° |
| Ground jump launch / gravity |8.5m/s /24m/s²|unchanged|
| Measured first jump rise,120Hz |1.4692m|1.4692m|
| Universal air jump |none|one,7m/s launch;0.9917m rise;2.4608m combined peak|
| Mantle |none|0.25–1.8m above feet;0.65m reach beyond capsule;5m/s;0.75m maximum drop behind lip|
| Flight lift / response |16m/s /3|14m/s /2.5|
| Flight vertical acceleration |48m/s²|35m/s²|
| Flight horizontal boost |8× immediately|8× after0.5s ramp;14 multiplier-units/s|
| Full VECTOR running Flight speed |72m/s|72m/s|
| Flight fuel burn |1unit/s|1.1units/s|
| Fuel capacity / ground recharge |6units /2.5units/s|unchanged; base airtime5.455s instead of6s|
| Measured Flight ascent in baseline2s observation |29.1983m|25.0592m (−14.176%)|
| Horizontal travel in same observation |143.4750m|127.9875m|
| Fuel consumed in same observation |1.9916|2.1908|

Flight measurements use the same120Hz harness ordering:239 controller ticks in the2s coroutine observation. Progression multipliers, loadout gates, unrelated power costs, camera aiming, Feel, Sidekick materials/database and legacy PrototypeTuning constants were preserved.

The actual seed2409 route starts on the quay at(-22,0.13,-207), crosses the pier and platform railings, and arrives on the visible Harbour Light podium roof. There are no fixture colliders or mid-route teleports. Acceptance checks the entire capsule footprint inside the visible cylinder, correcting an earlier test that accepted its invisible box-collider corner. Earlier captures and failures remain in the evidence directories.

| Route | Time including1s landing settle | Height gained | Jumps | Mantles | Fuel consumed |
|---|---:|---:|---:|---:|---:|
| Fire+Strength, no Flight |8.8250s|3.9500m|4|1|0|
| Flight+Strength |2.0583s|3.9500m|1|0|1.1550|

This proves one practical lower-roof route. Sheer high-rises still need intermediate solid ledges; visual-only façade bands are not climbable. The generated seed has Downtown buildings21.6–106.1m, Residential6–18.5m and Docks7.2–11.1m, with taller landmarks. No assertion is made that every roof is accessible.

All counts below are fresh PASS / FAIL results from this movement task, not historical overnight counts. Reloads ran in separate Editor processes. Source558d610 owns the final broad regression; the subsequent Flight handoff correction has its own targeted rerun.

| Suite | PASS / FAIL |
|---|---:|
| Movement jump |116 /0|
| Movement mantle, including visible city roof |36 /0|
| Movement camera limits + upward hits |38 /0|
| Existing camera flow |45 /0|
| First person |211 /0; reload5 /0|
| Backflip / Hurricane |64 /0|
| Humanoid |69 /0|
| City / powers |55 /0; reload5 /0|
| Roster VerifyExisting |194 /0; reload3 /0|
| Mode VerifyExisting |79 /0; reload2 /0|
| Flight including motion-owner transitions |37 /0|
| Paired visible-roof route |28 /0|

Every listed process exited0. The bundled SDK clean rebuild passed with0 warnings and0 errors. Real progression save and Sidekick database hashes remained unchanged in the sequential test manifests.

Failure evidence is retained. The latest Flight transition control failed9 /1 before correction: MoveAbility preserved the old full-speed ramp. Resetting it at ability and mantle handoffs passed the same assertion plus actual Flight→mantle→Flight controls. Earlier mantle failures exposed real billboard/antenna clearance and railing constraints; the final geometric checks retain those negative controls. A visual audit rejected the first route's invisible collider-corner arrival and strengthened acceptance to the visible cylinder footprint.

Obsolete fixture assumptions were repaired without weakening gameplay gates: Backflip/Humanoid explicitly reject jumps while the menu is open before testing accepted gameplay jumps; City checks the configured fuel burn and retains empty-fuel refusal plus airborne recharge refusal. First-person testing keeps the old −85° tight-geometry control alongside the new −89° limit. Roster/Mode VerifyExisting skip asset bootstrap but run the same assertions against current recovered assets.

The matched benchmark uses the same seed2409 densest Downtown street, active idle hero, real gameplay camera with one render per frame at1280×720, Ultra quality,29 NPCs and three4-second rounds. Initial hero position/camera direction and city structure counts match; NPC motion and final captures differ over time. No rendering/content/LOD cuts were made.

| Street measurement | Before | After, first run | After, unchanged repeat |
|---|---:|---:|---:|
| Round FPS |72.99 /75.07 /75.63|61.46 /74.32 /68.61|79.53 /77.27 /61.63|
| Median FPS |75.07|68.61 (−8.61%)|77.27 (+2.93%)|
| Median frame time |13.32ms|14.58ms (+1.26ms)|12.94ms (−0.38ms)|
| Median whole-frame GC |1720B/frame|1719B/frame|1691B/frame|

Both after processes exited0 at source`d320807`, with one benchmark Editor and unchanged real save/database hashes. The Editor's own import worker is not a second independently launched Editor. The first slowdown was retained and investigated; it did not reproduce as a consistent median slowdown, though the repeat still had a slow round. Across all six after rounds, the median is71.465FPS and14.015ms, so choosing only the faster repeat would overstate the evidence. Performance acceptance remains **inconclusive**, not a declared pass. A controlled, quieter-host before/after capture is still needed to resolve this variance; standalone-player performance also remains unverified.

The first profiled script-update sample was0.704ms versus0.710ms before. No added whole-frame allocation was demonstrated; this counter includes Editor/NPC/diagnostic work and cannot isolate movement. The blocked-headroom mantle probe separately allocated0 managed bytes over1000 calls. Neither measure proves every active traversal path is allocation-free. Startup CPU observation found a busy Chrome renderer; that was not synchronized to samples and does not prove the slowdown's cause. User applications were left running.

Evidence: `Verification/World/movement-before`, `movement-after`, and `movement-after-repeat`; exact per-suite manifests and failure controls are under `Verification/Movement`. `Verification/Movement/final-gate-summary.txt` identifies source hashes and real gate counts.

Henry must judge double-jump strength, mantle smoothness, camera comfort near geometry, and whether Flight's reduction is too much or too little. Automated controls establish capability and safety bounds, not fun or balance quality. Standalone-player performance, every city seed/ledge, controller hardware input feel, and extended manual play remain unverified. The broader overnight build and its mission/cloud/presentation queue are outside this replacement task and are not declared finished.

Production edits are limited to `Assets/Scripts/SuperHeroController.cs`, `Assets/Scripts/GameTuning.cs`, `Assets/Resources/GameTuning.asset`, and `Assets/Resources/Powers/flight.asset`. Verification support changes are `Assets/Editor/MovementVerification.cs` and its meta, `Assets/Scripts/MovementVerificationRunner.cs` and its meta, `Assets/Scripts/FirstPersonVerificationRunner.cs`, `Assets/Scripts/BackflipHurricaneVerificationRunner.cs`, `Assets/Scripts/HumanoidVerificationRunner.cs`, `Assets/Scripts/CityVerificationRunner.cs`, `Assets/Editor/RosterVerification.cs`, `Assets/Editor/ModeVerification.cs`, `Assets/Scripts/WorldProfileRunner.cs`, and `tools/verify-sequential.py`. `.gitignore` excludes sandbox saves/pointers; `STATUS.md` retains the phase history. The complete changed-file inventory, including verification captures and results, is in `Verification/Movement/changed-files.txt`.
