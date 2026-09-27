# Overnight presentation packages

Phase 3 uses the existing Animation Rigging, DOTween and Cartoon FX packages in Built-in RP. Run `PresentationSetup.Configure` to regenerate owned prefabs under Resources/Presentation. Vendor prefabs and Side_Kick_Data.db are unchanged. Repeating setup produced byte-identical Resources assets and metadata.

## Rigging

SharedHumanoid.controller remains the clip owner. MultiAim adds restrained head awareness; TwoBoneIK aims both hands toward the telekinesis-held body. Constraints live beneath Animator.avatarRoot, with target objects outside the animated skeleton. This is essential for native animation stream binding on the Sidekick skeleton. No root motion or physics-root animation is added.

Look weight, cone, distance, response and hand reach/spread live in HumanoidAnimationTuning.asset. Gaze yields to first-person and flight overlays. Actions, death, hit reactions and frozen/fleeing NPCs suppress the rig. Far NPC presentation disables the RigBuilder graph; promotion rebuilds it. Custom prefabs without an authored rig retain their original Animator presentation. Sidekick NPCs remain OFF; all four optional species have owned authored rig prefabs for future use.

Tests require actual hand alignment as well as a valid graph, and treat native PropertyStreamHandle exceptions as failures. Initial attempts exposed constraints outside avatarRoot; moving them fixed the real binding failure. Two later fixture failures were corrected: the first-person check had MenuOpen=true, and a helper returned Home before its outer test accessed the session. These were not camera or gameplay regressions.

## UI and effects

DOTween handles the existing HUD banner fade/slide with an unscaled sequence, killed on expiry/destruction. Existing banner priority, hold times, layout, alert exclusion and crosshair clearance remain authoritative. Gameplay clocks are unchanged.

Heavy impacts selectively use the existing CFXR2 Ground Hit prefab through a four-slot pool. Its vendor scripts and point lights are disabled, materials remain shared, particles are capped, and bursts are throttled to 180ms. The existing Feel debris pool remains active for light hits, landings, breaks and heavy-hit debris. There is no second camera-shake system.

## Evidence and limits

Verification/Presentation contains current captures and logs without replacing historical verification. Three heroes exercise rigging, death, hits, flight, first person and held-object alignment; BackflipHurricane verifies the attack clips and timing on all three. Sidekick, FirstPerson, HUD phases 1–3 and Feel run with positive/negative controls and separate-process reloads where supported.

Feel's interleaved heavy-impact stress, 1920x1080 gameplay camera plus HUD: effects ON 316.2/300.7 FPS, OFF 344.2/336.6 FPS; means 308.4 vs 340.4 FPS (-9.4%, approximately +0.30ms/frame). ON draws 234/233 vs OFF 225/226. Four Cartoon slots peak active; materials 91→91 and ParticleSystems 22→22. This compares combined Cartoon and existing debris against both OFF; it does not isolate Cartoon cost or measure rigging A/B. Editor throughput is not player-build performance. No human animation/effect feel review is claimed.

Final live-capture Feel rerun: 143 PASS, 0 FAIL. ON 317.4/321.9 FPS vs OFF 341.0/337.2; means 319.7 vs 339.1 (-5.7%, +0.18ms/frame). Same pool/draw bounds. Both runs retained; throughput variability is not an isolated effect-cost result. Live heavy-impact capture inspected: an amber impact flash supplements the existing cube debris.
