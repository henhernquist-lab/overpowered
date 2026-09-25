using System.Collections.Generic;
using UnityEngine;

public sealed class WorldSession : MonoBehaviour
{
    public static WorldSession Instance { get; private set; }
    public static string VerificationSavePath;
    public GameTuning Tuning { get; private set; }
    public CityDistrict City { get; private set; }
    public SuperHeroController Hero { get; private set; }
    public PlayerProgression Progression { get; private set; }
    public PowerUser Powers { get; private set; }
    public GameModeSession Mode { get; private set; }
    public readonly List<CityNpc> Npcs = new List<CityNpc>();
    public readonly List<CrimeEvent> Crimes = new List<CrimeEvent>();
    /// Attack-token budget shared by every hostile NPC of this city (tuning: Resources/Enemies/EnemyRoster.asset).
    public readonly AttackTokenPool AttackTokens = new AttackTokenPool();
    public float Heat { get; private set; }
    public int Stars => Mathf.Clamp(Mathf.CeilToInt(Heat),0,Tuning.Heat.MaximumStars);
    public float Health { get; private set; }
    public bool PlayerDead => Health<=0;
    public event System.Action<bool> PlayerDamaged;
    public event System.Action PlayerRespawned;
    /// Raised after a mode encounter has been placed and initialised (HUD alerts listen; spawning is unchanged).
    public event System.Action<CrimeEncounter> EncounterSpawned;
    /// Raised by AddHeat with the change actually applied (after clamping), only when Heat changed (HUD heat flash).
    /// Continuous decay (TickHeat) and the respawn reset do not raise it.
    public event System.Action<float> HeatAdded;
    public bool MenuOpen;
    public string Message = "Explore rooftops, stop crimes, or press H to switch sides.";
    public int ChaosProgress { get; private set; }
    float troubleAgo, responseTimer, crimeTimer, deathTimer;
    public void Initialize(GameTuning tuning, CityDistrict city, SuperHeroController hero)
    {
        Instance=this; Tuning=tuning; City=city; Hero=hero; Health=tuning.Movement.Health;
        var definitions=Resources.LoadAll<PowerDefinition>("Powers");
        System.Array.Sort(definitions,(a,b)=>string.CompareOrdinal(a.Id,b.Id));
        Progression=hero.gameObject.AddComponent<PlayerProgression>();
        Progression.Initialize(tuning.Progression,definitions,VerificationSavePath);
        Powers=hero.gameObject.AddComponent<PowerUser>(); Powers.Initialize(hero,Progression,definitions,tuning.Movement);
        hero.Initialize(Powers,tuning.Movement);
        if(GameFlow.Instance!=null&&GameFlow.Instance.ActiveMode!=null)
        { Mode=gameObject.AddComponent<GameModeSession>(); Mode.Initialize(this,GameFlow.Instance.ActiveMode); Message=Mode.Definition.Description; }
        int civilians=Mode==null?tuning.City.Civilians:Mode.Definition.Civilians;
        for (int i=0;i<civilians;i++) CityNpc.Spawn(this,city.Sidewalks[i%city.Sidewalks.Count],NpcRole.Civilian);
        ReconcilePolice();
        if(Mode!=null) Mode.Begin();
        else for (int i=0;i<tuning.Crimes.MaximumActive;i++) SpawnCrime((CrimeKind)(i%3),city.Sidewalks[(i*7)%city.Sidewalks.Count]);
    }
    void Update()
    {
        if (Tuning==null) return;
        if(Mode!=null)
        {
            if(Mode.Ended) return;
            if(Input.GetKeyDown(KeyCode.Escape)) Mode.SetPaused(!Mode.Paused);
            if(Mode.Paused) return;
        }
        if (Input.GetKeyDown(KeyCode.Tab)) { MenuOpen=!MenuOpen; Cursor.lockState=MenuOpen?CursorLockMode.None:CursorLockMode.Locked; Cursor.visible=MenuOpen; }
        if (Mode==null&&Input.GetKeyDown(KeyCode.Escape)) { MenuOpen=true; Cursor.lockState=CursorLockMode.None; Cursor.visible=true; }
        if (!MenuOpen && Input.GetKeyDown(KeyCode.H)) RequestSideSwitch();
        if (!PlayerDead && Hero.transform.position.y<Tuning.Movement.KillPlane) DamagePlayer(Health);
        if (PlayerDead)
        {
            deathTimer+=Time.deltaTime;
            if (deathTimer>=Tuning.Movement.RespawnDelay)
            {
                var cc=Hero.GetComponent<CharacterController>(); cc.enabled=false; Hero.transform.position=City.Spawn+Vector3.up*Tuning.Movement.Height; cc.enabled=true;
                Hero.ResetMotion(); Health=Tuning.Movement.Health; deathTimer=0; Heat=0; ReconcilePolice();
                PlayerRespawned?.Invoke();
            }
        }
        TickHeat(Time.deltaTime);
        responseTimer+=Time.deltaTime; crimeTimer+=Time.deltaTime;
        if (responseTimer>=Tuning.Heat.ResponseInterval) { responseTimer=0; ReconcilePolice(); }
        Crimes.RemoveAll(c=>c==null || c.Resolved);
        if (Mode==null && crimeTimer>=Tuning.Crimes.SpawnInterval && Crimes.Count<Tuning.Crimes.MaximumActive)
        { crimeTimer=0; SpawnCrime((CrimeKind)Random.Range(0,3),City.Sidewalks[Random.Range(0,City.Sidewalks.Count)]); }
    }
    /// The H-key action. Modes lock the side unless their definition sets AllowSideSwitch.
    public string RequestSideSwitch()
    {
        if (Mode!=null && !Mode.Definition.AllowSideSwitch) return Message="Side is set by this mode. Return home to choose another mode.";
        return Message=Progression.SwitchSide()?"Side changed. Your powers and character are unchanged.":"Side switch cooling down";
    }
    public void TickHeat(float dt)
    {
        float before=troubleAgo; troubleAgo+=dt;
        float decayTime=Mathf.Max(0,troubleAgo-Tuning.Heat.DecayDelay)-Mathf.Max(0,before-Tuning.Heat.DecayDelay);
        Heat=Mathf.Max(0,Heat-decayTime*Tuning.Heat.DecayPerSecond);
    }
    public void AddHeat(float amount) { float before=Heat; Heat=Mathf.Clamp(Heat+amount,0,Tuning.Heat.MaximumStars); Mode?.ObserveHeat(Heat); if (amount>0) troubleAgo=0; if (Heat!=before) HeatAdded?.Invoke(Heat-before); }
    public void Alarm(Vector3 position)
    {
        foreach (var npc in Npcs) if (npc!=null && npc.Role==NpcRole.Civilian && Vector3.Distance(position,npc.transform.position)<Tuning.Npcs.AlarmRadius) npc.Alarm(position);
    }
    public void OnDestruction(Vector3 position)
    {
        if(Mode!=null&&Mode.Ended) return;
        Alarm(position); AddHeat(Tuning.Heat.DestructionHeat);
        if (Progression.Data.Side!=PlayerSide.Villain) return;
        Progression.AddXp(Tuning.Progression.DestructionXp,position,"destruction"); ChaosProgress++;
        if (Mode==null && ChaosProgress>=Tuning.Crimes.ChaosTarget) { ChaosProgress=0; Progression.AddXp(Tuning.Progression.CrimeXp,position,"chaos objective"); Message="Chaos objective complete: XP earned"; }
    }
    public void OnAssault(CityNpc npc)
    {
        Alarm(npc.transform.position);
        if (npc.Role!=NpcRole.Criminal || Progression.Data.Side==PlayerSide.Villain) AddHeat(Tuning.Heat.AssaultHeat);
    }
    public void OnDefeat(CityNpc npc)
    {
        if(Mode!=null&&Mode.Ended) return;
        if ((Progression.Data.Side==PlayerSide.Hero && npc.Role==NpcRole.Criminal) ||
            (Progression.Data.Side==PlayerSide.Villain && npc.Role!=NpcRole.Criminal))
            Progression.AddXp(npc.Role==NpcRole.Civilian?Tuning.Progression.CivilianXp:Tuning.Progression.EnemyXp,npc.transform.position,"defeat");
        if (npc.Role!=NpcRole.Criminal) AddHeat(Tuning.Heat.DefeatHeat);
    }
    public void DamagePlayer(float damage) { if (damage>0&&!PlayerDead && (Mode==null||!Mode.Ended)) { Health=Mathf.Max(0,Health-damage); PlayerDamaged?.Invoke(PlayerDead); if (PlayerDead) {Powers.Release(false);Mode?.PlayerDefeated();} } }
    public void ResolveCrime(CrimeEvent crime)
    {
        if(Mode!=null) {crime.Encounter?.TryComplete(); return;}
        bool hero=Progression.Data.Side==PlayerSide.Hero;
        AddHeat(hero?-Tuning.Heat.CrimeReduction:Tuning.Heat.CrimeHeat);
        Progression.AddXp(Tuning.Progression.CrimeXp,crime.transform.position,"crime");
        Message=hero?"Crime stopped: XP earned, Heat reduced":"Crime assisted: chaos XP earned, Heat increased";
    }
    public CrimeEvent SpawnCrime(CrimeKind kind,Vector3 position)
    {
        if(Mode!=null)
        {
            var definition=System.Array.Find(Mode.Definition.Encounters,e=>e.Kind==kind)??Mode.Definition.Encounters[0];
            return SpawnEncounter(definition,position);
        }
        var marker=GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name=kind+" event";
        marker.transform.position=position+Vector3.up*Tuning.Crimes.MarkerHeight; marker.transform.localScale=Vector3.one*Tuning.Crimes.MarkerSize;
        marker.GetComponent<Collider>().enabled=false; marker.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Fire);
        var crime=marker.AddComponent<CrimeEvent>(); crime.Kind=kind; Crimes.Add(crime);
        if (kind!=CrimeKind.Fire) { crime.Criminal=CityNpc.Spawn(this,position,NpcRole.Criminal); if(crime.Criminal!=null) crime.Criminal.Crime=crime; }
        return crime;
    }
    public CrimeEvent SpawnEncounter(EncounterDefinition definition,Vector3 position)
    {
        var marker=GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name=definition.DisplayName;
        marker.transform.position=position+Vector3.up*Tuning.Crimes.MarkerHeight;
        marker.transform.localScale=Vector3.one*Tuning.Crimes.MarkerSize;
        marker.GetComponent<Collider>().enabled=false; marker.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Fire);
        var crime=marker.AddComponent<CrimeEvent>(); crime.Kind=definition.Kind; Crimes.Add(crime);
        crime.Encounter=marker.AddComponent<CrimeEncounter>(); crime.Encounter.Initialize(this,crime,definition,position);
        EncounterSpawned?.Invoke(crime.Encounter);
        return crime;
    }
    public void ReconcilePolice()
    {
        if(Mode!=null&&!Mode.Definition.SpawnPolice) return;
        int count=0; CityNpc hunter=null;
        foreach(var npc in Npcs) if(npc!=null&&!npc.Dead) { if(npc.Role==NpcRole.Cop&&npc.Encounter==null) count++; if(npc.Role==NpcRole.PursuingHero) hunter=npc; }
        int desired=Tuning.Heat.FriendlyPatrolCount+Stars*Tuning.Heat.CopsPerStar;
        while(count<desired)
        {
            Vector3 from=Hero.transform.position+Quaternion.Euler(0,count*360f/Mathf.Max(1,desired),0)*Vector3.forward*Tuning.Heat.SpawnDistance;
            if(CityNpc.Spawn(this,City.NearestSidewalk(from),NpcRole.Cop)==null) break;
            count++;
        }
        for(int i=Npcs.Count-1;i>=0&&count>desired;i--) if(Npcs[i]!=null&&!Npcs[i].Dead&&Npcs[i].Role==NpcRole.Cop&&Npcs[i].Encounter==null) { var npc=Npcs[i]; Npcs.RemoveAt(i); Destroy(npc.gameObject); count--; }
        if(Stars>=Tuning.Heat.HeroThreshold&&hunter==null) CityNpc.Spawn(this,City.NearestSidewalk(Hero.transform.position+Vector3.forward*Tuning.Heat.SpawnDistance),NpcRole.PursuingHero);
        if(Stars<Tuning.Heat.HeroThreshold&&hunter!=null) Destroy(hunter.gameObject);
    }
    void OnDestroy() { if(Instance==this) Instance=null; }
}
