Regression runs for Step 2-3 (Free Play / Endless Fight), 2026-09-22.
ModeVerification and AudioVerification ran in the APFS clone (scratchpad/op-regression), refreshed with rsync and
byte-compared (diff -rq) against the working tree before each run. MenuPresentationVerification ran in the real tree
(its tracked results in Verification/Menus/ were intentionally regenerated).

ModeVerification  UNMODIFIED            exit 1  FAIL "free-play COMING SOON CONTROL refuses launch." (obsolete assertion; Free Play now launches by design)
ModeVerification  clone-only patch      exit 0  79 PASS / 0 FAIL   (patch: clone-only-ModeVerificationRunner.diff)
ModeVerification  Reload (2nd process)  exit 0  2 PASS / 0 FAIL
AudioVerification UNMODIFIED            exit 1  FAIL "Music DSP sample cursor advances" after 42 PASS
AudioVerification HEAD 8774aee CONTROL  exit 1  SAME FAIL after 42 PASS  -> environmental, not caused by this change
AudioVerification clone-only patch      exit 1  SAME FAIL after 42 PASS  (line-61 patch never reached)
Cause of the audio failure: every Unity process after the Mac's 22:20 sleep logs
  'FMOD failed to initialize the output device ... (57)' / 'FMOD initialized on nosound output'
at startup, before project code loads; audio runs earlier the same day (17:21-19:12) did not. With no output device the
DSP sample cursor cannot advance. AudioVerification must be re-run on a machine with a working audio device.
Logs (*.log) are git-ignored; they are here for local inspection only.
