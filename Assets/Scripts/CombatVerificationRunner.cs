#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

/// Telegraphed combat: real scenes, real CityNpc AI and NavMesh, real WorldSession.DamagePlayer, real SuperHeroController
/// backflip. The hero's input is disabled (WorldSession.MenuOpen; no devices in batch mode); the hero is placed and moved
/// only through its CharacterController. Test-only harness conveniences are logged where used (Heal = reflection).
public sealed class CombatVerificationRunner : MonoBehaviour
{
    public string Folder;
    public Action<int> Finished;
    readonly List<string> output=new List<string>();
    WorldSession W=>WorldSession.Instance;
    GameFlow Flow=>GameFlow.Instance;
    EnemyRoster roster;EnemyArchetype rusher,gunner,brute;EndlessWaveDirector waves;HumanoidAnimationTuning anim;
    GameModeDefinition hero,villain,freePlay,endless,endlessVillain;
    string only;
    Vector3 heroSpot,crossing,lane=Vector3.forward,right=Vector3.right;
    RenderTexture target;
    static readonly System.Reflection.PropertyInfo healthProperty=typeof(WorldSession).GetProperty("Health");

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-combatOnly");only=at>=0&&at+1<args.Length?args[at+1]:null;
        var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Write();Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}
            if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Write();Finished(0);
    }
    void Log(string text){output.Add(text);Debug.Log("[COMBAT] "+text);}
    void Check(bool valid,string text){if(!valid)throw new Exception(text);Log("PASS "+text);}
    void Write(){File.WriteAllLines(Path.Combine(Folder,only==null?"results.txt":"results-"+only+".txt"),output);}

    // ---------------------------------------------------------------- helpers
    IEnumerator Scene(string name)
    {
        float deadline=Time.realtimeSinceStartup+40;
        while(Flow.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>deadline)throw new Exception("Scene timeout "+name);yield return null;}
        for(int i=0;i<5;i++)yield return null;
        if(W!=null)W.MenuOpen=true; // input disabled; SuperHeroController still integrates gravity and the backflip
    }
    IEnumerator Until(Func<bool> condition,float seconds,string what)
    {float end=Time.time+seconds;while(!condition()){if(Time.time>end)throw new Exception("Timeout ("+seconds+"s): "+what);yield return null;}}
    IEnumerator Frames(int n){for(int i=0;i<n;i++)yield return null;}
    void Move(Vector3 position){var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=position;cc.enabled=true;W.Hero.ResetMotion();Physics.SyncTransforms();}
    void Face(Vector3 direction){direction.y=0;if(direction.sqrMagnitude>1e-4f)W.Hero.transform.rotation=Quaternion.LookRotation(direction.normalized);}
    /// TEST HARNESS: restores the player's health through reflection (WorldSession has no heal API); logged wherever used.
    void Heal(){healthProperty.SetValue(W,W.Tuning.Movement.Health);}
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    static Vector3 FlatDir(Vector3 v){v.y=0;return v.sqrMagnitude>1e-6f?v.normalized:Vector3.forward;}
    IEnumerator Clear()
    {
        foreach(var npc in W.Npcs.ToArray())if(npc!=null)Destroy(npc.gameObject);
        W.AttackTokens.BudgetOverride=-1;
        yield return null;yield return null;
    }
    IEnumerator Ground()
    {
        var cc=W.Hero.GetComponent<CharacterController>();
        yield return Frames(3);
        yield return Until(()=>cc.isGrounded&&!W.Hero.BackflipActive,4,"hero grounded");
    }
    IEnumerator BackflipReady(){yield return Until(()=>W.Hero.BackflipCooldown<=0f&&!W.Hero.BackflipActive,4,"backflip cooldown");}
    CityNpc Spawn(EnemyArchetype archetype,Vector3 position,NpcRole role=NpcRole.Criminal)
    {
        var npc=CityNpc.Spawn(W,position,role,archetype);
        if(npc==null)throw new Exception("Spawn failed at "+position);
        npc.AlwaysAggro=true;return npc;
    }
    Vector3 OnMesh(Vector3 p){if(!NavMesh.SamplePosition(p,out var hit,3f,NavMesh.AllAreas))throw new Exception("No NavMesh near "+p);return hit.position;}
    void Capture(string name)
    {
        var cam=Camera.main;var follow=cam.GetComponent<ThirdPersonCamera>();follow.enabled=true;
        if(target==null){target=new RenderTexture(1280,720,24){name="Combat 1280x720"};target.Create();}
        var previousTarget=cam.targetTexture;cam.targetTexture=target;cam.Render();cam.targetTexture=previousTarget;
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());Destroy(image);
        Log($"CAPTURE {name}.png (real ThirdPersonCamera placement {cam.transform.position} fwd {cam.transform.forward}, 1280x720)");
    }
    string Clip(EnemyArchetype a)=>a.Kind==AttackKind.Ranged?"Shoot":a.Kind==AttackKind.Slam?"Hurricane Kick":"Punch";
    bool CueAt(AudioCue cue,Vector3 position)
    {
        var audio=AudioDirector.Instance;if(audio==null)return false;var clips=audio.Tuning.Find(cue).Clips;
        return audio.Sources.Any(s=>s.isPlaying&&clips.Contains(s.clip)&&Vector3.Distance(s.transform.position,position)<.5f);
    }
    int Windups()=>W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Phase==AttackPhase.Windup);

    IEnumerator Checks()
    {
        roster=Resources.Load<EnemyRoster>("Enemies/EnemyRoster");anim=Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning");
        rusher=Resources.Load<EnemyArchetype>("Enemies/Rusher");gunner=Resources.Load<EnemyArchetype>("Enemies/Gunner");brute=Resources.Load<EnemyArchetype>("Enemies/Brute");
        waves=Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves");
        var catalog=Resources.LoadAll<GameModeDefinition>("Modes");GameModeDefinition Mode(string id)=>catalog.First(m=>m.Id==id);
        hero=Mode("hero");villain=Mode("villain");freePlay=Mode("free-play");endless=Mode("endless-fight");endlessVillain=Mode("endless-fight-villain");
        var robber=Resources.Load<EnemyArchetype>("Enemies/Robber");
        // Balance pass: city/encounter criminals use the Robber archetype (Rusher body, trigger reach covering the 3 m capture
        // radius); Endless waves still pick Rusher/Gunner/Brute from their own composition table.
        Check(roster!=null&&rusher!=null&&gunner!=null&&brute!=null&&robber!=null&&roster.Criminal==robber&&roster.Cop==gunner&&roster.PursuingHero==brute,
            $"Roster asset maps Criminal->{roster?.Criminal?.name}, Cop->{roster?.Cop?.name}, PursuingHero->{roster?.PursuingHero?.name}; token budget {roster?.MaxConcurrentAttackers}, ring {roster?.WaitRingRadius}m.");
        foreach(var a in new[]{rusher,gunner,brute,robber})
            Log($"ARCHETYPE {a.name}: kind={a.Kind} speed={a.MoveSpeed} health x{a.HealthMultiplier} damage x{a.DamageMultiplier} trigger={a.TriggerDistance} reach={a.Reach} radius={a.Radius} range={a.Range} windup={a.WindupSeconds}s cooldown={a.CooldownSeconds}s knockback={a.Knockback}m band={a.PreferredDistance}+/-{a.PreferredBand} scale={a.VisualScale} accent={a.Accent}");
        yield return Scene(GameFlow.HomeScene);
        SampleShootClip();
        if(only==null||only=="archetypes"||only=="tokens")
        {
            yield return Sandbox();
            if(only!="tokens"){yield return Behaviour();yield return Trials();yield return Readability();}
            if(only!="archetypes")yield return Tokens();
            Flow.Home();yield return Scene(GameFlow.HomeScene);
        }
        if(only==null||only=="sessions")yield return Sessions();
        if(only==null||only=="endless")yield return Endless();
        Log("LIMIT: batch mode has no keyboard/mouse; the hero is driven only through SuperHeroController.TryBackflip and CharacterController.Move. Feel (readability at speed, fairness under a real player's reaction time) is not measured.");
    }

    /// Where the Shooting Gun clip actually fires (supports ShootImpactSeconds): left-hand position sampled at 30 Hz.
    void SampleShootClip()
    {
        var model=Instantiate(anim.Model);var animator=model.GetComponent<Animator>();var hand=animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var lines=new List<string>();float bestRise=0,riseAt=0,bestOut=0,outAt=0;Vector3 last=Vector3.zero;
        for(int f=0;f*(1f/30f)<=anim.ShootEndSeconds+1e-4f;f++)
        {
            float t=f/30f;anim.Shoot.SampleAnimation(model,t);var p=model.transform.InverseTransformPoint(hand.position);
            if(t>=anim.ShootStartSeconds-1e-4f)
            {
                lines.Add($"t={t:F3} x={p.x:F3} y={p.y:F3}");
                if(-p.x>bestOut){bestOut=-p.x;outAt=t;}
                if(f>0&&p.y-last.y>bestRise){bestRise=p.y-last.y;riseAt=t;}
            }
            last=p;
        }
        Destroy(model);
        Log("SHOOT CLIP left hand (model space) "+string.Join(" | ",lines));
        Log($"SHOOT CLIP: arm fully out (min x) at t={outAt:F3}s; steepest upward recoil step ends at t={riseAt:F3}s; configured ShootImpactSeconds={anim.ShootImpactSeconds:F3}s.");
        Check(anim.ShootImpactSeconds>anim.ShootStartSeconds&&anim.ShootImpactSeconds<anim.ShootEndSeconds,"ShootImpactSeconds lies inside the played Shoot window.");
    }

    // ---------------------------------------------------------------- sandbox city (Free Play data, 0 civilians, no police)
    IEnumerator Sandbox()
    {
        var sandbox=Instantiate(freePlay);sandbox.name="free-play (combat sandbox copy)";sandbox.Civilians=0;sandbox.SpawnPolice=false;
        Check(Flow.Select(sandbox),"Sandbox: runtime COPY of the free-play definition (0 civilians, no police, no encounters) selected through GameFlow.");
        yield return Scene(GameFlow.CityScene);
        Check(W.Npcs.Count==0&&W.Progression.Data.Side==PlayerSide.Hero&&W.Stars==0,"Sandbox city: 0 NPCs, Hero side (Criminals hostile), 0 Heat stars.");
        // District world: street crossings of the spawn district, nearest the spawn first (was the four 3x3 crossings).
        int home=W.City.DistrictAt(W.City.Spawn);
        foreach(var site in W.City.EncounterSites.Where((p,i)=>W.City.EncounterSiteDistrict[i]==home).OrderBy(p=>(p-W.City.Spawn).sqrMagnitude))
        {
            if(!NavMesh.SamplePosition(site,out var centre,3,NavMesh.AllAreas))continue;
            Vector3 near=site-lane*12,far=site+lane*8;
            if(!NavMesh.SamplePosition(near,out var a,2,NavMesh.AllAreas)||!NavMesh.SamplePosition(far,out var b,2,NavMesh.AllAreas))continue;
            var path=new NavMeshPath();
            if(!NavMesh.CalculatePath(b.position,a.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete||path.corners.Length!=2)continue;
            if(Physics.Linecast(a.position+Vector3.up*1.2f,b.position+Vector3.up*1.2f)||Physics.Linecast(a.position+Vector3.up*1.2f,a.position+Vector3.up*1.2f+right*4.5f))continue;
            heroSpot=a.position;crossing=centre.position;
            Log($"LANE: hero at {heroSpot}, enemies spawn along +Z (straight NavMesh path, 2 corners, clear 1.2m line of sight; 4.5m clear to +X for strafing); crossing {crossing}.");
            break;
        }
        Check(crossing!=Vector3.zero,"Found a straight, unobstructed test lane through a street crossing.");
    }

    // ---------------------------------------------------------------- distinct behaviour, measured
    sealed class Measured{public float Speed,MeanVelocity,MaxHealth,BandMin=float.MaxValue,BandMax;public int Windups;}
    IEnumerator Behaviour()
    {
        var results=new Dictionary<EnemyArchetype,Measured>();
        foreach(var a in new[]{rusher,gunner,brute})
        {
            yield return Clear();Heal();Move(heroSpot);Face(lane);yield return Ground();
            var npc=Spawn(a,heroSpot+lane*20);var m=new Measured{MaxHealth=npc.MaxHealth};results[a]=m;
            float t0=-1,d0=0,t1=-1,d1=0;var velocities=new List<float>();float end=Time.time+15;
            while(t1<0)
            {
                if(Time.time>end)throw new Exception(a.name+" never approached");
                float d=Flat(npc.transform.position,W.Hero.transform.position);
                if(t0<0&&d<=19f){t0=Time.time;d0=d;}
                else if(t0>=0&&d<=13f){t1=Time.time;d1=d;}
                else if(t0>=0)velocities.Add(new Vector2(npc.Agent.velocity.x,npc.Agent.velocity.z).magnitude);
                yield return null;
            }
            m.Speed=(d0-d1)/(t1-t0);m.MeanVelocity=velocities.Count>0?velocities.Average():0;
            Log($"MEASURED {a.name} approach: {d0:F2}m -> {d1:F2}m in {t1-t0:F3}s = {m.Speed:F2} m/s (mean NavMeshAgent speed {m.MeanVelocity:F2} m/s over {velocities.Count} frames; configured {a.MoveSpeed}); spawned max health {m.MaxHealth:F2}.");
            if(a==gunner)
            {
                // Held distance: player stands still for 6 s after the Gunner first enters its band.
                yield return Until(()=>Flat(npc.transform.position,W.Hero.transform.position)<=a.PreferredDistance+a.PreferredBand,10,"gunner reaches band");
                float hold=Time.time+6,hp0=W.Health;int w0=npc.Windups;var samples=new List<float>();
                while(Time.time<hold){float d=Flat(npc.transform.position,W.Hero.transform.position);samples.Add(d);m.BandMin=Mathf.Min(m.BandMin,d);m.BandMax=Mathf.Max(m.BandMax,d);yield return null;}
                m.Windups=npc.Windups-w0;
                Log($"MEASURED Gunner held distance over 6.0s ({samples.Count} frames, player stationary): min {m.BandMin:F2}m, max {m.BandMax:F2}m, mean {samples.Average():F2}m (band {a.PreferredDistance-a.PreferredBand}-{a.PreferredDistance+a.PreferredBand}m); it fired {m.Windups} telegraphed shots from there, player health {hp0:F0} -> {W.Health:F0}.");
                Check(m.BandMin>=a.PreferredDistance-a.PreferredBand-.5f&&m.BandMax<=a.PreferredDistance+a.PreferredBand+.5f&&m.BandMin>roster.WaitRingRadius&&m.Windups>=1,
                    $"Gunner HOLDS its band while the player stands still (min {m.BandMin:F2} / max {m.BandMax:F2} m, never inside the {roster.WaitRingRadius}m melee ring) and still attacks ({m.Windups} shots).");
            }
        }
        var r=results[rusher];var g=results[gunner];var b=results[brute];
        Check(r.Speed>g.Speed&&g.Speed>b.Speed,$"Approach speed measured: Rusher {r.Speed:F2} > Gunner {g.Speed:F2} > Brute {b.Speed:F2} m/s.");
        foreach(var pair in results)Check(Mathf.Abs(pair.Value.Speed-pair.Key.MoveSpeed)<=pair.Key.MoveSpeed*.1f,$"{pair.Key.name} measured speed {pair.Value.Speed:F2} m/s within 10% of its asset value {pair.Key.MoveSpeed}.");
        float standard=W.Tuning.Npcs.CopHealth;
        Check(Mathf.Abs(r.MaxHealth/g.MaxHealth-rusher.HealthMultiplier/gunner.HealthMultiplier)<.001f&&Mathf.Abs(b.MaxHealth/g.MaxHealth-brute.HealthMultiplier/gunner.HealthMultiplier)<.001f&&Mathf.Abs(g.MaxHealth-standard*gunner.HealthMultiplier)<.001f,
            $"Spawned max health (Criminal role, 0 stars): Rusher {r.MaxHealth:F1} : Gunner {g.MaxHealth:F1} : Brute {b.MaxHealth:F1} = {r.MaxHealth/g.MaxHealth:F2} : 1 : {b.MaxHealth/g.MaxHealth:F2} (standard enemy {standard}).");
    }

    // ---------------------------------------------------------------- hit / dodge / asymmetry / late / death
    enum Trial{Hit,Backflip,Strafe,Late,Death}
    IEnumerator Trials()
    {
        yield return RunTrial(rusher,Trial.Hit);yield return RunTrial(rusher,Trial.Backflip);yield return RunTrial(rusher,Trial.Late);yield return RunTrial(rusher,Trial.Death);
        yield return RunTrial(brute,Trial.Hit);yield return RunTrial(brute,Trial.Backflip);yield return RunTrial(brute,Trial.Late);yield return RunTrial(brute,Trial.Death);
        yield return RunTrial(gunner,Trial.Hit);yield return RunTrial(gunner,Trial.Strafe);yield return RunTrial(gunner,Trial.Backflip);yield return RunTrial(gunner,Trial.Late);yield return RunTrial(gunner,Trial.Death);
    }
    IEnumerator RunTrial(EnemyArchetype a,Trial trial)
    {
        string label=$"{a.name} {trial.ToString().ToUpperInvariant()}";
        yield return Clear();Heal();Move(heroSpot);Face(lane);yield return Ground();yield return BackflipReady();
        float spawnAt=a.Kind==AttackKind.Ranged?a.PreferredDistance:5f;
        var npc=Spawn(a,heroSpot+lane*spawnAt);var pose=npc.GetComponent<HumanoidPresentation>();
        npc.Freeze(1.1f); // AudioDirector registers new NPCs once per ActorRefreshInterval (1 s); hold so the cue check is fair
        float hp=W.Health;
        float timeout=Time.time+8;
        while(npc.Phase!=AttackPhase.Windup)
        {
            if(Time.time>timeout)throw new Exception(label+": no windup");
            if(W.Health!=hp)throw new Exception(label+": damage before any windup");
            yield return null;
        }
        // Same frame as the NPC committed (coroutines resume after every Update).
        int startFrame=Time.frameCount;Vector3 heroStart=W.Hero.transform.position;float startDistance=Flat(npc.transform.position,heroStart);
        Check(W.AttackTokens.Holds(npc)&&W.AttackTokens.Count==1,$"{label}: windup started at {startDistance:F2}m holding an attack token (pool {W.AttackTokens.Count}/{W.AttackTokens.Budget}).");
        var telegraph=npc.Telegraph;
        Check(telegraph!=null&&telegraph.Visible&&telegraph.Renderer.enabled&&telegraph.Renderer.gameObject.activeInHierarchy&&telegraph.Renderer.sharedMaterial==CityMaterials.Get(CityColor.Fire)&&telegraph.Renderer.GetComponent<Collider>()==null,
            $"{label}: telegraph renderer active at windup start, shared palette material '{telegraph?.Renderer.sharedMaterial.name}', no collider.");
        if(AudioDirector.Instance!=null)AudioDirector.Instance.StopAll();
        if(trial==Trial.Backflip){Face(npc.transform.position-W.Hero.transform.position);Check(W.Hero.TryBackflip(),$"{label}: hero facing the enemy backflips at windup start ('{W.Hero.LastBackflipResult}').");}
        Vector3 perp=Vector3.Cross(Vector3.up,FlatDir(npc.CommittedDirection)).normalized;
        var cc=W.Hero.GetComponent<CharacterController>();
        bool captured=false,clipChecked=false;int releases=npc.Releases;float killAt=npc.WindupStartTime+a.WindupSeconds*.5f,wouldRelease=npc.WindupStartTime+a.WindupSeconds;
        while(npc!=null&&npc.Releases==releases)
        {
            if(W.Health!=hp)throw new Exception($"{label}: damage DURING the windup ({hp} -> {W.Health}) at {Time.time-npc.WindupStartTime:F3}s");
            if(!clipChecked&&Time.frameCount>startFrame)
            {
                clipChecked=true;
                Check(pose.AttackState==Clip(a)&&pose.State==Clip(a)&&pose.Animator.GetCurrentAnimatorStateInfo(0).IsName(Clip(a)),$"{label}: '{Clip(a)}' clip playing from windup start at fitted rate {pose.AttackFittedRate:F3} (impact marker scheduled for release).");
            }
            if(trial==Trial.Strafe&&npc.Phase==AttackPhase.Windup)cc.Move(perp*W.Tuning.Movement.WalkSpeed*Time.deltaTime);
            if(trial==Trial.Hit&&!captured&&Time.time-npc.WindupStartTime>=a.WindupSeconds*.6f){captured=true;Capture("windup-"+a.name.ToLowerInvariant());Check(telegraph.Visible,$"{label}: telegraph still visible at {(Time.time-npc.WindupStartTime)/a.WindupSeconds:P0} of the windup (scale {telegraph.Renderer.transform.localScale}).");}
            if(trial==Trial.Death&&Time.time>=killAt)break;
            if(Time.time>wouldRelease+2)throw new Exception(label+": no release");
            yield return null;
        }
        if(trial==Trial.Death)
        {
            float progress=(Time.time-npc.WindupStartTime)/a.WindupSeconds;
            npc.Damage(npc.Health,null);
            Check(npc.Dead&&!telegraph.Visible&&!telegraph.Renderer.gameObject.activeInHierarchy&&!W.AttackTokens.Holds(npc)&&W.AttackTokens.Count==0&&npc.Cancels==1&&npc.Phase!=AttackPhase.Windup,
                $"{label}: killed at {progress:P0} of the windup -> telegraph hidden, token released (pool {W.AttackTokens.Count}), windup cancelled.");
            yield return Until(()=>Time.time>=wouldRelease+.3f,3,"past would-be release");
            Check(W.Health==hp&&npc.Releases==0&&pose.DeathCount==1,$"{label}: NO damage at its would-be release (+0.3s): health {hp:F0} -> {W.Health:F0}, releases {npc.Releases}; Death clip played.");
            yield break;
        }
        // Release frame (coroutine resumes right after the NPC's Update that released).
        float lost=hp-W.Health;Vector3 heroAtRelease=W.Hero.transform.position;
        Check(!telegraph.Visible&&!telegraph.Renderer.gameObject.activeInHierarchy&&W.AttackTokens.Count==0,$"{label}: telegraph renderer hidden and token returned at release.");
        Check(Mathf.Abs(npc.LastMeasuredWindup-a.WindupSeconds)<=Mathf.Max(.04f,Time.deltaTime*1.5f),$"{label}: measured windup (release - windup start) = {npc.LastMeasuredWindup*1000:F1} ms vs asset {a.WindupSeconds*1000:F0} ms.");
        string geometry=a.Kind==AttackKind.Ranged?$"capsule distance outside the {a.Radius}m line half-width = {npc.LastReleaseMargin:F2}m":$"horizontal distance from disc centre {Flat(heroAtRelease,npc.CommittedPoint):F2}m vs radius {a.Radius}+player {W.Tuning.Movement.Radius} (margin {npc.LastReleaseMargin:F2}m)";
        float travelled=Flat(heroAtRelease,heroStart);
        var cue=a.Kind==AttackKind.Ranged?AudioCue.Gunshot:AudioCue.Punch;
        Check(CueAt(cue,npc.transform.position),$"{label}: '{cue}' cue playing at the enemy on the release frame ({(npc.LastReleaseHit?"hit":"miss")}): Attacked fires at release, hit or miss.");
        switch(trial)
        {
            case Trial.Hit:
            case Trial.Late:
                Check(npc.LastReleaseHit&&Mathf.Abs(lost-npc.ContactDamage)<.001f,$"{label}: player standing still inside the attack took EXACTLY {lost:F2} (= {a.name} damage {npc.ContactDamage:F2}) at release; {geometry}.");
                break;
            case Trial.Backflip:
                if(a.Kind==AttackKind.Ranged)Check(npc.LastReleaseHit&&Mathf.Abs(lost-npc.ContactDamage)<.001f,$"{label} ASYMMETRY CONTROL: backflipping straight away along the locked aim line ({travelled:F2}m travelled) is STILL HIT for {lost:F2}; {geometry}.");
                else Check(!npc.LastReleaseHit&&lost==0&&npc.LastReleaseMargin>0,$"{label} DODGE: backflip at windup start -> NO damage; player travelled {travelled:F2}m; at release {geometry}.");
                break;
            case Trial.Strafe:
                Check(!npc.LastReleaseHit&&lost==0&&npc.LastReleaseMargin>0,$"{label} DODGE: strafing {travelled:F2}m perpendicular to the locked aim line during the windup -> NO damage; at release {geometry}.");
                break;
        }
        yield return Frames(3);
        Log($"{label}: presentation impact marker frame {pose.LastAttackImpactFrame} t={pose.LastAttackImpactTime:F4} vs release frame {npc.LastReleaseFrame} t={npc.LastReleaseTime:F4} ({(pose.LastAttackImpactTime-npc.LastReleaseTime)*1000:F1} ms, {pose.LastAttackImpactFrame-npc.LastReleaseFrame} frames).");
        if(trial==Trial.Hit)Check(pose.LastAttackImpactFrame>=0&&Mathf.Abs(pose.LastAttackImpactFrame-npc.LastReleaseFrame)<=2,$"{label}: clip impact marker lands within 2 rendered frames of the damage.");
        if(trial==Trial.Late)
        {
            Face(npc.transform.position-W.Hero.transform.position);float after=W.Health;
            Check(W.Hero.TryBackflip(),$"{label} CONTROL: backflip only AFTER release (the {lost:F2} hit already landed at release).");
            yield return new WaitForSeconds(HeroAbilityTuning.BackflipSeconds);
            Check(W.Health<=after&&hp-W.Health>=npc.ContactDamage-.001f,$"{label} CONTROL: late backflip cannot undo it: health {hp:F0} -> {W.Health:F0} (the timing window is real).");
        }
        if(trial==Trial.Hit)
        {
            yield return new WaitForSeconds(a.Knockback>0?a.KnockbackSeconds+.1f:.3f);
            float shove=Flat(W.Hero.transform.position,heroAtRelease);
            if(a.Knockback>0)Check(shove>=a.Knockback*.8f,$"{label}: knockback moved the player {shove:F2}m (asset {a.Knockback}m).");
            else Check(shove<.1f,$"{label}: no knockback CONTROL (moved {shove:F2}m).");
        }
    }

    // ---------------------------------------------------------------- readability
    IEnumerator Readability()
    {
        yield return Clear();Heal();Move(heroSpot);Face(lane);yield return Ground();
        var set=new[]{rusher,gunner,brute};var npcs=new List<CityNpc>();
        for(int i=0;i<3;i++)
        {
            var npc=Spawn(set[i],heroSpot+lane*7+right*((i-1)*2.6f));npc.Freeze(60);npcs.Add(npc);
        }
        yield return null;
        foreach(var npc in npcs){npc.transform.rotation=Quaternion.LookRotation(-lane);}
        yield return new WaitForSeconds(.6f);
        Capture("archetypes-side-by-side");
        var c=W.Tuning.Npcs;var joints=new List<Material>();
        foreach(var npc in npcs)
        {
            var a=npc.Archetype;var pose=npc.GetComponent<HumanoidPresentation>();var capsule=npc.GetComponent<CapsuleCollider>();
            var renderers=npc.GetComponentsInChildren<SkinnedMeshRenderer>();var joint=renderers.First(r=>r.name.Contains("Joints")).sharedMaterial;var body=renderers.First(r=>!r.name.Contains("Joints")).sharedMaterial;joints.Add(joint);
            Check(npc.transform.localScale==Vector3.one&&pose.VisualRoot.name=="Landing squash (visual only)"&&pose.VisualRoot.parent==npc.transform&&Mathf.Abs(pose.VisualRoot.localScale.x-a.VisualScale)<1e-4f,
                $"{a.name}: silhouette x{pose.VisualRoot.localScale.x:F2} on the visual root only (physics root scale {npc.transform.localScale}).");
            Check(Mathf.Abs(capsule.radius-c.Radius*a.VisualScale)<1e-4f&&Mathf.Abs(capsule.height-c.Height*a.VisualScale)<1e-4f&&Mathf.Abs(npc.Agent.radius-c.Radius*a.VisualScale)<1e-4f,
                $"{a.name}: body matches size: capsule {capsule.height:F2}x{capsule.radius:F3}m, NavMeshAgent radius {npc.Agent.radius:F3}m.");
            Check(joint==CityMaterials.Get(a.Accent)&&body==CityMaterials.Get(CityColor.Red),$"{a.name}: joints accent '{joint.name}', body '{body.name}' (role colour) - shared palette materials.");
        }
        Check(joints.Distinct().Count()==3,"Three distinct joint accents across the archetypes.");
    }

    // ---------------------------------------------------------------- attack-token budget
    List<CityNpc> Crowd()
    {
        var list=new List<CityNpc>();var dirs=new[]{Vector3.forward,Vector3.right,Vector3.back,Vector3.left};var dists=new[]{11f,15f,19f};
        var kinds=new[]{rusher,rusher,rusher,rusher,rusher,gunner,gunner,brute,brute};
        var path=new NavMeshPath();int k=0;
        foreach(var d in dists)foreach(var dir in dirs)
        {
            if(k>=kinds.Length)break;
            if(!NavMesh.SamplePosition(crossing+dir*d,out var hit,2,NavMesh.AllAreas))continue;
            if(!NavMesh.CalculatePath(hit.position,crossing,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
            var npc=Spawn(kinds[k++],hit.position);
            npc.SetCombatStats(npc.MaxHealth,0f); // zero damage so the stationary player survives the 10 s sample; attacks still resolve
            list.Add(npc);
        }
        if(list.Count<kinds.Length)throw new Exception("Crowd spawn: only "+list.Count);
        return list;
    }
    sealed class CrowdSample{public int MaxWindups,MaxHolders,Frames,Releases,Hits,Attackers;public float MeanWaiting,WaitingMin=float.MaxValue,WaitingMax;public int WaitingSamples;}
    IEnumerator SampleCrowd(List<CityNpc> crowd,float seconds,CrowdSample s,string capture)
    {
        var start=crowd.ToDictionary(n=>n,n=>n.Releases);var hitStart=crowd.ToDictionary(n=>n,n=>n.Hits);double waiting=0;float end=Time.time+seconds;bool shot=false;
        while(Time.time<end)
        {
            int windups=Windups();s.MaxWindups=Mathf.Max(s.MaxWindups,windups);s.MaxHolders=Mathf.Max(s.MaxHolders,W.AttackTokens.Count);s.Frames++;
            foreach(var n in crowd)
                if(n!=null&&!n.Dead&&n.Archetype.Kind!=AttackKind.Ranged&&n.Phase==AttackPhase.Approach)
                {float d=Flat(n.transform.position,W.Hero.transform.position);waiting+=d;s.WaitingSamples++;s.WaitingMin=Mathf.Min(s.WaitingMin,d);s.WaitingMax=Mathf.Max(s.WaitingMax,d);}
            if(capture!=null&&!shot&&windups>=Mathf.Min(2,s.MaxWindups)&&windups>0&&Time.time>end-seconds*.5f){shot=true;Capture(capture);}
            yield return null;
        }
        s.MeanWaiting=s.WaitingSamples>0?(float)(waiting/s.WaitingSamples):0;
        s.Releases=crowd.Where(n=>n!=null).Sum(n=>n.Releases-start[n]);s.Hits=crowd.Where(n=>n!=null).Sum(n=>n.Hits-hitStart[n]);s.Attackers=crowd.Count(n=>n!=null&&n.Releases>start[n]);
    }
    IEnumerator Tokens()
    {
        // Engage-timeout CONTROL: a token holder that cannot reach the player (hero hovering 3.2 m up, above the Rusher's
        // reach) must give its token back after EngageTimeoutSeconds instead of holding it forever.
        yield return Clear();Heal();W.Hero.enabled=false;Move(crossing+Vector3.up*3.2f);
        var climber=Spawn(rusher,crossing+lane*3f);
        yield return Until(()=>climber.Phase==AttackPhase.Engage&&W.AttackTokens.Holds(climber),3,"rusher engages the hovering hero");
        float engagedAt=Time.time;
        yield return Until(()=>!W.AttackTokens.Holds(climber),roster.EngageTimeoutSeconds+.5f,"engage timeout returns the token");
        Check(climber.Windups==0&&W.Health==W.Tuning.Movement.Health&&Time.time-engagedAt>=roster.EngageTimeoutSeconds-.05f,
            $"Engage-timeout CONTROL: Rusher engaged the unreachable hovering hero, never wound up, and returned its token after {Time.time-engagedAt:F2} s (EngageTimeoutSeconds {roster.EngageTimeoutSeconds}).");
        W.Hero.enabled=true;
        yield return Clear();Heal();Move(crossing);Face(lane);yield return Ground();
        var crowd=Crowd();
        Log($"CROWD: {crowd.Count} hostile enemies ({crowd.Count(n=>n.Archetype==rusher)} Rushers, {crowd.Count(n=>n.Archetype==gunner)} Gunners, {crowd.Count(n=>n.Archetype==brute)} Brutes) spawned 11-19 m out along the four street arms; explicit damage 0 (attacks still commit, release and resolve hits).");
        yield return new WaitForSeconds(3f);
        var two=new CrowdSample();yield return SampleCrowd(crowd,10f,two,"crowd-budget-2");
        Log($"MEASURED budget {W.AttackTokens.Budget}: {two.Frames} frames over 10 s: max simultaneous windups {two.MaxWindups}, max tokens held {two.MaxHolders}, releases {two.Releases} ({two.Hits} would-be hits) by {two.Attackers} different enemies; waiting melee enemies (no token): mean {two.MeanWaiting:F2} m from the player (min {two.WaitingMin:F2}, max {two.WaitingMax:F2}, {two.WaitingSamples} samples), ring {roster.WaitRingRadius} m.");
        Check(two.MaxWindups==roster.MaxConcurrentAttackers&&two.MaxHolders<=roster.MaxConcurrentAttackers,$"Budget {roster.MaxConcurrentAttackers}: observed max simultaneous windups = {two.MaxWindups} (tokens held never above {two.MaxHolders}).");
        Check(Mathf.Abs(two.MeanWaiting-roster.WaitRingRadius)<=roster.RingSlack,$"Waiting enemies hold the ring: mean {two.MeanWaiting:F2} m vs radius {roster.WaitRingRadius} +/- {roster.RingSlack} m.");
        Check(two.Attackers>=4,$"The budget rotates through the crowd: {two.Attackers} different enemies attacked in 10 s.");
        // Leaked-token CONTROL: kill a token holder mid-windup.
        CityNpc victim=null;yield return Until(()=>(victim=crowd.FirstOrDefault(n=>n!=null&&!n.Dead&&n.Phase==AttackPhase.Windup))!=null,6,"a windup to interrupt");
        int granted=W.AttackTokens.Granted;float killed=Time.time;victim.Damage(victim.Health,null);
        Check(!W.AttackTokens.Holds(victim)&&W.AttackTokens.Count<roster.MaxConcurrentAttackers,$"Killed token holder {victim.Archetype.name} mid-windup: its token is back in the pool (held {W.AttackTokens.Count}/{W.AttackTokens.Budget}).");
        yield return Until(()=>W.AttackTokens.Granted>granted&&W.AttackTokens.LastGrantee!=victim,victim.Archetype.CooldownSeconds,"another enemy takes a token within one cooldown");
        Check(true,$"Leaked-token CONTROL: {W.AttackTokens.LastGrantee.Archetype.name} got a token {(W.AttackTokens.LastGrantTime-killed)*1000:F0} ms after the kill (one {victim.Archetype.name} cooldown = {victim.Archetype.CooldownSeconds*1000:F0} ms).");
        // Freeze CONTROL.
        CityNpc frozen=null;yield return Until(()=>(frozen=crowd.FirstOrDefault(n=>n!=null&&!n.Dead&&n.Phase==AttackPhase.Windup))!=null,6,"a windup to freeze");
        int cancels=frozen.Cancels;frozen.Freeze(1.5f);
        Check(frozen.Phase!=AttackPhase.Windup&&!W.AttackTokens.Holds(frozen)&&!frozen.Telegraph.Visible&&frozen.Cancels==cancels+1,$"Freeze CONTROL: frozen {frozen.Archetype.name} cancels its windup, hides the telegraph, releases its token.");
        // Pause CONTROL (session pause cancels every pending windup and returns all tokens).
        yield return Until(()=>Windups()>0,6,"a windup before pausing");
        W.Mode.SetPaused(true);yield return null;
        Check(Windups()==0&&W.AttackTokens.Count==0&&crowd.All(n=>n==null||n.Telegraph==null||!n.Telegraph.Visible),$"Pause CONTROL: 0 windups, 0 tokens held, every telegraph hidden while paused (timeScale {Time.timeScale}).");
        W.Mode.SetPaused(false);yield return null;
        yield return Allocations(crowd);
        // Budget CONTROL: 99.
        yield return Clear();Move(crossing);yield return Ground();W.AttackTokens.BudgetOverride=99;
        crowd=Crowd();yield return new WaitForSeconds(3f);
        var open=new CrowdSample();yield return SampleCrowd(crowd,10f,open,"crowd-budget-99");
        Log($"MEASURED budget {W.AttackTokens.Budget} CONTROL: max simultaneous windups {open.MaxWindups}, max tokens held {open.MaxHolders}, releases {open.Releases} by {open.Attackers} enemies; waiting mean {open.MeanWaiting:F2} m ({open.WaitingSamples} samples).");
        Check(open.MaxWindups>=roster.MaxConcurrentAttackers+2,$"Budget CONTROL: with the budget raised to 99 the observed max simultaneous windups rises to {open.MaxWindups} (budget {roster.MaxConcurrentAttackers}: {two.MaxWindups}).");
        W.AttackTokens.BudgetOverride=-1;yield return Clear();
    }

    /// Managed allocations per frame with the crowd cycling through windups vs the same crowd frozen (CONTROL).
    /// Profiler counter "GC Allocated In Frame" covers the whole main thread (Editor included), so only the DIFFERENCE is meaningful.
    IEnumerator Allocations(List<CityNpc> crowd)
    {
        var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame");
        yield return Frames(10);
        long active=0,frozen=0;int activeFrames=0,frozenFrames=0,windupFrames=0,releases0=0,releases1=0;
        for(int i=0;i<crowd.Count;i++)if(crowd[i]!=null)releases0+=crowd[i].Releases;
        float end=Time.time+3f;
        while(Time.time<end){yield return null;active+=recorder.LastValue;activeFrames++;for(int i=0;i<W.Npcs.Count;i++)if(W.Npcs[i]!=null&&W.Npcs[i].Phase==AttackPhase.Windup){windupFrames++;break;}}
        for(int i=0;i<crowd.Count;i++)if(crowd[i]!=null)releases1+=crowd[i].Releases;
        for(int i=0;i<crowd.Count;i++)if(crowd[i]!=null&&!crowd[i].Dead)crowd[i].Freeze(30);
        yield return Frames(10);
        end=Time.time+3f;
        while(Time.time<end){yield return null;frozen+=recorder.LastValue;frozenFrames++;}
        bool valid=recorder.Valid;recorder.Dispose();
        double a=(double)active/activeFrames,b=(double)frozen/frozenFrames;
        Log($"MEASURED managed allocation (profiler 'GC Allocated In Frame', valid={valid}): crowd ACTIVE {a:F1} B/frame over 3 s / {activeFrames} frames ({windupFrames} frames with a windup, {releases1-releases0} releases) vs same crowd FROZEN {b:F1} B/frame over {frozenFrames} frames; difference {a-b:F1} B/frame.");
    }

    // ---------------------------------------------------------------- real Hero / Villain sessions
    IEnumerator Sessions()
    {
        Check(Flow.Select(hero),"Real Hero mode selected.");yield return Scene(GameFlow.CityScene);
        yield return new WaitForSeconds(.5f);
        W.AddHeat(roster!=null?W.Tuning.Heat.HeroThreshold:4);W.ReconcilePolice();yield return null;
        MapCheck("HERO",PlayerSide.Hero);
        Flow.Home();yield return Scene(GameFlow.HomeScene);
        Check(Flow.Select(villain),"Real Villain mode selected.");yield return Scene(GameFlow.CityScene);
        yield return new WaitForSeconds(.5f);
        W.AddHeat(W.Tuning.Heat.HeroThreshold);W.ReconcilePolice();yield return null;
        MapCheck("VILLAIN",PlayerSide.Villain);
        // Real hostile Villain-mode cop (Gunner): telegraphed shot, and the audio cue follows the release.
        var audio=AudioDirector.Instance;var cop=W.Npcs.Where(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Cop&&n.Encounter==null).OrderBy(n=>Flat(n.transform.position,W.Hero.transform.position)).First();
        cop.Agent.Warp(W.City.Spawn);Move(cop.transform.position+Vector3.forward*6f);Face(cop.transform.position-W.Hero.transform.position);
        foreach(var n in W.Npcs)if(n!=null&&n!=cop&&n.Role!=NpcRole.Civilian)n.Freeze(20);
        // The teleports above must not inherit a windup the cop locked on the player's OLD position: freeze cancels it.
        cop.Freeze(.3f);Heal();float hp=W.Health,movedAt=Time.time;
        yield return Until(()=>cop.Phase==AttackPhase.Windup&&cop.WindupStartTime>movedAt,6,"villain cop windup");
        int releases=cop.Releases;
        Check(W.Health==hp&&cop.Telegraph.Visible,$"Villain mode: hostile Cop ({cop.Archetype.name}) commits a telegraphed windup; no damage yet.");
        if(audio!=null)audio.StopAll();
        yield return Until(()=>cop.Releases>releases,cop.WindupSeconds+1,"villain cop release");
        Check(W.Health<hp&&cop.LastReleaseHit,$"Villain mode: the Cop's release hit the standing player: health {hp:F0} -> {W.Health:F0}.");
        Check(CueAt(AudioCue.Gunshot,cop.transform.position),"Cop gunshot cue is playing AT the releasing cop on its release frame (real hostile attack; AudioDirector subscribes to CityNpc.Attacked).");
        Flow.Home();yield return Scene(GameFlow.HomeScene);
    }
    void MapCheck(string label,PlayerSide side)
    {
        var alive=W.Npcs.Where(n=>n!=null&&!n.Dead).ToList();
        foreach(var n in alive)
        {
            var expected=n.Role==NpcRole.Civilian?null:roster.For(n.Role);
            if(n.Archetype!=expected)throw new Exception($"{label}: {n.Role} has archetype {(n.Archetype!=null?n.Archetype.name:"none")}, roster says {(expected!=null?expected.name:"none")}");
        }
        string Count(NpcRole role)=>$"{alive.Count(n=>n.Role==role)} {role} ({string.Join("/",alive.Where(n=>n.Role==role).Select(n=>n.Archetype!=null?n.Archetype.name:"-").Distinct())}, hostile {alive.Count(n=>n.Role==role&&n.Hostile)})";
        Check(alive.Any(n=>n.Role==NpcRole.Criminal)&&alive.Any(n=>n.Role==NpcRole.Cop)&&alive.Any(n=>n.Role==NpcRole.PursuingHero)&&W.Progression.Data.Side==side,
            $"{label} session roles map to roster archetypes: {Count(NpcRole.Criminal)}; {Count(NpcRole.Cop)}; {Count(NpcRole.PursuingHero)}; {Count(NpcRole.Civilian)}.");
        bool heroSide=side==PlayerSide.Hero;
        Check(alive.Where(n=>n.Role==NpcRole.Criminal).All(n=>n.Hostile==heroSide)&&alive.Where(n=>n.Role==NpcRole.Cop||n.Role==NpcRole.PursuingHero).All(n=>n.Hostile!=heroSide),$"{label}: role still decides hostility (Criminals hostile={heroSide}, Cops/PursuingHero hostile={!heroSide}).");
    }

    // ---------------------------------------------------------------- Endless composition + wave-5 FPS
    EndlessWaveState State=>W.Mode.Director as EndlessWaveState;
    IEnumerator StartNextWave(){var s=State;int wave=s.Wave;W.Mode.Tick(s.IntermissionLeft+.001f);Check(s.Wave==wave+1&&!s.Intermission,$"Wave {s.Wave} started.");yield break;}
    IEnumerator ClearWave()
    {
        var s=State;int wave=s.Wave,guard=0;
        while(s.Wave==wave&&!s.Intermission){foreach(var npc in s.Alive.ToArray())npc.Damage(100000,W.Powers);W.Mode.Tick(.01f);yield return null;if(++guard>300)throw new Exception("wave never cleared");}
    }
    void Composition(string label,int wave)
    {
        var s=State;var alive=s.Alive;
        var expected=Enumerable.Range(0,alive.Count).Select(i=>waves.ArchetypeFor(wave,i)).ToList();
        var shares=waves.Composition.Where(c=>c.Archetype!=null&&c.Weight>0&&wave>=c.FromWave).ToList();int total=shares.Sum(c=>c.Weight);
        var counts=alive.GroupBy(n=>n.Archetype).ToDictionary(g=>g.Key,g=>g.Count());
        string mix=string.Join(", ",counts.Select(p=>$"{p.Key.name} {p.Value}"));
        Check(alive.Select(n=>n.Archetype).SequenceEqual(expected),$"{label}: spawned archetypes in spawn order = the asset's composition ({string.Join(",",alive.Select(n=>n.Archetype.name.Substring(0,1)))}).");
        foreach(var share in shares)
        {
            int n=counts.TryGetValue(share.Archetype,out var v)?v:0;float ideal=alive.Count*(float)share.Weight/total;
            Check(n>=Mathf.FloorToInt(ideal)&&n<=Mathf.CeilToInt(ideal),$"{label}: {share.Archetype.name} x{n} = weight {share.Weight}/{total} of {alive.Count} (ideal {ideal:F2}).");
        }
        Check(counts.Keys.All(a=>shares.Any(c=>c.Archetype==a)),$"{label}: no archetype before its FromWave; mix {mix}.");
        foreach(var npc in alive)
            if(Mathf.Abs(npc.MaxHealth-waves.HealthFor(wave)*npc.Archetype.HealthMultiplier)>.001f||Mathf.Abs(npc.ContactDamage-waves.DamageFor(wave)*npc.Archetype.DamageMultiplier)>.001f)
                throw new Exception($"{label}: {npc.Archetype.name} health {npc.MaxHealth}/damage {npc.ContactDamage} != wave x multiplier");
        Log($"RECORD {label}: {alive.Count} alive, mix {mix}; per-archetype health {string.Join(", ",counts.Keys.Select(a=>$"{a.name} {waves.HealthFor(wave)*a.HealthMultiplier:F1}"))}; damage {string.Join(", ",counts.Keys.Select(a=>$"{a.name} {waves.DamageFor(wave)*a.DamageMultiplier:F2}"))} (wave health {waves.HealthFor(wave):F2}, damage {waves.DamageFor(wave):F2}).");
    }
    IEnumerator Endless()
    {
        Check(Flow.Select(endless),"Real Endless Fight (Hero) selected.");yield return Scene(GameFlow.CityScene);
        var s=State;Move(s.Arena+new Vector3(0,3.2f,-8));
        yield return StartNextWave();Composition("ENDLESS WAVE 1",1);
        Check(s.Alive.All(n=>n.Archetype==rusher&&n.Role==NpcRole.Criminal),"Wave 1 is all Rushers (Criminal role).");
        for(int w=1;w<5;w++){yield return ClearWave();Heal();yield return StartNextWave();}
        Composition("ENDLESS WAVE 5",5);
        Check(s.Alive.All(n=>n.Role==NpcRole.Criminal&&n.Hostile)&&s.Alive.Select(n=>n.Archetype).Distinct().Count()==3,"Wave 5 mixes all three archetypes, all hostile Criminals (role decides hostility/colour).");
        // Wave-5 FPS at the REAL gameplay camera with the crowd fighting the grounded hero (health restored each frame).
        Move(s.Arena);Face(lane);yield return Ground();
        var t=new Timing();yield return MeasureFps("Endless wave 5",t);
        Log($"FPS wave 5 (gameplay camera, grounded hero in the fight): {t.Fps:F2} FPS, mean {t.Mean:F2} ms, p95 {t.P95:F2} ms; windups during sample {t.Windups}, max simultaneous windups {t.MaxWindups} (budget {W.AttackTokens.Budget}); skinning {t.Skinning:F3} ms, batch-mode overhead {t.BatchMode:F3} ms, PlayerLoop {t.PlayerLoop:F3} ms.");
        Check(t.MaxWindups<=roster.MaxConcurrentAttackers&&t.Windups>0,$"Real Endless wave 5: {t.Windups} telegraphed attacks during the FPS sample, never more than {t.MaxWindups} winding up at once.");
        // Session end releases every token.
        yield return Until(()=>W.AttackTokens.Count>0,5,"a token holder before the session ends");
        int held=W.AttackTokens.Count;var world=W;var crowd=s.Alive.ToList();
        world.DamagePlayer(world.Health);yield return null;
        Check(world.Mode.Ended&&world.AttackTokens.Count==0&&crowd.All(n=>n==null||n.Phase!=AttackPhase.Windup)&&crowd.All(n=>n==null||n.Telegraph==null||!n.Telegraph.Visible),$"Session end CONTROL: {held} tokens held at the defeat -> 0 held, no windups, telegraphs hidden on the next frame.");
        yield return Scene(GameFlow.ResultsScene);Flow.Home();yield return Scene(GameFlow.HomeScene);
        // Villain side gets the full mix too (Cops).
        Check(Flow.Select(endlessVillain),"Real Endless Fight (Villain) selected.");yield return Scene(GameFlow.CityScene);
        s=State;Move(s.Arena+new Vector3(0,3.2f,-8));
        yield return StartNextWave();for(int w=1;w<3;w++){yield return ClearWave();Heal();yield return StartNextWave();}
        Composition("VILLAIN ENDLESS WAVE 3",3);
        Check(s.Alive.All(n=>n.Role==NpcRole.Cop&&n.Hostile)&&s.Alive.Select(n=>n.Archetype).Distinct().Count()==3,"Villain Endless wave 3: hostile Cops in all three archetypes.");
        W.Mode.ReturnHome();yield return Scene(GameFlow.HomeScene);
    }
    sealed class Timing{public double Fps,Mean,P95,Skinning,BatchMode,PlayerLoop;public int Frames,Windups,MaxWindups;}
    sealed class Recorded{public string Name;public ProfilerRecorder Recorder;public double Sum;}
    IEnumerator MeasureFps(string label,Timing timing)
    {
        // PerformanceProfileRunner method: REAL ThirdPersonCamera placement, camera disabled, one manual Render() per frame.
        var cam=Camera.main;var follow=cam.GetComponent<ThirdPersonCamera>();follow.enabled=true;
        if(target==null){target=new RenderTexture(1280,720,24){name="Combat 1280x720"};target.Create();}
        cam.targetTexture=target;cam.enabled=false;
        var crowd=State.Alive.ToList();var starts=crowd.ToDictionary(n=>n,n=>n.Windups);
        var warm=System.Diagnostics.Stopwatch.StartNew();while(warm.Elapsed.TotalSeconds<2){Heal();cam.Render();yield return null;}
        var times=new List<double>();var watch=System.Diagnostics.Stopwatch.StartNew();double prior=0;
        while(watch.Elapsed.TotalSeconds<4){Heal();cam.Render();timing.MaxWindups=Mathf.Max(timing.MaxWindups,Windups());yield return null;double now=watch.Elapsed.TotalSeconds;times.Add((now-prior)*1000);prior=now;}
        watch.Stop();
        timing.Frames=times.Count;timing.Fps=times.Count/watch.Elapsed.TotalSeconds;timing.Mean=times.Average();var sorted=times.OrderBy(x=>x).ToList();timing.P95=sorted[Mathf.Min(sorted.Count-1,(int)(sorted.Count*.95f))];
        string[] wanted={"PostLateUpdate.UpdateAllSkinnedMeshes","PostLateUpdate.BatchModeUpdate","PlayerLoop"};
        var all=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(all);
        var recorders=new List<Recorded>();
        foreach(var name in wanted)foreach(var h in all)if(ProfilerRecorderHandle.GetDescription(h).Name==name){recorders.Add(new Recorded{Name=name,Recorder=new ProfilerRecorder(h,1,ProfilerRecorderOptions.Default)});break;}
        foreach(var r in recorders)if(!r.Recorder.IsRunning)r.Recorder.Start();
        int frames=0;var profiled=System.Diagnostics.Stopwatch.StartNew();
        while(profiled.Elapsed.TotalSeconds<3){Heal();cam.Render();timing.MaxWindups=Mathf.Max(timing.MaxWindups,Windups());yield return null;foreach(var r in recorders)if(r.Recorder.Valid)r.Sum+=r.Recorder.LastValue;frames++;}
        foreach(var r in recorders)r.Recorder.Dispose();
        double Ms(string name){var r=recorders.Find(x=>x.Name==name);return r!=null&&frames>0?r.Sum/frames/1e6:double.NaN;}
        timing.Skinning=Ms(wanted[0]);timing.BatchMode=Ms(wanted[1]);timing.PlayerLoop=Ms(wanted[2]);
        timing.Windups=crowd.Where(n=>n!=null).Sum(n=>n.Windups-starts[n]);
        Heal();cam.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.Combine(Folder,"endless-wave-5-gameplay-camera.png"),image.EncodeToPNG());Destroy(image);
        Log($"MEASURED {label}: single-render FPS={timing.Fps:F2}, frames={timing.Frames}; humanoids={FindObjectsByType<HumanoidPresentation>().Length}; camera {cam.transform.position} fwd {cam.transform.forward}; hero {W.Hero.transform.position}; {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName}. CAPTURE endless-wave-5-gameplay-camera.png. TEST HARNESS: player health restored by reflection every frame so the crowd keeps attacking.");
        cam.enabled=true;cam.targetTexture=null;
    }
}
#endif
