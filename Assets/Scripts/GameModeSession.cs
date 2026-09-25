using System.Collections.Generic;
using UnityEngine;

/// How one encounter ended (HUD objective banner). Xp = what the success grants from GameModeDefinition.SuccessXp; 0 on failure.
public readonly struct EncounterOutcome
{
    public readonly CrimeEncounter Encounter; public readonly EncounterDefinition Definition; public readonly bool Success; public readonly string Reason; public readonly int Xp; public readonly Vector3 Site;
    public EncounterOutcome(CrimeEncounter encounter,bool success,string reason,int xp) { Encounter=encounter; Definition=encounter!=null?encounter.Definition:null; Success=success; Reason=reason; Xp=xp; Site=encounter!=null?encounter.Site:Vector3.zero; }
}
public sealed class GameModeSession : MonoBehaviour
{
    public GameModeDefinition Definition { get; private set; }
    public WorldSession World { get; private set; }
    public int Score { get; private set; }
    public int XpEarned { get; private set; }
    public int Successes { get; private set; }
    public int Failures { get; private set; }
    public int Defeats { get; private set; }
    public float Elapsed { get; private set; }
    public bool Ended { get; private set; }
    public bool Paused { get; private set; }
    public string Feedback { get; private set; }
    public int Rescues {get;private set;}
    public float PeakHeat {get;private set;}
    public ModeDirectorState Director {get;private set;}
    /// Raised when an encounter ends, BEFORE its rewards are granted and before the goal/limit checks (so a banner is
    /// queued ahead of any level-up the reward causes). Additive: scoring and rules are unchanged.
    public event System.Action<EncounterOutcome> EncounterResolved;
    int startLevel,startXp;
    float spawnClock; int nextEncounter;
    public void Initialize(WorldSession world,GameModeDefinition definition)
    {
        World=world; Definition=definition;
        world.Progression.SetModeSide(definition.SideFromProfile?world.Progression.Data.Side:definition.Side,!definition.AllowSideSwitch);
        startLevel=world.Progression.Data.Level;startXp=world.Progression.Data.Xp;PeakHeat=world.Heat;
        world.Progression.XpAwarded+=Awarded;
        Feedback=definition.Description;
    }
    public void Begin()
    {
        if(Definition.Director!=null) Director=Definition.Director.Begin(this);
        for(int i=0;i<Definition.InitialEncounters;i++) SpawnNext();
    }
    void Awarded(int amount) { if(!Ended) XpEarned+=amount; }
    public void AddScore(int value) { if(!Ended) Score=Mathf.Max(0,Score+value); }
    public void RecordRescue(){if(!Ended)Rescues++;}
    public void ObserveHeat(float heat){if(!Ended)PeakHeat=Mathf.Max(PeakHeat,heat);}
    public void Tick(float dt)
    {
        if(Ended||Paused) return;
        Elapsed+=dt; spawnClock+=dt;
        if(Definition.SessionSeconds>0 && Elapsed>=Definition.SessionSeconds) { Finish(SessionOutcome.TimedOut,"Session time limit reached."); return; }
        if(spawnClock>=Definition.SpawnInterval) { spawnClock=0; SpawnNext(); }
        if(Director!=null) Director.Tick(dt);
    }
    void Update() { Tick(Time.deltaTime); }
    public CrimeEvent SpawnNext()
    {
        World.Crimes.RemoveAll(c=>c==null||c.Resolved);
        if(Ended||World.Crimes.Count>=Definition.MaximumEncounters||Definition.Encounters==null||Definition.Encounters.Length==0) return null;
        // Place set-pieces at street intersections, leaving space for physics cars and escape routes.
        var sites=new List<Vector3>();
        var city=World.Tuning.City; float pitch=city.BlockSize+city.StreetWidth;
        for(int x=0;x<city.Blocks-1;x++) for(int z=0;z<city.Blocks-1;z++)
            sites.Add(new Vector3((x-(city.Blocks-2)*.5f)*pitch,0,(z-(city.Blocks-2)*.5f)*pitch));
        if(sites.Count==0) sites.Add(World.City.Spawn);
        sites.Sort((a,b)=>(a-World.Hero.transform.position).sqrMagnitude.CompareTo((b-World.Hero.transform.position).sqrMagnitude));
        foreach(var site in sites)
        {
            if(World.Crimes.Exists(c=>c!=null&&c.Encounter!=null&&Vector3.Distance(c.Encounter.Site,site)<Definition.SiteSeparation)) continue;
            var definition=Definition.Encounters[nextEncounter++%Definition.Encounters.Length];
            return World.SpawnEncounter(definition,site);
        }
        return null;
    }
    public void EncounterEnded(CrimeEncounter encounter,bool success,string reason)
    {
        if(Ended) return;
        EncounterResolved?.Invoke(new EncounterOutcome(encounter,success,reason,success?Mathf.Max(0,Definition.SuccessXp):0));
        if(success)
        {
            Successes++; AddScore(Definition.SuccessScore); World.Progression.AddXp(Definition.SuccessXp,encounter.Site,"objective"); World.AddHeat(Definition.SuccessHeat);
        }
        else { Failures++; AddScore(-Definition.FailureScorePenalty); World.AddHeat(Definition.FailureHeat); }
        Feedback=(success?"SUCCESS — ":"FAILED — ")+encounter.Definition.DisplayName+": "+reason;
        World.Message=Feedback;
        if(Definition.SuccessGoal>0&&Successes>=Definition.SuccessGoal) Finish(SessionOutcome.Won,"Encounter goal completed.");
        else if(Definition.FailureLimit>0&&Failures>=Definition.FailureLimit) Finish(SessionOutcome.Lost,"Too many failed encounters.");
    }
    public void PlayerDefeated()
    {
        if(Ended) return; Defeats++;
        if(Definition.DefeatLimit>0&&Defeats>=Definition.DefeatLimit) Finish(SessionOutcome.Lost,"Player defeat limit reached.");
    }
    public void SetPaused(bool pause)
    {
        if(Ended) return; Paused=pause; World.MenuOpen=pause; TimeArbiter.SetMenuPaused(pause);   // timeScale pause?0:1; the menu always wins over a hit pause
        Cursor.lockState=pause?CursorLockMode.None:CursorLockMode.Locked; Cursor.visible=pause;
    }
    public void Finish(SessionOutcome outcome,string reason,bool home=false)
    {
        if(Ended) return;
        Ended=true; World.MenuOpen=true; World.Powers.Release(false);
        home|=!Definition.ShowResults;
        var result=new SessionResult {ModeId=Definition.Id,ModeName=Definition.DisplayName,Outcome=outcome,Reason=reason,Score=Score,Xp=XpEarned,Successes=Successes,Failures=Failures,Defeats=Defeats,Seconds=Elapsed,
            Side=World.Progression.Data.Side,Rescues=Rescues,PeakHeat=PeakHeat,TimeLimit=Definition.SessionSeconds,StartLevel=startLevel,StartXp=startXp,Layout=Definition.Results};
        if(Director!=null) Director.Describe(result);
        int previousBest=World.Progression.BestScore(Definition.Id);
        World.Progression.RecordSession(Definition.Id,outcome==SessionOutcome.Won,Score,XpEarned,result.Wave);
        result.BestScore=Mathf.Max(previousBest,Score); result.NewBest=Score>previousBest;
        result.EndLevel=World.Progression.Data.Level; result.EndXp=World.Progression.Data.Xp;
        GameFlow.Instance.Results(result,home);
    }
    /// Pause-menu actions (PrototypeHUD draws these; verification calls the same methods).
    public void EndToResults() { Finish(SessionOutcome.Abandoned,"Returned from pause menu."); }
    public void ReturnHome() { Finish(SessionOutcome.Abandoned,"Returned home.",true); }
    void OnDestroy() { if(World!=null&&World.Progression!=null) World.Progression.XpAwarded-=Awarded; TimeArbiter.Reset(); Time.timeScale=1; }
}
