# Shared frozen-state silhouette

A six-ray branched snowflake floats above frozen NPCs and props. Cream foreground and a navy geometric border use CityPalette's shared materials, so the state reads by shape against cyan/teal surfaces. The pool owns one shared mesh and 64 startup markers; it grows only when a previously unseen simultaneous peak exceeds that reserve, preserving an indicator on every frozen target. All palette material assets are resolved at startup. No per-freeze material is created.

## One implementation for every freeze source

- `CityNpc.Freeze(duration)` is the only NPC freeze entry point and always enters the shared `FrozenLook` (Cyan palette material, held Animator + presentation overlay, pooled frost glyph). Callers in the current main catalog: direct Ice (`IceEffect`), Thermal Shock and Frostwake (`SynergyRunner.AffectNearby`), Glacier Fist (`SynergyRunner.ModifyMelee` -> `AffectNearby`), Cryo Crush (`LiftSlamEffect` with `Freeze=1`). A new source that calls `Freeze` gets the presentation by default.
- `FrozenBody.Apply` is the only prop freeze entry point (direct Ice only; no synergy freezes props) and enters the same `FrozenLook`.
- `Freeze(duration, ice:false)` is the one explicit opt-out: police suppression in crime encounters (`CrimeEncounter`, 0.6 s every 1 s) reuses the same gameplay hold but is not ice. Before this opt-out, the shared look made suppressed robbers flicker Cyan with a snowflake. Gameplay is unchanged: `Frozen` still reports true for a suppressed NPC exactly as before; `IceFrozen` is the presentation condition.

`FrozenLook` owns the marker, material snapshot, Animator speed and presentation-overlay hold. Thaw, death, destruction, disabling and session unload release markers; a marker is only released/reported by the look it is currently rented to. Frozen props restore exact prior Rigidbody constraints, including a disable/re-enable cycle.

## Verification

`FreezeReadabilityVerification.Run` (evidence `Verification/FreezeReadability/`):
- direct Ice on an NPC and a Teal physics prop, Thermal Shock, Frostwake, Glacier Fist and Cryo Crush, each through the real ownership/equip and synergy gates in its own Free Play session; the test also asserts that exactly these four synergies are the freezing ones in the current catalog;
- an unfrozen Teal cop beside the frozen all-Cyan target (cops carry a Cyan joint accent by design, so colour alone is ambiguous);
- rendered visibility of the same glyph in front of a Cyan wall and a Teal wall (glyph on/off frames, luma contrast of the changed pixels) plus palette WCAG contrast of cream/navy against Cyan and Teal;
- non-ice suppression control, off-axis and far controls, marker reuse without growth, material count, thaw, disable/re-enable, death, destruction and session cleanup.

Initial strict material-count runs failed at 108→109: the newly loaded object was the vendor shared `cfxr proc ring local space add` material, first resolved by Thermal Shock's heavy-impact effect. Cartoon pool startup now resolves all shared renderer material assets. The repeat measured markers 64→64, zero growth and materials 109→109. This was a lazy shared-asset load, not a newly instantiated freeze material.

Limits: rendered batch evidence does not substitute for human readability review; no isolated dense-freeze benchmark. Pool growth allocates a marker at a new peak (never a material); ordinary reused freezes allocate no new renderer/mesh/material. The glyph uses the lit Standard palette material, so its brightness follows scene lighting (relevant to the future night pass). The cloud branch will cut the catalog to five synergies; re-run this verifier after that merge.
