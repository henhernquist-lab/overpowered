# Agent file scope

This repository is worked on by multiple agents in parallel, on separate branches, in one
shared checkout. Overlapping edits have already caused real problems on this project. This
document records the ownership boundaries so the next round of parallel work does not collide.

## Rules for every agent

1. **Do not edit files outside your assigned scope.** Not even "to make the build pass."
2. **Branch per packet.** One branch per agent; commit only to your own branch. Never commit
   or merge to `main` — a human reviews and merges.
3. **Read-only means read-only.** Reading files outside your scope is expected and encouraged;
   writing to them is not.
4. **Appended-only files:** `STATUS.md` is append-only. Add a dated section at the top in the
   existing house style. Never rewrite or reorder other agents' entries.
5. **Shared generated state:** `Library/`, `Logs/`, `obj/`, `bin/`, and `UserSettings/` are
   local build artifacts; never stage or commit them, and never delete them to "fix" a run
   another agent may be using.
6. **Editor locks:** Unity batch-mode runs take the project lock. Check for a running Unity
   process on this project before starting one; wait for it to finish. Never kill another
   agent's process.

## Exclusive ownership map (current round)

| Area | Owner | Branch |
|---|---|---|
| Audio system: `Assets/Audio/`, `Assets/Scripts/*Audio*`, `Assets/Resources/AudioTuning.asset` | grok-audio packet (`handoff/grok-audio.md`) | `feat/audio` |
| Encounter/mode data (`Assets/Resources/Encounters/`, `ModeRules/`, `Modes/`), `README.md`, `docs/`, STATUS append | content-docs packet (`handoff/glm-content-docs.md`) | `feat/content-docs` |
| Performance work: `Assets/Editor/PerformanceProfile.cs`, `Assets/Scripts/PerformanceProfileRunner.cs`, `Assets/Resources/CityArtSettings.asset`, `Assets/Resources/CityLayout.asset` | performance pass (see STATUS diagnosis entry) | `main` work packet |
| `Assets/Resources/Powers/*.asset`, `Assets/Resources/GameTuning.asset`, `Assets/Resources/MenuPresentationTuning.asset`, `Assets/Resources/CityPalette.asset` | performance pass | `main` work packet |
| `Assets/Scenes/` | performance pass | `main` work packet |

Everything under `Assets/Scripts/` and `Assets/Editor/` not named in a packet's grant is
**shared source**: no concurrent agent edits it; changes go through the performance pass's
branch or a new dedicated packet with an explicit grant.

## Current-round grant details (content-docs, this branch)

Allowed to create or edit, and nothing else:

- `Assets/Resources/Encounters/*.asset` (+ `.meta` for new files)
- `Assets/Resources/ModeRules/*.asset` (+ `.meta`)
- `Assets/Resources/Modes/*.asset` (+ `.meta`)
- `README.md`
- `docs/**`
- One dated append to `STATUS.md`

Explicitly out of scope for this packet: `Assets/Scripts/`, `Assets/Editor/`,
`Assets/Audio/`, `Assets/Scenes/`, `Verification/` inputs (the verifier writes its own
evidence), `Assets/Resources/AudioTuning.asset`, `CityArtSettings.asset`,
`CityLayout.asset`, `CityPalette.asset`, `GameTuning.asset`,
`MenuPresentationTuning.asset`, and everything under `Assets/Resources/Powers/`.

## If two scopes genuinely overlap

Do not resolve it silently. Record the conflict in your STATUS entry and leave the file
untouched; the human reconciles. A missing fix shipped late is cheaper than two agents
editing one asset.
