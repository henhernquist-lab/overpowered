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

public sealed class AudioVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public bool Reload;
    readonly List<string> output=new List<string>();
    AudioDirector A=>AudioDirector.Instance;WorldSession W=>WorldSession.Instance;
    Camera camera;RenderTexture target;
    string Folder=>Path.GetFullPath("Verification/Audio");
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Write();Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Write();Finished(0);
    }
    void Log(string text){output.Add(text);Debug.Log(text);}
    void Check(bool pass,string text){if(!pass)throw new Exception(text);Log("PASS "+text);}
    void Write(){File.WriteAllLines(Path.Combine(Folder,Reload?"reload.txt":"results.txt"),output);}
    int Playing(AudioCue cue){var clips=A.Tuning.Find(cue).Clips;return A.Sources.Count(s=>s.isPlaying&&clips.Contains(s.clip));}
    AudioSource PlayingSource(AudioCue cue)=>A.Sources.FirstOrDefault(s=>s.isPlaying&&A.Tuning.Find(cue).Clips.Contains(s.clip));
    void SourceCheck(AudioCue cue)
    {
        var s=PlayingSource(cue);var c=A.Tuning.Find(cue);
        Check(s!=null&&s.clip!=null&&s.volume>0&&s.outputAudioMixerGroup==c.Group&&Mathf.Approximately(s.spatialBlend,c.Spatial?1:0),$"{cue}: actual AudioSource.isPlaying, assigned clip={s?.clip.name}, volume={s?.volume:F3}, group={s?.outputAudioMixerGroup?.name}, spatialBlend={s?.spatialBlend}.");
    }
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+40;
        while(GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null)){if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout "+name);yield return null;}
        yield return new WaitForSecondsRealtime(1.1f);
    }
    void Move(Vector3 p){var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=p;cc.enabled=true;W.Hero.transform.forward=Vector3.forward;Physics.SyncTransforms();}
    void Energy(float amount){typeof(PowerUser).GetProperty("Energy").SetValue(W.Powers,amount);}
    void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);Check(A!=null&&A.Sources.Length==24,"Single automatic director, exactly 24 preallocated AudioSources.");
        int count=0;foreach(var cue in A.Tuning.Cues)
        {
            Check(cue.Group!=null&&cue.Clips!=null&&cue.Clips.Length>0&&cue.Clips.All(c=>c!=null&&c.length>0),$"{cue.Id} references real clips and a mixer group.");
            foreach(var clip in cue.Clips){if(cue.Spatial)Check(clip.channels==1,clip.name+" positional mono.");count++;}
        }
        Check(A.Tuning.Cues.Length==Enum.GetValues(typeof(AudioCue)).Length,$"All {A.Tuning.Cues.Length} cue types / {count} clip assignments populated (no silent placeholders).");
        foreach(var key in new[]{"MasterVolume","MusicVolume","SFXVolume","UIVolume","AmbientVolume"})Check(A.Tuning.Mixer.GetFloat(key,out float value),$"Exposed mixer parameter {key} is valid.");
        SourceCheck(AudioCue.Music);Check(PlayingSource(AudioCue.Music).timeSamples>0,"Music DSP sample cursor advances (not just bookkeeping).");
        var menu=FindAnyObjectByType<ModeScreens>();var disabledButton=new Button(()=>GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"))){name="verification-disabled"};disabledButton.SetEnabled(false);menu.Root.Add(disabledButton);yield return null;A.StopAll();Submit(disabledButton);Check(Playing(AudioCue.UiClick)==0&&!GameFlow.Instance.Loading,"Disabled UI CONTROL emits no click and launches nothing.");
        using(var hover=PointerOverEvent.GetPooled()){hover.target=menu.ModeButtons["hero"];menu.ModeButtons["hero"].SendEvent(hover);}SourceCheck(AudioCue.UiHover);
        Submit(menu.ModeButtons["hero"]);SourceCheck(AudioCue.UiClick);yield return Scene(GameFlow.CityScene);
        PrepareSession();
        Check(A.BoundWorld==W,"Director binds shipping world without gameplay/bootstrap edits.");
        if(Reload){SourceCheck(AudioCue.CityBed);Check(A.Tuning.Mixer.FindMatchingGroups("").Length==5,"SECOND PROCESS loads persisted mixer with all five groups.");Log("SECOND PROCESS asset/reference reload passed; fresh isolated save used.");yield break;}
        A.StopAll();yield return new WaitForSeconds(.2f);Check(Playing(AudioCue.Hit)==0,"Undamaged player CONTROL has no Hit source.");W.DamagePlayer(1);SourceCheck(AudioCue.Hit);
        A.StopAll();Energy(100);W.Hero.DebugSetResources(6,3,0);float before=Time.time;Check(W.Hero.TryPunch(),"Real paid punch accepted.");Check(Playing(AudioCue.Punch)==0,"Windup CONTROL: no punch-impact audio at activation.");
        yield return new WaitForSeconds(W.Hero.PunchWindupSeconds+.025f);SourceCheck(AudioCue.Punch);Log($"TIMING accepted={before:F4}, actual impact={W.Hero.LastImpactTime:F4}, source timeSamples={PlayingSource(AudioCue.Punch).timeSamples}; configured windup={W.Hero.PunchWindupSeconds*1000:F1}ms; audio callback is PunchImpacted.");
        A.StopAll();W.Hero.DebugSetResources(6,0,0);Check(!W.Hero.TryPunch(),"Zero-charge punch refused.");yield return new WaitForSeconds(.2f);Check(Playing(AudioCue.Punch)==0,"Zero-charge CONTROL plays no punch cue after windup.");
        // Hurricane Kick shares the Super Strength charge pool and cooldown with the punch, and fires HurricaneKickImpacted (not PunchImpacted).
        A.StopAll();Energy(100);W.Hero.DebugSetResources(6,3,0);Check(W.Hero.Charges==3&&W.Hero.Cooldown==0,"Kick setup CONTROL: shared Strength pool refilled to 3 charges and shared cooldown cleared.");float kickBefore=Time.time,kickPrior=W.Hero.LastKickImpactTime;Check(W.Hero.TryHurricaneKick(),"Real paid Hurricane Kick accepted.");Check(Playing(AudioCue.Punch)==0,"Kick windup CONTROL: no impact audio at kick activation.");
        yield return new WaitForSeconds(W.Hero.KickWindupSeconds+.025f);Check(W.Hero.LastKickImpactTime!=kickPrior&&W.Hero.LastKickImpactTime-kickBefore>=W.Hero.KickWindupSeconds-.001f,"Kick impact really fired after its windup (HurricaneKickImpacted).");SourceCheck(AudioCue.Punch);Log($"TIMING kick accepted={kickBefore:F4}, actual impact={W.Hero.LastKickImpactTime:F4}, source timeSamples={PlayingSource(AudioCue.Punch).timeSamples}; configured windup={W.Hero.KickWindupSeconds*1000:F1}ms; audio callback is HurricaneKickImpacted.");
        A.StopAll();W.Hero.DebugSetResources(6,0,0);Check(!W.Hero.TryHurricaneKick(),$"Zero-charge kick refused ('{W.Hero.LastKickResult}').");yield return new WaitForSeconds(W.Hero.KickWindupSeconds+.025f);Check(Playing(AudioCue.Punch)==0,"Zero-charge kick CONTROL plays no punch cue after the kick windup.");
        Check(W.Powers.Strength.Definition.ResourceCost==0,"Shipping Strength costs 0 energy: its rejection CONTROL is charges/cooldown, not energy. Flight uses fuel; Fire/Ice/Telekinesis energy controls follow.");
        var prop=GameObject.CreatePrimitive(PrimitiveType.Cube);prop.name="Audio damage control";prop.transform.position=W.Hero.transform.position+Vector3.right*3;prop.AddComponent<Rigidbody>().useGravity=false;var breakable=prop.AddComponent<BreakableProp>();breakable.Configure(W.Tuning.Props);A.StopAll();breakable.TakeDamage(1,null);Check(Playing(AudioCue.Destruction)==0,"Damaged-but-unbroken prop CONTROL emits no destruction.");breakable.TakeDamage(10000,null);SourceCheck(AudioCue.Destruction);yield return null;
        W.Progression.AddXp(1000);foreach(var p in W.Powers.Powers)if(!W.Progression.Owns(p.Definition))W.Progression.Buy(p.Definition);
        // Hero Forge gate CONTROL in the default Flight + Strength session: every owned cue-bound power is refused while
        // unequipped, with resources ready and a valid target, and emits NO cue and charges nothing.
        foreach(var binding in A.Tuning.Powers)
        {
            var power=W.Powers.Powers.First(p=>p.Definition.Effect==binding.Effect);power.Cooldown=0;power.Charges=3;Energy(100);
            var targetBody=GameObject.CreatePrimitive(PrimitiveType.Cube);targetBody.transform.position=W.Powers.AimOrigin+Vector3.forward*5;targetBody.AddComponent<Rigidbody>().useGravity=false;Physics.SyncTransforms();
            Check(W.Progression.Owns(power.Definition)&&!W.Powers.IsEquipped(power.Definition),$"{power.Definition.DisplayName} owned but NOT equipped (session loadout {W.Powers.EquippedA.Id} + {W.Powers.EquippedB.Id}).");
            A.StopAll();bool selected=W.Powers.Select(power),used=W.Powers.Use(power);
            Check(!selected&&!used&&W.Powers.Message=="Power not equipped"&&power.Charges==3&&power.Cooldown==0&&W.Powers.Energy==100&&W.Powers.HeldBody==null,$"CONTROL: unequipped {power.Definition.DisplayName} refused by the equip gate (\"{W.Powers.Message}\"); charges 3->{power.Charges}, energy 100->{W.Powers.Energy:F0}.");
            yield return null;Check(Playing(binding.Cue)==0,$"CONTROL: refused unequipped {power.Definition.DisplayName} plays NO {binding.Cue} cue.");Destroy(targetBody);
        }
        // Positive checks: equip the cue-bound powers in pairs through PlayerProgression.SetLoadout, each pair in a fresh session.
        // An odd remainder pairs with Flight so the flight checks below keep an equipped Flight.
        var pending=A.Tuning.Powers.ToList();
        while(pending.Count>0)
        {
        var batch=pending.Take(2).ToArray();pending.RemoveRange(0,batch.Length);
        var first=W.Powers.Powers.First(p=>p.Definition.Effect==batch[0].Effect).Definition;
        var second=batch.Length>1?W.Powers.Powers.First(p=>p.Definition.Effect==batch[1].Effect).Definition:W.Powers.Flight.Definition;
        yield return EquipSession(first,second);
        foreach(var binding in batch)
        {
            var power=W.Powers.Powers.First(p=>p.Definition.Effect==binding.Effect);W.Powers.Select(power);power.Cooldown=0;power.Charges=3;Energy(100);
            var targetBody=GameObject.CreatePrimitive(PrimitiveType.Cube);targetBody.transform.position=W.Powers.AimOrigin+Vector3.forward*5;targetBody.AddComponent<Rigidbody>().useGravity=false;Physics.SyncTransforms();
            A.StopAll();Check(W.Powers.Use(power),power.Definition.DisplayName+" paid activation succeeds.");SourceCheck(binding.Cue);W.Powers.Release(false);Destroy(targetBody);yield return null;
            A.StopAll();power.Cooldown=0;power.Charges=3;Energy(0);Check(!W.Powers.Use(power),power.Definition.DisplayName+" no-energy activation refused.");Check(Playing(binding.Cue)==0,power.Definition.DisplayName+" no-energy CONTROL emits no cue.");
        }
        }
        if(!W.Powers.IsEquipped(W.Powers.Flight.Definition))yield return EquipSession(W.Powers.Flight.Definition,W.Powers.Strength.Definition);
        // Holding F is not PowerUser.Use: exercise resource consumption, then its public presentation state boundary.
        Energy(100);W.Powers.Flight.Fuel=1;Check(W.Powers.ConsumeFlight(.1f),"Real flight resource consumption accepted.");
        typeof(SuperHeroController).GetProperty("PresentationState").SetValue(W.Hero,new HeroPresentationState(Vector3.forward*4,false,true));A.StopAll();yield return new WaitForSeconds(.12f);SourceCheck(AudioCue.FlightStart);SourceCheck(AudioCue.FlightLoop);
        typeof(SuperHeroController).GetProperty("PresentationState").SetValue(W.Hero,new HeroPresentationState(Vector3.zero,false,false));W.Powers.Flight.Fuel=0;Check(!W.Powers.ConsumeFlight(.1f),"Empty-fuel flight CONTROL refuses consumption.");yield return new WaitForSeconds(.7f);Check(Playing(AudioCue.FlightLoop)==0&&Playing(AudioCue.FlightStart)==0,"Not-flying CONTROL fades/stops flight sources.");
        var pose=W.Hero.GetComponent<HumanoidPresentation>();pose.VerificationState=new HeroPresentationState(Vector3.forward*6,true,false);A.StopAll();bool step=false;float until=Time.time+pose.Tuning.Cast.length/Mathf.Max(.1f,pose.Tuning.CastPlayback)+1;while(Time.time<until){yield return null;if(Playing(AudioCue.Footstep)>0){step=true;break;}}Check(step,$"Central distance accumulator plays real footstep sources: speed={pose.MeasuredSpeed:F2}, state={pose.State}, health={W.Health:F1} (waits for prior cast to finish).");pose.VerificationState=new HeroPresentationState(Vector3.zero,true,false);yield return new WaitForSeconds(.5f);Check(Playing(AudioCue.Footstep)==0,"Stationary CONTROL has no footsteps.");pose.VerificationState=null;
        A.StopAll();Move(W.City.Spawn+Vector3.up*5);camera.transform.position=W.Hero.transform.position+new Vector3(0,2,-8);W.Hero.enabled=true;
        float landDeadline=Time.time+5;while(Playing(AudioCue.Land)==0&&Time.time<landDeadline)yield return null;SourceCheck(AudioCue.Land);A.StopAll();Check(W.Hero.TryJump(),"Real grounded jump accepted.");SourceCheck(AudioCue.Jump);W.Hero.enabled=false;Move(new Vector3(0,100,0));camera.transform.position=W.Hero.transform.position+new Vector3(0,1,-8);
        // Backflip is grounded-only and fires BackflipStarted (not Jumped). Re-ground the hero with its real CharacterController first.
        A.StopAll();Move(W.City.Spawn+Vector3.up*2);W.Hero.ResetMotion();camera.transform.position=W.Hero.transform.position+new Vector3(0,2,-8);W.Hero.enabled=true;var heroBody=W.Hero.GetComponent<CharacterController>();yield return null;yield return null; // isGrounded is stale until the controller's own Move() runs after the teleport.
        Check(!heroBody.isGrounded,"Backflip setup CONTROL: hero is genuinely airborne after the 2m teleport (fresh contact state).");float groundDeadline=Time.time+5;while(!heroBody.isGrounded&&Time.time<groundDeadline)yield return null;yield return new WaitForSeconds(.3f);Check(heroBody.isGrounded,"Backflip setup CONTROL: hero has real ground contact.");
        A.StopAll();Check(W.Hero.TryBackflip(),"Real grounded backflip accepted.");SourceCheck(AudioCue.Jump);
        A.StopAll();Check(!W.Hero.TryBackflip(),$"Immediate second backflip refused ('{W.Hero.LastBackflipResult}').");Check(Playing(AudioCue.Jump)==0,"Refused backflip CONTROL plays no jump cue.");
        yield return new WaitForSeconds(HeroAbilityTuning.BackflipSeconds+.15f);groundDeadline=Time.time+3;while(!heroBody.isGrounded&&Time.time<groundDeadline)yield return null;A.StopAll();Check(!W.Hero.TryBackflip()&&W.Hero.LastBackflipResult=="Blocked: cooldown",$"Grounded backflip inside its cooldown refused ('{W.Hero.LastBackflipResult}', {W.Hero.BackflipCooldown:F2}s left).");Check(Playing(AudioCue.Jump)==0,"Cooldown-refused backflip CONTROL plays no jump cue.");
        W.Hero.enabled=false;Move(new Vector3(0,100,0));camera.transform.position=W.Hero.transform.position+new Vector3(0,1,-8);
        A.StopAll();W.DamagePlayer(W.Health);SourceCheck(AudioCue.Death);yield return new WaitForSeconds(W.Tuning.Movement.RespawnDelay+.2f);Move(new Vector3(0,100,0));
        W.AddHeat(-100);yield return new WaitForSeconds(.8f);float calm=A.Sources[0].volume,quiet=A.Sources[1].volume;Check(A.Sources[0].isPlaying&&!A.Sources[1].isPlaying&&quiet==0,"Heat 0 CONTROL: calm bed playing, siren stopped at volume 0.");
        W.AddHeat(5);yield return new WaitForSeconds(.8f);SourceCheck(AudioCue.SirenBed);Check(A.Sources[1].volume>quiet+.2f&&A.Sources[0].volume<calm,$"Heat 0->5: city {calm:F4}->{A.Sources[0].volume:F4}, siren {quiet:F4}->{A.Sources[1].volume:F4}; intensity={A.HeatIntensity:F4}.");
        A.StopAll();int cap=A.Tuning.Find(AudioCue.Destruction).MaxConcurrent;for(int i=0;i<cap+10;i++)A.Play(AudioCue.Destruction,W.Hero.transform.position);Check(Playing(AudioCue.Destruction)==cap,$"Concurrency CONTROL: {cap+10} simultaneous requests, exactly {Playing(AudioCue.Destruction)} actual sources playing (cap {cap}); pool remains {A.Sources.Length}.");
        A.StopAll();var near=A.Play(AudioCue.Punch,W.Hero.transform.position);Check(near!=null&&near.isPlaying,"Near spatial CONTROL plays.");Check(A.Play(AudioCue.Punch,new Vector3(10000,10000,10000))==null,"Far spatial CONTROL does not consume a voice.");
        foreach(var npc in W.Npcs)if(npc!=null&&!npc.Dead)npc.enabled=true;W.ReconcilePolice();
        camera.transform.position=new Vector3(-65,60,-80);camera.transform.LookAt(Vector3.zero);target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;camera.enabled=true;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        Log($"BENCH population civilians={W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Civilian)}, cops={W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Cop)}; single enabled camera rendering 1280x720 (no manual Camera.Render double-render).");
        double off1=0,on1=0,on2=0,off2=0;yield return Benchmark(false,"disabled A",v=>off1=v);yield return Benchmark(true,"enabled B",v=>on1=v);yield return Benchmark(true,"enabled B2",v=>on2=v);yield return Benchmark(false,"disabled A2",v=>off2=v);
        double off=(off1+off2)/2,on=(on1+on2)/2;Log($"AUDIO COST paired means: disabled={off:F2} FPS, enabled={on:F2} FPS, loss={off-on:F2} FPS; frame-time delta={1000/on-1000/off:F3}ms. {(off-on>2?"EXCEEDS requested ~2 FPS budget; investigation needed.":"Within requested ~2 FPS budget in this run; noise/Editor limits apply.")}");
        A.enabled=true;yield return new WaitForSecondsRealtime(1.1f);GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);Check(A.Sources.Length==24&&A.BoundWorld==null&&Playing(AudioCue.FlightLoop)==0&&Playing(AudioCue.CityBed)==0,"Return Home keeps one pool; no stale world/flight/city audio.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/villain"));yield return Scene(GameFlow.CityScene);W.Hero.enabled=false;
        var cop=W.Npcs.First(n=>n!=null&&n.Role==NpcRole.Cop&&!n.Dead);Move(cop.transform.position+Vector3.forward*.6f);camera=Camera.main;camera.GetComponent<ThirdPersonCamera>().enabled=false;camera.transform.position=W.Hero.transform.position+new Vector3(0,2,-5);
        // Balance pass: Villain patrol police only open fire from VillainPolice.HostileFromStars (1 star), so the gunshot check raises Heat to it.
        W.AddHeat(W.Tuning.Heat.VillainPolice.HostileFromStars-W.Heat);Check(cop.Hostile,$"Heat {W.Stars} star(s): patrol cop hostile (per-side police data).");
        A.StopAll();int shots=cop.Releases;float shotDeadline=Time.time+4;while(cop.Releases==shots&&Time.time<shotDeadline)yield return null;SourceCheck(AudioCue.Gunshot);var shotClips=A.Tuning.Find(AudioCue.Gunshot).Clips;Check(A.Sources.Any(s=>s.isPlaying&&shotClips.Contains(s.clip)&&Vector3.Distance(s.transform.position,cop.transform.position)<.5f)&&cop.LastReleaseHit&&W.Health<W.Tuning.Movement.Health,$"Cop gunshot is driven by a real hostile NPC attack/damage event (gunshot source at the releasing cop; health {W.Health:F0}).");
        // Spawn-time registration: Spawn and this check share one call stack, so the periodic Update refresh CANNOT have run in
        // between. Only the CityNpc.Spawned event can make a brand-new NPC tracked here (previously up to ActorRefreshInterval late).
        {var fresh=CityNpc.Spawn(W,W.Hero.transform.position+Vector3.forward*8f,NpcRole.Cop);Check(fresh!=null,"Fresh cop spawned for the registration check.");Check(A.Tracks(fresh),$"Freshly spawned NPC is tracked in the SAME call stack as its Spawn (periodic refresh interval is {A.Tuning.ActorRefreshInterval:F2}s, so it cannot have run).");Destroy(fresh.gameObject);yield return null;}
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);Check(FindObjectsByType<AudioDirector>().Length==1,"Mode switch CONTROL retains exactly one director/pool.");
        Log("LIMIT: no human has heard or judged the mix. Tests assert real source playback, clips, volumes and routing, not audible-device capture. Flight state is controlled at the public presentation boundary after real fuel checks; no hardware F-key automation. FPS is Editor throughput, not standalone performance.");
    }
    void PrepareSession()
    {
        W.Hero.enabled=false;Move(new Vector3(0,100,0));camera=Camera.main;camera.GetComponent<ThirdPersonCamera>().enabled=false;camera.transform.position=W.Hero.transform.position+new Vector3(0,1,-8);camera.transform.forward=Vector3.forward;
        foreach(var npc in W.Npcs)if(npc!=null){npc.enabled=false;if(npc.Agent.enabled&&npc.Agent.isOnNavMesh)npc.Agent.isStopped=true;}
    }
    // Loadouts are fixed per session: equip through the real pre-session API at Home, then start a fresh Hero session.
    IEnumerator EquipSession(PowerDefinition a,PowerDefinition b)
    {
        W.Hero.enabled=false;GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
        var profile=FindAnyObjectByType<ModeScreens>().Profile;var loadout=profile.Data.Loadout;
        Check(profile.Owns(a)&&profile.Owns(b)&&profile.SetLoadout(profile.SelectedHero,a,b,loadout.Primary,loadout.Secondary),$"Owned {a.DisplayName} + {b.DisplayName} equipped through PlayerProgression.SetLoadout before a fresh session.");
        Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")),"Fresh Hero session requested.");yield return Scene(GameFlow.CityScene);
        PrepareSession();
        Check(A.BoundWorld==W&&W.Powers.IsEquipped(a)&&W.Powers.IsEquipped(b)&&A.Sources.Length==24,$"Director rebinds the fresh session (same 24-source pool); session receives {W.Powers.EquippedA.Id} + {W.Powers.EquippedB.Id}.");
    }
    IEnumerator Benchmark(bool enabled,string label,Action<double> done)
    {
        A.enabled=enabled;yield return new WaitForSecondsRealtime(1.2f);var samples=new List<double>();var watch=System.Diagnostics.Stopwatch.StartNew();double last=0;int draws=0,maxVoices=0;
        while(watch.Elapsed.TotalSeconds<6)
        {
            // Same low-rate effects workload in both controls. Disabled director must reject it.
            if(samples.Count%15==0){var audible=camera.transform.position+camera.transform.forward*8;A.Play(AudioCue.Gunshot,audible);A.Play(AudioCue.Destruction,audible);}
            yield return null;double now=watch.Elapsed.TotalSeconds;samples.Add((now-last)*1000);last=now;draws+=UnityStats.drawCalls;maxVoices=Mathf.Max(maxVoices,A.Sources.Count(s=>s.isPlaying));
        }
        double fps=samples.Count/watch.Elapsed.TotalSeconds;samples.Sort();Log($"MEASURED audio {label}: frames={samples.Count}, seconds={watch.Elapsed.TotalSeconds:F3}, FPS={fps:F2}, p95={samples[(int)(samples.Count*.95)]:F3}ms, drawCalls={draws/(float)samples.Count:F1}, peak playing sources={maxVoices}.");
        Check(enabled?maxVoices>=4:maxVoices==0,enabled?"Enabled benchmark includes actual beds and simultaneous SFX.":"Disabled benchmark CONTROL has zero playing sources.");done(fps);
    }
}
#endif
