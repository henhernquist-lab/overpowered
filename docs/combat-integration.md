# Combat integration into the playable game — 2026-10-23

The developer requested that the two enemies be put into the current game. Merge `767ee20` combines combat delivery `76b17fd` with playable baseline `536c73f`; it preserves the parkour courses and existing Home BATTLE LAB route. The combined commit is applied to `codex/synergies-2-final-four` in `/Users/melaniehernquist/Documents/ChatGPT/op-overnight-recovery`. No push.

Skirmisher and Bulwark are both in the Home **BATTLE LAB** enemy selector. Both Hero and Villain **Endless Fight** use the full wave table: Skirmisher from wave 3, Bulwark from wave 5, with Rusher/Gunner/Brute retained. Campaign encounters remain encounter-based and Free Play receives no automatic waves.

Native Unity 6000.6.0f1: **828 PASS / 0 FAIL across 9 serial processes**, all exit 0, personal-save/Sidekick database hashes unchanged.

| Entry | PASS / FAIL | Seconds |
| --- | ---: | ---: |
| `BattleLabVerification.Run` | 38 / 0 | 52.51 |
| `CombatLabVerification.Run` | 369 / 0 | 49.58 |
| `CombatLabVerification.Reload` | 4 / 0 | 26.04 |
| `ParkourVerification.Run` | 84 / 0 | 27.26 |
| `ParkourVerification.Reload` | 5 / 0 | 25.47 |
| `ModeExpansionVerification.Run` | 179 / 0 | 87.39 |
| `ModeExpansionVerification.Reload` | 10 / 0 | 25.71 |
| `HeroForgeVerification.Run` | 136 / 0 | 61.32 |
| `HeroForgeVerification.Reload` | 3 / 0 | 24.23 |

The Home verifier now requires all five definitions and actually spawns/defeats each new type after clicking the real Home route. Lab behavior/guard/evasion and isolated reload, parkour records/rewards/reload, live wave composition and kill progression, and Forge persistence all pass. Runtime and Editor assemblies also compile with the three existing analyzer warnings; 31 guard policy and 80 data/baseline checks pass.

Merge resolutions retain five lab definitions, one common progression normalization method (validates transient loadouts while their null SavePath prevents writes), existing practice comments and both complete STATUS histories. GameFlow/Home UI, build settings, the lab scene, parkour code/data and universal movement are preserved. Exact changed-production and preserved-feature hashes are in `selected-evidence.json`. Fresh regression artifacts from 17 paths are archived here; previous evidence was restored by hash. Raw native logs remain local and ignored. `manifest.json` and per-process check/observation files are fresh integration evidence; the older 6,161-check combat delivery remains historical evidence for its own source hashes.

The original checkout had substantial uncommitted work. Incoming overlap is limited to .gitignore and the already-identical PoisonProjectilePool metadata. Transfer preserves the local ignore additions and metadata GUID; its final audit is recorded separately. No unrelated local edits are committed.

Human feel/balance, physical input and sustained standalone performance remain unverified. The short Editor performance observations are retained in each process's observations and archived results; they are not a controlled before/after benchmark. Known indirect-hit origin and moving-obstacle limitations remain documented in `docs/combat-lab.md`.
