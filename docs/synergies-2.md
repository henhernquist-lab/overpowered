# Synergies 2.0 — Signature Ability Architecture & Handoff Guide

## Overview

Synergies 2.0 replaces pair-specific combination synergies with **primary-owned signature abilities**:

- Every primary combat power (`PowerRole.Combat`) owns **one unique signature ability**.
- A superhero equips **2 Primary Powers + 1 Secondary Power + 1 Equipped Synergy**.
- The two equipped primaries determine which **two signature abilities** are available in Hero Forge; the player selects **one** of those two.
- Signature abilities do **not** require a specific second primary power.

The final-four implementation, current tuning, acceptance evidence and human checklist are in [synergies-2-final-four.md](synergies-2-final-four.md). The extension guide below remains as architecture reference.

### Roster Status

| Primary Power (`Id`) | Signature Ability (`Id`) | Status | Effect Class |
|---|---|---|---|
| `strength` | `seismic-slam` (Seismic Slam) | Implemented (Phase 1) | `SeismicSlamEffect` |
| `fire` | `solar-flare` (Solar Flare) | Implemented (Phase 1) | `SolarFlareSignatureEffect` |
| `ice` | `glacial-prison` (Glacial Prison) | Implemented (Phase 1) | `GlacialPrisonEffect` |
| `lightning` | `thunderstorm` (Thunderstorm) | Implemented (Phase 1) | `ThunderstormEffect` |
| `telekinesis` | `meteor-crush` (Meteor Crush) | Implemented (Phase 2) | `MeteorCrushEffect` |
| `darkness` | `abyssal-grasp` (Abyssal Grasp) | Implemented (Phase 2) | `AbyssalGraspEffect` |
| `laser-eyes` | `prism-barrage` (Prism Barrage) | Implemented (Phase 2) | `PrismBarrageEffect` |
| `poison` | `plague-bloom` (Plague Bloom) | Implemented (Phase 2) | `PlagueBloomEffect` |

---

## Transitional Feature Gate (`SignatureCatalog`)

`Assets/Resources/SignatureCatalog.asset` (`SignatureCatalog.cs`) controls when Synergies 2.0 replaces the legacy pair-synergy catalog (`ForgeCatalog.Synergies`):

- `DeveloperPreview` (`false` on disk): when enabled in the Inspector or by tests in Editor/dev builds, enables the signature system immediately for implemented primaries.
- `EnableWhenComplete` (`true` on disk): automatically enables the signature system for all builds as soon as `SignatureCatalog.Complete` is `true` (all 8 `PowerRole.Combat` powers in `Resources/Powers` have a unique implemented `PowerSynergyDefinition` in `SignatureCatalog.Abilities`).
- All eight mappings now exist. With the authored `EnableWhenComplete` gate enabled after integration verification, normal gameplay uses signatures without DeveloperPreview. If the gate is explicitly off or a registry is incomplete, the existing legacy resolution remains available as a safe fallback.

---

## Handoff Guide for Gemini 4 Argon (Implementing Signatures 5–8)

### 1. How to Implement Activation Behavior
Create a `SynergyEffect` subclass under `Assets/Scripts/Forge/`:
- Override `bool CanBegin(SynergyRunner r)`:
  - Validate targeting/preconditions before spending energy or cooldown.
  - Set `r.Target` (or `r.Captive`) if needed. Return `false` to refuse cleanly with no energy or cooldown spent.
- Override `IEnumerator Execute(SynergyRunner r)`:
  - `SynergyRunner.TryActivate()` has already verified ownership/eligibility, spent `r.EnergyCost`, started `r.Cooldown`, ended any active channel/grab, and rolled `r.ActivationDamage = r.User.RollDamage(d.Damage * r.User.HeroStats.PowerDamage)` **once** for the activation.
  - Trigger cast animation via `r.User.GetComponent<HumanoidPresentation>()?.Cast()` (or `r.User.Hero.PresentPunch()`).

### 2. How to Configure Cooldowns, Energy, and Damage
Create the effect asset in `Assets/Resources/Forge/SignatureEffects/<id>.asset` and the `PowerSynergyDefinition` asset in `Assets/Resources/Forge/Signatures/<id>.asset`:
- Set `Id` (`meteor-crush`, `abyssal-grasp`, `prism-barrage`, `plague-bloom`) and `DisplayName`.
- Set `OwnerPrimary` to the corresponding `PowerDefinition` in `Assets/Resources/Powers/`. Leave `PowerA` and `PowerB` empty (`{fileID: 0}`).
- Set `Cooldown` (typically `22–28s`), `EnergyCost` (typically `25–35`), `Damage`, `Force`, `Radius`, `Range`, `Duration`, `Primary`, and `Secondary` (`CityColor`).
- **Damage & Difficulty Integration**:
  - Always derive hit/tick damage from `r.ActivationDamage` (never call `RollDamage` inside a tick loop).
  - Apply damage via `npc.Damage(amount, r.User, assault, impulse)` (or `r.Impact(pos, multiplier)`). `CityNpc.Damage` applies `r.User.ResolveDamage` (`Difficulty.PlayerDamage`) once and publishes `NpcDamageResult` to the HUD damage numbers and health bars.
  - Always check `npc != null && !npc.Dead && npc.Hostile` so civilians and friendly police are never harmed.

### 3. How to Integrate VFX
- Reuse `SignatureVfx.Get()` (`Assets/Scripts/Forge/SignatureVfx.cs`), `PowerVfx.Get()`, `r.Vfx.Burst(...)`, and `FeelDirector.Instance?.Particles.Burst(...)`.
- Pre-allocate any new pooled `LineRenderer`s or shared-primitive transforms inside `SignatureVfx.Build()`, using `CityMaterials.Get(swatch)` or `CityMaterials.GetVfx(swatch)`.
- Add cleanup to `SignatureVfx.HideAll()` and `finally` blocks in `Execute(SynergyRunner r)` so `r.Cancel()` never leaves visuals active.

### 4. How to Register a New Signature & Expose the Complete Roster
1. Append the 4 new `PowerSynergyDefinition` assets (`meteor-crush`, `abyssal-grasp`, `prism-barrage`, `plague-bloom`) to `Abilities` in `Assets/Resources/SignatureCatalog.asset`.
2. Once all 8 combat primaries have a unique valid entry in `SignatureCatalog.Abilities`, `SignatureCatalog.Complete` automatically evaluates to `true`.
3. With `EnableWhenComplete: 1` already set on `SignatureCatalog.asset`, `SignatureCatalog.Enabled` becomes `true` automatically! (Note: check legacy tests like `SynergyAvailabilityVerificationRunner` or `RosterVerificationRunner` if they assert legacy pair behavior when `SignatureCatalog.Enabled` flips on by default).

### 5. How to Test a New Signature
Run `Signatures2Verification.Run` and `Signatures2Verification.Reload` via `tools/verify-sequential.py`:
```bash
python3 -u tools/verify-sequential.py --combat-roll 0.5 --out Verification/Synergies2/run1 \
  Signatures2Verification.Run Signatures2Verification.Reload
```
