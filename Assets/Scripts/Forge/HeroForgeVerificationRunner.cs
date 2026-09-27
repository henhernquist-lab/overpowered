#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEditor;

public sealed class HeroForgeVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public bool Reload;
    readonly List<string> output=new List<string>();
    string runtimeFailure;
    void Awake(){Application.logMessageReceived+=ObserveLog;}
    void OnDestroy(){Application.logMessageReceived-=ObserveLog;}
    void ObserveLog(string message,string trace,LogType type)
    {
        if((type==LogType.Exception||type==LogType.Error||type==LogType.Assert)&&
            (trace.Contains("Assets/Scripts/")||trace.Contains("Synergy")||trace.Contains("HeroForge")))
            runtimeFailure=message;
    }
    WorldSession W=>WorldSession.Instance;
    ForgeCatalog F=>Resources.Load<ForgeCatalog>("ForgeCatalog");
    PowerDefinition Power(string id)=>Resources.Load<PowerDefinition>("Powers/"+id);
    IEnumerator Start()
    {
        Directory.CreateDirectory("Verification/Forge");var stack=new Stack<IEnumerator>();stack.Push(Checks());
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
    void Write(){File.WriteAllLines("Verification/Forge/"+(Reload?"reload.txt":"results.txt"),output);}
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+45;
        while(GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout");yield return null;}
        yield return new WaitForSecondsRealtime(.5f);
    }
    void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);var menu=FindAnyObjectByType<ModeScreens>();
        Check(F.Heroes.Length>=3,"Three selectable data-defined heroes.");
        if(Reload)
        {
            var l=menu.Profile.Data.Loadout;
            Check(l.HeroId=="nova"&&l.PowerA=="fire"&&l.PowerB=="ice"&&l.Primary==CityColor.Red&&l.Secondary==CityColor.Cyan,"SECOND PROCESS restores exact hero, pair and suit roles.");
            var fresh=new GameObject("Fresh control").AddComponent<PlayerProgression>();fresh.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),Path.GetFullPath("Verification/Forge/fresh-"+Guid.NewGuid()+".json"));
            Check(fresh.Data.Level==1&&fresh.Data.Loadout.PowerA=="flight"&&fresh.Data.Loadout.PowerB=="strength","Fresh save CONTROL starts level 1 / default pair.");yield break;
        }
        Submit(menu.ForgeButton);yield return null;
        var screen=menu.ForgeScreen;
        Check(screen!=null&&screen.Root.resolvedStyle.display!=DisplayStyle.None,"Home button opens Forge in existing panel.");
        Check(screen.Preview!=null&&screen.Preview.IsCreated(),"Character preview render texture exists.");
        Check(!menu.Profile.SetLoadout(F.Heroes[0],Power("flight"),Power("flight"),CityColor.Blue,CityColor.Cyan),"Duplicate-pair CONTROL rejected by save API.");
        // Every shipping power is owned from the start (InitiallyUnlocked data): no point is spent to equip any pair.
        Check(Resources.LoadAll<PowerDefinition>("Powers").All(p=>menu.Profile.Owns(p))&&menu.Profile.Data.Points==0,"Fresh profile owns every power with 0 points spent (InitiallyUnlocked data).");
        Check(!screen.Root.Query<Button>().ToList().Any(b=>b.text.StartsWith("UNLOCK")),"Forge shows no UNLOCK purchase buttons.");
        {
            // Ownership gate CONTROL (kept intact): a power missing from the save is still refused by Forge and SetLoadout.
            var owned=menu.Profile.Data.Powers.Find(o=>o.Id=="fire");menu.Profile.Data.Powers.Remove(owned);
            bool refused=!screen.SelectPower(1,Power("fire"))&&!menu.Profile.SetLoadout(F.Heroes[0],Power("flight"),Power("fire"),CityColor.Blue,CityColor.Cyan);
            menu.Profile.Data.Powers.Add(owned);
            Check(refused&&menu.Profile.Owns(Power("fire")),"Unowned-power CONTROL (ownership removed in memory only) rejected by Forge and SetLoadout; ownership restored.");
        }
        menu.Profile.AddXp(2000);foreach(var p in Resources.LoadAll<PowerDefinition>("Powers"))if(!menu.Profile.Owns(p))Check(menu.Profile.Buy(p),"Existing progression unlock "+p.Id);
        Check(screen.SelectHero(F.Hero("nova")),"Select NOVA definition.");
        Check(screen.SetColors(CityColor.Red,CityColor.Cyan),"Suit palette roles saved.");
        Check(screen.SelectPower(0,Power("ice"))&&menu.Profile.EquippedA!=menu.Profile.EquippedB,"Slot 1 collision repairs slot 2 immediately.");
        Check(!screen.SlotB.choices.Contains(Power("ice").DisplayName),"Slot 2 UI excludes duplicate.");
        Check(screen.SelectPower(0,Power("fire"))&&screen.SelectPower(1,Power("ice")),"Select Fire + Ice.");
        Check(F.Resolve(Power("flight"),Power("strength"))==F.Resolve(Power("strength"),Power("flight")),"Pair lookup is order-independent.");
        Capture(screen.Preview,"forge-preview.png");
        yield return CaptureMenu(menu);
        screen.Close();Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")),"Enter Hero with saved build.");yield return Scene(GameFlow.CityScene);
        Check(W.Powers.EquippedA==Power("fire")&&W.Powers.EquippedB==Power("ice"),"Gameplay receives exact equipped pair.");
        float fuel=W.Powers.Flight.Fuel;Check(!W.Powers.ConsumeFlight(.5f)&&W.Powers.Flight.Fuel==fuel,"Unequipped flight CONTROL refuses real fuel API.");
        Check(!W.Powers.Use(W.Powers.Strength)&&!W.Powers.Select(W.Powers.Strength),"Unequipped Strength CONTROL cannot be selected or fired directly.");
        Check(W.Hero.TryPunch(),"Basic punch remains available without Strength.");yield return new WaitForSeconds(.3f);
        Check(Mathf.Approximately(W.Hero.LastForce,F.BasicForce),"Basic melee force is reduced, not hidden super strength: "+W.Hero.LastForce+" N.s.");
        Check(!W.Progression.SetLoadout(F.Heroes[0],Power("flight"),Power("strength"),CityColor.Blue,CityColor.Cyan),"Live loadout mutation CONTROL refused.");
        Check(W.Hero.GetComponent<HumanoidPresentation>().Animator.gameObject.name=="NOVA","Selected humanoid model instantiated.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);menu=FindAnyObjectByType<ModeScreens>();
        Check(menu.Profile.SetLoadout(F.Hero("titan"),Power("strength"),Power("flight"),CityColor.Teal,CityColor.Amber),"Save TITAN reverse Sonic pair.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        var hero=W.Hero;var cc=hero.GetComponent<CharacterController>();var slam=W.Powers.SynergyRunner;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Forge isolated physics control";floor.transform.position=new Vector3(0,149.5f,0);floor.transform.localScale=new Vector3(30,1,30);
        floor.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Road);
        cc.enabled=false;hero.transform.position=new Vector3(0,151,0);cc.enabled=true;hero.ResetMotion();
        var near=Prop(new Vector3(3,151,0));var far=Prop(new Vector3(12,151,0));
        var camera=Camera.main;camera.GetComponent<ThirdPersonCamera>().enabled=false;camera.transform.position=new Vector3(10,157,-12);camera.transform.LookAt(new Vector3(0,151,0));
        yield return new WaitForSeconds(1);
        Vector3 before=near.position;Check(slam.TryActivate(),"Sonic Slam accepted on valid ground.");Check(!slam.TryActivate(),"Active/cooldown CONTROL refuses immediate spam.");
        W.Mode.SetPaused(true);Vector3 pausedPosition=hero.transform.position;float pausedCooldown=slam.Cooldown;
        yield return new WaitForSecondsRealtime(.2f);
        Check(hero.transform.position==pausedPosition&&Mathf.Approximately(slam.Cooldown,pausedCooldown)&&!slam.TryActivate(),"Pause CONTROL freezes synergy motion/cooldown and rejects activation.");
        W.Mode.SetPaused(false);
        float peak=hero.transform.position.y;float until=Time.time+7;
        while(slam.Busy&&Time.time<until){peak=Mathf.Max(peak,hero.transform.position.y);yield return null;}
        yield return new WaitForFixedUpdate();
        Check(slam.Impacts==1&&peak>152,"Sonic Slam rises then collides with actual ground exactly once.");
        Check(near.linearVelocity.magnitude>1&&Vector3.Distance(before,near.position)>.01f,"Sonic force moves real 45kg Rigidbody.");
        Check(Mathf.Abs(far.linearVelocity.x)<.01f&&Mathf.Abs(far.linearVelocity.z)<.01f,"Outside-radius CONTROL has no horizontal launch.");
        Log($"MEASURED Sonic peak={peak:F3}m, force={slam.LastForce:F1} N.s, bodies={slam.AffectedBodies}, near velocity={near.linearVelocity.magnitude:F3}m/s, displacement={Vector3.Distance(before,near.position):F3}m; cooldown={slam.Cooldown:F3}s.");
        Check(!slam.TryActivate()&&slam.Cooldown>0,"Cooldown still blocks after animation ends.");
        Check(slam.Vfx.Emissions==1&&slam.Vfx.PoolCount==F.EffectPoolSize,"Actual pooled ParticleSystem / ring emitted once.");
        var particles=new ParticleSystem.Particle[64];int particleCount=slam.Vfx.LastParticles.GetParticles(particles);Vector3 firstParticle=particles[0].position;
        Check(particleCount>0&&particleCount<=F.ParticlesPerBurst,"Real ParticleSystem contains bounded live particles.");
        yield return new WaitForSeconds(.12f);camera.fieldOfView=45;
        int later=slam.Vfx.LastParticles.GetParticles(particles);
        Check(later>0&&Vector3.Distance(firstParticle,particles[0].position)>.1f,"Particle positions advance over real frames (not a frozen emission counter).");
        var renderers=FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include);
        var pooled=renderers.Where(renderer=>renderer.name.StartsWith("Pooled synergy")).ToArray();
        Check(pooled.Length==F.EffectPoolSize&&pooled.All(renderer=>renderer.mesh==pooled[0].mesh),"All six VFX slots share the same particle mesh.");
        CaptureCamera(camera,"sonic-impact.png");
        hero.DebugSetResources(2,3,0);Check(W.Powers.ConsumeFlight(.5f)&&Mathf.Approximately(hero.FlightFuel,1.5f),"Equipped Flight positive control consumes configured fuel.");
        Check(hero.TryPunch(),"Equipped Strength uses existing paid punch.");Check(!hero.TryHurricaneKick(),"Shared punch/kick cooldown still rejects kick.");yield return new WaitForSeconds(.5f);
        Check(hero.LastForce==W.Powers.Stats(W.Powers.Strength).Force,"Equipped Strength preserves original force.");
        hero.DebugSetResources(2,0,0);Check(!hero.TryHurricaneKick(),"Zero Strength charges still reject Hurricane Kick.");
        Check(hero.TryBackflip(),"Backflip remains available.");float hp=W.Health;W.DamagePlayer(1);Check(W.Health==hp-1,"Backflip CONTROL has no invincibility.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);menu=FindAnyObjectByType<ModeScreens>();
        Check(menu.Profile.SetLoadout(F.Hero("nova"),Power("fire"),Power("ice"),CityColor.Red,CityColor.Cyan),"Save final loadout for separate-process test.");
        int level=menu.Profile.Data.Level;GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/villain"));yield return Scene(GameFlow.CityScene);
        Check(W.Powers.EquippedA==Power("fire")&&W.Progression.Data.Level==level,"Villain mode switch preserves build and progression.");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
        yield return AllSynergies();
        yield return Performance();
        menu=FindAnyObjectByType<ModeScreens>();menu.Profile.SetLoadout(F.Hero("nova"),Power("fire"),Power("ice"),CityColor.Red,CityColor.Cyan);
        Log("LIMIT: no human feel test; real gameplay entry points, not hardware key injection. Free Play/Endless Fight remain disabled existing definitions.");
    }
    /// The shipping synergy set is CAPPED at exactly these five (Henry's rule). The eight legacy pair synergies were removed
    /// from ForgeCatalog; no other pair may resolve. Sonic Slam is exercised in depth above; Thermal Shock here; Solar Flare,
    /// Void Grasp and Eclipse Beam in RosterVerification.
    public static readonly string[] ShippingSynergies={"sonic-slam","thermal-shock","solar-flare","void-grasp","eclipse-beam"};
    static readonly string[][] ShippingPairs={new[]{"flight","strength"},new[]{"fire","ice"},new[]{"fire","laser-eyes"},new[]{"darkness","telekinesis"},new[]{"darkness","laser-eyes"}};
    static readonly string[] RemovedSynergies={"phoenix-dive","frostwake","orbit-throw","meteor-punch","inferno-orbit","glacier-fist","cryo-crush","meteor-slam"};
    IEnumerator AllSynergies()
    {
        var ids=F.Synergies.Where(s=>s!=null).Select(s=>s.Id).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        Check(F.Synergies.All(s=>s!=null)&&ids.SequenceEqual(ShippingSynergies.OrderBy(s=>s,StringComparer.Ordinal)),"Shipping catalog is EXACTLY the five capped synergies: "+string.Join(", ",ids));
        Check(RemovedSynergies.All(id=>F.Synergies.All(s=>s.Id!=id)&&Resources.Load<PowerSynergyDefinition>("Forge/Synergies/"+id)==null),"The eight removed legacy synergies are neither in the catalog nor loadable from Resources.");
        var powers=Resources.LoadAll<PowerDefinition>("Powers").Where(p=>!p.Id.StartsWith("verification-")).ToArray();
        int resolved=0;
        for(int a=0;a<powers.Length;a++)for(int b=a+1;b<powers.Length;b++)
        {
            var s=F.Resolve(powers[a],powers[b]);
            int pair=Array.FindIndex(ShippingPairs,x=>(x[0]==powers[a].Id&&x[1]==powers[b].Id)||(x[1]==powers[a].Id&&x[0]==powers[b].Id));
            bool ok=s==F.Resolve(powers[b],powers[a])&&(pair>=0?s!=null&&s.Id==ShippingSynergies[pair]:s==null);
            Check(ok,$"{powers[a].Id} + {powers[b].Id} -> {(s!=null?s.Id:"none")} ({(pair>=0?"capped pair":"no synergy expected")}).");
            if(s!=null)resolved++;
        }
        Check(resolved==5,"Exactly five unordered pairs resolve a synergy.");
        var thermal=F.Synergies.Single(d=>d.Id=="thermal-shock");
        {
            var definition=thermal;var menu=FindAnyObjectByType<ModeScreens>();
            Check(menu.Profile.SetLoadout(F.Heroes[0],definition.PowerA,definition.PowerB,CityColor.Blue,CityColor.Cyan),"Equip "+definition.DisplayName);
            GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
            W.Hero.enabled=false;var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=new Vector3(0,151,0);cc.enabled=true;W.Hero.ResetMotion();W.Hero.transform.forward=Vector3.forward;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,149.5f,0);floor.transform.localScale=new Vector3(100,1,100);
            var camera=Camera.main;camera.GetComponent<ThirdPersonCamera>().enabled=false;camera.transform.position=new Vector3(0,152,-8);camera.transform.forward=Vector3.forward;
            var r=W.Powers.SynergyRunner;
            var victim=Actor(new Vector3(0,151,6));var control=Actor(new Vector3(2,151,6));
            // Damage is measured as health LOST, so both measured actors get explicit headroom well above any hit here.
            float headroom=Mathf.Max(1000,definition.Damage*definition.BonusMultiplier*10);
            victim.SetCombatStats(headroom,victim.ContactDamage);control.SetCombatStats(headroom,control.ContactDamage);
            Check(victim.Health>definition.Damage*definition.BonusMultiplier&&control.Health>definition.Damage,"Thermal measured actors have uncapped headroom: "+victim.Health+" HP each vs expected hits "+definition.Damage*definition.BonusMultiplier+" / "+definition.Damage);
            victim.Freeze(20);
            Physics.SyncTransforms();
            float hp=victim.Health,controlHp=control.Health;
            Check(r.TryActivate(),definition.DisplayName+" activation accepted.");
            float until=Time.time+8;
            while(r.Busy&&Time.time<until)yield return null;
            yield return new WaitForFixedUpdate();
            Check(!r.Busy,definition.DisplayName+" completes within bounded duration.");
            Check(r.Cooldown>0,definition.DisplayName+" cooldown remains after action.");
            float bonus=hp-victim.Health,normal=controlHp-control.Health;
            Check(bonus>normal&&Mathf.Abs(bonus-definition.Damage*definition.BonusMultiplier)<.1f,"Thermal affected-target bonus CONTROL: "+bonus+" vs clean "+normal);
            Check(r.Vfx.Emissions>0,definition.DisplayName+" emits bounded pooled effects.");
            Log("SYNERGY "+definition.DisplayName+": impacts="+r.Impacts+", VFX emissions="+r.Vfx.Emissions+", cooldown="+r.Cooldown.ToString("F2"));
            GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
        }
    }
    CityNpc Actor(Vector3 point)
    {
        var npc=CityNpc.Spawn(W,W.City.Sidewalks[0],NpcRole.Criminal);npc.enabled=false;npc.Agent.enabled=false;npc.transform.position=point;return npc;
    }
    IEnumerator CaptureMenu(ModeScreens menu)
    {
        var target=new RenderTexture(1440,900,24);target.Create();menu.Panel.targetTexture=target;
        yield return null;yield return null;Capture(target,"forge-menu.png");menu.Panel.targetTexture=null;target.Release();Destroy(target);
    }
    void CaptureCamera(Camera camera,string name)
    {
        var target=new RenderTexture(1280,720,24);target.Create();var old=camera.targetTexture;camera.targetTexture=target;camera.Render();Capture(target,name);camera.targetTexture=old;target.Release();Destroy(target);
    }
    IEnumerator Performance()
    {
        var menu=FindAnyObjectByType<ModeScreens>();menu.Profile.SetLoadout(F.Heroes[0],Power("flight"),Power("strength"),CityColor.Blue,CityColor.Cyan);
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        W.Hero.enabled=false;var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=new Vector3(0,100,0);cc.enabled=true;W.AddHeat(5);W.ReconcilePolice();
        var camera=Camera.main;camera.GetComponent<ThirdPersonCamera>().enabled=false;camera.transform.position=new Vector3(-65,60,-80);camera.transform.LookAt(Vector3.zero);
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;camera.enabled=true;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        var runner=W.Powers.SynergyRunner;double off=0,on=0;
        Log($"BENCH population civilians={W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Civilian)}, cops={W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Cop)}; 1280x720 single camera.");
        for(int phase=0;phase<4;phase++)
        {
            bool active=phase==1||phase==2;
            yield return new WaitForSecondsRealtime(.7f);
            var watch=System.Diagnostics.Stopwatch.StartNew();int frames=0,draws=0;double next=0;
            while(watch.Elapsed.TotalSeconds<5)
            {
                if(active&&watch.Elapsed.TotalSeconds>=next){runner.Vfx.Burst(camera.transform.position+camera.transform.forward*15,runner.Definition);next=watch.Elapsed.TotalSeconds+.1;}
                yield return null;frames++;draws+=UnityStats.drawCalls;
            }
            double fps=frames/watch.Elapsed.TotalSeconds;if(active)on+=fps/2;else off+=fps/2;
            Log($"MEASURED Forge {(active?"FX stress":"idle")} phase={phase}: FPS={fps:F2}, draws={draws/(float)frames:F1}, pool={runner.Vfx.PoolCount}.");
        }
        Log($"FORGE COST paired means: idle={off:F2} FPS, repeated pooled FX={on:F2} FPS, difference={off-on:F2} FPS, frame delta={1000/on-1000/off:F3}ms. Stress emits every 100ms; not a standalone GPU-time measurement.");
        camera.targetTexture=null;target.Release();Destroy(target);
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
    }
    Rigidbody Prop(Vector3 position){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.position=position;go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(CityColor.Wood);var rb=go.AddComponent<Rigidbody>();rb.mass=45;go.AddComponent<BreakableProp>().Configure(Resources.Load<GameTuning>("GameTuning").Props);return rb;}
    void Capture(RenderTexture target,string file)
    {
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes("Verification/Forge/"+file,image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;
    }
}
#endif
