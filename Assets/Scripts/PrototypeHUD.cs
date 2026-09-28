using System.Collections.Generic;
using UnityEngine;

/// Legacy IMGUI layer. The player-facing HUD is GameHud (UI Toolkit). What remains here:
/// the developer debug panel (hidden by default, toggled with DebugKey), the Esc pause menu, the Tab upgrade menu and
/// the defeated notice. The static helpers stay as they were (verification suites call them).
public sealed class PrototypeHUD : MonoBehaviour
{
    Vector2 scroll;
    /// F3 is unbound elsewhere (lead audit, HUD Phase 1).
    public const KeyCode DebugKey=KeyCode.F3;
    /// Developer text panels (the old always-on boxes plus world encounter labels). Off by default.
    public bool DebugVisible {get;private set;}
    /// IMGUI Repaint events seen, and how many of them drew the debug panel (verification reads both).
    public int RepaintEvents {get;private set;}
    public int DebugRepaints {get;private set;}
    /// The F3 handler; verification calls the same method.
    public void ToggleDebug(){DebugVisible=!DebugVisible;}
    void Update(){if(Input.GetKeyDown(DebugKey))ToggleDebug();}
    public const string ResumeOption="Resume", ResultsOption="End session / Results", HomeOption="Return home (keep earned XP)";
    /// Pause-menu entries for a session; modes with ShowResults=false offer no results screen.
    public static string[] PauseOptions(GameModeSession session) =>
        session.Definition.ShowResults ? new[]{ResumeOption,ResultsOption,HomeOption} : new[]{ResumeOption,HomeOption};
    public static void ChoosePause(GameModeSession session,string option)
    {
        if(option==ResumeOption) session.SetPaused(false);
        else if(option==ResultsOption) session.EndToResults();
        else if(option==HomeOption) session.ReturnHome();
    }
    /// Objective/timer lines; empty unless the mode shows Objectives. The timer only appears when a time limit exists.
    public static List<string> ObjectiveLines(WorldSession w)
    {
        var lines=new List<string>(); var s=w.Mode;
        if(s==null||(s.Definition.Hud&ModeHud.Objectives)==0) return lines;
        lines.Add($"{s.Definition.DisplayName}: completed {s.Successes}/{s.Definition.SuccessGoal} · failed {s.Failures}/{s.Definition.FailureLimit} · defeats {s.Defeats}/{s.Definition.DefeatLimit}");
        lines.Add($"Score {s.Score} · session XP {s.XpEarned}"+(s.Definition.SessionSeconds>0?$" · remaining {Mathf.Max(0,s.Definition.SessionSeconds-s.Elapsed):F0}s":""));
        lines.Add(s.Feedback);
        foreach(var e in w.Crimes) if(e!=null&&e.Encounter!=null&&!e.Resolved)
        {
            float distance=Vector3.Distance(w.Hero.transform.position,e.Encounter.Site);
            Vector3 direction=Quaternion.Inverse(w.Hero.transform.rotation)*(e.Encounter.Site-w.Hero.transform.position);
            string bearing=(direction.z>=0?"ahead":"behind")+" / "+(direction.x>=0?"right":"left");
            lines.Add($"{e.Encounter.Definition.DisplayName} — {distance:F0}m {bearing} · {Mathf.Max(0,e.Encounter.Definition.Deadline-e.Encounter.Elapsed):F0}s\n{e.Encounter.Objective}");
        }
        return lines;
    }
    /// Session director status (e.g. WAVE n · LEFT k · SCORE s · BEST b), or null.
    public static string DirectorLine(WorldSession w) => w.Mode!=null&&w.Mode.Director!=null ? w.Mode.Director.HudLine : null;
    void OnGUI()
    {
        var w=WorldSession.Instance; if(w==null) return;
        GUI.skin.label.wordWrap=true;
        if(w.Mode!=null&&w.Mode.Paused)
        {
            GUILayout.BeginArea(new Rect(Screen.width*.5f-220,Screen.height*.25f,440,300),GUI.skin.box);
            GUILayout.Label("PAUSED — "+w.Mode.Definition.DisplayName);
            foreach(var option in PauseOptions(w.Mode)) if(GUILayout.Button(option,GUILayout.Height(45))) ChoosePause(w.Mode,option);
            GUILayout.EndArea(); return;
        }
        if(Event.current.type==EventType.Repaint) RepaintEvents++;
        if(DebugVisible) DrawDebug(w);
        if(w.PlayerDead) GUI.Box(new Rect(Screen.width*.5f-150,Screen.height*.5f-30,300,60),"Defeated — respawning; progression retained");
        if(w.MenuOpen) DrawPowersMenu(w);
    }
    /// The former always-on developer boxes, unchanged, plus the world-anchored encounter labels.
    void DrawDebug(WorldSession w)
    {
        if(Event.current.type==EventType.Repaint) DebugRepaints++;
        var p=w.Powers; var progress=w.Progression; var current=p.Selected;
        var hud=w.Mode==null?ModeHud.All:w.Mode.Definition.Hud;
        GUILayout.BeginArea(new Rect(14,14,510,Mathf.Min(610,Screen.height-28)),GUI.skin.box);
        GUILayout.Label("OVERPOWERED / "+progress.Data.Side+((hud&ModeHud.Heat)!=0?"   HEAT "+new string('*',w.Stars)+new string('-',w.Tuning.Heat.MaximumStars-w.Stars):""));
        var director=DirectorLine(w); if(!string.IsNullOrEmpty(director)) GUILayout.Label(director);
        if((hud&ModeHud.Health)!=0) GUILayout.Label($"Health {w.Health:F0}/{w.MaxHealth:F0} | Energy {p.Energy:F0}/{p.MaxEnergy:F0}");
        if((hud&ModeHud.Progression)!=0) {
        GUILayout.Label($"Level {progress.Data.Level} | XP {progress.Data.Xp}/{progress.RequiredXp} | Unspent points {progress.Data.Points}");
        Rect bar=GUILayoutUtility.GetRect(460,12); GUI.Box(bar,""); GUI.Box(new Rect(bar.x,bar.y,bar.width*progress.Data.Xp/progress.RequiredXp,bar.height),"");
        }
        if((hud&ModeHud.Powers)!=0) {
        if(p.IsEquipped(p.Flight.Definition))GUILayout.Label($"Flight {p.Flight.Fuel:F2}/{p.Stats(p.Flight).Duration:F2}s (ground recharge)");
        if(p.Forge!=null){
            GUILayout.Label($"{p.HeroDefinition.DisplayName} / {p.EquippedA.DisplayName} + {p.EquippedB.DisplayName}");
            GUILayout.Label($"{p.Forge.SynergyKey}: {p.Synergy?.DisplayName??"No synergy"} / {p.SynergyRunner.Cooldown:F1}s — {p.SynergyRunner.Feedback}");
        }
        if(current!=null) GUILayout.Label($"{current.Definition.DisplayName} {current.Charges}/{p.Stats(current).Charges} charges | cooldown {current.Cooldown:F2}s");
        }
        GUILayout.Label(p.Message); GUILayout.Label(w.Hero.LastPunchResult); GUILayout.Label(w.Message);
        GUILayout.Label("WASD · Shift run · Space jump/ascend · F hold flight\nLMB selected power · E punch · RMB hurricane kick · 1–5 select · Hold R interact\nQ backflip · Tab powers/upgrades · Esc pause / results / home");
        if(p.HeldBody!=null) GUILayout.Label("Telekinesis: LMB hurl; timeout releases safely.");
        if(w.Mode==null&&progress.Data.Side==PlayerSide.Villain) GUILayout.Label($"Chaos: destroy props {w.ChaosProgress}/{w.Tuning.Crimes.ChaosTarget}");
        foreach(var line in ObjectiveLines(w)) GUILayout.Label(line);
        GUILayout.EndArea();
        var camera=Camera.main;
        if(camera!=null&&(hud&ModeHud.Objectives)!=0) foreach(var crime in w.Crimes)
        {
            if(crime==null||crime.Resolved) continue;
            Vector3 point=camera.WorldToScreenPoint(crime.transform.position); if(point.z<=0) continue;
            float distance=Vector3.Distance(w.Hero.transform.position,crime.transform.position-Vector3.up*w.Tuning.Crimes.MarkerHeight);
            string text=$"{(crime.Encounter!=null?crime.Encounter.Definition.DisplayName:crime.Kind.ToString())} · {distance:F0}m";
            if(distance<w.Tuning.Crimes.ResolveRadius*2) text+="\n"+crime.Prompt;
            GUI.Label(new Rect(point.x-100,Screen.height-point.y,400,110),text);
        }
    }
    void DrawPowersMenu(WorldSession w)
    {
        var p=w.Powers; var progress=w.Progression;
        GUILayout.BeginArea(new Rect(Mathf.Max(12,Screen.width-570),30,550,Mathf.Max(200,Screen.height-60)),GUI.skin.box);
        GUILayout.Label("POWERS — choose upgrades (world continues)");
        scroll=GUILayout.BeginScrollView(scroll);
        foreach(var power in p.Powers)
        {
            var d=power.Definition; int tier=progress.Tier(d); var s=p.Stats(power);
            GUILayout.Label($"{p.Powers.IndexOf(power)+1}. {d.DisplayName} · {(tier<0?"LOCKED":"Tier "+tier)}");
            GUILayout.Label(d.Description);
            GUILayout.Label($"Charges {s.Charges} · cooldown {s.Cooldown:F2}s · force {s.Force:F0} N·s\nDamage {s.Damage:F0} · duration {s.Duration:F1}s · range {s.Range:F1}m · energy {d.ResourceCost:F0}");
            GUILayout.BeginHorizontal(); GUI.enabled=tier>=0&&p.IsEquipped(d);
            if(GUILayout.Button("Select")) p.Select(power);
            // Powers are owned from the start (InitiallyUnlocked data); points only buy upgrade tiers, never ownership.
            int cost=tier>=0&&tier<d.Upgrades.Length?d.Upgrades[tier].PointCost:0;
            GUI.enabled=tier>=0 && tier<d.Upgrades.Length && progress.Data.Points>=cost;
            if(GUILayout.Button(tier<0?"Not owned":tier<d.Upgrades.Length?$"Upgrade ({cost} point)":"Max tier")) progress.Buy(d);
            GUI.enabled=true; GUILayout.EndHorizontal(); GUILayout.Space(12);
        }
        GUILayout.EndScrollView(); GUILayout.Label($"Side switch cooldown: {progress.SwitchRemaining:F1}s");
        if(!progress.SideLocked&&GUILayout.Button("Switch Hero / Villain")) w.RequestSideSwitch();
        if(progress.SideLocked) GUILayout.Label("This mode sets your side. Escape opens session controls.");
        if(GUILayout.Button("Close (Tab)")) { w.MenuOpen=false; Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false; }
        GUILayout.EndArea();
    }
}
