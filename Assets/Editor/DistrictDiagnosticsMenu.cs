using UnityEditor;
using UnityEngine;

/// Editor toggle for DistrictDiagnostics (OFF by default, per machine via EditorPrefs). When on, every play session records
/// per-district diagnostics and writes Verification/DistrictDiagnostics/district-diagnostics-<mode>-<time>{-buckets.csv,
/// -totals.csv,.json} when the session ends. Players' builds use the -districtDiagnostics command-line flag instead.
[InitializeOnLoad]
public static class DistrictDiagnosticsMenu
{
    const string Key = "Overpowered.DistrictDiagnostics", Toggle = "Overpowered/Diagnostics/Record District Diagnostics", WriteNow = "Overpowered/Diagnostics/Write District Diagnostics Now";
    static DistrictDiagnosticsMenu() { DistrictDiagnostics.Enabled = EditorPrefs.GetBool(Key, false); }
    [MenuItem(Toggle)]
    static void Flip() { bool on = !EditorPrefs.GetBool(Key, false); EditorPrefs.SetBool(Key, on); DistrictDiagnostics.Enabled = on; Debug.Log("District diagnostics " + (on ? "ON (applies to the next session)" : "OFF")); }
    [MenuItem(Toggle, true)]
    static bool FlipCheck() { Menu.SetChecked(Toggle, EditorPrefs.GetBool(Key, false)); return true; }
    [MenuItem(WriteNow)]
    static void Write()
    {
        var world = WorldSession.Instance; var d = world != null ? world.GetComponent<DistrictDiagnostics>() : null;
        if (d == null) { Debug.LogWarning("No recording session (enter a city session with the toggle on)."); return; }
        var paths = d.Write(string.IsNullOrEmpty(d.Folder) ? DistrictDiagnostics.DefaultFolder : d.Folder);
        Debug.Log("District diagnostics written:\n" + string.Join("\n", paths));
    }
    [MenuItem(WriteNow, true)] static bool WriteCheck() => EditorApplication.isPlaying;
}
