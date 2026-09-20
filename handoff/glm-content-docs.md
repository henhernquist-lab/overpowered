# WORK PACKET — Content data + documentation accuracy pass for "Overpowered"

You are adding **data-only game content** and doing a **documentation accuracy pass** on a Unity 6
superhero prototype. This packet is self-contained. Read all of it before starting.

**This task is deliberately scoped to be low-risk: you write ScriptableObject `.asset` data and
Markdown. You write no C# and no architecture.** That is intentional, not an oversight.

## Your branch

```
git checkout -b feat/content-docs
```

Commit to `feat/content-docs` only. **Never commit to `main`.** Two other agents are working in
this repo on other branches simultaneously. Staying inside your file scope is the single most
important rule in this packet — overlapping edits have already caused real problems on this
project.

## The project

- **Repo:** `/Users/melaniehernquist/Documents/ChatGPT/op` — Unity **6000.6.0f1**, macOS.
- Third-person superhero prototype: a seeded low-poly city, Hero and Villain modes, powers
  (punch, flight, fire blast, ice, telekinesis), a Heat/police wanted system, destructible props,
  civilian and cop NPCs, XP/level progression with a JSON save, UI Toolkit menus.
- Read `AGENTS.md` first — it is short and states the architectural rules.
- `STATUS.md` is ~290 lines and accurate. Read the top section; skim the rest.

## YOUR FILE SCOPE — exclusive, do not go outside it

You may create and edit **only** these:

```
Assets/Resources/Encounters/*.asset       (new + existing)
Assets/Resources/ModeRules/*.asset        (new + existing)
Assets/Resources/Modes/*.asset            (new + existing)
README.md
docs/**                                   (new directory)
```

Plus the matching `.meta` files for anything new you create.

**Files you must NOT touch** (owned by other agents):
anything under `Assets/Scripts/`, anything under `Assets/Editor/`, `Assets/Audio/`,
`Assets/Resources/AudioTuning.asset`, `Assets/Resources/CityArtSettings.asset`,
`Assets/Resources/CityLayout.asset`, `Assets/Resources/CityPalette.asset`,
`Assets/Resources/GameTuning.asset`, `Assets/Resources/MenuPresentationTuning.asset`,
`Assets/Resources/Powers/*.asset`, `Assets/Scenes/`, anything under `Verification/`, `STATUS.md`
(except the one append described at the end).

### HARD PERFORMANCE CONSTRAINT — read this twice

The game currently runs at roughly **26–40 FPS** and is under active performance investigation.
**Your content must not increase the number of rendered objects, props, or NPCs per frame.**

Concretely:
- **Do not** touch `CityArtSettings.asset` or `CityLayout.asset`. They control how many buildings
  and props get generated. They are explicitly outside your scope.
- **Do not** raise civilian counts, cop counts, or spawn counts in any encounter you write.
- New encounters must **reuse existing node/objective kinds** at counts comparable to the
  existing ones. Look at what `bank.asset`, `arson.asset` and `convoy.asset` already do and stay
  in that envelope.

Encounter and mode-rule data is a good fit for you precisely because it costs essentially nothing
to render. Content that adds draw calls would directly fight the performance work in flight, and
would be reverted.

## Task 1 — new encounter content

Existing encounters live in `Assets/Resources/Encounters/`: `bank.asset`, `arson.asset`,
`convoy.asset`. They are `EncounterDefinition` ScriptableObjects — read
`Assets/Scripts/EncounterDefinition.cs` and `Assets/Scripts/CrimeEncounter.cs` to understand
every field before you write any. **Read-only:** do not edit those `.cs` files.

Add **3–4 new encounters** that combine the existing mechanics in new ways. AGENTS.md is explicit
about this: *"Add definitions/rules rather than mode-ID switches."* You are adding data that the
existing systems already know how to run — you are not adding new mechanics.

Write the `.asset` files as Unity YAML, following the exact structure of the existing ones
(copy one and modify — match `m_Script` GUID and the serialization format precisely, or Unity
will not load them). Give each a distinct identity: different node layout, timing, reward
weighting, and Hero-vs-Villain framing.

## Task 2 — mode rules review

`Assets/Resources/ModeRules/Hero.asset` and `Villain.asset` drive win/loss conditions. Read
`Assets/Scripts/HeroModeRules.cs` and `VillainModeRules.cs` (read-only) to see which fields
actually do something. If you add encounters, make sure both mode rule sets reference them
appropriately. Do not invent new rule *types* — only data.

## Task 3 — documentation accuracy pass

`README.md` is ~9KB and was written across several milestones. **Verify every factual claim in it
against the code as it actually exists today**, and correct what is wrong. Specific things known
to be stale or worth checking:

1. **The build command is broken.** README/AGENTS.md/STATUS.md reference
   `dotnet build Overpowered.Build.csproj --no-restore -p:UseSharedCompilation=false`.
   **`dotnet` is not installed on this machine** and is not in any standard location. Document
   that Unity's own batch-mode compile is the current build gate, and note the dotnet command as
   historical. Do not try to install dotnet.
2. **Render pipeline.** Confirm and state plainly that this is the **Built-in Render Pipeline** —
   there is no URP, no render pipeline asset, no Renderer Features, and no custom shaders.
   (`Packages/manifest.json` has no URP package; `ProjectSettings/GraphicsSettings.asset` has
   `m_CustomRenderPipeline: {fileID: 0}`.) This has been a repeated source of confusion.
3. **Performance.** State the real measured range honestly (~26–40 FPS Editor throughput) and
   that it is under investigation. Do not quote the historical 155 FPS figure without noting it
   was a different, much simpler gray-box city.
4. Check that every file path, menu command and scene name mentioned in README.md actually exists.
   Fix the ones that do not. **Verify, do not assume.**

Then create `docs/` with:
- `docs/architecture.md` — a short, accurate map of the systems and which file owns what,
  derived from reading the code. Keep it under ~200 lines and factual.
- `docs/agent-scope.md` — the file-ownership boundaries between concurrent agents, so the next
  round of parallel work does not collide. This project has had real problems from three agents
  editing one repo; write the doc that prevents it.

**Accuracy over volume.** A short correct document is worth far more here than a long plausible
one. If you cannot verify a claim from the code, either leave it out or mark it explicitly as
unverified.

## Verification

You are writing data and docs, so your bar is different from a code agent's — but it is not zero.

1. **Your `.asset` files must actually load in Unity.** Verify with a real batch-mode run:
   ```
   cd /Users/melaniehernquist/Documents/ChatGPT/op
   /Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity \
     -batchmode -quit -projectPath "$(pwd)" -executeMethod ModeVerification.Run \
     -logFile "$(pwd)/Verification/Content/unity-run.log"
   ```
   **No Unity Editor may be open on the project** or the run fails on the project lock — check
   with `pgrep -f 'projectPath /Users/melaniehernquist/Documents/ChatGPT/op'` first, and if
   another agent's run is in progress, wait for it. Do not kill anything.
   A malformed `.asset` shows up as a null reference or a "script missing" error in the log —
   **read the log and confirm it is clean.** An encounter that silently fails to deserialize is
   the exact failure mode to guard against here.
2. **Control:** confirm the three PRE-EXISTING encounters still load and run after your changes.
   Adding content must not break what already worked.
3. For the docs: every path, command and claim you write must be one you actually checked.

## When you are done

1. **APPEND** a dated entry to `STATUS.md`. **Never overwrite or rewrite STATUS.md** — append a
   new section at the top under its own heading, matching the existing house style. State what
   content you added, what you corrected in the docs, your verification output, and explicitly
   what you did **not** verify (e.g. you have not playtested the new encounters — no human has).
2. Commit to `feat/content-docs` and push. Do not merge to `main` — a human reviews it.
3. Report: the encounters you added, the specific README claims you found to be wrong and how you
   corrected them, your verification log output, and anything you could not verify.

**Be honest.** If a README claim could not be checked, say so. If an encounter loads but you have
no way to know whether it is fun or balanced, say that too — no human has played this game yet,
and nobody should pretend otherwise.
