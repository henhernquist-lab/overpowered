# Overnight continuation queue

Recovered from Henry's pasted Claude handoff on 2026-09-27. The requests below remain the work contract.

## Current checkpoints

- Phase 0: already committed before Codex continuation.
- Phase 1: Sidekick heroes and optimized bodies merged on main (`f3040c7`); fresh Run + separate-process Reload passed (`f19c9fa`). NPCs remain mannequins after measured Sidekick cost.
- Cloud integration: isolated `integrate/cloud-1` worktree. Fixes under validation; do not enable missions or merge until required checks pass.
- Phase 2: Built-in Unity Toon Shader work on `codex/toon` in the former wt-sidekick checkout; package resolution underway.
- Phase 3: Animation Rigging look-at / Telekinesis hands, DOTween HUD, pooled Cartoon FX. Original local-agent notes establish this scope; implementation still pending.
- Phases 4–9: pending in the order below. Free Play and Endless already exist, but the audit must assess their current post-Forge behavior, not historical tests alone.
- Push: unavailable from command-line Git (no usable GitHub credential). Local main checkpoints preserved.

## Original requested queue

OVERNIGHT QUEUE. Henry is asleep until morning. Do not stop to ask questions. When a decision is needed, make the most reasonable call, log it in STATUS.md under "OVERNIGHT DECISIONS", and keep going. Only stop for something irreversible (deleting user work, force-pushing, removing a package that's referenced). Commit and push main after every phase so usage limits never cost progress.
Finish your current phases (0-3) first. Keep integrating the cloud branch between phases as already instructed. Then continue in this order:
=== PHASE 4: FREE PLAY + ENDLESS FIGHT AUDIT ===
These were scoped weeks ago. Check git history and the code: are both modes fully built, partial, or missing? Report honestly. Finish anything partial using the existing GameMode architecture. Free Play: no objectives, full city, all powers. Endless Fight: escalating waves, score + best score saved via the existing save system. Verify both flows home -> mode -> results -> home.
=== PHASE 5: ICE READABILITY ===
Frozen-cyan is too close to the teal some NPCs wear. Don't just recolor: add a colorblind-safe indicator on frozen targets (a small frost icon above the head and/or an outline pulse) so frozen is readable by shape, not only color. Apply it to every freeze source, including synergies that freeze.
=== PHASE 6: LIGHTING ===

Day/dusk/night cycle, tunable length. Hero Mode defaults to daytime, Villain Mode to dusk/night (reinforces the blue vs orange identity).
Night needs to stay readable: streetlights on, lit windows, emissive signs. Don't make the game dark and muddy.
Power lights: Fire, Lightning, and Laser Eyes cast a short-lived colored point light on cast/impact. Pool them, cap how many exist at once.
Built-in RP only. Verify with screenshots at each time of day and FPS at night with many lights active.

=== PHASE 7: CINEMACHINE + TIMELINE (both already installed, currently unused) ===

Hero Forge: camera orbit/swoop around the assembled hero when a loadout is picked.
Combat camera: move the third-person camera onto Cinemachine only if it keeps everything already working. After switching, re-run the aim-accuracy suite (Fire Blast, Ice, Telekinesis, Laser Eyes at 15m and 22m), first-person toggle, and hit-pause/camera-kick feel values. If aim accuracy regresses at all, revert this part and say so.
A short (~10s) Timeline intro on first launch that sets tone before the home screen: skyline flyover, hero landing. Skippable. Seen-state stored in the existing save.

=== PHASE 8: SPLINES ===
Replace random civilian wandering and cop patrol with Spline-based routes along sidewalks/boulevards per district, so movement looks intentional. Keep the distant-NPC cheap-AI system working. FPS check in the densest district.
=== PHASE 9: NEW POWERS FROM CLOUD ===
Once cloud's new-powers work merges (Darkness, Laser Eyes, Lightning, Force Field, Speed, Poison):

Verify every new power equips and works in Hero Forge, same standard as the original 5.
Presentation work cloud can't do: Laser Eyes needs a real beam VFX + channel pose; Force Field needs a visible shield + block pose (procedural blend is fine, like Flight's tilt).
FPS with all 11 powers in a real fight.

=== MORNING REPORT ===
End with a STATUS.md entry written for Henry to read first thing: what finished, what's partial, what failed and why, what needs his playtest, and the exact next step. Push main. send to local right?

