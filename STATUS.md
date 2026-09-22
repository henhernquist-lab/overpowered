# Prototype Status

## Pooled audio — 2026-09-22 (feat/audio)

### Delivery

Implementation commit: ebf7d46 on feat/audio. Push was attempted with `git push -u origin feat/audio` and failed (exit 128): `fatal: could not read Username for 'https://github.com': Device not configured`. Local commits are retained; a human must authenticate GitHub and retry the push. Nothing was merged to main. Delivery worktree: /private/tmp/op-audio-delivery; the original checkout remains on the other agent's abilities branch. Authored code/docs pass the whitespace check; untouched Unity-generated YAML and original licence bytes retain their source whitespace.

### Built and ownership

- Automatic persistent AudioDirector: 24 preallocated AudioSources, four reserved for city/siren/flight/music beds and 20 round-robin one-shots. Per-cue caps, pitch/volume variation, distance culling, positional mono world cues and 2D UI/music. Saturated requests are dropped, not allocated. Audio randomness does not change the gameplay seed.
- 18 cue types / 21 real CC0 clips: punch, three footsteps, flight start/loop, landing, destruction, fire, ice, telekinesis, gunshot, hit, death, jump, UI click/hover, city/siren beds and music. Every file's source URL, author, licence and download date is in Assets/Audio/ATTRIBUTION.md; original Kenney licences retained in Assets/Audio/Licenses. Offline conversion is reproducible with prepare_clips.py. All positional files are mono; all files are Ogg Vorbis; music streams.
- Inspector tuning: Assets/Resources/AudioTuning.asset owns clips, levels, jitter, pitch, caps, distances/rolloff, pool budget, actor cadence, footstep spacing, fades and Heat response. Assets/Audio/Overpowered.mixer routes Master -> Music/SFX/UI/Ambient; all five volume parameters are exposed. Overpowered/Audio/Create missing audio assets authors missing assets without overwriting existing tuning.
- Existing events only: powers, player damage/death/respawn, jump/landing, punch impact and NPC attacks. Footsteps use measured speed plus a central distance accumulator; NPC hit/death counters are sampled centrally. No new per-NPC Update or per-sound objects. Registry refresh is once per second; state sampling every 50ms; actor capacity 128.
- Important hook corrections to the packet: holding Flight does NOT emit PowerUser.Activated, so start/loop follow the existing Flying presentation state and actual fuel remains gameplay-owned. Punch audio subscribes to PunchImpacted, not activation, preserving the configured 125ms windup. Strength costs zero energy, so its refusal control is zero charges; Fire/Ice/Telekinesis use real zero-energy controls, Flight uses empty fuel.
- The ONLY existing gameplay edit is two additive lines in BreakableProp: static Destroyed event and invocation where Break actually succeeds. No damage, health, shards, physics, power, character, menu or progression logic changed.
- UI callbacks are delegated from the existing panel root (including dynamically added buttons); disabled buttons are ignored. Scene/disable cleanup unsubscribes hooks and stops world audio; music/UI tails can survive transitions. Heat 0..5 drives calm vs siren/tension, not a new wanted system.
- Branch isolation: based on 9c3c67f. Another agent changed the shared checkout to feat/backflip-hurricane-kick, so delivery uses a separate feat/audio worktree; that agent's branch/commit is untouched and is NOT merged here. Original shared-checkout audio working files are retained, not discarded.

### Judgment calls, cuts and verification limits

No human has heard or judged this mix. Source playback/DSP cursors are verified, not speaker output, loudness balance or perceived sound quality. Flight is an engine/wind-like texture; ice/telekinesis are electronic power textures; death is a descending feedback tone, not a human vocal. City is the source's near-seamless loop; music repeats a full track with edge fades, not a musically seamless composition. Cop audio uses the existing contact-range attack event: this does not implement ranged combat.

Flight and footsteps are controlled at the public presentation boundary after real fuel controls; jump/landing use real CharacterController motion. No physical F-key input automation. UI home/disabled controls and Hero/Villain scene transitions were exercised; a full results/upgrade-button listening pass was not. NPC registration can lag by one second and ignores actors beyond 128; no large-crowd saturation benchmark beyond the recorded population. No allocation-profiler measurement was made (bounded, centralized design is not a measured zero-GC claim). No volume-options UI or saved preferences added.

The original Tabasco gunshot archive was rejected: its CC0 page conflicts with an included CC-BY licence. The shipped 1911 report instead comes from the explicitly CC0 Free Firearm Sound Library (four authors recorded in attribution).

### Build and measured verification

Unity 6000.6.0f1 isolated project /private/tmp/op-audio-verify-6s0bUo, shipping city/assets plus this packet. Both AudioVerification.Run and AudioVerification.Reload exited **0** in separate processes. Isolation avoids shared-project Editor/ILPP locks; no existing Library or Logs were deleted. Tested audio source/assets match the delivery worktree byte-for-byte.

Commands (no -quit; each runner exits itself):

```sh
Unity -batchmode -projectPath /private/tmp/op-audio-verify-6s0bUo -executeMethod AudioVerification.Run -logFile /private/tmp/op-audio-run3.log
Unity -batchmode -projectPath /private/tmp/op-audio-verify-6s0bUo -executeMethod AudioVerification.Reload -logFile /private/tmp/op-audio-reload.log
```

Unity compile/runtime checks passed. The logs are NOT error-free: UnityEditor.Search.SearchDatabase threw an indexing ArgumentOutOfRangeException during startup, and licensing refresh logged token/entitlement errors before succeeding; neither is an audio/runtime assertion failure. Earlier verification attempts exposed test mistakes (a zero-energy Strength control despite its zero cost; checking footsteps during a still-playing cast). These were corrected to valid controls, not replaced with bookkeeping assertions.

A bundled dotnet SDK was found inside Unity; none was installed. Supplemental build in the delivery worktree succeeded: **0 errors, 16 pre-existing CS0618 warnings in PerformanceProfileRunner.cs** (untouched). Full compiler output: Verification/Audio/build.txt. Thus this is not advertised as a warning-free build.

ABBA samples: 26 civilians + 14 cops, one enabled 1280x720 camera, 6s per segment, actual beds and SFX active in enabled samples (8–9 simultaneous sources), zero playing sources when disabled. Paired mean: disabled **34.39 FPS**, enabled **34.33 FPS**; reported loss **0.07 FPS**, frame-time difference **0.058ms**. This is within noise, not proof of a precise 0.058ms cost. No >2 FPS regression observed in this run. Editor throughput only, not standalone performance; draw calls ~1115–1119, slight population/activity drift remains.

Verbatim main output (also Verification/Audio/results.txt):

```text
PASS Single automatic director, exactly 24 preallocated AudioSources.
PASS Punch references real clips and a mixer group.
PASS punch-1 positional mono.
PASS punch-2 positional mono.
PASS Footstep references real clips and a mixer group.
PASS step-1 positional mono.
PASS step-2 positional mono.
PASS step-3 positional mono.
PASS FlightStart references real clips and a mixer group.
PASS flight-start positional mono.
PASS FlightLoop references real clips and a mixer group.
PASS flight-loop positional mono.
PASS Land references real clips and a mixer group.
PASS land positional mono.
PASS Destruction references real clips and a mixer group.
PASS debris positional mono.
PASS Fire references real clips and a mixer group.
PASS fire positional mono.
PASS Ice references real clips and a mixer group.
PASS ice positional mono.
PASS Telekinesis references real clips and a mixer group.
PASS telekinesis positional mono.
PASS Gunshot references real clips and a mixer group.
PASS gunshot positional mono.
PASS Hit references real clips and a mixer group.
PASS hit positional mono.
PASS Death references real clips and a mixer group.
PASS death positional mono.
PASS Jump references real clips and a mixer group.
PASS jump positional mono.
PASS UiClick references real clips and a mixer group.
PASS UiHover references real clips and a mixer group.
PASS CityBed references real clips and a mixer group.
PASS SirenBed references real clips and a mixer group.
PASS Music references real clips and a mixer group.
PASS All 18 cue types / 21 clip assignments populated (no silent placeholders).
PASS Exposed mixer parameter MasterVolume is valid.
PASS Exposed mixer parameter MusicVolume is valid.
PASS Exposed mixer parameter SFXVolume is valid.
PASS Exposed mixer parameter UIVolume is valid.
PASS Exposed mixer parameter AmbientVolume is valid.
PASS Music: actual AudioSource.isPlaying, assigned clip=music, volume=0.072, group=Music, spatialBlend=0.
PASS Music DSP sample cursor advances (not just bookkeeping).
PASS Disabled UI CONTROL emits no click and launches nothing.
PASS UiHover: actual AudioSource.isPlaying, assigned clip=ui-hover, volume=0.160, group=UI, spatialBlend=0.
PASS UiClick: actual AudioSource.isPlaying, assigned clip=ui-click, volume=0.600, group=UI, spatialBlend=0.
PASS Director binds shipping world without gameplay/bootstrap edits.
PASS Undamaged player CONTROL has no Hit source.
PASS Hit: actual AudioSource.isPlaying, assigned clip=hit, volume=0.522, group=SFX, spatialBlend=1.
PASS Real paid punch accepted.
PASS Windup CONTROL: no punch-impact audio at activation.
PASS Punch: actual AudioSource.isPlaying, assigned clip=punch-2, volume=0.617, group=SFX, spatialBlend=1.
TIMING accepted=1.6305, actual impact=1.7628, source timeSamples=969; configured windup=125.0ms; audio callback is PunchImpacted.
PASS Zero-charge punch refused.
PASS Zero-charge CONTROL plays no punch cue after windup.
PASS Shipping Strength costs 0 energy: its rejection CONTROL is charges/cooldown, not energy. Flight uses fuel; Fire/Ice/Telekinesis energy controls follow.
PASS Damaged-but-unbroken prop CONTROL emits no destruction.
PASS Destruction: actual AudioSource.isPlaying, assigned clip=debris, volume=0.556, group=SFX, spatialBlend=1.
PASS Fire Blast paid activation succeeds.
PASS Fire: actual AudioSource.isPlaying, assigned clip=fire, volume=0.585, group=SFX, spatialBlend=1.
PASS Fire Blast no-energy activation refused.
PASS Fire Blast no-energy CONTROL emits no cue.
PASS Ice paid activation succeeds.
PASS Ice: actual AudioSource.isPlaying, assigned clip=ice, volume=0.440, group=SFX, spatialBlend=1.
PASS Ice no-energy activation refused.
PASS Ice no-energy CONTROL emits no cue.
PASS Telekinesis paid activation succeeds.
PASS Telekinesis: actual AudioSource.isPlaying, assigned clip=telekinesis, volume=0.361, group=SFX, spatialBlend=1.
PASS Telekinesis no-energy activation refused.
PASS Telekinesis no-energy CONTROL emits no cue.
PASS Real flight resource consumption accepted.
PASS FlightStart: actual AudioSource.isPlaying, assigned clip=flight-start, volume=0.300, group=SFX, spatialBlend=1.
PASS FlightLoop: actual AudioSource.isPlaying, assigned clip=flight-loop, volume=0.160, group=SFX, spatialBlend=1.
PASS Empty-fuel flight CONTROL refuses consumption.
PASS Not-flying CONTROL fades/stops flight sources.
PASS Central distance accumulator plays real footstep sources: speed=6.00, state=Locomotion, health=99.0 (waits for prior cast to finish).
PASS Stationary CONTROL has no footsteps.
PASS Land: actual AudioSource.isPlaying, assigned clip=land, volume=0.525, group=SFX, spatialBlend=1.
PASS Real grounded jump accepted.
PASS Jump: actual AudioSource.isPlaying, assigned clip=jump, volume=0.177, group=SFX, spatialBlend=1.
PASS Death: actual AudioSource.isPlaying, assigned clip=death, volume=0.384, group=SFX, spatialBlend=1.
PASS Heat 0 CONTROL: calm bed playing, siren stopped at volume 0.
PASS SirenBed: actual AudioSource.isPlaying, assigned clip=siren-bed, volume=0.240, group=Ambient, spatialBlend=0.
PASS Heat 0->5: city 0.3500->0.1925, siren 0.0000->0.2400; intensity=1.0000.
PASS Concurrency CONTROL: 13 simultaneous requests, exactly 3 actual sources playing (cap 3); pool remains 24.
PASS Near spatial CONTROL plays.
PASS Far spatial CONTROL does not consume a voice.
BENCH population civilians=26, cops=14; single enabled camera rendering 1280x720 (no manual Camera.Render double-render).
MEASURED audio disabled A: frames=201, seconds=6.007, FPS=33.47, p95=38.611ms, drawCalls=1119.0, peak playing sources=0.
PASS Disabled benchmark CONTROL has zero playing sources.
MEASURED audio enabled B: frames=210, seconds=6.016, FPS=34.91, p95=36.455ms, drawCalls=1119.0, peak playing sources=9.
PASS Enabled benchmark includes actual beds and simultaneous SFX.
MEASURED audio enabled B2: frames=203, seconds=6.016, FPS=33.74, p95=39.785ms, drawCalls=1119.0, peak playing sources=8.
PASS Enabled benchmark includes actual beds and simultaneous SFX.
MEASURED audio disabled A2: frames=212, seconds=6.003, FPS=35.32, p95=34.922ms, drawCalls=1114.6, peak playing sources=0.
PASS Disabled benchmark CONTROL has zero playing sources.
AUDIO COST paired means: disabled=34.39 FPS, enabled=34.33 FPS, loss=0.07 FPS; frame-time delta=0.058ms. Within requested ~2 FPS budget in this run; noise/Editor limits apply.
PASS Return Home keeps one pool; no stale world/flight/city audio.
PASS Gunshot: actual AudioSource.isPlaying, assigned clip=gunshot, volume=0.601, group=SFX, spatialBlend=1.
PASS Cop gunshot is driven by a real hostile NPC attack/damage event.
PASS Mode switch CONTROL retains exactly one director/pool.
LIMIT: no human has heard or judged the mix. Tests assert real source playback, clips, volumes and routing, not audible-device capture. Flight state is controlled at the public presentation boundary after real fuel checks; no hardware F-key automation. FPS is Editor throughput, not standalone performance.
```

Verbatim separate-process asset reload (also Verification/Audio/reload.txt):

```text
PASS Single automatic director, exactly 24 preallocated AudioSources.
PASS Punch references real clips and a mixer group.
PASS punch-1 positional mono.
PASS punch-2 positional mono.
PASS Footstep references real clips and a mixer group.
PASS step-1 positional mono.
PASS step-2 positional mono.
PASS step-3 positional mono.
PASS FlightStart references real clips and a mixer group.
PASS flight-start positional mono.
PASS FlightLoop references real clips and a mixer group.
PASS flight-loop positional mono.
PASS Land references real clips and a mixer group.
PASS land positional mono.
PASS Destruction references real clips and a mixer group.
PASS debris positional mono.
PASS Fire references real clips and a mixer group.
PASS fire positional mono.
PASS Ice references real clips and a mixer group.
PASS ice positional mono.
PASS Telekinesis references real clips and a mixer group.
PASS telekinesis positional mono.
PASS Gunshot references real clips and a mixer group.
PASS gunshot positional mono.
PASS Hit references real clips and a mixer group.
PASS hit positional mono.
PASS Death references real clips and a mixer group.
PASS death positional mono.
PASS Jump references real clips and a mixer group.
PASS jump positional mono.
PASS UiClick references real clips and a mixer group.
PASS UiHover references real clips and a mixer group.
PASS CityBed references real clips and a mixer group.
PASS SirenBed references real clips and a mixer group.
PASS Music references real clips and a mixer group.
PASS All 18 cue types / 21 clip assignments populated (no silent placeholders).
PASS Exposed mixer parameter MasterVolume is valid.
PASS Exposed mixer parameter MusicVolume is valid.
PASS Exposed mixer parameter SFXVolume is valid.
PASS Exposed mixer parameter UIVolume is valid.
PASS Exposed mixer parameter AmbientVolume is valid.
PASS Music: actual AudioSource.isPlaying, assigned clip=music, volume=0.072, group=Music, spatialBlend=0.
PASS Music DSP sample cursor advances (not just bookkeeping).
PASS Disabled UI CONTROL emits no click and launches nothing.
PASS UiHover: actual AudioSource.isPlaying, assigned clip=ui-hover, volume=0.160, group=UI, spatialBlend=0.
PASS UiClick: actual AudioSource.isPlaying, assigned clip=ui-click, volume=0.600, group=UI, spatialBlend=0.
PASS Director binds shipping world without gameplay/bootstrap edits.
PASS CityBed: actual AudioSource.isPlaying, assigned clip=city-bed, volume=0.350, group=Ambient, spatialBlend=0.
PASS SECOND PROCESS loads persisted mixer with all five groups.
SECOND PROCESS asset/reference reload passed; fresh isolated save used.
```

## Content data and documentation accuracy pass — 2026-09-21 (current)

Content/docs packet (`feat/content-docs`). Data-only encounter content plus a README accuracy
pass. **No C# was written or changed**; no tuning asset owned by another packet was touched.
Nothing here raises any spawn or population count.

### Content added (all inside the existing encounter envelope)

Three new `EncounterDefinition` assets in `Assets/Resources/Encounters/`, plus `.meta` files.
They reuse only existing `CrimeKind` semantics and existing `ModeRules` — no new mechanics,
matching the count envelope of `bank`/`convoy`/`arson` (2–3 actors per list, 1–2 cars, 1–3
loot nodes, ≤2 hazards; total live objects per encounter does not exceed the shipping set):

- **`vault.asset` — Vault run** (Kind 1 Robbery): multi-loot heist — 3 robbers / 1 civilian /
  2 cops / 1 car / 5 crates / **3 loot** / 0 fire; 180 s deadline, robbers run at 35 s.
  Hero framing: one fast hostage, three stops. Villain framing: three-loot sweep, escape 20 m.
- **`siege.asset` — Cop siege** (Kind 0 Mugging): rescue-heavy standoff — 2 robbers /
  **3 civilians** / 3 cops / 1 car / 5 crates / 1 loot; 240 s deadline, slow runners (2.5 m/s),
  runners wait 55 s. Hero framing: three rescues against a big police presence. Villain
  framing: longest escape (24 m).
- **`blackout.asset` — Blackout blitz** (Kind 2 Fire): hazard sweep — 2 robbers / 2 civilians /
  2 cops / 1 car / 5 crates / 1 loot / **2 fire**; 180 s deadline, robbers run at 30 s,
  civilians endangered at 75 s. Hero framing: two simultaneous fires plus fast runners.
  Villain framing: sabotage-plus-escape.

Both shipping mode definitions (`Assets/Resources/Modes/hero.asset`, `villain.asset`) now
reference all six encounters in their `Encounters` pools. The three pre-existing encounters
remain first in pool order, so pool-cycling sequences and the first-spawn encounter are
unchanged. `free-play`/`endless-fight` (not playable) were not modified.

### Documentation

- **`README.md` rewritten** against the code as it exists today. Corrections: the build gate
  (see environment note below), an explicit Built-in Render Pipeline section (no URP/HDRP
  package, `m_CustomRenderPipeline: {fileID: 0}`, zero `.shader` files, Standard shader only —
  there is no pipeline asset or Renderer Feature to configure), the performance section now
  states the measured **~26–40 FPS Editor** range and that the quoted 155 FPS was a different,
  much simpler gray-box city (with the harness double-render caveat), every `Overpowered →`
  menu item re-checked against `Assets/Editor/` (added the existing
  `Create menu presentation data` item), mode-limits wording matched the data (SuccessGoal 5,
  3 failures/defeats, 900 s), encounter timing ranges stated as ranges, and the power-menu
  select keys described correctly. `Assets/Resources/Effects/` and `ModeRules/` asset paths
  verified to exist and are now listed.
- **`docs/architecture.md`** (new): per-system table of code/data ownership, flow, extension
  points, and the performance diagnosis summary. Every path in it was checked.
- **`docs/agent-scope.md`** (new): exclusive file-ownership map for the concurrent agent
  packets (content-docs / audio / performance), shared-source rules, append-only STATUS rule,
  and the conflict protocol.

### Verification (real output)

Ran per the repo's own instructions — an isolated project copy, editor closed, **without
`-quit`** (each verification exits itself; the packet's suggested command included `-quit`,
which contradicts the repo docs — noted as a correction):

```sh
Unity -batchmode -projectPath /tmp/op-content-verify -executeMethod ModeVerification.Run \
  -logFile .../Verification/Content/unity-run.log
```

**Exit code 0. 80 PASS / 0 FAIL** (`Verification/Content/mode-results.txt`, copied from the
run's `Verification/Modes/results.txt`; the raw log stays in `Verification/Content/` and is
excluded by the project's global `*.log` ignore rule). Controls all passed, including the
pre-existing-content ones this packet must not regress: the first populated event is still
`bank` (`3 robbers, 2 civilians, 2 responders, 8 props, 2 loot`), COMING SOON refusal,
population cap, failure/timeout/defeat limits, third data-only mode, and both shipping-mode
flows. All three new encounter GUIDs appear in the import log (confirmed loaded, not silently
dropped). Benchmark sample from the same run: `civilians=28, cops=12, events=2 … FPS=45.82,
p95=25.85ms` — within the run-to-run spread of previous samples, no population increase.

### Environment notes (checked, honestly)

- **`dotnet` is not on PATH** (previous entry's claim holds), **but** the Unity-bundled SDK at
  `…/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet` **does exist** and compiling with
  it by full path works today: `dotnet build Overpowered.Build.csproj --no-restore
  -p:UseSharedCompilation=false` → **0 errors, 16 pre-existing CS0618 warnings**. The previous
  entry's "cannot currently be run" is therefore stale in one respect: the command needs the
  full path (or `dotnet` restored to PATH), and the README now documents exactly that.
- **Batch-mode Unity on the main project path currently fails to reach compile**: every run
  stalls/exits at `ILPPTrigger: Can't find file /tmp/ilpp.sock-…` retries. Root cause traced:
  an ILPP runner (injected by `com.unity.ai.assistant`, added to the manifest on Sep 20) leaves
  `Library/ilpp.pid`; after any killed editor session subsequent runs loop on the dead socket.
  Clearing `Library/ilpp.pid` + `Library/Bee` did not clear it on the main path this session;
  the successful run above used a fresh copy. Also: three unrelated Unity editors have been
  running at ~100% CPU on other projects for days (load average ~8), which slows everything.
  Neither issue is caused by this packet's content; both are recorded so they are not
  rediscovered. The repo's own README already mandates running verifiers on a copy.

- `git push -u origin feat/content-docs` fails with the same credential error recorded in the
  2026-09-20 entry (`could not read Username for 'https://github.com'`) — HTTPS remote, no
  `gh` CLI, no SSH keys. **This branch's work is committed locally (`7dadc51`) and ready;
  pushing it requires a human.**

### Not verified / limits (explicit)

- **No human has playtested the new encounters.** Balance numbers (deadlines, runner speeds,
  loot counts, escape distances) are design judgment inside the existing envelope; nothing
  claims they are fun or correctly tuned. Hero-mode failure risk is real in `siege`
  (3 rescues, 240 s) and `blackout` (fast runners + fires) — unmeasured.
- The automated flow exercises the pool's first three spawns per session; later-cycle
  weighting of the six-encounter pool is not separately verified.
- The main-project ILPP failure above is diagnosed, not fixed — fixing it would mean touching
  tooling/library state outside this packet's scope. The successful verification was obtained
  on an isolated copy, which the repo docs already require.
- `siege`/`blackout`/`vault` have not been exercised individually end-to-end by the harness
  (it drives the shipping first-spawn encounter and completes events generically); they were
  loaded, pooled, and the full suite passed with them present.

## Performance diagnosis, menu completion and agent handoff — 2026-09-20

Planning/architecture pass. **No optimization was implemented.** This entry records a measured
diagnosis, the completion of the interrupted menu work, and two delegated work packets.

### State verification against this document

All 6 local commits through `42b62da` are real and consistent with what this file claims, and
all 64 previously referenced verification artifacts exist and are git-tracked. **No stale or
fabricated claim was found in STATUS.md.** Two corrections belong to the surrounding tooling,
not to this document:

- **`dotnet` is not installed on this machine.** The `dotnet build Overpowered.Build.csproj
  --no-restore -p:UseSharedCompilation=false` gate named in AGENTS.md and throughout this file
  **cannot currently be run** (no `dotnet`, `mono` or `msbuild` on PATH or in standard locations).
  `bin/`, `obj/` and `Verification/Humanoid/build.txt` show it worked on Sep 16 and is now gone.
  **Unity's own batch-mode compile is the build gate** until that is restored.
- **There is no outline Renderer Feature and no URP in this project.** A briefing to this pass
  asserted one existed and was toggleable, and asked for it as a performance control. Confirmed
  at runtime: `GraphicsSettings.currentRenderPipeline == null`, no URP package in
  `Packages/manifest.json`, no pipeline asset, zero custom shaders, `Shader.Find("Standard")`
  throughout. **That control could not be run because the thing does not exist.** It is recorded
  here so the claim does not resurface.

### Measured FPS diagnosis

New diagnostic harness, `Assets/Editor/PerformanceProfile.cs` + `Assets/Scripts/PerformanceProfileRunner.cs`.
Reverts every control; changes nothing permanently. Run with
`-executeMethod PerformanceProfile.Run`. Exit 0. Full output and the attribution table are in
`Verification/Performance/results.txt`; capture in `city.png` / `city-uncombined.png`.

```text
POPULATION: buildings=36 (234 renderers), props=247 (772 renderers), civilians=26, cops=10, animators=40, skinnedRenderers=80, propRigidbodies=211, totalRenderers=1140
SETTINGS: qualityLevel=5 'Ultra', shadows=All, pixelLightCount=4, lodBias=2, shadowDistance=150, GPU=AMD Radeon Pro 5300, CPU=Intel(R) Core(TM) i9-10910 CPU @ 3.60GHz
MATERIALS: palette materials=17, enableInstancing=true on 17 of them, shader=Standard
MESHES: 1060 MeshFilters reference 1021 DISTINCT sharedMeshes.
```

**The mesh-combining optimization is the primary cost, not a mitigation.** `CityArt.Combine`
merges per root and per material, producing 1,021 distinct meshes from 1,060 MeshFilters — a
~1:1 ratio, so static batching and GPU instancing cannot merge anything. `enableInstancing=true`
on all 17 palette materials was doing nothing. Disabling the existing `CombineMeshes` toggle and
rebuilding drops the city to **3 distinct shared primitive meshes**:

| Sample | draw calls | batches | FPS |
|---|---|---|---|
| 00 baseline, combining ON (as shipped) | 2,222 | 2,058 | 37.68 |
| 13 rebuild, combining OFF | **266** | **102** | **45.28** (+20.2%) |

**Draw calls are nevertheless not the bottleneck.** An 88% draw-call reduction buys only +20%,
while disabling prop *renderers* (control 02) removes fewer draw calls and gains **+47.3%**. The
dominant cost is per-renderer CPU work — culling, sorting and submission across ~1,140
renderers — which batching does not remove. Second load source: the shared humanoid mesh is
**28,374 vertices × 40 actors ≈ 1.13M of the 1.24M single-render vertices (91%)**.

**Hypotheses the data did NOT support.** The drift control re-measured baseline at **+7.9%**
against its own 8% tolerance, which sets the noise floor. At or below it, and therefore
**unmeasured rather than measured-as-zero**: shadows off +5.6%, `updateWhenOffscreen=false`
+6.3%, `AnimatorCullingMode.CullUpdateTransforms` +6.6%, NPC animators disabled +8.9%, prop
Rigidbody `Discrete` −0.2%. Shadows were predicted to roughly double cost and did not, at
`Ultra` with `shadowDistance=150`. The one modest confirmed win is `CityMaterials.LateUpdate`'s
unconditional per-frame `Apply()` at **+12.7%**.

**A measurement artifact affects every recorded number in this file.** All benchmark harnesses
set `camera.targetTexture` on a still-enabled camera *and* call `camera.Render()` in the sample
loop, rendering the scene **twice per sampled frame**. Removing it measures **+32.0%** (37.68 →
49.73 FPS). The 155 FPS baseline carried the same artifact, so the *regression* comparison
stands, but absolute throughput has been understated by roughly a third throughout.

Ranked by impact-to-effort for the next pass, against these numbers as the before-baseline:
(1) stop combining into unique meshes — share one mesh per prop kind so instancing applies;
(2) reduce live renderer count / cull distant props, which is where the +47% actually sits;
(3) reduce humanoid vertex count or add character LOD; (4) make `CityMaterials.Apply()` event-driven;
(5) fix the double-render in the harnesses so future numbers are real.

**Limits:** Editor Play Mode at 1280×720, fixed camera, player parked airborne. Not a
standalone-player or hands-on figure. City population is not static across the run (NPCs die
during controls), which contributes to the 7.9% drift; sub-10% effects need a tighter re-run to
resolve. Two `MissingReferenceException` crashes from destroyed NPCs were fixed by re-filtering
to live objects; **no control was weakened or removed** to obtain a pass.

### Menu presentation completed and verified

The interrupted home/results rebuild is finished. The only change was the backdrop capture in
`Assets/Resources/MenuPresentationTuning.asset`, reached over 4 rendered iterations:
`SkylineCamera (-65,22,-105) → (-22,7,-138)`, `SkylineLook (0,19,0) → (0,27,0)`, and
`SkylineFieldOfView 48 → 32`. The FOV change was load-bearing: at 48 the city subtends too small
an angle and reads as a low band of boxes regardless of camera position. No code was changed.

`MenuPresentationVerification.Run` exits **0** with **38 PASS / 0 FAIL**
(`Verification/Menus/results.txt`), covering both Hero and Villain Home → Play → Results → Home
flows, the disabled Coming Soon modes under both a submit-event and a flow-guard control, and
the real upgrade purchase routed through the existing `PlayerProgression.Buy`
(`Strength tier 0→1, points 1→0, force 1350→1890`) with a zero-point repeat-purchase rejection
control. UI throughput, A/B/B/A:

```text
MEASURED home static A: FPS=2152.17, mean=0.464ms, p95=0.559ms, drawCalls=2.0
MEASURED home animated B: FPS=906.77, mean=1.103ms, p95=1.284ms, drawCalls=4.0
MEASURED home animated B2: FPS=900.63, mean=1.110ms, p95=1.303ms, drawCalls=4.0
MEASURED home static A2: FPS=2261.26, mean=0.442ms, p95=0.515ms, drawCalls=2.0
MEASURED villain results animated: FPS=894.27, mean=1.118ms, p95=1.309ms, drawCalls=5.0
```

The repeats are tight, so motion's ~2.4× cost is a real effect, not drift. All eight captures
were visually inspected. The backdrop now genuinely reads as a **street-level city view** — no
building top faces, sky above the rooflines, road receding to a vanishing point. **Honest limit:
it is a low-rise street, not a dramatic high-rise skyline, because the source buildings are only
6–28m.** A towering skyline would require taller buildings in `CityLayout`, which was not changed.

Two defects found and recorded, not fixed:
- **Latent:** `MenuSkyline.Get` calls `art.Initialize()`, reassigning the global
  `CityMaterials.Current`, then destroys that root — whose `OnDestroy` destroys every palette
  material and nulls `Current`. Safe today because it only runs in Home/Results with no live
  city, but it would corrupt a live city's materials if a capture ever ran during gameplay.
- **Cosmetic:** on the results screen the "YOUR PROGRESS IS SAVED." label collides with the
  bottom edge of the XP panel.

### Delegation and repo state

Two standalone work packets are in `handoff/`, with non-overlapping exclusive file scopes:
`grok-audio.md` (branch `feat/audio` — the whole audio system, sourcing CC0 clips included; the
game currently has zero audio) and `glm-content-docs.md` (branch `feat/content-docs` — encounter
and mode-rule data plus a README accuracy pass). GLM's packet forbids touching
`CityArtSettings.asset` / `CityLayout.asset` or raising any spawn count, so new content cannot
fight the performance work above.

**The 6 outstanding commits were NOT pushed.** `git push origin main` fails with
`could not read Username for 'https://github.com': Device not configured` — HTTPS remote, no
`gh` CLI, no SSH keys, and the `osxkeychain` helper has no credential available to a
non-interactive shell. The commits are intact and ready; the push requires a human.

**No human playtest has been performed.** Nothing in this entry claims gameplay feel, audio
quality, encounter balance, or standalone-player performance.


## Shared humanoid / Mixamo presentation (previous milestone)

Prerequisites committed: camera repair **16fd426**, city art **a5f8278**. The capsule visuals are replaced by the supplied Mixamo Beta humanoid (`Idle.fbx` model, two skinned meshes / 28,374 vertices). Player, cops, civilians, criminals and the pursuing Hero instantiate the **same model and `Assets/Resources/SharedHumanoid.controller`**, not copied controllers. Surface/joint materials use the existing city palette (blue player, amber civilians, teal cops, red other NPCs, dark metal joints); no imported material instances or texture pipeline were introduced.

### Assets, configuration and ownership

- `Overpowered → Animation → Batch import Mixamo Humanoids` is the explicit one-time Editor importer for all 15 supplied FBXs. Every model uses Humanoid / Create From This Model; all avatars were checked valid/human. Locomotion clips loop; actions do not. Root transforms are baked and `Animator.applyRootMotion=false`. Bone optimization is disabled so procedural code can access the skeleton. The actual landing filename is **`Fall A Land To Run Forward.fbx`**, not the shorter name in the request. No extra animation downloads were assumed.
- `Overpowered → Animation → Build shared humanoid presentation` runs that importer and rebuilds the single controller in place, preserving its GUID and the tuning asset. This is an explicit authoring command, not an automatic callback that overwrites manual Animator edits. `Assets/Resources/HumanoidAnimationTuning.asset` contains clip/model/controller references and all new speed thresholds, playback rates, transitions, punch timing, flight poses and panic parameters. Thresholds remap actual damped velocity into a continuous blend coordinate, so Inspector changes take effect without rebuilding the controller. Backflip and Hurricane Kick have references and clearly named **Unwired** states, with no gameplay dispatch or transition into them.
- Animator owns the base Humanoid skeleton: Idle → Walking → Jog Forward → Standing Run Forward is a continuous 1D blend at 0 / 1.8 / 5 / 9 m/s. Negative local forward velocity selects Standing Run Back. The existing controller still turns toward travel; this does **not** introduce a new backward-strafe control mode. Cops use Pistol Run when moving and Shooting Gun on their existing attack event. Jump/Land/Hit/Death/Punch/Cast are action states dispatched by presentation events, with smooth returns to locomotion. Repeated actions restart their clips. Fire Blast and Ice set the same data flag (`CastingPresentation`) and trigger Casting Spell only after successful activation. Backflip/Kick remain intentionally unused.
- **Flight:** `HumanoidPresentation.LateUpdate` blends all mapped bone rotations toward the imported neutral pose and neutralizes hips translation, then aims upper/lower arms, straightens the legs and lifts the head. Hover has relaxed knees, arms slightly out and a 0.065m / 0.7Hz visual bob. Horizontal velocity blends to 78° body pitch, both arms extended overhead (forward in the flying pose), straight trailing legs and lifted head. Exponential blend rates govern entering/exiting flight and hover/forward changes. Animator supplies the fading base pose during transitions, but full flight is procedural, not a hidden flight clip. Active punch/cast/hit actions retain upper-body bone authority; flight still positions the visual root/lower body. Physics roots never receive these pose rotations.
- **Panic:** real civilian alarms select Standing Run Forward at 1.45× playback, add 24° visual forward lean, raise/flail both arms (28° oscillation at 2.7Hz), and turn the head periodically (72° overlay at 0.65Hz). The existing flee destination receives a perpendicular sine offset (up to 1.5m at 0.75Hz), then still passes through the existing NavMesh sampling/path logic. Civilian gameplay flee speed remains 5m/s. Arms/head yield to active action clips; dead NPCs fade out of panic. This is not a second AI/navigation system.
- **Landing overlap:** the existing `ProceduralHeroAnimation` remains attached in `HumanoidSquashOnly` mode and writes only a dedicated visual parent's scale. Its existing `ProceduralAnimationTuning.asset` remains the one authority for landing compression/recovery. The Land clip supplies the pose; old procedural punch, ground bob/lean and landing back-tilt are disabled for humanoids. A separate child owns new flight/ground/panic lean. Thus no transform is simultaneously written by the old procedural component and the new adapter. The old full procedural component remains usable independently; its capsule-hierarchy verifier is historical and superseded for the shipping humanoid by `HumanoidVerification`.

### Gameplay boundary / judgments

Reference-pose **actual skinned vertices**, not conservative animation bounds, are measured once at spawn: 1.7971m height, uniformly scaled 1.0016× to 1.8000m with feet aligned to the physics root. Player CharacterController remains **height 1.8m / radius .38m / center Y .9m**; NPC CapsuleCollider/NavMeshAgent remain **1.8m / .35m**. No limb colliders or ragdolls were added. Animated limbs and horizontal flight can extend outside the unchanged upright capsule; detailed per-limb environment collision is not claimed.

Punch timing necessarily gains a short authoritative windup, rather than moving force to a fragile Animator event: accepted activation still pays charges/energy and starts cooldown immediately; the controller schedules the existing `CombatImpact.Blast` after **125ms**, independent of whether a renderer is visible. This comes from source clip start 11/30s, impact 17/30s, playback 1.6×. Import sampling found the left hand's strongest extension near source frame 17. The evaluated Animator pose crossing that same source-time marker is recorded independently for verification. Death/session end cancels a pending impact; interruption by a nonlethal hit defers the hit pose until the punch marker, not the damage itself. Root motion, punch force/damage/range/charges/cooldown/recharge, flight fuel, Heat, progression, mode rules and movement speeds remain their existing systems. Timed tests now wait for the real windup before asserting physics results.

NPC death disables navigation/collision immediately, plays Death instead of rotating the entire root 90°, and keeps the body for at least the clip's 3.033s duration (rather than the old 1.5s removal). This is presentation lifetime, not delayed defeat/reward logic. Player damage/respawn events drive hit/death/recovery. Inspection found that Shooting Gun is a **left-handed side draw**, not a straight-ahead rifle pose: its useful firing segment is configured at source 1.25–2.5s, 1.5× playback, with a smoothly applied 90° visual-only yaw toward the NPC's forward target direction. These values are Inspector tunables. It presents the **existing contact-range cop damage action**; no new gun projectile, ranged combat, firearm model, muzzle flash or sound system is claimed. Casting does not lock movement or delay existing Fire/Ice effects. Clip playback defaults shorten lengthy hit/cast/shoot performances, not their gameplay cooldowns.

### Verification

`HumanoidVerification.Run` runs real Unity Play Mode in the isolated project, with isolated saves and assembly reload protection. It moves the real CharacterController over a speed range, feeds its measured velocity to the presentation adapter, exercises real paid punches / damage / powers / jump / fall / cop attacks, records evaluated clip weights and pose timing, captures rendered humanoids, and benchmarks the populated city. Flight visual checks deliberately feed controlled airborne states through the shipping overlay; fuel is tested separately through the real power resource path. These are **automated pose and gameplay controls**, not claimed keyboard/mouse human feel acceptance or a standalone player benchmark.

Final Humanoid Unity run **exited 0**. Real output (`Verification/Humanoid/results.txt`):

```text
BLEND requested=0.90 actual controller=0.900 animator Speed=0.899 m/s: Idle=0.501, Walking=0.499
BLEND requested=3.40 actual controller=3.400 animator Speed=3.398 m/s: Walking=0.501, Jog Forward=0.499
BLEND requested=7.00 actual controller=7.000 animator Speed=6.998 m/s: Jog Forward=0.501, Standing Run Forward=0.499
PUNCH source start=0.3667s impact=0.5667s playback=1.60; configured windup=125.00ms; visual marker t=6.16452 frame=522; real force t=6.16452 frame=522; offset=0.00ms/0 frames.
PASS Real force=1350 N·s, mass=45kg, velocity=17.192m/s, displacement=3.782m.
PASS Zero-charge CONTROL refuses punch.
PASS Undamaged NPC CONTROL plays neither hit nor death.
PASS Lethal damage triggers Death, disables navigation/collision; no root 90-degree flip.
FLIGHT hover weight=0.998 pitch=0.00 hand=(-0.355, 0.922, 0.130)
FLIGHT transition frame 4: blend=0.426, pitch=33.22
FLIGHT transition frame 12: blend=0.750, pitch=58.51
FLIGHT transition frame 59: blend=0.998, pitch=77.83
PASS Hover vs forward distinct: body difference=77.83deg, hand height 0.922->1.977m; maximum rendered-frame pitch change=8.60deg.
PASS Live fleeing civilian: Standing Run Forward at 1.45x, lean=23.99deg, raised/flailing hand Y=1.11..1.70m; head yaw=-87.7..51.6deg.
PASS Actual controller fall: 1 impact at 15.540m/s; Land clip played with single existing squash minY=0.786, recovered=1.000; collider=1.800m.
PASS SAME controller asset on player and all 33 civilians/cops/criminals.
MEASURED populated skinned/Animator city: civilians=26, cops=10, active animators=40; 1280x720, frames=199, seconds=5.002, FPS=39.78, p95=26.85ms, drawcalls median/max=2220/2222; previous city seed2409=50.25 FPS (-20.8%), historical155=(-74.3%); AMD Radeon Pro 5300.
```

The speed sweep also measured 0, 1.8, 5 and 9 m/s; intermediate blend weights were genuinely nonzero. Cooldown, windup/no-premature-force, zero-charge/no-extra-event and recharge controls passed. Flight fuel retained **6 → 4 → 0**, then **2.5 after 1s grounded**. Real Fire and Ice both played Cast; actual jumping played Jump. A live hostile cop reduced health **100 → 92**, moving cops played Pistol Run, and the sampled shooting hand was **(0.064, 1.469, 0.732)m** ahead of its local root after visual yaw alignment. Full Hero/Villain Home → Play → Results → Home, third-mode definition, failure/timeout/defeat controls passed (`mode-regression.txt`, exit 0). A separate Unity process restored level 7 / XP 327 / 5 points / 6 sessions / 3 wins, while its fresh-save control started level 1 / XP 0 / 0 points / 0 sessions (`mode-reload.txt`, exit 0).

Rendered images were actually inspected: `idle-model.png`, `hover.png`, `forward-flight.png`, `normal-jog.png`, `panic-20.png`, `panic-60.png`, `punch-impact.png`, `death.png`, `cop-shoot.png`, `populated-city.png`. Hover is upright with relaxed knees and lowered/outward arms; forward flight is nearly horizontal with extended arms and straight trailing legs. Panic has visibly raised/flailing arms and a deeper lean than the normal jog control. Smoothness is measured over **uncaptured live frames**, since synchronous screenshot readback stalls the editor and would contaminate frame-step timing; the first capture-instrumented run is retained, not presented as smooth-frame evidence.

**Performance regressed:** final 39.78 FPS is **20.8% below 50.25 FPS**, and 74.3% below the historical gray-city 155 FPS. Earlier runs measured **33.48 and 33.39 FPS** (`initial-pass.txt`, `second-pass.txt`), so observed Editor throughput is about **33–40 FPS**, not a promised stable 40. All 40 Animators evaluate, including off-screen characters; no actor/detail/animation culling or LOD cut was made to improve the number. Same 1280×720 actual-camera setup and Radeon Pro 5300 as the city pass; shared-machine load and evolving visual pose vary between runs. No standalone performance or hands-on feel certification is claimed.

The **separate existing city/power stress regression also passed** (`city-regression.txt`, exit 0): naturally exhausting three punch charges, cooldown/recharge, seventh-definition projectile physics, Telekinesis grab/hurl, Ice freeze/expiry, destruction raising Heat, cops **4 → 10**, pursuing Hero, civilian panic, Heat decay, side objectives and XP all remain functional. Its later legacy-sandbox performance sample was **26.41 FPS / p95 51.10ms** with 24 civilians and 10 cops. This is lower than the fresh mode benchmark above and **83.0% below that harness's historical 155.01 FPS**; it is retained, not excluded from the reported performance envelope. Across these different test workloads the measured range is therefore **26–40 FPS**. That legacy harness does not collect draw calls; the fresh populated-mode sample records 2,220 median / 2,222 max. This is an unresolved performance limitation.

That power regression's own separate-process reload also passed (`city-reload.txt`, exit 0): level 5 / XP 155 / 1 point / Villain, Strength tier 1 (1,890 N·s), Telekinesis unlocked; a fresh save again started level 1 / 0 points.

Final targeted `HumanoidVerification.DeathControl` also exited 0 (`death-interruption.txt`): dying during a paid punch's windup cancels the force and plays Death; actual respawn clears pending presentation state, and subsequent real damage immediately plays Hit again. This prevents a canceled punch from leaving future hit reactions deferred forever.

`dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` passes **0 warnings / 0 errors** (`build.txt`). An initial punch timing check correctly failed at 135.46ms / 11 frames because Unity's fixed-time start offset scaled with state speed; switching to a normalized clip offset corrected it (`initial-timing-failure.txt` retained). This is a measured marker/force alignment, not a claim of sub-frame GPU/physics simultaneity. The pre-existing Editor Search indexing exception remains unrelated to gameplay. Legacy capsule tests are not claimed as current-model verification; current ownership and limits are explicit above.

## Stylized city art pass (previous milestone)

Camera prerequisite was completed and committed first as **16fd426**. This pass keeps that scene-camera flow, the existing `CityLayout` seed/placement algorithm, movement/abilities, mode rules and progression.

### Added

- **Four coherent building archetypes:** brick warehouses, sandstone terraces, teal offices and slate towers. They vary footprint proportions and upper-storey setbacks inside the existing building slots. Original seeded heights and the three taller landmarks remain authoritative. Floors have geometric trim/banding, repeated dark/amber windows, actual recessed ground-floor entrance pockets, shop canopies and abstract sign glyphs. No building interiors or new layout algorithm.
- **Rooftop traversal layer:** every building has a roof deck, low physical parapets, HVAC unit, vent and roof-access structure. Seeded rules add water tanks on legs, antennae and occasional billboards. The center stays available for landing and existing rooftop XP discoveries; real collision geometry supports the decks/terraces. Roof equipment is physical and mostly destructible; structural decks/parapets/access structures are not. Rooftop access doors are visible but do not open.
- **Street level:** cream curbs, zebra crossings and amber lane dashes; matched lamps with shape-based street signs, benches, trash cans, hydrants, bus shelters, newspaper boxes, blocky planted shrubs and parked cars with cabins, wheels, lights and bumpers. Crime-event throwable cars reuse the same car model; crates/barrels remain simple matching wooden primitives. No real-word signage, traffic/bus simulation or texture assets.
- **One material palette:** `Assets/Resources/CityPalette.asset` is the sole source for generated material colors. A named-swatch Inspector exposes Road, Pavement, Cream, Brick, Sand, Teal, Slate, Roof, Glass, Amber, Metal, Wood, Leaf, Red, Blue, Cyan and Fire. The palette uses warm masonry, cool teal/navy, cream trim, dark roads and amber accents. Materials are shared and updated live when a swatch changes; no per-object material instances. The existing player/NPCs, crime markers, projectile visuals and debris also use these slots so the new city does not clash with the old prototype. Projectile definitions expose a palette slot for future data-driven color selection. Existing legacy color fields remain serialized but no longer control generated material colors.
- **Authoring data:** `Assets/Resources/CityArtSettings.asset` holds styles, facade/roof dimensions, prop sizes/masses/destructibility, normalized placement anchors, probabilities, jitter and mesh combining. Rules use a separate deterministic PRNG derived from the existing city seed; they do not perturb `CityLayout`. `Overpowered → Bake current art placements to editable data` records kind, base position, yaw and rooftop flag, then enables authored placement mode. Those records override generation, so manual edits survive subsequent runs. Roof placement receives a 0.1m starting clearance over its recorded base. If building heights/layout change after baking, re-bake or move those anchors yourself.
- **Destruction reused:** every new destructible prop has one Rigidbody and the existing `BreakableProp`. Compound visual parts are children of one proxy BoxCollider; the old hit/health/force/shard path handles breaking. Shards inherit the parent palette material. No second destruction/health/reward system. Structural building geometry remains indestructible.

### Rendering decisions, limits and explicit omissions

Low-poly primitive geometry with opaque, low-smoothness Standard materials, not realism. Window/sign detail is geometry, not texture maps. Meshes are combined per building/material and per prop/material **from the outset**; this is an Inspector option, not a post-benchmark content cut. Uncombined geometry can be inspected by disabling `Combine Meshes`. A ProBuilder conversion/export tool is **not implemented**; baked placement data is the supported hand-authoring boundary. Facade composition and prop silhouette ratios are code-defined recipes; placement rules, scale, mass, colors and building proportions are data.

Furniture uses simple bounding-box collision, including bus shelters (not walk-in interiors) and water tanks. Lamp heads/windows use palette colors, not extra point lights or a night-light simulation. No LOD/culling authoring, occlusion bake, art-quality lighting replacement, standalone build benchmark, or human traversal/feel acceptance is claimed. The existing directional sun (1.2 intensity, 45/-35 orientation), ambient color and Built-in rendering pipeline remain unchanged. There was **no active post-processing Volume** to preserve; none was introduced. Graphics/Quality project settings were not edited.

**Performance regression is significant.** Initial measurements were 41.91 / 52.04 FPS for the two seeds, with 2,046 / 2,080 draw calls, versus the recorded 155 FPS baseline. That first harness used an extra capture camera; its complete output is preserved in `Verification/Art/initial-two-camera-run.txt`. A matched-camera repeat (the existing camera, follow temporarily disabled, as in the historical harness) measured 36.25 / 47.12 FPS and 2,054 / 2,088 draw calls (`first-matched-camera-run.txt`). No detail was removed or runtime performance tuning applied in response. Final shipping-content measurements follow below. This remains a substantial performance cost to address in a dedicated profiling pass, not a claimed 155-FPS visual upgrade.

### Art verification

`CityArtVerification.Run` builds both seeds in real Hero sessions, renders street/roof/whole-city captures, tests shared palette changes/restoration, compares deterministic and authored placement controls, punches an actual generated newspaper box, checks lighting, and samples FPS plus Unity's actual draw-call/SetPass counters with civilians/cops active. Saves are isolated. Captures and raw outputs are under `Verification/Art/`. FPS uses a 1280×720 RenderTexture, wall-clock timings and the historical camera position (-65,60,-80), looking at the origin; the player is held airborne. This is Editor throughput, not a standalone-player or hands-on combat guarantee. The historical baseline had 24 civilians; these mode runs have 26 (including two encounter civilians) and 10 active/on-NavMesh cops. Hardware remains Intel i9-10910 / Radeon Pro 5300. Current and historical runs are not a controlled dedicated-machine GPU benchmark.

Final Unity run exited 0. Seed **2409** produced **247 new props (159 rooftop / 88 street)** and 9 of each building style. Seed **3226** produced **253 (163 rooftop / 90 street)**: 10 sandstone, 9 teal, 8 slate and 9 brick. The second seed had 9 rather than 7 bus stops, 27 rather than 24 antennae, and 13 rather than 12 billboards; heights, style assignments and street jitter also changed. Both had 36 HVAC units, 36 vents, 36 roof access structures, 15 water towers and 18 parked cars. Placement JSON for each seed is retained, with same-seed exact-repeat and authored-placement controls.

Selected **real output** (complete output: `Verification/Art/results.txt`):

```text
PASS Layout CONTROL: original CityLayout placements/heights/reward records are unchanged by art generation.
PASS ALL live renderers use shared materials from CityPalette; unregistered material count=0.
PASS Palette CONTROL: changed ONE Amber swatch RGBA(1.000, 0.700, 0.260, 1.000) -> RGBA(0.310, 0.860, 0.800, 1.000); 115 renderers sharing it updated (windows, lamps, vehicle lights, actors).
PASS Palette restore CONTROL returns all shared Amber surfaces to the original swatch.
PASS New newspaper box uses the EXISTING BreakableProp and one real Rigidbody, no parallel damage implementation.
PASS New prop physics: mass=45kg, punch=1350 N·s, displacement=8.978m, velocity=23.008m/s.
PASS Existing sun intensity=1.2, rotation=(45,-35,0), ambient=(.45,.5,.6) unchanged.
PASS Existing Built-in render pipeline retained; no active post-processing Volume was present/added.
MEASURED seed=2409: 1280x720 Editor actual renders, frames=252, seconds=5.015, FPS=50.25, p95=30.45ms, draw calls median/max=2074/2074, SetPass median=88; vs recorded 155 FPS=-67.6%; GPU=AMD Radeon Pro 5300.
MEASURED seed=3226: 1280x720 Editor actual renders, frames=268, seconds=5.011, FPS=53.48, p95=24.53ms, draw calls median/max=2108/2108, SetPass median=80; vs recorded 155 FPS=-65.5%; GPU=AMD Radeon Pro 5300.
PASS Camera repair retained through both seeded mode runs and return Home.
```

**Final performance is still 65.5–67.6% below the recorded baseline.** Both initial and matched-camera runs are preserved; the last run also includes the matching crime-event car models. No LOD/content cut was made. The force test relocates one generated newspaper box above a clear road to isolate it, then measures motion only after a real charged punch; final destruction goes through existing damage/shard code. Rendered street, rooftop and city images were visually inspected. `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` is clean: **0 warnings, 0 errors** (`Verification/Art/build.txt`). The editor's pre-existing Search indexing exception still appears independently of gameplay.

The full mode regression after the art changes passed (Unity exit 0): Home → Hero → Results → Home, then Villain → Results → Home, plus a definition-only third mode and failure/timeout/defeat controls (`Verification/Art/mode-regression.txt`). This pass did not run a standalone build or certify hands-on balance. The recorded high draw-call cost remains an explicit unresolved limitation, not a hidden cut.

An initial regression attempt was interrupted by a delayed editor assembly reload during Play, which cleared runtime singleton references and caused `ThirdPersonCamera.LateUpdate` null-reference errors. It was stopped and is **not** counted as a passing run (`Verification/Art/interrupted-regression.txt`). The camera now safely handles a temporarily unavailable world, and the mode verifier locks assembly reloads until test completion. Full live recompilation/restoration of the generated world is not supported by this change: restart Play after code recompilation. Unrelated animation assets added to the workspace during this task are not part of the art implementation or commit.

## Camera diagnosis and repair

The saved Home, Prototype and Results scenes had **zero GameObjects and zero Cameras**. The mode refactor relied on `GameFlow.SceneReady` to create menu cameras and `PrototypeBootstrap.BuildCity` to create the player camera at runtime. No disabled scene camera was found. The `Prototype` heading is a scene container, not a newly introduced parent GameObject; the saved scene contained no such parent. A Project search result is not evidence of a Camera instance in the loaded scene.

Read the actual open editor's `Logs/Editor.log`: it contains earlier `CS1061` errors (`WorldSession.Mode` / `SpawnEncounter` missing) from the incomplete refactor, followed by successful assembly reloads. The clean baseline renders Home and both gameplay modes successfully; **a runtime camera-factory failure was not reproduced**. The reproducible “No cameras rendering” condition is the camera-less edit-time scene. The baseline's error check also caught the existing `ArgumentOutOfRangeException` in `UnityEditor.Search.SearchDatabase.EnumerateAll`, not in camera/gameplay code. The verifier records this specific editor exception separately, without hiding other errors. No claim is made that a disabled Project-search hit was repaired or that the Search package exception was fixed.

Repair: each of the three scenes now saves one enabled, top-level **Main Camera**, tagged MainCamera, with an AudioListener and `GameCamera` component. Runtime setup reuses that camera, explicitly restores Display 1/full viewport/enabled state, configures menus to clear their background, and attaches the existing third-person follow logic in Hero/Villain. It no longer needs to invent an invisible-to-the-editor camera from scratch. Newly created menu scenes also receive a camera. `Overpowered → Repair scene cameras` is an explicit repair utility, not an automatic scene rewrite. The city still generates only after Play/mode selection; a camera alone does not generate an edit-time city preview.

Verification uses `CameraVerification.Baseline`, `Verify`, and `DirectPrototype` in an isolated Unity project. The disabled-camera control checks that a disabled camera drops out of Unity's rendering camera list. Pixel captures render the **actual scene camera**, not an extra verification camera, at 640×360, then restore the Display target. Menu captures show the camera background, not IMGUI; batch rendering does not certify mouse interaction or Game View repaint cadence. The historical baseline output is retained even though its blanket error check failed on the unrelated Search exception.

Fixed flow and direct-Prototype startup both passed (exit 0): Home, Hero, Hero pause/results/Home, Villain, Villain pause/results/Home each had exactly one active enabled camera on Display 1. All captures overwrote **230,400 / 230,400** pixels; gameplay images contained many colors and the camera followed the current hero. Each saved scene now reports `roots=1, cameras=1, enabled=1` before Play. The disabled-camera controls passed. Full output and actual-camera captures are in `Verification/Cameras/`. `dotnet build` passed with **0 warnings, 0 errors**.

## Home and game-mode sessions (current)

Built a mode wrapper around the existing city, controller, five powers, progression/save, Heat/police, HUD and procedural presentation. No replacement controller, combat system, city generator or animation system was introduced.

- **Startup and flow:** `Home` is the first build scene. OVERPOWERED lists Hero and Villain as playable; Free Play and Endless Fight say COMING SOON and reject selection. Selecting a definition loads `Prototype`; a completed/failed/timed-out session loads `Results`, with score, XP earned, outcomes and counts. Results returns to Home. Starting Play directly in Prototype also redirects to Home unless an isolated legacy verification explicitly requests the sandbox.
- **Mode architecture:** `GameModeDefinition` assets supply side, reusable `ModeRules`, encounter pool, spawn cadence/population, win/fail/defeat/time limits, rewards and HUD flags. The menu discovers assets; no mode-ID registry/switch was added. `GameModeSession` owns the session lifecycle; the existing Hero/Villain crime entry points route into `CrimeEncounter` and the selected rules. A new mode can reuse rules or provide its own rule subclass/asset without editing abilities, city, save or menu code.
- **Hero:** neutral police pursue/suppress robbers. Stop every robber by capture (held R) or combat, physically displace/destroy blockades, hold R to rescue cyan civilians, and extinguish fire nodes in arson events. Resolving just the marker or one actor is insufficient. Civilian death or any escaped robber fails the event.
- **Villain:** the same city and encounter population; cops attack the player through existing Heat AI. Steal gold loot, wreck at least three encounter props, interact with sabotage/fire nodes where present, then escape beyond 22m. Robbers are allies. Destruction earns existing XP and Heat; Heat-driven reinforcements and the pursuing Hero remain intact. Vertical flight escape is intentionally allowed.
- **Set-pieces:** bank break-out, street ambush and arson definitions. Each spawns three robbers with different exit routes, two endangered civilians, two responding cops, two throwable cars, four supply crates, two rescue blockades and two loot nodes. Arson adds two fire nodes. Cars/crates use real Rigidbodies and the existing force/damage/shard system. Encounters appear at separated street intersections; actors use the existing NavMesh. Scenario objects are removed when the event ends or the city unloads; temporary physical shards retain their existing lifetime.
- **Feedback/UI:** live objective sub-counts, distances/bearings, world markers, deadlines, success/failure feedback, session score/XP/counts. Escape is a true pause with resume, results or return-home actions; Tab remains the live power-upgrade menu. H cannot change sides mid-session. Mode HUD flags control health, powers, progression, Heat and objectives.
- **Save extension:** earned XP and purchased powers remain shared across modes. Added session/win counts, best score, last mode and last-session XP to the existing atomic JSON save. Old version-1 files remain compatible with zero-initialized session fields. Rewards save immediately; session outcomes save once. Transient city state, health, Heat, resources and active encounters still reset between sessions.

### Session choices and consequences

Both shipping modes are **objective-count based: five completed encounters wins**, not an enforced 10–15-minute survival session. A **900s / 15-minute cap** produces timeout results. **Three failed encounters or three player defeats loses**; the first two defeats use the existing respawn behavior. These limits can be disabled with zero or edited per mode in the Inspector.

One event starts immediately; another can spawn every **50s**, up to **two active**. Each has a **210s deadline**. Robbers scatter locally, then try their city exits after **45s**. Unrescued civilians begin taking **1 damage/s after 90s**. Hero failure on escape/death/timeout adds **0.75 Heat**, removes **25 score** (floor zero), and counts toward the loss limit; it does not remove earned XP. Success grants **100 score / 60 XP**, plus existing combat rewards; civilian rescue gives **20 score**. Hero completion lowers Heat by **1**. Villain completion adds **1 Heat**, wrecked encounter props give **5 score** each, and existing destruction XP is retained. Villain deadline failure also adds 0.75 Heat and counts against the session. Physics collateral can kill a civilian: rescue requires care with blast direction, not indiscriminate area attacks.

All of those numbers and population counts live in the mode/encounter assets. Powers, props, movement and animation retain their existing authoritative tuning assets.

### Explicit cuts / prototype limits

- **Not implemented by design:** Free Play and Endless Fight gameplay. Their disabled catalog definitions are present; no fake playable buttons.
- **No art pass:** primitive actors/cars/crates and basic IMGUI screens. Bank/ambush share the common encounter choreography; they are not authored bank interiors or vehicle-driving missions. Arson has extra interactive fire nodes, not a spreading-fire simulation. Civilians are immobilized by a gameplay blockade condition until rescued, not physically pinned/ragdolled. Cop support uses navigation/suppression/damage, not firearms.
- **Not claimed:** human keyboard/mouse feel acceptance, a 15-minute hands-on balance run, native-resolution player-build FPS, or a standalone distribution build. Automated completion controls position the player and supply hold/damage inputs directly to gameplay methods; they do not prove navigation/input ergonomics or difficulty. The physics rescue check uses an actual charged punch.
- UI button callbacks and real scene transitions are exercised via the same flow methods in batch Play Mode, not automated mouse clicks. World rendering is captured separately; no batch screenshot of IMGUI menus is claimed.
- The original expansion notes below are historical. Their H-switch, single-step crimes, repeating Villain chaos goal, and non-pausing Escape behavior are superseded by these modes. Legacy sandbox paths remain only for isolated regression checks.

### Mode verification

Reproducible checks: `ModeVerification.Run` followed by `ModeVerification.Reload` in a **separate Unity process**, using a temporary project copy and isolated save paths. The first run temporarily creates a third definition reusing Hero rules/encounters, with a one-encounter goal; the runtime discovers it without new system code. The definition is removed after testing; a copy is retained as evidence under `Verification/Modes/`.

Actual final Unity Play Mode output (`Verification/Modes/results.txt`; process exit 0):

```text
PASS Startup HOME, no city or player spawned.
PASS free-play COMING SOON CONTROL refuses launch.
PASS endless-fight COMING SOON CONTROL refuses launch.
PASS Populated event: 3 robbers, 2 trapped civilians, 2 responders, 8 real Rigidbody props (2 cars, 4 supplies, 2 blockades), 2 loot nodes.
PASS Untouched marker CONTROL does not resolve encounter.
PASS Blocked civilian CONTROL refuses rescue before blockade is moved/broken.
PASS Rescue physics: actual punch AddExplosionForce(1350 N·s, Impulse), mass=45kg; displacement=10.000m.
PASS Hero police CONTROL: neutral cop in attack range does not damage player.
PASS Pause freezes session clock and encounter simulation.
PASS Ignored Hero event: failures=1, Heat 1.75 -> 2.50 (+0.75).
PASS Hero PLAY -> RESULTS: Won, success=5, failure=1, score=700, XP=825.
PASS Hero RESULTS -> HOME; city unloaded.
PASS Hero/Villain regenerated the SAME seeded city layout.
PASS Mode-switch persistence: level=5, XP=162, points=4, wins=1.
PASS Earned points purchase strength upgrade; tier=1 must survive restart.
PASS Villain live cop AI attacked: health 100 -> 76.
PASS Existing Heat escalation retained: cops 4 -> 10 at Heat 3.00.
PASS Configured spawn-clock interval adds second multi-part encounter.
PASS Population-cap CONTROL refuses a third simultaneous encounter.
PASS Villain escape CONTROL: loot/destruction alone cannot resolve while inside scene.
PASS Villain PLAY -> RESULTS: Won, success=5, score=700, XP=780.
PASS Villain RESULTS -> HOME; city unloaded.
PASS Third mode discovered/selected from a definition ONLY; same rule and encounter assets, goal=1, no registry changes.
PASS Data-only third mode loaded, ran, and won at its configured single-encounter goal.
PASS Failure-limit CONTROL: third failed encounter produces LOST results.
PASS Timeout CONTROL: configured 900 seconds produces TIMED OUT results.
PASS Defeat CONTROL: two respawns allowed; actual third player death produces LOST results.
MEASURED populated events: civilians=28, cops=12, events=2; 1280x720 rendered Editor Play Mode; frames=1237, seconds=5.007, FPS=247.08, p95=6.48ms; Intel(R) Core(TM) i9-10910 CPU @ 3.60GHz; AMD Radeon Pro 5300.
```

The FPS sample renders a dedicated top-down 1280×720 camera every sampled frame while live world/NavMesh/NPC updates continue, with two active encounters, six robbers, 28 civilians and 12 cops. The player is held airborne for test safety. It measures wall-clock Editor throughput, **not a standalone or hands-on combat FPS guarantee**. The captured frame is `Verification/Modes/populated-event.png`. Police neutrality is tested away from hostile robbers to avoid attributing their attacks to the cop. Timers/interaction durations are advanced through runtime methods; the test does not wait 15 real minutes. `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false` passes with **0 warnings / 0 errors** (`Verification/Modes/build.txt`). Unity's existing `UnityEditor.Search` startup indexing exception appeared but did not prevent the completed tests.

Separate-process reload (`Verification/Modes/reload.txt`; exit 0):

```text
PASS SECOND UNITY PROCESS exact reload: level=7, XP=327, points=5, sessions=6, wins=3, best score=700; all power tiers/rooftops retained.
PASS Fresh-save CONTROL in second process: level=1, XP=0, points=0, sessions=0.
```

The saved Strength upgrade was tier 1; the comparison covers the full progression JSON, not just the printed fields. The three wins are Hero, Villain and the temporary third mode; the other three outcomes cover failed encounters, timeout and defeats.

Existing city/power and procedural-animation regressions also exited 0 after this change. Their current output is retained separately as `Verification/Modes/city-regression.txt` and `animation-regression.txt`, without overwriting the earlier milestone's evidence. The city regression rechecked flight draining **6 → 4 → 0**, grounded recharge, normal punch/cooldown/zero-charge/recharge controls, seventh-power physics, Heat/police, Telekinesis, Ice and progression. The 20 animation checks include the actual controller fall and recovery. Those tests deliberately use the legacy isolated sandbox; the mode suite above independently exercises the new shipping flow.

## City and progression expansion (previous milestone)

Implemented all six systems from the attached expansion brief, extending the existing controller and retaining procedural animation:

- **City:** seeded 3×3 blocks, 36 solid buildings with varied heights and three tall landmarks, sidewalks/roads, five one-time rooftop XP discoveries, and crates, barrels, benches, trash cans, streetlights, and parked cars with Rigidbody physics and breakable shards. Buildings do not break. `CityLayout` can capture generated placements into editable data for later authored/ProBuilder layout work.
- **Powers:** Flight and Super Strength now use Inspector-editable PowerDefinition assets and the generic charge/cooldown/upgrade pipeline. Added Telekinesis (spring-force hold + impulse hurl), Fire Blast (colliding explosive projectile), and Ice (timed NPC/rigidbody freeze). Resources, force, damage, range, duration, costs, and upgrade tiers are data. Reusable effect assets dispatch behavior without a central power switch or hardcoded catalog. Existing gameplay constants remain in their original file as defaults.
- **Progression:** deterministic XP thresholds grant spendable points; powers are never randomly granted or automatically upgraded. Enemy defeats, appropriate crime/chaos actions, and rooftop exploration award XP. JSON saves persist XP, level, points, side, unlocks/tiers, and rooftop claims. Atomic replacement and a backup are used because explicit versioned JSON is inspectable and expandable; normal saves are separate from verification saves.
- **Sides/objectives:** H switches the same player between Hero and Villain with a 10s default cooldown. Mugging, robbery, and fire events spawn with world markers. Nearby interaction or defeating criminals resolves Hero crimes; Villains can assist events, destroy props for repeating chaos objectives, and gain XP from cops/civilians. Cops are neutral to Heroes and hostile to Villains. Switching retains powers and upgrades.
- **Heat/AI:** 0–5-star Heat increases from property destruction, assaults, and hostile actions; it decays after a quiet delay. Additional and tougher cops spawn by tier, with a pursuing Hero NPC at high Heat. Runtime-built Unity NavMesh supports wandering/fleeing civilians and enemies that pathfind, attack, receive damage/freeze, and die. Player death respawns with progression intact.
- **HUD:** health, energy, selected power charges/cooldown, flight fuel, XP bar/level, unspent points, Heat stars, side, event markers, and scrolling power selection/unlock/upgrade buttons. Tab opens the menu; H changes side; R interacts; E punches; LMB uses the selected power. See README for all controls.

### Configuration and judgments

`GameTuning.asset` groups each system's settings in the Inspector. Each power's authoritative values and upgrade tiers live in its own asset. Animation settings stay separate. The five named shipping powers start with Flight and Strength unlocked. The brief's “seventh power” check uses two temporary definitions reusing the projectile effect, not extra shipping abilities.

This is a primitive blockout, not an art pass: parked cars and benches have simple box silhouettes, streetlights are physics poles, and NPCs are colored capsules. Crime scenarios use timed/contextual interactions and criminal NPCs rather than cinematics or detailed rescue simulations. Fire interaction takes 2s; it is not a propagating fire simulation. Enemies use ground NavMesh paths and cannot fly after a player above rooftops; escaping vertically is possible. Police are neutral to the Hero even when property damage raises Heat. Layout generation happens at Play startup; restart Play after changing the seed. Menus do not pause combat.

Transient health, Heat, charges, destroyed objects, and active events are intentionally not saved. There is no structural building destruction. Human keyboard/mouse playtesting and a standalone-player performance benchmark remain unperformed. No required system is left as an architecture-only stub; the limitations above describe the implemented prototype depth.

### Expansion verification

Actual Unity Play Mode output, separate-process reload results, a data-only seventh-power artifact, and the rendered district capture are recorded under `Verification/City/`. Verification ran in a project copy with isolated save files. `dotnet build Overpowered.Build.csproj` completed with **0 warnings, 0 errors**. Unity gameplay, separate-process reload, and animation regression all exited successfully. The editor still reports its pre-existing Search indexing exception at startup; it did not prevent these tests from running.

Measured output from the final run:

```text
PASS Seventh power discovered from DATA only; exact same projectile effect asset as Fire Blast.
PASS Seventh projectile hit real Rigidbody: impulse setting=200 N·s; displacement=12.992m; velocity=32.672m/s.
PASS Unspent CONTROL: level=2, points=1, force unchanged=1350.
PASS Upgraded strength: force=1890, max charges=4, points=0.
PASS Zero-point upgrade CONTROL rejected.
PASS Real prop destruction: Heat=3.50; police 4 -> 10; high-tier max HP=137.
PASS High Heat spawns one pursuing Hero NPC.
PASS Lay-low decay: 3.50 -> 2.80 after configured delay plus 10s.
PASS Hero fire outcome: Heat delta=-1.00, XP awarded=60.
PASS Same fire event as Villain: Heat delta=1.00; opposite Hero outcome; same character=Overpowered Hero.
PASS Hostile cop attacked through live AI: player HP 100 -> 72.
PASS Defeating cop as Villain awards XP and disables dead NPC navigation.
PASS Separate Unity process restored exact save: level=5, XP=155, points=1, side=Villain.
PASS Separate process retained strength upgrade tier=1; force=1890
PASS Unlocked Telekinesis persisted.
PASS Fresh-save CONTROL: level=1, points=0.
MEASURED 1280x720 rendered Editor Play Mode: frames=776; seconds=5.006; FPS=155.01; p95 frame ms=10.33.
```

The benchmark had 24 civilians and 10 enabled, on-NavMesh cops, on an Intel i9-10910 / AMD Radeon Pro 5300. A 1280×720 RenderTexture was actually rendered each sampled frame; Stopwatch wall time measured throughput while world/AI updates ran. The camera viewed the populated district from above, with the player airborne; this is not a hands-on combat or standalone-player benchmark. The much lighter 4kg verification target reached 444.154m/s on a 1800 N·s telekinetic hurl; shipping prop masses are 45–400kg, so this test is a force-path check, not a claim that the test object's speed is balanced.

Other passed controls include real flight resource consumption (6 → 4 → 0s, then 2.5s after one second grounded), punch cooldown and empty-charge rejection/recovery, physical Telekinesis grab/hurl, timed Ice freeze/unfreeze, civilian fleeing, repeat-side-switch rejection, and a complete NavMesh route. The retained 20-check procedural-animation regression passed in the city: one actual 5m landing at 13.340m/s, minimum visual height scale 0.817, recovered scale 1.000, and unchanged 1.800m controller height.

## Original arena milestone (historical; superseded by city above)

- Third-person controller: WASD movement, Left Shift run, Space jump.
- Flight: hold `F` while airborne. Flight has 6.0 seconds of fuel and refills at 2.5 fuel/second while grounded.
- Super-strength punch: left mouse or `E`. It has 3 charges, a 0.45-second cooldown, and recovers a charge every 1.25 seconds.
- Tiny physics arena generated at play time with crates, barrels, and stacked objects. Punches use `Rigidbody.AddForce` / `AddExplosionForce`; props retain physics and can break apart from sufficiently hard impacts.
- HUD provides live numbers for fuel, charges, cooldown, force applied, and last punch outcome.

## Original arena verification controls (historical)

Enter Play Mode, then press `V` to run the built-in deterministic validation sequence. It prints concrete telemetry to the Console and shows it in the HUD: flight fuel is sampled at 6.000, 4.000, and 0.000 seconds; it then demonstrates ground recharge. The punch test records three successful charged punches, a rejected zero-charge punch, a rejected cooldown attempt, then a recovered successful punch. The force test prints the exact configured force (1350 N) and affected rigidbody count.

The verifier drives the same resource and punch methods used by gameplay; it is deliberately available in the running prototype so the values can be checked alongside the actual physics response.

The original V demo is no longer attached by the city bootstrap. Use the city verification described above; the old demo assigned some state directly and should not be read as independent measurement of all gameplay behavior.

## Procedural animation pass

- Added collider-free arms and contrasting fists to the existing capsule. A successful punch snaps the right shoulder forward and pitches/twists the body, then eases to rest. Rejected cooldown/empty-charge attempts do not animate.
- Moving flight pitches forward; hovering or ascending without horizontal travel levels out. Ground movement leans in the actual local travel direction, with a small distance-driven vertical bob that fades at rest.
- A landing event reports downward impact speed from the actual CharacterController movement. Falls above the minimum impact threshold produce a quick foot-pivot squash, slight widening, backward tilt, and recovery. Sub-skin-width resting contact transitions are filtered from the event; light impacts also do not trigger the animation.
- All animation timing, angle, intensity, speed thresholds, and blend rates are Inspector-editable in `Assets/Resources/ProceduralAnimationTuning.asset`. Select that asset in the Project window; edits take effect during Play Mode. Asset edits persist, so undo unwanted experiments. Defaults: punch attack/recovery 0.065/0.240s, arm rotation -100 degrees; flight lean 48 degrees; run lean 10 degrees and bob 0.045m; landing compression/recovery 0.055/0.240s and maximum squash 22%.
- Existing gameplay constants remain in `PrototypeTuning.cs`. Animation configuration is a ScriptableObject rather than C# constants because constants cannot be edited in the Inspector.
- `ProceduralHeroAnimation` only writes visual children. The controller publishes `PresentationState`, `PunchStarted`, and `Landed`; a future Animator adapter can subscribe to those same signals without changing ability logic. Disable/remove the procedural component when replacing it; disabling restores the resting pose. No animation clips or Animator controller were introduced.
- Scope: this pass implements the four requested placeholder animations. The attached broader city/powers/progression brief was treated as background, not implemented in this animation pass. Existing package edits are not part of this commit.

### Animation verification

`Assets/Editor/ProceduralAnimationVerification.cs` is a reproducible Unity batch Play Mode check. Run Unity with `-batchmode -projectPath <project> -executeMethod ProceduralAnimationVerification.Run -logFile <log>` (omit `-quit`; the check exits with its result). It samples controlled animation states, checks successful/rejected real punch calls, and then lets the real controller fall onto the arena. Output is written to `Verification/Animation/results.txt`; with graphics enabled it also renders six pose samples. This check is separate from the original `V` resource demo above.

Actual verification on Unity 6000.6.0f1, in a temporary project copy to preserve the open editor session: 20 checks passed, process exit 0. Recorded results and six inspected rendered poses are in `Verification/Animation/`.

- Successful punch: 100.000-degree arm rotation at 0.065s; recovered to rest. Cooldown and zero-charge controls emitted no additional punch event.
- Moving flight: 48.000-degree pitch; hover/vertical-ascent control: level. Diagonal run: pitch/roll +7.071/-7.071 degrees; bob over 120 steps: 0.0000–0.0450m; idle bob faded to zero.
- Controlled full-strength landing: scale (1.100, 0.780, 1.100), -13.000-degree backward tilt; weak-contact control remained unsquashed.
- Actual 5m controller fall: exactly one landing event, 13.117m/s impact, minimum visual Y scale 0.820, recovery to 1.000. Controller height remained 1.800m. All controlled poses preserved physics-root position, rotation, and scale. Disabling the component restored the resting pose.

Limits: deterministic pose samples and an actual automated fall were verified; no human keyboard/mouse feel session was performed. Unity compiled the scripts successfully, but the editor log included existing deprecated object-lookup warnings and a UnityEditor.Search startup indexing exception; these did not prevent the Play Mode checks from passing. No city/progression work or gameplay-resource re-verification is claimed by this pass.
