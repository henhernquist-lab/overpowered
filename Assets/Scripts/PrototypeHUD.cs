using UnityEngine;

public sealed class PrototypeHUD : MonoBehaviour
{
    Vector2 scroll;
    void OnGUI()
    {
        var w=WorldSession.Instance; if(w==null) return;
        var p=w.Powers; var progress=w.Progression; var current=p.Selected;
        GUILayout.BeginArea(new Rect(14,14,490,310),GUI.skin.box);
        GUILayout.Label("OVERPOWERED / "+progress.Data.Side+"   HEAT "+new string('*',w.Stars)+new string('-',w.Tuning.Heat.MaximumStars-w.Stars));
        GUILayout.Label($"Health {w.Health:F0}/{w.Tuning.Movement.Health:F0} | Energy {p.Energy:F0}/{w.Tuning.Movement.Energy:F0}");
        GUILayout.Label($"Level {progress.Data.Level} | XP {progress.Data.Xp}/{progress.RequiredXp} | Unspent points {progress.Data.Points}");
        Rect bar=GUILayoutUtility.GetRect(460,12); GUI.Box(bar,""); GUI.Box(new Rect(bar.x,bar.y,bar.width*progress.Data.Xp/progress.RequiredXp,bar.height),"");
        GUILayout.Label($"Flight {p.Flight.Fuel:F2}/{p.Stats(p.Flight).Duration:F2}s (ground recharge)");
        if(current!=null) GUILayout.Label($"{current.Definition.DisplayName} {current.Charges}/{p.Stats(current).Charges} charges | cooldown {current.Cooldown:F2}s");
        GUILayout.Label(p.Message); GUILayout.Label(w.Hero.LastPunchResult); GUILayout.Label(w.Message);
        GUILayout.Label("WASD · Shift run · Space jump/ascend · F hold flight\nLMB selected power · E punch · 1–5 select · R interact\nTab powers/upgrades · H change side · Esc release mouse");
        if(p.HeldBody!=null) GUILayout.Label("Telekinesis: LMB hurl; timeout releases safely.");
        if(progress.Data.Side==PlayerSide.Villain) GUILayout.Label($"Chaos: destroy props {w.ChaosProgress}/{w.Tuning.Crimes.ChaosTarget}");
        GUILayout.EndArea();
        if(w.PlayerDead) GUI.Box(new Rect(Screen.width*.5f-150,Screen.height*.5f-30,300,60),"Defeated — respawning; progression retained");
        else GUI.Label(new Rect(Screen.width*.5f-5,Screen.height*.5f-10,20,20),"+");
        if(progress.LastError!=null) GUI.Box(new Rect(14,330,600,45),"SAVE ERROR: "+progress.LastError);
        var camera=Camera.main;
        if(camera!=null) foreach(var crime in w.Crimes)
        {
            if(crime==null||crime.Resolved) continue;
            Vector3 point=camera.WorldToScreenPoint(crime.transform.position); if(point.z<=0) continue;
            float distance=Vector3.Distance(w.Hero.transform.position,crime.transform.position-Vector3.up*w.Tuning.Crimes.MarkerHeight);
            string text=$"{crime.Kind} · {distance:F0}m"; if(distance<w.Tuning.Crimes.ResolveRadius) text+="\n"+crime.Prompt;
            GUI.Label(new Rect(point.x-100,Screen.height-point.y,450,55),text);
        }
        if(!w.MenuOpen) return;
        GUILayout.BeginArea(new Rect(Mathf.Max(12,Screen.width-570),30,550,Mathf.Max(200,Screen.height-60)),GUI.skin.box);
        GUILayout.Label("POWERS — choose upgrades (world continues)");
        scroll=GUILayout.BeginScrollView(scroll);
        foreach(var power in p.Powers)
        {
            var d=power.Definition; int tier=progress.Tier(d); var s=p.Stats(power);
            GUILayout.Label($"{p.Powers.IndexOf(power)+1}. {d.DisplayName} · {(tier<0?"LOCKED":"Tier "+tier)}");
            GUILayout.Label(d.Description);
            GUILayout.Label($"Charges {s.Charges} · cooldown {s.Cooldown:F2}s · force {s.Force:F0} N·s\nDamage {s.Damage:F0} · duration {s.Duration:F1}s · range {s.Range:F1}m · energy {d.ResourceCost:F0}");
            GUILayout.BeginHorizontal(); GUI.enabled=tier>=0;
            if(GUILayout.Button("Select")) p.Select(power);
            int cost=tier<0?d.UnlockCost:tier<d.Upgrades.Length?d.Upgrades[tier].PointCost:0;
            GUI.enabled=tier<d.Upgrades.Length && progress.Data.Points>=cost;
            if(GUILayout.Button(tier<0?$"Unlock ({cost} point)":tier<d.Upgrades.Length?$"Upgrade ({cost} point)":"Max tier")) progress.Buy(d);
            GUI.enabled=true; GUILayout.EndHorizontal(); GUILayout.Space(12);
        }
        GUILayout.EndScrollView(); GUILayout.Label($"Side switch cooldown: {progress.SwitchRemaining:F1}s");
        if(GUILayout.Button("Switch Hero / Villain")) progress.SwitchSide();
        if(GUILayout.Button("Close (Tab)")) { w.MenuOpen=false; Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false; }
        GUILayout.EndArea();
    }
}
