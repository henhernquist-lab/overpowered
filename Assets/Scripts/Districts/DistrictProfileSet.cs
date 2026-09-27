using System;
using UnityEngine;

/// Which gameplay profile each CityLayout district uses (Resources/DistrictProfiles). The district NAME appears only here,
/// as data. Enabled=false (the setup default) or no asset: every district is neutral, i.e. the old behaviour.
[CreateAssetMenu(menuName = "Overpowered/Districts/Profile set")]
public sealed class DistrictProfileSet : ScriptableObject
{
    [Serializable] public sealed class Entry { [Tooltip("CityLayout district Name (exact, case-insensitive).")] public string District; public DistrictGameplayProfile Profile; }
    public bool Enabled;
    public Entry[] Entries = new Entry[0];
    public DistrictGameplayProfile For(string district)
    {
        if (!Enabled || Entries == null || string.IsNullOrEmpty(district)) return null;
        foreach (var e in Entries) if (e != null && e.Profile != null && string.Equals(e.District, district, StringComparison.OrdinalIgnoreCase)) return e.Profile;
        return null;
    }
    public static DistrictProfileSet Current => Resources.Load<DistrictProfileSet>("DistrictProfiles");
}
