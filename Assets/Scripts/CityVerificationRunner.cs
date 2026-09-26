#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using Debug=UnityEngine.Debug;

public sealed class CityVerificationRunner : MonoBehaviour
{
    public string Mode;
    public Action<int> Finished;
    readonly List<string> lines=new List<string>();
    WorldSession w;
    string Output=>Path.GetFullPath("Verification/City");
    bool failed;
    void Check(bool condition,string text)
    {
        lines.Add((condition?"PASS ":"FAIL ")+text); Debug.Log("[CITY VERIFY] "+lines[lines.Count-1]);
        if(!condition) throw new InvalidOperationException(text);
    }
    IEnumerator Start()
    {
        // Loadouts are fixed per session; equipping another pair means Home -> SetLoadout -> a new sandbox session.
        DontDestroyOnLoad(gameObject);
        var run=Run();
        while(true)
        {
            object current;
            try { if(!run.MoveNext()) break; current=run.Current; }
            catch(Exception e) { lines.Add("EXCEPTION "+e); failed=true; break; }
            yield return current;
        }
        File.WriteAllLines(Path.Combine(Output,Mode=="reload"?"reload-results.txt":"results.txt"),lines);
        Finished?.Invoke(failed?1:0);
    }
    IEnumerator Run()
    {
        yield return null; w=WorldSession.Instance;
        Check(w!=null,"World bootstrapped with city, powers, and progression.");
        var camera=Prepare();
        if(Mode=="reload")
        {
            var expected=JsonUtility.FromJson<ProgressSave>(File.ReadAllText(Path.Combine(Output,"expected-save.json")));
            Check(w.Progression.Data.Level==expected.Level&&w.Progression.Data.Xp==expected.Xp&&w.Progression.Data.Points==expected.Points&&w.Progression.Data.Side==expected.Side,
                $"Separate Unity process restored exact save: level={w.Progression.Data.Level}, XP={w.Progression.Data.Xp}, points={w.Progression.Data.Points}, side={w.Progression.Data.Side}.");
            Check(w.Progression.Tier(w.Powers.Strength.Definition)==1,"Separate process retained strength upgrade tier=1; force="+w.Powers.Stats(w.Powers.Strength).Force);
            Check(w.Progression.Owns(w.Powers.Powers.Find(p=>p.Definition.Id=="telekinesis").Definition),"Telekinesis owned in the separate process (InitiallyUnlocked).");
            string fresh=Path.Combine(Output,"fresh-"+Guid.NewGuid().ToString("N")+".json");
            var control=new GameObject("Fresh save control").AddComponent<PlayerProgression>(); control.Initialize(w.Tuning.Progression,Resources.LoadAll<PowerDefinition>("Powers"),fresh);
            Check(control.Data.Level==1 && control.Data.Points==0,"Fresh-save CONTROL: level=1, points=0.");
            yield break;
        }
        Check(w.City.Buildings.Count==36,"Seeded city: "+w.City.Buildings.Count+" solid buildings; civilians="+Count(NpcRole.Civilian));
        var layout=Resources.Load<CityLayout>("CityLayout"); var same=layout.Generate(w.Tuning.City);
        Check(same[0].Position==w.City.Buildings[0].Position && same[7].Size==w.City.Buildings[7].Size,"Same seed reproduced layout samples.");
        var path=new NavMeshPath();
        Check(NavMesh.CalculatePath(w.City.Sidewalks[0],w.City.Sidewalks[8],NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete,"NavMesh has complete sidewalk-to-sidewalk route.");
        Check(w.Progression.Data.Level==1&&w.Progression.Data.Points==0,"Fresh progression: level=1, points=0.");
        float original=w.Powers.Stats(w.Powers.Strength).Force;
        w.Progression.AddXp(w.Progression.RequiredXp);
        Check(w.Progression.Data.Level==2&&w.Progression.Data.Points==1&&w.Powers.Stats(w.Powers.Strength).Force==original,
            $"Unspent CONTROL: level={w.Progression.Data.Level}, points={w.Progression.Data.Points}, force unchanged={original}.");
        Check(w.Progression.Buy(w.Powers.Strength.Definition),"Spent one point on strength.");
        Check(w.Powers.Stats(w.Powers.Strength).Force>original,$"Upgraded strength: force={w.Powers.Stats(w.Powers.Strength).Force}, max charges={w.Powers.Stats(w.Powers.Strength).Charges}, points={w.Progression.Data.Points}.");
        Check(!w.Progression.Buy(w.Powers.Strength.Definition),"Zero-point upgrade CONTROL rejected.");

        var seventh=w.Powers.Powers.Find(p=>p.Definition.Id=="verification-7");
        Check(w.Powers.Powers.Count==7&&seventh.Definition.Effect==w.Powers.Powers.Find(p=>p.Definition.Id=="fire").Definition.Effect,
            "Seventh power discovered from DATA only; exact same projectile effect asset as Fire Blast.");
        // Hero Forge gate CONTROL: the owned seventh power is not in the default Flight + Strength loadout, so it is refused.
        Check(w.Progression.Owns(seventh.Definition)&&!w.Powers.IsEquipped(seventh.Definition),$"Seventh power owned but NOT equipped (session loadout {w.Powers.EquippedA.Id} + {w.Powers.EquippedB.Id}).");
        var target=Target(w.Powers.AimOrigin+Vector3.forward*8); target.useGravity=false; Physics.SyncTransforms();
        Vector3 before=target.position; int shots=Projectiles(); int charges=seventh.Charges; float cooldown=seventh.Cooldown, energy=w.Powers.Energy; var selected=w.Powers.Selected;
        bool selectedSeventh=w.Powers.Select(seventh), usedSeventh=w.Powers.Use(seventh);
        Check(!selectedSeventh&&!usedSeventh&&w.Powers.Message=="Power not equipped"&&w.Powers.Selected==selected&&seventh.Charges==charges&&seventh.Cooldown==cooldown&&w.Powers.Energy==energy,
            $"CONTROL: unequipped seventh power refused by the equip gate (Select/Use false, \"{w.Powers.Message}\"); charges {charges}->{seventh.Charges}, cooldown {cooldown:F2}->{seventh.Cooldown:F2}, energy {energy:F2}->{w.Powers.Energy:F2}.");
        yield return new WaitForSeconds(.6f);
        Check(Projectiles()==shots&&Vector3.Distance(target.position,before)<.001f&&target.linearVelocity.magnitude<.001f,
            $"CONTROL: refused seventh power spawned no projectile ({shots} before/after) and the target did not move: displacement={Vector3.Distance(target.position,before):F4}m.");
        Destroy(target.gameObject);

        w.Powers.Flight.Fuel=w.Powers.Stats(w.Powers.Flight).Duration;
        w.Powers.ConsumeFlight(2); float after2=w.Powers.Flight.Fuel;
        w.Powers.ConsumeFlight(4); float empty=w.Powers.Flight.Fuel;
        Check(after2==4&&empty==0&&!w.Powers.ConsumeFlight(.1f),$"Real flight resource path: start=6, after 2s={after2}, after 6s={empty}; empty rejects.");
        w.Powers.Tick(1,true); Check(w.Powers.Flight.Fuel==2.5f,"Ground recharge after 1s=2.5s.");
        w.Hero.DebugSetResources(6,3); Check(w.Hero.TryPunch(),"Charged punch normal CONTROL fires.");
        Check(!w.Hero.TryPunch(),"Punch cooldown rejects immediate retry.");
        yield return new WaitForSeconds(w.Hero.PunchWindupSeconds+.05f);
        w.Powers.Tick(.5f,false); w.Hero.TryPunch();yield return new WaitForSeconds(w.Hero.PunchWindupSeconds+.05f);
        w.Powers.Tick(.5f,false); w.Hero.TryPunch();yield return new WaitForSeconds(w.Hero.PunchWindupSeconds+.05f);w.Powers.Tick(.5f,false);
        Check(w.Hero.Charges==0&&!w.Hero.TryPunch(),"Three punches exhausted charges; zero-charge fire rejected.");
        w.Powers.Tick(1,true); Check(w.Hero.Charges>0&&w.Hero.TryPunch(),"Time-driven recharge restored a working punch.");
        yield return new WaitForSeconds(w.Hero.PunchWindupSeconds+.05f);

        // Buy additional powers through earned levels, never inject ownership or tier state.
        for(int i=0;i<3;i++) w.Progression.AddXp(w.Progression.RequiredXp);
        var tk=w.Powers.Powers.Find(p=>p.Definition.Id=="telekinesis"); var ice=w.Powers.Powers.Find(p=>p.Definition.Id=="ice");
        int pointsBefore=w.Progression.Data.Points;
        Check(w.Progression.Owns(tk.Definition)&&w.Progression.Owns(ice.Definition)&&w.Progression.Tier(tk.Definition)==0&&w.Progression.Tier(ice.Definition)==0&&w.Progression.Data.Points==pointsBefore&&pointsBefore==3,
            $"Telekinesis and Ice owned from the start at tier 0 (InitiallyUnlocked data); the {pointsBefore} earned points stay unspent for tiers.");
        {
            // Hero Forge gate CONTROL: owned but unequipped Telekinesis and Ice are refused before grabbing/freezing anything.
            var probe=Target(w.Powers.AimOrigin+Vector3.forward*6); probe.useGravity=false; Physics.SyncTransforms();
            int tkCharges=tk.Charges, iceCharges=ice.Charges; float gateEnergy=w.Powers.Energy;
            bool tkSelected=w.Powers.Select(tk), tkUsed=w.Powers.Use(tk); string tkMessage=w.Powers.Message;
            bool iceSelected=w.Powers.Select(ice), iceUsed=w.Powers.Use(ice); string iceMessage=w.Powers.Message;
            Check(!tkSelected&&!tkUsed&&!iceSelected&&!iceUsed&&tkMessage=="Power not equipped"&&iceMessage=="Power not equipped"&&w.Powers.HeldBody==null&&probe.constraints!=RigidbodyConstraints.FreezeAll&&probe.GetComponent<FrozenBody>()==null&&tk.Charges==tkCharges&&ice.Charges==iceCharges&&w.Powers.Energy==gateEnergy,
                "CONTROL: unequipped Telekinesis and Ice refused by the equip gate (Select/Use false); nothing grabbed, frozen or charged.");
            Destroy(probe.gameObject);
        }
        var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");
        // A shipping hero only equips powers on its roster, so the data-only seventh power needs a hero whose roster has it.
        // That hero exists IN MEMORY ONLY (CreateInstance, DontSave, no asset file) and is appended to the loaded catalog at
        // runtime; the catalog is never marked dirty and its original array is restored in finally (and again in Finish()).
        var testHero=TestHero(forge.Heroes[0],seventh.Definition,w.Powers.Strength.Definition);
        int shippingHeroes=forge.Heroes.Length; Register(forge,testHero);
        IEnumerator e;
        try
        {
            Check(Array.IndexOf(forge.Heroes,testHero)>=0&&forge.Heroes.Length==shippingHeroes+1&&!EditorUtility.IsDirty(forge)&&!AssetDatabase.Contains(testHero),
                $"Test hero '{testHero.Id}' registered in memory only (no asset file; catalog not dirty); roster={string.Join("+",Array.ConvertAll(testHero.AvailablePowers,p=>p.Id))}.");
            e=Equip(testHero,seventh.Definition,w.Powers.Strength.Definition,"seventh power + Strength"); while(e.MoveNext()) yield return e.Current;
            camera=Prepare(); seventh=w.Powers.Powers.Find(p=>p.Definition.Id=="verification-7");
            w.Powers.Select(seventh);
            target=Target(w.Powers.AimOrigin+Vector3.forward*8);
            before=target.position;
            Check(w.Powers.Use(seventh),"Seventh power activated via normal selected-power path.");
            yield return new WaitForSeconds(.6f);
            Check(Vector3.Distance(target.position,before)>1f && target.linearVelocity.magnitude>1f,
                $"Seventh projectile hit real Rigidbody: impulse setting={seventh.Definition.Force} N·s; displacement={Vector3.Distance(target.position,before):F3}m; velocity={target.linearVelocity.magnitude:F3}m/s.");
            Destroy(target.gameObject);
            e=Equip(forge.Heroes[0],tk.Definition,ice.Definition,"Telekinesis + Ice"); while(e.MoveNext()) yield return e.Current;
        }
        finally { RestoreCatalog(); }
        Check(Array.IndexOf(forge.Heroes,testHero)<0&&forge.Heroes.Length==shippingHeroes&&!EditorUtility.IsDirty(forge),$"Catalog restored to its {forge.Heroes.Length} shipping heroes once the Telekinesis + Ice session started; never marked dirty.");
        lines.Add("LIMIT: the seventh-power test hero is registered in the loaded ForgeCatalog IN MEMORY ONLY (ScriptableObject.CreateInstance, no asset file, catalog never marked dirty or saved) and removed as soon as the Telekinesis + Ice session (shipping hero vector) has started; the shipping roster itself cannot equip verification-7.");
        Destroy(testHero);
        camera=Prepare(); tk=w.Powers.Powers.Find(p=>p.Definition.Id=="telekinesis"); ice=w.Powers.Powers.Find(p=>p.Definition.Id=="ice");
        var held=Target(w.Powers.AimOrigin+Vector3.forward*6); held.useGravity=false;
        w.Powers.Select(tk); Physics.SyncTransforms();
        Check(w.Powers.Use(tk)&&w.Powers.HeldBody==held,"Telekinesis grabs the aimed Rigidbody.");
        yield return new WaitForSeconds(.3f);
        Check(w.Powers.Use(tk)&&w.Powers.HeldBody==null,"Second activation hurls the already-paid grab.");
        yield return new WaitForFixedUpdate();
        Check(held.linearVelocity.magnitude>10,$"Telekinesis hurl velocity={held.linearVelocity.magnitude:F3}m/s."); Destroy(held.gameObject);
        var frozen=Target(w.Powers.AimOrigin+Vector3.forward*6); frozen.useGravity=false;
        w.Powers.Select(ice); Physics.SyncTransforms();
        Check(w.Powers.Use(ice)&&frozen.constraints==RigidbodyConstraints.FreezeAll,"Ice freezes a targeted Rigidbody.");
        yield return new WaitForSeconds(w.Powers.Stats(ice).Duration+.1f);
        Check(frozen.constraints!=RigidbodyConstraints.FreezeAll,"Ice expires and restores original physics constraints."); Destroy(frozen.gameObject);

        w.AddHeat(-w.Heat); int low=Count(NpcRole.Cop);
        for(int i=0;i<10;i++)
        {
            var prop=Target(new Vector3(-20,1,-15-i*2)).gameObject.AddComponent<BreakableProp>(); prop.Configure(w.Tuning.Props);
            prop.TakeDamage(w.Tuning.Props.Health,w.Powers);
        }
        w.ReconcilePolice(); int high=Count(NpcRole.Cop);
        Check(w.Heat>3&&high>low,$"Real prop destruction: Heat={w.Heat:F2}; police {low} -> {high}; high-tier max HP={MaxCopHealth():F0}.");
        Check(Count(NpcRole.PursuingHero)==1,"High Heat spawns one pursuing Hero NPC.");
        Check(w.Npcs.Exists(n=>n.Role==NpcRole.Civilian&&n.Fleeing),"Civilians react to nearby destruction by fleeing.");
        float heatBefore=w.Heat; w.TickHeat(w.Tuning.Heat.DecayDelay+10);
        Check(w.Heat<heatBefore,$"Lay-low decay: {heatBefore:F2} -> {w.Heat:F2} after configured delay plus 10s.");
        w.ReconcilePolice();

        w.AddHeat(2-w.Heat);
        var heroCrime=w.SpawnCrime(CrimeKind.Fire,w.City.Spawn); float beforeHero=w.Heat; int xpHero=w.Progression.Data.Xp;
        Check(heroCrime.Resolve(),"Hero resolves fire event.");
        float heroDelta=w.Heat-beforeHero;
        Check(heroDelta<0,$"Hero fire outcome: Heat delta={heroDelta:F2}, XP awarded={w.Tuning.Progression.CrimeXp}.");
        Check(w.Progression.SwitchSide(),"Side switched through normal cooldown-controlled action.");
        Check(!w.Progression.SwitchSide(),"Immediate repeat switch CONTROL rejected.");
        w.AddHeat(2-w.Heat);
        var villainCrime=w.SpawnCrime(CrimeKind.Fire,w.City.Spawn); float beforeVillain=w.Heat; villainCrime.Resolve();
        Check(w.Heat-beforeVillain>0,$"Same fire event as Villain: Heat delta={w.Heat-beforeVillain:F2}; opposite Hero outcome; same character={w.Hero.name}.");
        Check(w.Npcs.Exists(n=>n.Role==NpcRole.Cop&&n.Hostile),"Cops become hostile on side switch; owned strength tier remains "+w.Progression.Tier(w.Powers.Strength.Definition));

        var enemy=w.Npcs.Find(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Cop);
        Check(enemy!=null&&enemy.Agent.Warp(w.Hero.transform.position+Vector3.forward),"Existing patrol cop placed in attack range on NavMesh.");
        float hp=w.Health; yield return new WaitForSeconds(w.Tuning.Npcs.AttackCooldown+.2f);
        Check(enemy!=null&&w.Health<hp,$"Hostile cop attacked through live AI: player HP {hp:F0} -> {w.Health:F0}.");
        int xpBefore=w.Progression.Data.Xp; int levelBefore=w.Progression.Data.Level;
        enemy.Damage(enemy.Health,w.Powers);
        Check(enemy.Dead&&(w.Progression.Data.Xp!=xpBefore||w.Progression.Data.Level>levelBefore),"Defeating cop as Villain awards XP and disables dead NPC navigation.");

        // A real rendered camera plus running agents; no synthetic FPS derived from frame delta.
        w.ReconcilePolice(); PlaceHero(w.City.Spawn+Vector3.up*35);
        camera.transform.position=new Vector3(-65,60,-80); camera.transform.LookAt(Vector3.zero);
        var rt=new RenderTexture(1280,720,24); camera.targetTexture=rt;
        yield return new WaitForSeconds(1f);
        int civilianCount=Count(NpcRole.Civilian), cops=Count(NpcRole.Cop), activePaths=0;
        foreach(var npc in w.Npcs) if(npc.Role==NpcRole.Cop&&!npc.Dead&&npc.Agent.enabled&&npc.Agent.isOnNavMesh) activePaths++;
        Check(civilianCount>=20&&cops>=5&&activePaths>=5,$"Performance population: civilians={civilianCount}, cops={cops}, on-NavMesh cops={activePaths}.");
        var watch=Stopwatch.StartNew(); int frames=0; double previous=0; var durations=new List<double>();
        while(watch.Elapsed.TotalSeconds<5)
        {
            camera.Render(); yield return null; frames++; double now=watch.Elapsed.TotalSeconds; durations.Add(now-previous); previous=now;
        }
        watch.Stop(); durations.Sort();
        lines.Add($"MEASURED 1280x720 rendered Editor Play Mode: frames={frames}; seconds={watch.Elapsed.TotalSeconds:F3}; FPS={frames/watch.Elapsed.TotalSeconds:F2}; p95 frame ms={durations[Mathf.Min(durations.Count-1,(int)(durations.Count*.95))]*1000:F2}; GPU={SystemInfo.graphicsDeviceName}; CPU={SystemInfo.processorType}.");
        RenderTexture previousTarget=RenderTexture.active; RenderTexture.active=rt;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(Output,"city.png"),image.EncodeToPNG()); RenderTexture.active=previousTarget; camera.targetTexture=null; rt.Release(); Destroy(image); Destroy(rt);
        w.Progression.Save(); Check(w.Progression.LastError==null&&File.Exists(w.Progression.SavePath),$"Saved level={w.Progression.Data.Level}, strength tier={w.Progression.Tier(w.Powers.Strength.Definition)}; will verify in a separate Unity process.");
        File.WriteAllText(Path.Combine(Output,"expected-save.json"),JsonUtility.ToJson(w.Progression.Data));
    }
    Camera Prepare()
    {
        w.MenuOpen=true; w.Hero.enabled=false;
        var camera=Camera.main; camera.GetComponent<ThirdPersonCamera>().enabled=false;
        PlaceHero(w.City.Spawn); camera.transform.position=w.Hero.transform.position+new Vector3(0,1,-4); camera.transform.rotation=Quaternion.identity;
        return camera;
    }
    // Equips through the REAL pre-session API (Home profile -> PlayerProgression.SetLoadout), then boots a fresh sandbox session
    // from the same save, exactly as the editor entry starts this suite.
    IEnumerator Equip(HeroDefinition hero,PowerDefinition a,PowerDefinition b,string label)
    {
        var old=w; float end=Time.realtimeSinceStartup+60; ModeScreens menu=null;
        Check(!w.Progression.SetLoadout(hero,a,b,CityColor.Blue,CityColor.Cyan),"CONTROL: live in-session loadout change refused ("+label+").");
        GameFlow.Instance.Home();
        while(GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=GameFlow.HomeScene||(menu=FindAnyObjectByType<ModeScreens>())==null||menu.Profile==null)
        { if(Time.realtimeSinceStartup>end) throw new TimeoutException("Home scene timeout"); yield return null; }
        yield return null;
        var loadout=menu.Profile.Data.Loadout; var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");
        var outsider=Array.Find(forge.Heroes,h=>!forge.Allowed(h,a)||!forge.Allowed(h,b));
        if(outsider!=null) Check(!menu.Profile.SetLoadout(outsider,a,b,loadout.Primary,loadout.Secondary),$"CONTROL: hero {outsider.Id} whose roster lacks the pair cannot equip {label}.");
        Check(menu.Profile.Owns(a)&&menu.Profile.Owns(b)&&menu.Profile.SetLoadout(hero,a,b,loadout.Primary,loadout.Secondary),$"Equipped owned {label} on {hero.Id} through PlayerProgression.SetLoadout before the session.");
        SceneManager.LoadScene(GameFlow.CityScene);
        while(WorldSession.Instance==null||WorldSession.Instance==old||SceneManager.GetActiveScene().name!=GameFlow.CityScene)
        { if(Time.realtimeSinceStartup>end) throw new TimeoutException("City scene timeout"); yield return null; }
        yield return null; w=WorldSession.Instance;
        Check(w.Powers.EquippedA==a&&w.Powers.EquippedB==b&&w.Progression.Data.Level>1,$"New sandbox session receives {w.Powers.EquippedA.Id} + {w.Powers.EquippedB.Id} and keeps progression level={w.Progression.Data.Level}.");
    }
    static ForgeCatalog registeredCatalog; static HeroDefinition[] originalHeroes;
    static HeroDefinition TestHero(HeroDefinition look,PowerDefinition a,PowerDefinition b)
    {
        var hero=ScriptableObject.CreateInstance<HeroDefinition>(); hero.hideFlags=HideFlags.DontSave; hero.name="verification-hero";
        hero.Id="verification-hero"; hero.DisplayName="VERIFICATION HERO"; hero.AvailablePowers=new[]{a,b}; hero.DefaultA=a; hero.DefaultB=b;
        hero.CharacterPrefab=look.CharacterPrefab; hero.Portrait=look.Portrait; hero.Primary=look.Primary; hero.Secondary=look.Secondary; hero.VisualScale=look.VisualScale; hero.Animation=look.Animation;
        return hero;
    }
    static void Register(ForgeCatalog forge,HeroDefinition hero)
    {
        RestoreCatalog(); registeredCatalog=forge; originalHeroes=forge.Heroes;
        var heroes=new HeroDefinition[originalHeroes.Length+1]; originalHeroes.CopyTo(heroes,0); heroes[heroes.Length-1]=hero; forge.Heroes=heroes; // never SetDirty
    }
    /// Restores the loaded catalog's original hero array; safe to call repeatedly (also called by CityVerification.Finish).
    public static void RestoreCatalog()
    {
        if(registeredCatalog!=null&&originalHeroes!=null) registeredCatalog.Heroes=originalHeroes;
        registeredCatalog=null; originalHeroes=null;
    }
    int Projectiles() => FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length;
    void PlaceHero(Vector3 point)
    {
        var cc=w.Hero.GetComponent<CharacterController>(); cc.enabled=false; w.Hero.transform.position=point; w.Hero.transform.rotation=Quaternion.identity; cc.enabled=true; Physics.SyncTransforms();
    }
    Rigidbody Target(Vector3 position)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name="Verification physics target"; go.transform.position=position;
        var rb=go.AddComponent<Rigidbody>(); rb.mass=4; rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic; return rb;
    }
    int Count(NpcRole role) { int count=0; foreach(var n in w.Npcs) if(n!=null&&!n.Dead&&n.Role==role) count++; return count; }
    float MaxCopHealth() { float value=0; foreach(var n in w.Npcs) if(n!=null&&n.Role==NpcRole.Cop) value=Mathf.Max(value,n.Health); return value; }
}
#endif
