using System;
using UnityEngine;

[Flags] public enum ModeHud { Health=1, Powers=2, Progression=4, Heat=8, Objectives=16, All=31 }
public enum ResultsLayout { Objectives, Survival }
[CreateAssetMenu(menuName="Overpowered/Game Mode")]
public sealed class GameModeDefinition : ScriptableObject
{
    public string Id, DisplayName;
    [TextArea] public string Description;
    public int MenuOrder;
    public bool Playable=true;
    public PlayerSide Side;
    public ModeRules Rules;
    public EncounterDefinition[] Encounters;
    public ModeHud Hud=ModeHud.All;
    [Header("Session conditions (zero disables a limit)")]
    public int SuccessGoal=5, FailureLimit=3, DefeatLimit=3;
    public float SessionSeconds=900f;
    [Header("Population")]
    public int Civilians=24, InitialEncounters=1, MaximumEncounters=2;
    public bool SpawnPolice=true;
    public float SpawnInterval=50f, SiteSeparation=28f;
    [Header("Rewards and consequences")]
    public int SuccessScore=100, FailureScorePenalty=25, RescueScore=20, PropScore=5, SuccessXp=60;
    public float SuccessHeat=-1f, FailureHeat=.75f;
    [Header("Session flow (defaults keep the original locked-side, results-screen behaviour)")]
    [Tooltip("Start on the side saved in the profile instead of Side.")] public bool SideFromProfile;
    [Tooltip("H key and the Tab menu may switch Hero/Villain during the session; the side is not locked.")] public bool AllowSideSwitch;
    [Tooltip("False: the pause menu offers only Resume and Return home, and ending the session goes straight Home.")] public bool ShowResults=true;
    public ResultsLayout Results=ResultsLayout.Objectives;
    [Tooltip("Optional session director (e.g. endless waves). Null: the session is driven by encounters only.")] public ModeDirector Director;
}
public abstract class ModeRules : ScriptableObject
{
    public abstract string Objective(CrimeEncounter encounter);
    public abstract bool Complete(CrimeEncounter encounter);
    public abstract bool Failed(CrimeEncounter encounter);
}
