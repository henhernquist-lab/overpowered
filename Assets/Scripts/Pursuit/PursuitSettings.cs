using UnityEngine;

/// Pursuit tuning (optional asset Resources/PursuitSettings; these defaults apply without it).
[CreateAssetMenu(menuName = "Overpowered/Pursuit settings")]
public sealed class PursuitSettings : ScriptableObject
{
    [Tooltip("A hostile police NPC within this range with a clear line to the player is CONTACT.")] public float ContactRange = 35f;
    [Tooltip("How often the tracker samples (seconds). It never runs per frame.")] public float SampleSeconds = .25f;
    [Tooltip("Pursued -> Searching after this long without contact.")] public float LoseContactSeconds = 2.5f;
    [Tooltip("Searching -> Escaped after this long without contact AND at least EscapeDistance from where contact was lost.")] public float SearchSeconds = 8f, EscapeDistance = 45f;
    [Tooltip("Escaped is held this long (so missions and a future HUD can see it) before Clear / Alerted.")] public float EscapedHoldSeconds = 2f;
    public static PursuitSettings Current { get { var s = Resources.Load<PursuitSettings>("PursuitSettings"); if (s != null) return s; if (defaults == null) { defaults = CreateInstance<PursuitSettings>(); defaults.hideFlags = HideFlags.HideAndDontSave; } return defaults; } }
    static PursuitSettings defaults;
}
