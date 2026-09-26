#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Scripted difficulty sample. For each side (Hero / Villain mode) and each Heat level 0-5 a FRESH session starts, the
/// idle player stands at the live encounter site, Heat is held at that star level, and the runner measures for up to
/// SampleSeconds: cops alive, hostiles alive/engaged, incoming damage (DPS) and time to death.
/// TEST HARNESS: the mode is an in-memory clone with no defeat/failure/goal/timer limits and no timed encounter spawns,
/// so a death or an escaped robber cannot end the sample early. Shipping assets are not modified.
public sealed class BalanceVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public string Tag="before";
    const string Folder="Verification/Balance/";const float SampleSeconds=40f;
    readonly List<string> output=new List<string>();string runtimeFailure;
    WorldSession W=>WorldSession.Instance;
    public static readonly List<Row> Rows=new List<Row>();
    public struct Row{public PlayerSide Side;public bool Objective;public int Done,Stars,CopsMax,HostilesMax,EngagedMax,Hits;public float Damage,Seconds,Dps,Ttd;public bool Died;}
    void Awake(){Application.logMessageReceived+=ObserveLog;}
    void OnDestroy(){Application.logMessageReceived-=ObserveLog;}
    void ObserveLog(string message,string trace,LogType type)
    {if((type==LogType.Exception||type==LogType.Error||type==LogType.Assert)&&trace.Contains("Assets/Scripts/"))runtimeFailure=message;}
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{if(runtimeFailure!=null)throw new Exception("Gameplay Console error: "+runtimeFailure);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Write();Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Write();Finished(0);
    }
    void Log(string line){output.Add(line);Debug.Log(line);}
    void Check(bool ok,string line){if(!ok)throw new Exception(line);Log("PASS "+line);}
    void Write(){File.WriteAllLines(Folder+"sample-"+Tag+".txt",output);}
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+60;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout");yield return null;}
        yield return new WaitForSecondsRealtime(.5f);
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        Rows.Clear();
        var t=Resources.Load<GameTuning>("GameTuning");var roster=EnemyRoster.Current;
        Log($"CONFIG tag={Tag}; player health={t.Movement.Health}, no regeneration outside respawn; token budget={roster.MaxConcurrentAttackers}; detection range={t.Npcs.DetectionRange} m.");
        foreach(var side in new[]{PlayerSide.Hero,PlayerSide.Villain}){var p=t.Heat.Police(side);Log($"CONFIG police {side}: patrol={p.PatrolCount} + {p.CopsPerStar}/star, patrols hostile from {p.HostileFromStars} stars, responders hostile={p.RespondersHostile}, damage x{p.DamageMultiplier}; criminal archetype={roster.Criminal.name} (trigger {roster.Criminal.TriggerDistance} m, health x{roster.Criminal.HealthMultiplier}, damage x{roster.Criminal.DamageMultiplier}).");}
        Log($"CONFIG heat: pursuer at >= {t.Heat.HeroThreshold} stars, destruction +{t.Heat.DestructionHeat}, assault +{t.Heat.AssaultHeat}, defeat +{t.Heat.DefeatHeat}, decay {t.Heat.DecayPerSecond}/s after {t.Heat.DecayDelay}s.");
        foreach(var side in new[]{PlayerSide.Hero,PlayerSide.Villain})
            for(int stars=0;stars<=t.Heat.MaximumStars;stars++)
                yield return Sample(side,stars,false);
        foreach(var side in new[]{PlayerSide.Hero,PlayerSide.Villain})
            foreach(int stars in new[]{0,3})
                yield return Sample(side,stars,true);
        Log("TABLE (objective rows: Hero keeps within 2.2 m of the nearest robber and holds R to capture; Villain stands at the nearest loot and holds R; 25 s cap)");
        Log("TABLE side | stars | cops alive (max) | hostiles alive (max) | engaged attackers (max) | hits | damage | DPS | time to death");
        foreach(var r in Rows)Log($"TABLE {r.Side}{(r.Objective?" OBJECTIVE (done "+r.Done+")":"")} | {r.Stars} | {r.CopsMax} | {r.HostilesMax} | {r.EngagedMax} | {r.Hits} | {r.Damage:0.#} | {r.Dps:0.00} | {(r.Died?r.Ttd.ToString("0.0")+" s":"survived "+r.Seconds.ToString("0")+" s")}");
        yield return Controls();
        Log("LIMIT: idle player (no dodging, no fighting back) at the encounter site; batch mode, no human playtest. Numbers are one seeded run per cell, not averages.");
    }
    IEnumerator Sample(PlayerSide side,int stars,bool objective)
    {
        var shipping=Resources.Load<GameModeDefinition>(side==PlayerSide.Hero?"Modes/hero":"Modes/villain");
        var mode=Instantiate(shipping);mode.name=shipping.name;
        mode.DefeatLimit=0;mode.FailureLimit=0;mode.SuccessGoal=0;mode.SessionSeconds=0;mode.SpawnInterval=100000;mode.MaximumEncounters=1;
        GameFlow.Instance.Select(mode);yield return Scene(GameFlow.CityScene);
        var site=W.Crimes.First(c=>c!=null&&c.Encounter!=null).Encounter.Site;
        var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=site+Vector3.up*.3f;cc.enabled=true;W.Hero.ResetMotion();Physics.SyncTransforms();
        float heat=stars==0?0:stars-.5f;
        W.AddHeat(heat-W.Heat);W.ReconcilePolice();
        int hits=0;System.Action<bool> onHit=_=>hits++;W.PlayerDamaged+=onHit;
        var row=new Row{Side=side,Stars=stars,Objective=objective};var encounter=W.Crimes.First(c=>c!=null&&c.Encounter!=null).Encounter;
        // Objective rows: the encounter's own Update would call Interact(0) every frame (R not held in batch mode) and reset the
        // hold, so the harness drives the SAME Tick + Interact(dt) calls itself (TEST HARNESS, as the HUD suites do).
        if(objective)encounter.enabled=false;float start=Time.time,health=W.Health;
        while(Time.time-start<(objective?25f:SampleSeconds)&&!W.PlayerDead)
        {
            if(objective&&encounter!=null&&!encounter.Finished){encounter.Tick(Time.deltaTime);if(encounter!=null&&!encounter.Finished)Pursue(encounter,side);row.Done=side==PlayerSide.Hero?encounter.StoppedRobbers:encounter.LootTaken;}
            if(W.Heat<heat)W.AddHeat(heat-W.Heat+.0001f);   // hold the star level (a positive add also resets decay)
            row.CopsMax=Mathf.Max(row.CopsMax,W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Cop));
            row.HostilesMax=Mathf.Max(row.HostilesMax,W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Hostile));
            row.EngagedMax=Mathf.Max(row.EngagedMax,W.AttackTokens.Count);
            yield return null;
        }
        W.PlayerDamaged-=onHit;
        row.Seconds=Time.time-start;row.Died=W.PlayerDead;row.Ttd=row.Died?row.Seconds:-1;row.Damage=health-W.Health;row.Hits=hits;row.Dps=row.Damage/Mathf.Max(.01f,row.Seconds);
        if(W.Stars!=stars&&!row.Died)throw new Exception($"Heat hold failed: {W.Stars} stars, wanted {stars}");
        Rows.Add(row);
        Log($"SAMPLE {(objective?"OBJECTIVE ":"")}side={side} stars={stars} cops(max)={row.CopsMax} hostiles(max)={row.HostilesMax} engaged(max)={row.EngagedMax} hits={row.Hits} damage={row.Damage:0.#} over {row.Seconds:0.0}s -> DPS={row.Dps:0.00}; {(row.Died?"DIED at "+row.Ttd.ToString("0.0")+"s":"survived")}.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);Destroy(mode);
    }
    /// Objective attempt: stay next to the nearest open target and hold R (the same Interact the R key drives).
    void Pursue(CrimeEncounter e,PlayerSide side)
    {
        Vector3 hero=W.Hero.transform.position,goal;float keep;
        if(side==PlayerSide.Hero)
        {
            var robber=e.Robbers.Where(a=>!a.Captured&&!a.Escaped&&a.Npc!=null&&!a.Npc.Dead).OrderBy(a=>(a.Npc.transform.position-hero).sqrMagnitude).FirstOrDefault();
            if(robber==null)return;goal=robber.Npc.transform.position;keep=2.2f;
        }
        else
        {
            var loot=e.Loot.Where(n=>!n.Done&&n.Visual!=null).OrderBy(n=>(n.Visual.transform.position-hero).sqrMagnitude).FirstOrDefault();
            if(loot==null)return;goal=loot.Visual.transform.position;goal.y=e.Site.y;keep=1.2f;
        }
        Vector3 flat=hero-goal;flat.y=0;
        if(flat.magnitude>keep+.4f)
        {
            Vector3 at=goal+(flat.sqrMagnitude>.01f?flat.normalized:Vector3.back)*keep;
            if(UnityEngine.AI.NavMesh.SamplePosition(at,out var hit,3,UnityEngine.AI.NavMesh.AllAreas))at=hit.position;
            var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=at+Vector3.up*.1f;cc.enabled=true;W.Hero.ResetMotion();
        }
        e.Interact(Time.deltaTime);
    }
    IEnumerator Controls()
    {
        var hero=Rows.Where(r=>r.Side==PlayerSide.Hero&&!r.Objective).ToList();var villain=Rows.Where(r=>r.Side==PlayerSide.Villain&&!r.Objective).ToList();
        Log($"OBSERVED hero-side total damage over all levels={hero.Sum(r=>r.Damage):0.#}; villain-side={villain.Sum(r=>r.Damage):0.#}.");
        Log($"OBSERVED villain hostiles(max) by star: {string.Join(" / ",villain.Select(r=>r.HostilesMax))}; DPS by star: {string.Join(" / ",villain.Select(r=>r.Dps.ToString("0.0")))}.");
        Log($"OBSERVED hero hostiles(max) by star: {string.Join(" / ",hero.Select(r=>r.HostilesMax))}; DPS by star: {string.Join(" / ",hero.Select(r=>r.Dps.ToString("0.0")))}.");
        if(Tag!="after")yield break;
        var heroObjective=Rows.Where(r=>r.Side==PlayerSide.Hero&&r.Objective).ToList();
        Check(heroObjective.Sum(r=>r.Damage)>0&&heroObjective.All(r=>r.Hits>0),$"CONTROL Hero mode has real threat: capturing robbers costs {string.Join(" / ",heroObjective.Select(r=>r.Damage.ToString("0")+" HP ("+r.Hits+" hits)"))} at 0 / 3 stars.");
        Check(villain.Last().Dps>villain.First().Dps*1.5f&&villain.Last().HostilesMax>villain.First().HostilesMax,$"CONTROL Villain still escalates with Heat: DPS {villain.First().Dps:0.0} -> {villain.Last().Dps:0.0}, hostiles {villain.First().HostilesMax} -> {villain.Last().HostilesMax} (0 -> 5 stars).");
        for(int i=1;i<villain.Count;i++)Check(villain[i].HostilesMax>=villain[i-1].HostilesMax,$"Villain hostiles never drop as Heat rises: {villain[i-1].Stars}* {villain[i-1].HostilesMax} -> {villain[i].Stars}* {villain[i].HostilesMax}.");
        Check(villain.Last().Died,"CONTROL: an idle villain at 5 stars is still killed inside the sample window.");
    }
}
#endif
