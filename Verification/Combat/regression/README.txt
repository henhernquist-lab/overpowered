Step 4 (telegraphed combat) regression evidence - 2026-09-23
===========================================================

Where: APFS clone scratchpad/op-regression, re-synced from the working tree (rsync Assets/ ProjectSettings/ Packages/)
before EVERY run; `diff -rq` of Assets/ against the working tree was empty for unmodified runs and showed only the one
patched runner for PATCHED runs (summary.txt "bytecompare-diffs"). Final code: CityNpc.cs incl. knockback ground-stick.
One Unity process at a time (scratchpad/regsuite.sh). summary.txt is the machine-written table.

Run                                   exit  PASS  first FAIL
HumanoidVerification.Run               0     46   -
CityVerification.Run                   0     39   -
BackflipHurricaneVerification.Run      0     63   -
ModeVerification.Run  UNMODIFIED       1     46   Held interaction accepted at valid objective.
ModeVerification.Run  clone-patched    0     79   -   (+ ModeVerification.Reload, 2nd process: exit 0, 2 PASS)
AudioVerification.Run UNMODIFIED       1    107   Cop gunshot is driven by a real hostile NPC attack/damage event.
AudioVerification.Run clone-patched    0    109   -
ModeExpansionVerification.Run UNMOD    1     44   Wave 1 = FirstWaveCount 3 Criminals at base health 65 / damage 8.
ModeExpansionVerification.Run patched  0    111   -   (+ .Reload, 2nd process: exit 0, 10 PASS)

No timing-wait edits were needed: the instant-damage waits in CityVerificationRunner (~l.141), ModeVerificationRunner
(~l.127) and HumanoidVerificationRunner (~l.55) all pass UNMODIFIED; the Hero neutral-cop CONTROL is untouched and passes.

Why the three unmodified failures are consequences of the new combat (all diagnosed with clone-only DIAG runs):
- ModeVerification: Villain run hovers the input-disabled player at Site+15 m through Heat 3 (12 hostile cops) and a 5 s
  benchmark. Cops are now Gunners (22 m locked-line shots), so the player dies (mode-diagnostic-DIAG.txt: playerDead=True,
  health 0, defeats 1, hostileCops 12) and CrimeEncounter.Interact refuses. 15 m was safe only against 2 m contact damage.
  Patch: clone-only-ModeVerificationRunner-standoff.diff (hover 30 m, above Gunner range). No assertion changed.
- AudioVerification (NOT the CoreAudio fault: the Music DSP check now passes; 107 PASS before this failure): the first
  gunshot heard came from a DIFFERENT cop whose committed shot MISSED (margin 11.2 m) because the test teleported the player
  after that cop locked its aim (audio-diagnostic-DIAG.txt). Misses fire Attacked by design, so "a gunshot is playing" no
  longer implies "damage already applied". Patch: clone-only-AudioVerificationRunner-gunshot.diff waits for the CHOSEN cop's
  release and requires a gunshot source AT that cop, its hit, and the health drop (stronger attribution than before).
- ModeExpansionVerification: Sample() asserted every wave enemy has identical health/damage = base. Now per-enemy stats =
  wave health/damage x that enemy's archetype multiplier (still Heat-independent). Wave-5 Gunners also reach the hovering hero
  (first-pass/mode-expansion-sample-patch-only*: without the heal the hero dies mid-FPS sample, 69 PASS then camera destroyed).
  Patch: clone-only-ModeExpansionVerificationRunner-archetypes.diff (Sample states the new truth; MeasureFps restores health
  by reflection while sampling). All escalation assertions (x1.60 health, x1.40 damage, Heat-star comparison) unchanged.

first-pass/: the earlier pass (before the knockback ground-stick fix) incl. the sample-patch-only proof run. *.log files are
git-ignored by the repo's *.log rule; the *.txt results and DIAG extracts are the tracked evidence.
