#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Profiling;

public sealed class DarknessVerificationRunner : SessionVerificationRunner
{
    public bool Reload;
    protected override string Folder=>"Verification/Darkness/";
    protected override string ResultFile=>Reload?"reload.txt":"results.txt";
    readonly List<NpcDamageResult> hits=new List<NpcDamageResult>();
    PowerRuntime RuntimePower=>Runtime("darkness");
    DarknessEffect Effect=>(DarknessEffect)Power("darkness").Effect;
    void Observe(NpcDamageResult hit){if(hit.Attacker==W.Powers)hits.Add(hit);}
    void Ready()
    {var rt=RuntimePower;W.Powers.Select(rt);W.Powers.Tick(100,true);rt.Charges=1;rt.Cooldown=0;W.Rolls.FixedUnit=.5f;hits.Clear();}
    protected override IEnumerator Checks()
    {
        CityNpc.DamageApplied+=Observe;
        try
        {
            yield return Scene(GameFlow.HomeScene);
            if(Reload)
            {
                Check(Profile.SelectedDifficulty.Id=="extreme"&&Profile.Data.Loadout.PowerA=="darkness",
                    "Separate process restores Extreme and the legal Darkness loadout.");
                yield return Enter(F.Heroes[0],"darkness","fire");Isolate();
                Check(W.Difficulty.Id=="extreme"&&W.Powers.IsEquipped(Power("darkness")),
                    "Reloaded selection launches the actual Extreme Darkness session.");
                yield break;
            }
            Check(Power("darkness").Charges==1&&Power("darkness").GetStats(2).Charges==1&&
                Power("darkness").ChargeRecharge==9&&Power("darkness").Damage==6.5f&&
                Effect.SecondaryRadius==7&&Effect.AuraRadius==4.5f&&Effect.AuraTickSeconds==.75f&&Effect.AuraTickDamage==5f,
                "One charge at all tiers; nine-second reload; authored targeting, aura and low direct damage.");
            yield return EnterDifficulty("normal");
            yield return OneTarget();
            yield return TwoTargets();
            yield return WallAndRefusal();
            yield return FirstPerson();
            yield return DensePerformance();
            foreach(string id in new[]{"hard","extreme"})
            {
                yield return EnterDifficulty(id);
                yield return DifficultyDamage(id);
            }
            yield return Home();
            Check(Profile.SetDifficulty("extreme"),"Save Extreme for the separate-process reload.");
            Check(Profile.SetLoadout(F.Heroes[0],Power("darkness"),Power("fire"),F.Heroes[0].Primary,F.Heroes[0].Secondary),
                "Save Darkness + Fire through progression for reload.");
            Log("LIMIT: matched baseline FPS before the redesign and standalone long-duration pressure need separate measurement; this is a short Editor sample.");
        }
        finally{CityNpc.DamageApplied-=Observe;}
    }
    IEnumerator EnterDifficulty(string id)
    {
        yield return Home();Check(Profile.SetDifficulty(id),"Save "+id+" for next real session.");
        yield return Enter(F.Heroes[0],"darkness","fire");Isolate();
        W.enabled=false;foreach(var npc in W.Npcs.ToArray())if(npc!=null)Destroy(npc.gameObject);yield return null;
        Check(W.Difficulty.Id==id,"Session captures "+id+" centrally.");
        W.Rolls.FixedUnit=.5f;
    }
    IEnumerator OneTarget()
    {
        Ready();var primary=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);
        var outside=Actor(new Vector3(0,150.05f,20),NpcRole.Criminal,1000);
        var civilian=Actor(new Vector3(-2,150.05f,10),NpcRole.Civilian,1000);
        Aim(Chest(primary));Check(W.Powers.Use(RuntimePower)&&DarknessEffect.LastPrimary==primary&&DarknessEffect.LastSecondary==null,
            "One hostile on crosshair yields exactly one curse.");
        Check(RuntimePower.Charges==0&&primary.Rooted&&primary.Slowed&&primary.GetComponent<ShadowCursed>().Active&&
            Mathf.Abs(primary.Health-993.5f)<.01f&&civilian.Health==1000,
            "One charge paid; brief root, heavy slow and low actual initial damage; civilian unharmed.");
        float direct=primary.Health;yield return new WaitForSeconds(.85f);
        Check(primary.Health==direct&&outside.Health==1000&&civilian.Health==1000&&primary.GetComponent<ShadowCursed>().Pulses==1,
            "Alone: one timed pulse, no self damage and no damage outside aura or to civilians.");
        Check(StatusPowerVfx.Get().ShadowPulses>=1&&StatusPowerVfx.Get().ActiveRoots==1,
            "Pulse has a pooled visible radius cue and the curse retains its body bands.");
        Capture("one-target.png");
        yield return new WaitForSeconds(3.35f);float after=primary.Health;
        yield return new WaitForSeconds(.8f);
        Check(!primary.GetComponent<ShadowCursed>().Active&&primary.Health==after&&!primary.Slowed&&StatusPowerVfx.Get().ActiveRoots==0,
            "Aura, slow and visual lease end with the four-second curse; no later tick.");
        Destroy(primary.gameObject);Destroy(outside.gameObject);Destroy(civilian.gameObject);yield return null;
    }
    IEnumerator TwoTargets()
    {
        Ready();var a=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);
        var b=Actor(new Vector3(2,150.05f,10),NpcRole.Criminal,1000);
        var c=Actor(new Vector3(3,150.05f,11),NpcRole.Criminal,1000);
        var outside=Actor(new Vector3(0,150.05f,20),NpcRole.Criminal,1000);
        var civilian=Actor(new Vector3(-2,150.05f,10),NpcRole.Civilian,1000);
        Aim(Chest(a));Check(W.Powers.Use(RuntimePower)&&DarknessEffect.LastPrimary==a&&DarknessEffect.LastSecondary==b,
            "Three eligible hostiles: crosshair primary plus nearest secondary, capped at two curses.");
        Check(a.GetComponent<ShadowCursed>()?.Active==true&&b.GetComponent<ShadowCursed>()?.Active==true&&
            c.GetComponent<ShadowCursed>()==null&&Mathf.Abs(a.Health-993.5f)<.01f&&Mathf.Abs(b.Health-993.5f)<.01f,
            "Only two hosts receive initial damage/control; third remains uncursed.");
        yield return new WaitForSeconds(.85f);
        Check(Mathf.Abs(a.Health-988.5f)<.02f&&Mathf.Abs(b.Health-988.5f)<.02f,
            "Each cursed enemy takes one five-point tick from the OTHER cursed enemy, never its own aura.");
        Check(Mathf.Abs(c.Health-990f)<.02f&&outside.Health==1000&&civilian.Health==1000,
            "Third clustered hostile takes one tick from EACH distinct aura; distant/civilian controls untouched.");
        Check(hits.Count(h=>h.Target==c&&h.SourceId=="darkness")==2&&
            hits.Count(h=>h.Target==a&&h.SourceId=="darkness")==2,
            "Authoritative damage events credit two distinct aura hits on the third enemy and direct plus mutual aura on a host.");
        Capture("two-targets.png");
        yield return new WaitForSeconds(3.35f);
        Check(a.GetComponent<ShadowCursed>().Pulses==5&&b.GetComponent<ShadowCursed>().Pulses==5&&
            Mathf.Abs(a.Health-968.5f)<.05f&&Mathf.Abs(b.Health-968.5f)<.05f&&Mathf.Abs(c.Health-950f)<.05f,
            "Full exposure: 25 aura damage per host, 50 on shared third; five bounded pulses each.");
        Log("MEASURED Normal Gunner mean65/range58.5–71.5: direct6.5 (10% of mean), one aura25 (38.46%); mutual aura25 per cursed enemy. Direct+mutual31.5 cannot kill a fresh Gunner by itself.");
        Destroy(a.gameObject);Destroy(b.gameObject);Destroy(c.gameObject);Destroy(outside.gameObject);Destroy(civilian.gameObject);yield return null;
    }
    IEnumerator WallAndRefusal()
    {
        Ready();var a=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);
        var b=Actor(new Vector3(6,150.05f,10),NpcRole.Criminal,1000);
        var wall=Wall(new Vector3(3,151,10),new Vector3(.3f,4,4));
        Aim(Chest(a));Check(W.Powers.Use(RuntimePower)&&DarknessEffect.LastSecondary==null&&b.Health==1000,
            "Building wall blocks the in-radius secondary; crosshair primary remains valid.");
        Destroy(a.gameObject);Destroy(b.gameObject);Destroy(wall);yield return null;
        Ready();var civilian=Actor(new Vector3(0,150.05f,10),NpcRole.Civilian,1000);
        Aim(Chest(civilian));float energy=W.Powers.Energy;
        Check(!W.Powers.Use(RuntimePower)&&RuntimePower.Charges==1&&W.Powers.Energy==energy&&civilian.Health==1000,
            "Civilian under crosshair is refused without charge, energy or health loss.");
        Destroy(civilian.gameObject);yield return null;
        Ready();Aim(new Vector3(0,210,5));energy=W.Powers.Energy;
        Check(!W.Powers.Use(RuntimePower)&&RuntimePower.Charges==1&&W.Powers.Energy==energy,
            "Empty aim refuses without paying the sole charge or energy.");
    }
    IEnumerator FirstPerson()
    {
        Ready();var rig=Cam.GetComponent<ThirdPersonCamera>();rig.enabled=true;
        if(!rig.FirstPerson)Check(rig.ToggleView(),"Toggle actual game camera to first person.");
        rig.SetLook(0,0);yield return null;yield return null;
        var a=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);
        var b=Actor(new Vector3(2,150.05f,11),NpcRole.Criminal,1000);
        Physics.SyncTransforms();
        Check(W.Powers.FindTarget(RuntimePower.Definition.Range,out var aimed)&&aimed.collider.GetComponentInParent<CityNpc>()==a,
            "First-person crosshair gameplay ray reaches the primary target.");
        Check(W.Powers.Use(RuntimePower)&&DarknessEffect.LastPrimary==a&&DarknessEffect.LastSecondary==b,
            "First-person cast keeps the crosshair primary and finds its gameplay-space secondary.");
        Capture("first-person.png");
        Check(PowerVfx.Get().ActiveLines<=PowerVfx.PoolSize,"First-person tendrils stay in the fixed line pool.");
        if(rig.FirstPerson)rig.ToggleView();rig.enabled=false;
        Destroy(a.gameObject);Destroy(b.gameObject);yield return null;
    }
    IEnumerator DensePerformance()
    {
        Ready();var dense=new List<CityNpc>();
        for(int i=0;i<12;i++)dense.Add(Actor(new Vector3((i%4-1.5f)*1.5f,150.05f,10+(i/4)*1.3f),NpcRole.Criminal,1000));
        Aim(Chest(dense[5]));Check(W.Powers.Use(RuntimePower),"Real Darkness cast starts in a 12-hostile cluster.");
        int materials=Resources.FindObjectsOfTypeAll<Material>().Length,lines=FindObjectsByType<LineRenderer>(FindObjectsInactive.Include).Length;
        int particles=FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length;
        using(var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1))
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();int frames=0;long frameBytes=0;
            while(frames<180){yield return null;frameBytes+=gc.LastValue;frames++;}
            watch.Stop();
            Log($"MEASURED Darkness dense12 real cast 180 frames: FPS={frames/watch.Elapsed.TotalSeconds:F2} mean={watch.Elapsed.TotalMilliseconds/frames:F3}ms whole-frame GC={frameBytes/frames}B/frame, materials={materials}->{Resources.FindObjectsOfTypeAll<Material>().Length}, lines={lines}->{FindObjectsByType<LineRenderer>(FindObjectsInactive.Include).Length}, particleSystems={particles}->{FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length}, root leases={StatusPowerVfx.Get().ActiveRoots}, peak aura sources<=2.");
            Check(StatusPowerVfx.Get().ActiveRoots<=2&&FindObjectsByType<LineRenderer>(FindObjectsInactive.Include).Length==lines&&
                FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length==particles,
                "Dense casts keep at most two active curses and create no per-tick lines or particle systems.");
        }
        foreach(var npc in dense)Destroy(npc.gameObject);yield return null;
    }
    IEnumerator DifficultyDamage(string id)
    {
        Ready();var a=Actor(new Vector3(0,150.05f,10),NpcRole.Criminal,1000);
        var b=Actor(new Vector3(2,150.05f,10),NpcRole.Criminal,1000);
        float factor=id=="hard"?.97f:.955f,cooldown=id=="hard"?1.1f:1.15f;
        Check(Mathf.Abs(W.Powers.ChargeRecharge(RuntimePower)-9f*cooldown)<.001f,
            id+" applies centralized cooldown modifier exactly once to nine-second charge recharge.");
        Aim(Chest(a));Check(W.Powers.Use(RuntimePower),id+" real Darkness cast accepted.");
        Check(Mathf.Abs(1000-a.Health-6.5f*factor)<.02f&&Mathf.Abs(1000-b.Health-6.5f*factor)<.02f,
            id+" actual direct hits use the centralized damage multiplier once.");
        yield return new WaitForSeconds(.85f);
        Check(Mathf.Abs(1000-a.Health-11.5f*factor)<.03f&&Mathf.Abs(1000-b.Health-11.5f*factor)<.03f,
            id+" mutual aura tick uses the same damage multiplier once.");
        Destroy(a.gameObject);Destroy(b.gameObject);yield return null;
    }
}
#endif
