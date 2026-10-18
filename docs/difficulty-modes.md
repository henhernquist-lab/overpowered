# Difficulty implementation audit

Work in progress. Branch `codex/difficulty-modes`, baseline `1b0d6f9`. No completion claim.

| Multiplier | Normal | Hard | Extreme |
|---|---:|---:|---:|
| Player damage | 1 | .97 | .955 |
| Enemy health | 1 | 1.05 | 1.075 |
| Player health | 1 | .95 | .925 |
| Hostile spawn rate | 1 | 1.15 | 1.225 |
| Energy usage | 1 | 1.10 | 1.15 |
| Cooldown duration | 1 | 1.10 | 1.15 |
| Enemy outgoing damage | 1 | 1 | 1 |

Difficulty is a layer on existing modes, not duplicated mode assets. Selection is saved through PlayerProgression and copied into immutable session settings at startup. Normal preserves the corrected basic melee and TK baseline. Health rolls once after final wave and difficulty scaling; damage scales at final damage recipients. Enemy damage and individual attack cadence remain unchanged.

Recurring Heat police and mode encounter intervals divide by spawn rate. Endless currently fills available slots immediately, with no positive timer to shorten. Its zero-delay reinforcement behavior, intermission, composition, wave scaling and caps must stay unchanged to preserve Normal. Difficulty can raise Endless durability and resource pressure, but cannot make an already immediate refill faster. Scripted objective counts remain authored.

Verification and implementation details will be added as work completes.
