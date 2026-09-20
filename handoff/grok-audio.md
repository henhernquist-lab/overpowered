# WORK PACKET — Audio system for "Overpowered" (Unity 6 superhero prototype)

You are implementing the **entire audio system** for a Unity game that currently has **zero audio**.
This packet is self-contained. Read all of it before writing code.

## Your branch

```
git checkout -b feat/audio
```

Commit to `feat/audio` only. **Never commit to `main`.** Two other agents are working in this
repo on other branches at the same time; staying in your file scope is not optional.

## The project

- **Repo:** `/Users/melaniehernquist/Documents/ChatGPT/op` — Unity **6000.6.0f1**, macOS.
- **Built-in Render Pipeline.** Not URP. There is no URP package, no pipeline asset, no custom
  shaders. Do not add any.
- A third-person superhero prototype: a seeded low-poly city, Hero and Villain modes, powers
  (punch, flight, fire blast, ice, telekinesis), a Heat/police wanted system, destructible props,
  civilian and cop NPCs, XP/level progression with a JSON save, and a UI Toolkit home/results menu.
- Read `AGENTS.md` and `STATUS.md` first. STATUS.md is long; read the **top section** and skim
  the rest. It is accurate — trust it.
- Start scene: `Assets/Scenes/Home.unity`. Gameplay scene: `Assets/Scenes/Prototype.unity`.

**Performance context that constrains you:** the game currently runs at roughly 26–40 FPS in the
Editor and is under active performance investigation by another agent. **Do not add per-frame
allocation, do not add an `Update()` that runs per-NPC if you can avoid it, and do not
instantiate AudioSources per sound.** Pool them. An audio system that costs measurable frame
time will be reverted.

## YOUR FILE SCOPE — exclusive, do not go outside it

You may create and edit **only** these:

```
Assets/Scripts/Audio/**              (all new: AudioDirector, pooling, cue definitions)
Assets/Audio/**                      (all new: the actual .wav/.ogg clips + .meta)
Assets/Resources/AudioTuning.asset   (new ScriptableObject asset)
Assets/Editor/AudioSetup.cs          (new: menu command to build the tuning asset + mixer)
Assets/Editor/AudioVerification.cs   (new: batch-mode entry point)
Assets/Scripts/AudioVerificationRunner.cs (new: the verification runner)
Assets/Scripts/BreakableProp.cs      (EXISTING — see the one narrow exception below)
handoff/grok-audio-notes.md          (optional, your own working notes)
```

**`Assets/Scripts/BreakableProp.cs` is the single existing gameplay file you may touch**, and
only to add a destruction event. Add a `public static event System.Action<Vector3> Destroyed;`
(or similar) and invoke it where the prop actually breaks. **Additive only** — do not change any
existing damage, health, shard or physics logic in that file.

**Files you must NOT touch** (owned by other agents — editing them will cause a merge conflict
that costs someone real time):
`Assets/Scripts/HumanoidPresentation.cs`, `CityArt.cs`, `CityPalette.cs`, `CityDistrict.cs`,
`ModeScreens.cs`, `MenuSkyline.cs`, `PowerUser.cs`, `WorldSession.cs`, `SuperHeroController.cs`,
`PlayerProgression.cs`, `PowerDefinition.cs`, anything under `Verification/Performance/`,
`Verification/Menus/`, `Assets/Resources/Encounters/`, `Assets/Resources/Modes/`,
`Assets/Resources/ModeRules/`, `README.md`, `docs/`.

## Hooks that ALREADY EXIST — use these, no edits required

This is the key design constraint: **you can wire almost all audio without editing a single
existing file**, because the events you need are already public.

| What you want | Existing hook | File |
|---|---|---|
| Any power activated (punch, fire, ice, flight, telekinesis) | `PowerUser.Activated` — `event System.Action<PowerDefinition>` | `Assets/Scripts/PowerUser.cs:14` |
| Player took damage / died | `WorldSession.PlayerDamaged` — `event System.Action<bool>` (bool = died) | `Assets/Scripts/WorldSession.cs:20` |
| Player respawned | `WorldSession.PlayerRespawned` — `event System.Action` | `Assets/Scripts/WorldSession.cs:21` |
| Current wanted level (drives ambient/music intensity) | `WorldSession.Heat` (float, 0..5) | `Assets/Scripts/WorldSession.cs` |
| Per-actor animation state — footsteps, punch, hit, death, jump, land, cast | `HumanoidPresentation.State` (string), `.HitCount` (int), `.DeathCount` (int) — all public getters. **Poll these; do not edit the file.** | `Assets/Scripts/HumanoidPresentation.cs` |
| Prop destroyed | the one event you are adding to `BreakableProp.cs` | — |
| UI clicks | UI Toolkit `Button` in `ModeScreens.cs` — **do not edit it.** Register callbacks from your own component by querying the panel, or accept that UI clicks are wired in a later pass and say so. | — |

`HumanoidPresentation.State` returns strings like `"Punch"`, `"Hit"`, `"Death"`, `"Jump"`,
`"Land"`, `"Cast"` and locomotion states. Footsteps are best driven from
`HumanoidPresentation.MeasuredSpeed` (public float) plus a distance-accumulator, **not** from
animation events (the project deliberately avoids fragile Animator events — see AGENTS.md on the
punch windup).

## What to build

1. **Sourcing.** Find and commit genuinely **CC0 / public-domain** audio. Freesound (filter
   License = "Creative Commons 0"), OpenGameArt (CC0 only), Kenney.nl (CC0, has a free impact/UI
   pack). **Verify each licence individually** — do not assume a pack is CC0 because the site
   mostly is. Write `Assets/Audio/ATTRIBUTION.md` listing every file: source URL, author,
   licence, and the date you downloaded it. Keep clips short and mono where they are positional;
   compress to `.ogg` for anything over ~1 second.

   Coverage needed: punch impact, footsteps (a few variants), flight whoosh / hover loop,
   landing thud, prop destruction / debris, fire blast, ice, telekinesis, cop gunshot, player hit,
   death, ambient city loop, and UI click/hover. A handful of well-chosen clips beats a large
   library — variation via random pitch/volume is expected.

2. **An `AudioDirector`** — one pooled, central component. Round-robin a fixed pool of
   `AudioSource` components (start with ~24). 3D/positional for world sounds, 2D for UI and
   music. Random pitch/volume jitter per cue so repeats do not sound robotic. Hard-cap
   concurrent instances **per cue type** so 30 simultaneous footsteps cannot blow out the mix.

3. **`AudioTuning.asset`** — a ScriptableObject holding every clip reference, volume, pitch
   range, jitter, max-concurrent and rolloff distance, following the pattern of the existing
   `GameTuning.asset` / `HumanoidAnimationTuning.asset`. Everything tunable from the Inspector,
   nothing hardcoded. `Assets/Editor/AudioSetup.cs` provides a menu command to create it, the
   same way `Assets/Editor/MenuPresentationSetup.cs` does for the menu.

4. **An AudioMixer** with groups: Master → Music, SFX, UI, Ambient. Route every source. Expose
   the group volumes as mixer parameters.

5. **Ambient city bed** whose intensity tracks `WorldSession.Heat` (calm at 0, sirens/tension at
   5). Use the existing float — do not invent a second wanted-level system.

## Verification — this project's standard, and it is strict

Read `STATUS.md`'s verification sections to see the expected bar. Automated Unity batch-mode
checks with **explicit positive AND negative controls**. Follow the existing pattern in
`Assets/Editor/MenuPresentationVerification.cs` + `Assets/Scripts/MenuPresentationVerificationRunner.cs`
(an `[InitializeOnLoad]` editor class with `SessionState`, `RuntimeInitializeOnLoadMethod` hooks
and `EditorApplication.Exit(code)`; a `MonoBehaviour` runner driving a coroutine stack).

Run it with:
```
cd /Users/melaniehernquist/Documents/ChatGPT/op
/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$(pwd)" -executeMethod AudioVerification.Run \
  -logFile "$(pwd)/Verification/Audio/unity-run.log"
```
Do **not** pass `-quit`; the runner exits itself. **No Unity Editor may be open on the project**
or the run fails on the project lock — check with
`pgrep -f 'projectPath /Users/melaniehernquist/Documents/ChatGPT/op'` first.

Write results to `Verification/Audio/results.txt`. Your checks must include, each with a control:

| Check | Required control (the negative case) |
|---|---|
| A real paid punch plays the punch cue | A **refused** punch (zero charges) plays **no** cue — this is the important one; the project's punch pays resources immediately but applies force after a 125ms windup |
| Player damage plays the hit cue | An **undamaged** player plays nothing |
| Prop destruction plays the destruction cue | A prop merely *damaged but not broken* does not |
| Each power activation plays its distinct cue | A **failed** activation (no energy) plays none |
| Ambient intensity follows Heat | Heat 0 vs Heat 5 produce **measurably different** values, asserted numerically |
| Concurrency cap holds | Fire N+10 cues at once, assert no more than N sources are playing |
| Every clip is actually assigned | Assert no null `AudioClip` in the tuning asset — a silent system that "passes" is the failure mode to guard against |

**Also measure and report the frame-time cost**: sample FPS in the populated city with the
AudioDirector enabled and again with it disabled, in the same run. Report both numbers. If audio
costs more than ~2 FPS, say so plainly rather than hiding it.

Assert on **actual `AudioSource.isPlaying` / assigned `clip` / `volume` state**, not on your own
bookkeeping booleans. A test that only checks your own flags proves nothing.

## Build gate

`dotnet` is **not installed** on this machine, so the `dotnet build Overpowered.Build.csproj`
command in AGENTS.md and STATUS.md **cannot be run**. Unity's own batch-mode compile is the
build gate — if your verification run compiles and executes, the build is good. Do not try to
install dotnet.

## When you are done

1. Confirm your verification exits **0** and `Verification/Audio/results.txt` contains real output.
2. **APPEND** a dated entry to `STATUS.md`. **Never overwrite or rewrite STATUS.md.** Add your
   section at the top of the file under a new heading following the existing house style: what
   you added, ownership boundaries, explicit cuts and limits, the verification output verbatim,
   and anything you did NOT verify. This project's history includes a stale STATUS.md causing
   real problems — be precise about what you actually proved versus what you assumed.
3. Commit to `feat/audio` and push. Do not merge to `main` — a human reviews it.
4. Report: what you built, the real verification output, the audio frame-time cost, the licence
   provenance of every clip, and anything that does not work or that you could not verify.

**Be honest about limits.** Batch-mode Unity can verify that sources play with the right clips at
the right volumes; it **cannot** tell you the game sounds good. Do not claim a mix quality or
"feel" judgement that no human has made. Say plainly that no human has heard it.
