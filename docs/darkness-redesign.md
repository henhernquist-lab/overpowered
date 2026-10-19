# Darkness redesign — 2026-10-19

Branch: `gemini/darkness-redesign`. This report describes the committed source through `82d7c2f`. The earlier behavior and health baseline were recorded in `STATUS.md` before implementation.

## Behavior and tuning

Before: one target, two base charges, five-second recharge, 12 mean direct damage (10.8–13.2 with the current ±10% roll), and a 3.5-second full movement root. There was no slow or damaging aura; upgrades added charges.

Now: an authoritative crosshair hit curses the primary hostile. A bounded search can add the nearest valid hostile within **7 m** of the primary, subject to line of sight and the power's **22 m** range. One cast affects at most **two** hostiles; walls, civilians and allies remain ineligible. A cast without a valid target pays no charge or energy. The power has **one charge** at every upgrade tier, **9 s** base recharge, 15 energy cost and the existing 0.6 s activation cooldown. Difficulty's central recharge modifier gives **9/9.9/10.35 s** on Normal/Hard/Extreme, applied once.

Direct damage has a **6.5 HP mean**, **5.85–7.15 HP** Normal range under the existing ±10% roll, 10% of the current Gunner's 65 HP mean. The curse lasts **4 s**: **1.1 s root**, then **60% movement-speed reduction** (speed factor 0.4). Ranged and melee attack rules remain active; the effect restricts movement rather than silencing attacks.

Each cursed enemy emits a **4.5 m** hostile-only aura every **0.75 s**. A pulse has **5 HP mean**, **4.5–5.5 HP** Normal range. Five pulses during a full curse yield **25 HP mean** from one source, **38.46%** of mean Gunner health. The current zero-Heat Normal health ranges used for tuning are Gunner **58.5–71.5**, Rusher **35.1–42.9**, Brute **175.5–214.5** HP. A source never hits itself; two nearby cursed enemies hit each other, and a third hostile inside both auras takes one pulse from each distinct source. A fresh Gunner cannot be killed by one direct hit plus the other cursed enemy's full mutual aura alone: 6.5 + 25 = 31.5 mean against at least 58.5 HP. A third hostile under two auras can take 50 mean over full exposure. The central `CityNpc.Damage` path supplies the resolved difficulty modifier, health bar and damage number for each hit; there is no Darkness-specific difficulty multiplier or recursive spread.

Pooled activation tendrils, short body bands, narrow radius pulses and victim lashes provide the visible cues. The first-person cast uses the existing view-aware crosshair ray and keeps the snap near the victim, away from the camera. Aura overlap queries run at pulse time in bounded nonalloc storage; no per-tick materials, line objects or particle systems are made.

## Source and checkpoints

Game and verification files changed: `Assets/Resources/Powers/darkness.asset`, `Assets/Resources/Effects/Darkness.asset`, `Assets/Scripts/Powers/DarknessEffect.cs`, `Assets/Scripts/CityNpc.cs`, `Assets/Scripts/Powers/StatusPowerVfx.cs`, `Assets/Editor/DarknessVerification.cs` and its `.meta`, `Assets/Scripts/Powers/DarknessVerificationRunner.cs` and its `.meta`, `Assets/Scripts/Powers/RosterVerificationRunner.cs`, `Assets/Scripts/Powers/CombatBalanceVerificationRunner.cs`, `Assets/Scripts/Powers/PowerPolishVerificationRunner.cs`, `Assets/Scripts/Powers/EnemyHealthFeedbackVerificationRunner.cs`, `Assets/Scripts/StatusEffectVerificationRunner.cs`, `Assets/Editor/StatusEffectVerification.cs`, and `STATUS.md`. The Poison/Speed fixture updates wait for the actual physical Poison hit and select Speed through its legal secondary slot; they do not bypass gameplay gates.

Durable source checkpoints: `c938f9e` targeting, `65ab23e` movement, `a3cd727` aura/VFX, `519425e` dedicated verification, `82d7c2f` stronger regression controls. This document and selected evidence are a separate checkpoint.

## Actual verification

Selected final successful runs, each from a fresh **serial Unity process**, with exit 0 and unchanged personal-save and Sidekick database hashes:

| Evidence directory under `Verification/Darkness/` | Entry | PASS / FAIL |
| --- | --- | ---: |
| `regression-r3` | `DarknessVerification.Run`; separate-process `Reload`; `StatusEffectVerification.VerifyExisting` | 53/0; 5/0; 27/0 |
| `roster-r6` | `RosterVerification.VerifyExisting` | 221/0 |
| `regression-r2` | `CombatBalanceVerification.Run`; `PowerPolishVerification.Run` | 2346/0; 134/0 |
| `regression-r5` | `EnemyHealthFeedback`; `Combat`; `PowerPayoff` | 69/0; 176/0; 99/0 |
| `regression-r5` | `FirstPerson`; `Camera`; `Feel`; `HeroForge`; `Hud` | 211/0; 45/0; 143/0; 136/0; 308/0 |
| `regression-darkness-branch` | `Difficulty` Normal/Hard/Extreme; `Mode`; `EndlessEvents` | 1415/0; 79/0; 42/0 |

The dedicated run checks one/two/three targets, wall and civilian exclusion, empty aim, measured root/slow/restoration against an uncursed mover, self/outside/civilian aura exclusion, entry/expiry timing, mutual and double-aura damage, actual first-person aim and activation origin, one charge and all three recharge boundaries, and a dense 12-hostile cast. Screenshots: `Verification/Darkness/one-target.png`, `two-targets.png`, `first-person.png`. `regression-r5` was partly recorded while the shared checkout was briefly on a concurrent Laser branch with a separate Laser buff; the Darkness source was the same. Difficulty was rerun and passed on the requested Darkness branch after that collision. Historical failed attempts remain in their original manifests; obsolete fixture assumptions were corrected without weakening target, payment or damage gates. The selected rows above are **not** a claim that every historical run passed.

The source compiled with **0 warnings / 0 errors** in `Verification/Darkness-extended-build.txt`. A later final-build attempt could not start because this shell lacked the `dotnet` executable; no fresh build result is claimed from that attempt.

## Performance and remaining judgment

The previous night-city PowerPolish sample was **76.30 FPS / 13.106 ms idle** and **73.96 FPS / 13.520 ms with Darkness active**. The new sample was **81.99 FPS / 12.197 ms idle** and **72.94 FPS / 13.709 ms with Darkness active**. Active frame time increased **0.189 ms** across these short sequential Editor samples; idle changed too, so this is not a controlled standalone delta. The dedicated dense-12, 180-frame real-cast sample measured **173.49 FPS / 5.764 ms**, **2341 B/frame whole-frame GC**, fixed **113→113 loaded materials**, **121→121 lines**, **22→22 particle systems**, and at most two aura sources. Different scenes and instrumentation make the dense sample incomparable with the city-night sample. It does not isolate aura allocations. Long-running standalone and dynamic spawn-pressure performance remain unverified.

Human playtesting still needs to judge whether one charge and nine-second recharge feel useful; whether the nearest eligible second target feels predictable; whether 1.1-second root, 60% slow, 4.5 m radius and mutual aura strength read well; and whether Darkness is worth choosing over Ice, Poison or Lightning. Automated results establish implementation behavior, not that it feels balanced. The broader overnight queue and shipping mission gates are not declared complete here.
