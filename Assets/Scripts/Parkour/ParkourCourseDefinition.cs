using System;
using UnityEngine;

public enum ParkourAnchorKind
{
    WorldPoint,
    BuildingRoof,
    BuildingSetback,
    BuildingBase,
    LandmarkOffset
}

[Serializable]
public sealed class ParkourCheckpointSpec
{
    public string Label;
    public ParkourAnchorKind Anchor = ParkourAnchorKind.BuildingRoof;
    [Tooltip("District index (0=Downtown, 1=Park, 2=Residential, 3=Docks) or -1 for any.")]
    public int District = 0;
    [Tooltip("Reference world XZ used to deterministically locate the nearest generated building in the district.")]
    public Vector2 ReferencePosition;
    [Tooltip("Landmark name when Anchor is LandmarkOffset.")]
    public string LandmarkName;
    [Tooltip("Offset from the resolved anchor (for BuildingRoof/Setback: X/Z in [-1,1] normalized half-span, Y height offset above deck; for WorldPoint/LandmarkOffset: world offset).")]
    public Vector3 Offset = new Vector3(0f, 1.4f, 0f);
    [Min(1f)]
    public float TriggerRadius = 3.2f;
}

[CreateAssetMenu(menuName = "Overpowered/Parkour/Course")]
public sealed class ParkourCourseDefinition : ScriptableObject
{
    public string Id = "parkour-course";
    public string DisplayName = "PARKOUR COURSE";
    [TextArea] public string Description;
    [Header("Visual theme (CityColor palette swatches)")]
    public CityColor PrimaryColor = CityColor.Cyan;
    public CityColor SecondaryColor = CityColor.Blue;
    public CityColor FinishColor = CityColor.Cream;
    [Header("Timing & Rewards (existing XP progression only)")]
    [Min(1f)] public float TargetSeconds = 45f;
    [Min(0)] public int FirstCompletionXp = 75;
    [Min(0)] public int PersonalBestXp = 25;
    [Min(1.5f)] public float StartInteractRadius = 3.8f;
    [Header("Route definition (start + ordered checkpoints; final checkpoint is finish)")]
    public ParkourCheckpointSpec Start = new ParkourCheckpointSpec { Label = "Start", Offset = new Vector3(0f, 1.2f, 0f), TriggerRadius = 3.5f };
    public ParkourCheckpointSpec[] Checkpoints = new ParkourCheckpointSpec[0];

    public bool IsValid(out string reason)
    {
        if (string.IsNullOrWhiteSpace(Id)) { reason = "Course Id is empty."; return false; }
        if (string.IsNullOrWhiteSpace(DisplayName)) { reason = "Course DisplayName is empty."; return false; }
        if (Start == null) { reason = "Course Start spec is missing."; return false; }
        if (Checkpoints == null || Checkpoints.Length < 2) { reason = "Course requires at least 2 checkpoints."; return false; }
        for (int i = 0; i < Checkpoints.Length; i++)
        {
            if (Checkpoints[i] == null) { reason = $"Checkpoint {i} is null."; return false; }
            if (Checkpoints[i].TriggerRadius <= 0.25f) { reason = $"Checkpoint {i} radius is invalid."; return false; }
        }
        reason = null;
        return true;
    }
}
