# Two modes and guaranteed Pulsebreaker encounters

Branch: **codex/two-modes-guaranteed-pulsebreaker**. Baseline: **297eb49**, including the completed three-hero modular art integration and committed Pulsebreaker. Worktree: /Users/melaniehernquist/.codex/worktrees/two-mode-pulsebreaker/op-overnight-recovery. No push or merge into any other branch or checkout.

## Why the boss was not appearing

Two verified repository facts explain the reported absence. The original playable checkout is still **codex/vector-modular-customization at eb5290c**, which predates Pulsebreaker **3301e39**. Also, the earlier Pulsebreaker implementation committed the brain, archetype, tuning, event recipe and HUD, but **the shipping EndlessWaves.asset had no Events reference**. A normal Endless session therefore built no wave plan and never selected a boss slot. Its earlier round-spawning verifier explicitly cloned a director and assigned the events asset, so that passing test did not establish the shipping wiring.

The completed **44acb44** spawn-curve branch fixes that data connection and supplies the planned five-enemy curves and reduced boss-round regular budget. Reviewed code/data and the corresponding regression contracts were ported into this isolated branch without modifying or merging the source worktree. No dummy boss or new prefab registration was needed: CityNpc constructs the real actor from the registered EnemyArchetype, and PulsebreakerBoss owns its actual attack state machine.

## Player-facing modes

Home now has exactly **ENDLESS FIGHT** and **FREE PLAY** cards. Hero Forge remains a separate character-building control, with all three difficulties retained. Additional mode chips, campaign cards, Coming Soon routes and the Home Battle Lab route are removed. Battle Lab remains reachable through its existing developer Editor tooling/API; it is not a third player-facing choice.

GameModeDefinition.ShowOnHome controls presentation. The five existing mode asset IDs remain loadable and playable internally. Hidden legacy results cannot re-enable their old Play Again choice. The single Endless choice uses the saved allegiance at entry and locks it for that run; its regular opponent role remains hostile to that allegiance. Free Play retains existing H allegiance switching, open-world population, Heat, police and parkour, without a director, forced encounters, mandatory rounds or automatic bosses. There is no new mode-ID dispatch in runtime wave logic.

## Guaranteed boss accounting

The shipping schedule is **round >= 5 && round % 5 == 0**: 5, 10, 15, 20, 25 and onward. MinibossEvery is serialized as 5 in the existing events asset; this is deterministic, independent of random regular-enemy weights. Each plan has one boss slot. The boss trades three regular spawns out of the round budget and costs four existing alive-budget units. The existing progressive wave sizes, health/damage growth, weighted five-enemy rotation and MaxAlive 12 remain.

The boss is admitted first. The normal alive-count cap cannot silently spend its reserved slot on a regular; the cap=1 negative control still admits one boss. Spawned/accounting only advances once an actual brain is attached successfully. One static active boss is enforced, and attaching the same body is idempotent.

A boss attempt tries at most three rings, with 16 shipping candidate directions each (configured directions are clamped 1–64). Candidates require minimum player distance, ground height within 1m of the arena, a complete NavMesh path back to the arena and full boss-capsule clearance. A distant player is used as the boss ring centre so a new encounter is not needlessly stranded at the original arena. Regular enemies keep their existing ring path. Failed attempts retry at .5 seconds by default; a reserved-slot diagnostic/message appears at most every five seconds. A permanently blocked arena stays pending instead of awarding a fake defeat. Finite work per attempt is guaranteed; the system keeps retrying until a safe location becomes available.

Unexpected actor destruction decrements spawned count as before, but now the missing boss slot is explicitly reconstructed as **Pulsebreaker**, rather than letting the old spawn index replace it with a regular. Boss death must come through authoritative CityNpc.Damaged to set BossDefeated, pay its kill/score once and permit wave completion. Regular deaths alone cannot clear its round.

CityNpc still applies enemy health difficulty at spawn once. Boss damage remains an explicit unscaled per-wave mean until the existing shared player-damage boundary. No extra difficulty multiplier, immunity or combat-stat rewrite was added.

## Health bar

The existing GameHudBoss panel is reused, built once, centred near the top below round stats. It has a dark palette background, crimson health fill, PULSEBREAKER, actual numeric current/max HP and visible phase indicator. Overdrive at the real configured <=40% threshold reads **OVERDRIVE · PHASE 2**, with the existing Fire accent.

The HUD binds the existing target's **CityNpc.Damaged** event, reads CurrentHealth/MaxHealth directly, updates even hits smaller than the former .25%-of-health threshold, and observes active target/max-health/lifecycle changes. It never predicts damage or animates an invented health percentage. Subscriptions are replaced and removed with the target/HUD. It remains visible for the living encounter beyond the former 90m cutoff, works in both camera views, hides on pause/menu/session end and immediately on real death, and cannot remain pinned to a despawned actor.

The existing banner, XP-popup and waypoint avoidance logic now includes the boss rectangle. Level-up/milestone banners are laid out below it. This corrects the captured pre-fix banner overlap without suppressing the notification. Width is capped to 80% of the panel, with tested 1280x720 and 1920x1080 bounds.

## Compatibility and protected systems

No destructive save migration. ProgressSave remains Version 1; legacy IDs/records, hero identity, independent outfits/colors, XP/levels/upgrades, powers/secondary/signature, difficulty, combat and parkour records remain in the existing reader. The new menu visibility fields live on mode definitions, not the progression save. No movement physics, power/signature mechanics/tuning, hero art, skeletons/Avatars/controllers, city batching, vendor source or render-pipeline migration.

All five regular enemies still use the completed weighted curve: Rusher from 1, Gunner from 2, Brute and Skirmisher from 3, Bulwark from 5. The original Composition table remains the fallback. Existing event options remain available; the imported shipping data delays elite/modifier eligibility to round 99. They are not claimed disabled forever. Pulsebreaker is outside the five-enemy probability pool.

## Commits and changed files

- **6ee8a28**: two-mode menu/data, completed spawn curve/data connection, boss priority/retry/defeat accounting and authoritative health UI.
- **8e492df**: deterministic shipping-path, loss/retry/cap, health/layout and save fixtures; aligned curve/event simulator contracts.
- **59a2465**: actual encounter camera framing and existing briefing-dismissal path for captures.
- **c989c14**: boss keep-out layout, distant encounter and corpse-lifetime controls; bounded serial-launch shutdown grace. The report/evidence commit is the branch HEAD.

Product files: Assets/Scripts/GameModeDefinition.cs, ModeScreens.cs, GameFlow.cs, EndlessWaveDirector.cs, EndlessWaveEvents.cs; Enemies/EnemyCompositionCurve.cs; GameHud.cs; Hud/GameHudBoss.cs; Hud/GameHudFeedback.cs and Hud/GameHudGuidance.cs. Data: the five existing Resources/Modes assets and Resources/ModeDirectors/EndlessWaves.asset / EndlessWaveEvents.asset. Editor authoring/test/development controls: Assets/Editor/TwoModeDataAuthoring.cs, TwoModeVerification.cs, SpawnCurveVerification.cs, EndlessSimulation.cs. Test-only runners: Assets/Scripts/Enemies/TwoModeVerificationRunner.cs / SpawnCurveVerificationRunner.cs and the existing EndlessEventsVerificationRunner.cs. Full baseline-relative file inventory is archived with evidence.

## Executed verification

**4,074 PASS / 0 FAIL across 13 selected native Unity 6000.6.0f1 processes**, all exit 0 and unchanged personal-save/Sidekick DB hashes. Exact records: Verification/TwoModes/selected-evidence.json. Counts are logged assertion lines, including some per-frame controls, rather than unique test cases.

| Entry point | PASS | FAIL | Source |
| --- | ---: | ---: | --- |
| SpawnCurveVerification.Run | 2219 | 0 | 59a2465 |
| PulsebreakerVerification.Run | 414 | 0 | 59a2465 |
| PulsebreakerVerification.Reload | 8 | 0 | 59a2465 |
| HeroForgeVerification.VerifyExisting | 136 | 0 | 59a2465 |
| HeroForgeVerification.Reload | 3 | 0 | 59a2465 |
| FirstPersonVerification.Run | 211 | 0 | 59a2465 |
| FirstPersonVerification.Reload | 5 | 0 | 59a2465 |
| SidekickVerification.Run | 102 | 0 | 59a2465 |
| SidekickVerification.Reload | 7 | 0 | 59a2465 |
| TwoModeVerification.Run | 412 | 0 | c989c14 |
| TwoModeVerification.Reload | 14 | 0 | c989c14 |
| HeroForgeArtVerification.Run | 499 | 0 | c989c14 |
| HeroForgeArtVerification.Reload | 44 | 0 | c989c14 |

Nine shared regressions use 59a2465; the final shipping-path/layout and complete three-hero art/save checks use **c989c14**. No product source or asset change follows those final checks. Compilation/import succeeded for the selected runs; pre-existing vendor diagnostics remain separate from runtime-script assertions. Native authoring also passed 1 check, excluded from this runtime total.

The tests use actual Home callbacks, the shipping mode/director/events data and sequential rounds 1–15. They cover guaranteed 5/10/15 and negative 4/6/9; real five-archetype spawns and unlocks; real HP and single difficulty scaling; required boss defeat; cap=1 priority; blocked-location diagnostics/cooldown/recovery; lost-actor replacement; tiny damage events; Overdrive; first/third person; pause, distant bar and death cleanup; finite live actors and one reused boss panel; Free Play/Heat/police; old mode records; exact separate-process saves; actual modular costumes and all existing gameplay routes. The original Pulsebreaker suite separately exercises Slam, Cannon, Rush, phase mechanics, dodges, timing and negative controls.

Fresh screenshots include home-two-modes.png, round-5-phase-1-third.png, round-5-phase-1-first.png and round-5-overdrive.png. Final capture review confirmed the real bar and Overdrive, and the level-up banner below it. These are actual native camera/UI renders, not mockups; the original NPC visual is still the existing mannequin. Rendering/HP assertions are not claims of polished boss art.

Two later broad invocations were interrupted by an occupied slot. The launcher refused to start the next process while a separate **op-two-modes** job was active. No other process was terminated and no other worktree was edited. A bounded 10-second shutdown grace preserves that refusal rather than launching a concurrent Editor. The earlier complete-art Run passed 499 checks before its Reload was blocked; it is not double-counted, and the final accepted Run/Reload pair is from the fresh art-final invocation.

Historical artifacts: 768 tracked files checked/restored byte-exact, with 44 changed historical files restored and 51 genuine fresh changed/new artifacts archived under regression-artifacts. Sandbox save/pointer files are not personal saves. Source preservation verifies 6,910 protected baseline files, all 15 native hero meshes, and all 1,003 original local files unchanged. Existing performance history in STATUS is retained; no new standalone/FPS benchmark is claimed.


A development-only round jump is included under **Overpowered > Combat > Endless > Jump to round 5 / 10 / 15**. It requires a running Endless Play Mode session, replaces only that director's owned live actors, starts a real next-tick round and adds no saved round override or shipping shortcut. Ordinary shipping waves are also tested sequentially through 15; the jump is not the sole schedule evidence.

Retained preliminary evidence: initial test compilation used GetInstanceID, which Unity 6000.6 treats as an obsolete error; the fixture was corrected to object-reference identity without changing gameplay. The first committed Run/Reload passed **404/0 +14/0**. Initial diagnostic captures left the mission briefing over the action; the later fixture uses the real dismissal control and aims the existing camera at the actor. Earlier successful evidence remains separately labelled; no failed or outdated run is counted as a final success.

## Remaining limits and human playtest

The boss uses the pre-existing scaled shared mannequin, reactor/gauntlet shapes, animations and telegraphs; a bespoke polished boss character/VFX set is still missing. This task repairs the actual encounter and UI, not that separate art gap. No dummy replacement is supplied.

UNVERIFIED: physical keyboard/mouse input, human difficulty/pacing/readability, rare dynamic obstacle changes after spawn, long-session memory, sustained busy-city CPU/GPU/FPS and standalone builds. Native Editor timing is not a standalone benchmark. Pre-existing vendor tessellation/importer/API diagnostics are not described as a clean whole Console. The gameplay harness fails on script Error/Exception/Assert.

1. Open this worktree in Unity 6000.6.0f1 and start Assets/Scenes/Home.unity. Confirm exactly two gameplay cards, Hero Forge and Normal/Hard/Extreme.
2. Select a hero, outfit, powers and signature. Enter Endless Fight; clear rounds 1–4 and locate Pulsebreaker on round 5. Repeat at 10 and 15, or use the explicit Editor round-jump menu.
3. Test the existing jumpable Slam, aimed Cannon lane and committed Rush. Check readable windups and recovery, authoritative HP changes, Overdrive at 40%, and that the round waits for the boss.
4. Toggle B first/third person, open/close Tab/Esc menus, receive a level-up and kill the boss. Check bar visibility, layout, cleanup and round progression.
5. In Free Play explore, switch allegiance with H and provoke police/Heat. Confirm there are no mandatory waves or automatic boss rounds.
6. Quit/reopen and check hero/outfit, equipment, upgrades, difficulty and previous records. Profile a standalone long session before making performance claims.

No push/merge was performed. When separately authorized, integrate the whole branch into the chosen playable checkout, preserving its local work and the art integration base. Do not copy only the menu or boss UI while omitting the actual shipping Events reference, reserved-slot accounting or registered enemy assets. Rerun native verification in the destination.
