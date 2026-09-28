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
    [Tooltip("Optional weighted / banded / district-aware / seeded encounter picks. Null = round robin over Encounters (the original behaviour).")] public EncounterSelection Selection;
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
    [Header("Mission briefing (shown when the session starts; all three empty = no card)")]
    [Tooltip("The goal, one short line.")] public string BriefingGoal;
    [Tooltip("How you win, one short line (empty hides the row).")] public string BriefingWin;
    [Tooltip("How you lose, one short line (empty hides the row).")] public string BriefingLose;
    [Tooltip("Unscaled seconds before the card dismisses itself if the player gives no gameplay input.")] public float BriefingSeconds=7f;
    public bool HasBriefing => !string.IsNullOrWhiteSpace(BriefingGoal)||!string.IsNullOrWhiteSpace(BriefingWin)||!string.IsNullOrWhiteSpace(BriefingLose);
}
/// The kinds of task an encounter can ask of the player. Counts and targets come from the CrimeEncounter lists;
/// the player-facing words come from the ModeRules asset (ObjectiveTaskLabel), never from code.
/// Threats..Extract are mission-scenario tasks (EncounterScenario); their progress/targets come from the ScenarioState.
public enum ObjectiveTask { Robbers, Civilians, Hazards, Loot, Wreck, Escape, Threats, Hostages, Fires, Carry, Vault, Extract, Stage }
[Serializable] public sealed class ObjectiveTaskLabel { public ObjectiveTask Task; public string Label; }
/// One task of one encounter: its data label and live progress. Escape is a distance task (Done/Total are metres).
public readonly struct ObjectiveStep
{
    public readonly ObjectiveTask Task; public readonly string Label; public readonly int Done, Total, More; public readonly bool Valid;
    public ObjectiveStep(ObjectiveTask task, string label, int done, int total, int more) { Task=task; Label=label; Done=done; Total=total; More=more; Valid=true; DistanceStage=false; }
    public bool Distance => Task==ObjectiveTask.Escape||Task==ObjectiveTask.Extract||DistanceStage;
    /// A staged-mission step whose Done/Total are metres (reach / escape stages).
    public readonly bool DistanceStage;
    public ObjectiveStep(string label, int done, int total, int more, bool distance) { Task=ObjectiveTask.Stage; Label=label; Done=done; Total=total; More=more; Valid=true; DistanceStage=distance; }
    /// "STOP THE ROBBERS 2/3" or, for escape, "ESCAPE THE SCENE 14 M".
    public string Text => !Valid ? "" : Distance ? $"{Label} {Mathf.Max(0,Total-Done)} M" : $"{Label} {Done}/{Total}";
}
public abstract class ModeRules : ScriptableObject
{
    [Tooltip("Player-facing tasks in the order they are shown; the HUD shows the first incomplete one. Tasks whose encounter count is 0 are skipped.")]
    public ObjectiveTaskLabel[] Tasks = new ObjectiveTaskLabel[0];
    /// Every task with its progress, built from Tasks (the debug panel and verification suites read this string).
    public virtual string Objective(CrimeEncounter encounter)
    {
        if(encounter!=null&&encounter.Scenario!=null&&encounter.Scenario.TryCurrent(out var staged)) return staged.Text;
        var parts=new System.Collections.Generic.List<string>();
        foreach(var t in TasksFor(encounter)) if(t!=null&&Progress(encounter,t.Task,out int done,out int total)&&total>0) parts.Add(new ObjectiveStep(t.Task,t.Label,done,total,0).Text);
        return string.Join(" · ",parts);
    }
    /// The first incomplete task (in Tasks order) and how many other tasks are still incomplete; Valid=false when none.
    public ObjectiveStep Current(CrimeEncounter encounter)
    {
        if(encounter!=null&&encounter.Scenario!=null&&encounter.Scenario.TryCurrent(out var staged)) return staged;
        ObjectiveStep first=default; int more=0;
        foreach(var t in TasksFor(encounter))
        {
            if(t==null||!Progress(encounter,t.Task,out int done,out int total)||total<=0||done>=total) continue;
            if(!first.Valid) first=new ObjectiveStep(t.Task,t.Label,done,total,0); else more++;
        }
        return first.Valid ? new ObjectiveStep(first.Task,first.Label,first.Done,first.Total,more) : first;
    }
    /// A mission-scenario encounter shows its scenario's task labels; every other encounter shows this rule set's.
    ObjectiveTaskLabel[] TasksFor(CrimeEncounter e)=>e!=null&&e.Definition!=null&&e.Definition.Scenario!=null ? e.Definition.Scenario.Tasks : Tasks;
    /// Live progress of one task kind. Escape reports metres from the site (capped) out of PlayerEscapeDistance.
    public static bool Progress(CrimeEncounter e, ObjectiveTask task, out int done, out int total)
    {
        done=total=0; if(e==null||e.Definition==null) return false;
        if(e.Scenario!=null&&e.Scenario.Progress(task,out done,out total)) return true;
        switch(task)
        {
            case ObjectiveTask.Robbers: done=e.StoppedRobbers; total=e.Robbers.Count; return true;
            case ObjectiveTask.Civilians: done=e.Rescued; total=e.Civilians.Count; return true;
            case ObjectiveTask.Hazards: done=e.HazardsDone; total=e.Hazards.Count; return true;
            case ObjectiveTask.Loot: done=e.LootTaken; total=e.Loot.Count; return true;
            case ObjectiveTask.Wreck: done=Mathf.Min(e.DestroyedProps,e.Definition.DestructionGoal); total=e.Definition.DestructionGoal; return true;
            case ObjectiveTask.Escape:
                if(e.World==null||e.World.Hero==null) return false;
                total=Mathf.CeilToInt(e.Definition.PlayerEscapeDistance);
                float away=Vector3.Distance(e.World.Hero.transform.position,e.Site);
                done=away>e.Definition.PlayerEscapeDistance ? total : Mathf.Min(total-1,Mathf.FloorToInt(away)); return true;
        }
        return false;
    }
    /// World positions still to be dealt with for a task (waypoint candidates). Escape yields the nearest point on the
    /// PlayerEscapeDistance circle around the site.
    public static void Targets(CrimeEncounter e, ObjectiveTask task, System.Collections.Generic.List<Vector3> into)
    {
        into.Clear(); if(e==null) return;
        if(e.Scenario!=null) { e.Scenario.Targets(task,into); if(into.Count>0||task>=ObjectiveTask.Threats) return; }
        switch(task)
        {
            case ObjectiveTask.Robbers: foreach(var a in e.Robbers) if(!a.Captured&&!a.Escaped&&a.Npc!=null&&!a.Npc.Dead) into.Add(a.Npc.transform.position); break;
            case ObjectiveTask.Civilians: foreach(var a in e.Civilians) if(!a.Saved&&a.Npc!=null&&!a.Npc.Dead) into.Add(a.Npc.transform.position); break;
            case ObjectiveTask.Hazards: foreach(var n in e.Hazards) if(!n.Done&&n.Visual!=null) into.Add(n.Visual.transform.position); break;
            case ObjectiveTask.Loot: foreach(var n in e.Loot) if(!n.Done&&n.Visual!=null) into.Add(n.Visual.transform.position); break;
            case ObjectiveTask.Wreck: foreach(var p in e.Props) if(p!=null) into.Add(p.position); break;
            case ObjectiveTask.Escape:
                if(e.World==null||e.World.Hero==null) break;
                Vector3 away=e.World.Hero.transform.position-e.Site; away.y=0;
                if(away.sqrMagnitude<.01f) away=Vector3.forward;
                into.Add(e.Site+away.normalized*e.Definition.PlayerEscapeDistance); break;
        }
    }
    public abstract bool Complete(CrimeEncounter encounter);
    public abstract bool Failed(CrimeEncounter encounter);
}
