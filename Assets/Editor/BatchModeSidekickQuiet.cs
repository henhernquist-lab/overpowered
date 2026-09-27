using UnityEditor;
using UnityEngine;

/// Batch mode is only ever automated verification. Synty Sidekick's MenuBootstrapController opens its Character Creator window
/// on the first editor update of every session; in a batch run that window's queued part-library work later blocks the main
/// thread mid-verification (measured in HudPhase2Verification: one 5.3 s frame inside HostView.SendUpdate, right after
/// ModularCharacterWindow.AnimationUpdate; gone when the auto-open is skipped). Setting Sidekick's own per-session "already
/// opened" flag skips that auto-open for this session only: no vendor file or EditorPrefs change, interactive editors unaffected,
/// and the window still opens from Synty > Sidekick Character Tool.
[InitializeOnLoad]
static class BatchModeSidekickQuiet
{
    const string SidekickOpenedThisSession = "FirstInitDone"; // MenuBootstrapController.OpenWindowOnStartup's SessionState key
    static BatchModeSidekickQuiet()
    {
        if (!Application.isBatchMode || SessionState.GetBool(SidekickOpenedThisSession, false)) return;
        SessionState.SetBool(SidekickOpenedThisSession, true);
        Debug.Log("Batch mode: Sidekick Character Creator auto-open skipped for this session (verification run).");
    }
}
