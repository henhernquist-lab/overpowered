HumanoidVerificationRunner observer filter (2026-09-27). The three CONTROL runs used a temporary
-observerControl hook that was removed before commit; HumanoidVerification.Run-final is the committed code.
- search: Observe() fed a synthetic Exception whose stack is UnityEditor.Search.SearchDatabase only -> recorded as
  EDITOR-ONLY, run continues: exit 0, 68 PASS (the synthetic call exercises the filter predicate, not the Editor's
  real indexer).
- mixed: synthetic SearchDatabase stack that also reaches Assets/Scripts/ -> fatal: exit 1 "Runtime error".
- throw: a real InvalidOperationException thrown from a project MonoBehaviour through Unity's log path -> fatal:
  exit 1 "Runtime error" after 8 PASS.
