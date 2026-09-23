#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

/// Free Play + Endless Fight: real Home buttons, real GameFlow/scene loads, real NPC AI damage, real save files.
/// Every assertion reads live game state (WorldSession, CityNpc, the save file on disk, the retained UI tree).
/// Time is accelerated only through GameModeSession.Tick (the same call its Update makes).
public sealed class ModeExpansionVerificationRunner : MonoBehaviour
{
    public bool Reload;
    public string Folder;
    public Action<int> Finished;
    readonly List<string> output=new List<string>();
    WorldSession W=>WorldSession.Instance;
    GameFlow Flow=>GameFlow.Instance;
    ModeScreens ui;
    RenderTexture uiTarget, gameTarget;
    GameModeDefinition hero, villain, freePlay, endless, endlessVillain;
    EndlessWaveDirector waves;
    string only;
    string ExpectedPath=>Path.Combine(Folder,"save-expected.json");

    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-modeExpansionOnly");only=at>=0&&at+1<args.Length?args[at+1]:null;
        var stack=new Stack<IEnumerator>();stack.Push(Reload?ReloadChecks():Checks());
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
    void Log(string text){output.Add(text);Debug.Log("[MODE-EXPANSION] "+text);}
    void Check(bool valid,string text){if(!valid)throw new Exception(text);Log("PASS "+text);}
    void Write(){File.WriteAllLines(Path.Combine(Folder,Reload?"reload.txt":only==null?"results.txt":"results-"+only+".txt"),output);}

    // ---------------------------------------------------------------- helpers
    IEnumerator Scene(string name)
    {
        float deadline=Time.realtimeSinceStartup+40;
        while(Flow.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>deadline)throw new Exception("Scene timeout "+name+" (active: "+SceneManager.GetActiveScene().name+")");yield return null;}
        yield return null;
        if(W!=null)W.Hero.enabled=false; // no keyboard/mouse in batch mode; the hero is placed explicitly below
    }
    IEnumerator Menu()
    {
        ui=FindAnyObjectByType<ModeScreens>();Check(ui!=null,"Menu component exists in "+SceneManager.GetActiveScene().name);
        if(uiTarget!=null){uiTarget.Release();Destroy(uiTarget);}uiTarget=new RenderTexture(1280,720,24){name="Verification UI"};uiTarget.Create();ui.Panel.targetTexture=uiTarget;
        for(int i=0;i<15;i++)yield return null;
    }
    void Submit(Button button){using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}}
    void Capture(RenderTexture target,string name)
    {
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());Destroy(image);Log("CAPTURE "+name+".png (actual render target)");
    }
    void Move(Vector3 position){var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=position;cc.enabled=true;Physics.SyncTransforms();}
    int Count(NpcRole role)=>W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==role);
    int Hostiles(NpcRole role)=>W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==role&&n.Hostile);
    static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
    ProgressSave Disk()=>JsonUtility.FromJson<ProgressSave>(File.ReadAllText(WorldSession.VerificationSavePath));
    /// Advances the session clock through GameModeSession.Tick in 1 s steps while real frames keep running.
    IEnumerator Advance(GameModeSession session,float seconds)
    {
        float done=0;int steps=0;
        while(done<seconds-1e-4f&&!session.Ended){float dt=Mathf.Min(1f,seconds-done);session.Tick(dt);done+=dt;if(++steps%10==0)yield return null;}
        for(int i=0;i<10;i++)yield return null;
    }
    IEnumerator WaitRealtime(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)yield return null;}
    IEnumerator Respawn()
    {
        float until=Time.realtimeSinceStartup+W.Tuning.Movement.RespawnDelay+5;
        while(W!=null&&W.PlayerDead){if(Time.realtimeSinceStartup>until)throw new Exception("Respawn timeout");yield return null;}
    }

    IEnumerator Checks()
    {
        var catalog=Resources.LoadAll<GameModeDefinition>("Modes");
        GameModeDefinition Mode(string id)=>catalog.First(m=>m.Id==id);
        hero=Mode("hero");villain=Mode("villain");freePlay=Mode("free-play");endless=Mode("endless-fight");endlessVillain=Mode("endless-fight-villain");
        waves=Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves");
        yield return Scene(GameFlow.HomeScene);yield return Menu();
        Check(ui.Profile.Data.Level==1&&ui.Profile.Data.SessionsPlayed==0&&ui.Profile.Data.ModeRecords.Count==0,"Fresh isolated save CONTROL: level 1, 0 sessions, 0 mode records.");
        foreach(var id in new[]{"free-play","endless-fight","endless-fight-villain"})
        {
            var button=ui.ModeButtons[id];
            Check(button.enabledInHierarchy&&button.worldBound.width>0&&button.worldBound.yMin>ui.ModeButtons["hero"].worldBound.yMax,$"Home shows ENABLED '{id}' in the data-driven extras row below the side cards (rect {button.worldBound}).");
        }
        Check(ui.Root.Query<Label>().ToList().All(l=>l.text!="COMING SOON"),"No COMING SOON placeholder remains on Home.");
        Capture(uiTarget,"home-extras");
        if(only!="endless"){yield return FreePlay();yield return FreePlayControls();}
        if(only!="freeplay"){yield return EndlessHero();yield return EndlessVillain();}
        Log("LIMIT: no hardware keyboard/mouse in batch mode. H key = WorldSession.RequestSideSwitch (the method the H handler calls); pause buttons = PrototypeHUD.ChoosePause (the method the IMGUI buttons call). Session time is accelerated via GameModeSession.Tick; NPC AI, attacks, respawn and police response run in real frames.");
    }

    // ---------------------------------------------------------------- FREE PLAY
    IEnumerator FreePlay()
    {
        Submit(ui.ModeButtons["free-play"]);yield return Scene(GameFlow.CityScene);
        var session=W.Mode;var d=session.Definition;
        Check(d==freePlay&&Flow.ActiveMode==freePlay&&d.Rules==null&&d.Encounters.Length==0,"Real Home button -> GameFlow.Select launched Free Play with Rules=null and 0 encounter definitions.");
        Check(!W.Progression.SideLocked&&W.Progression.Data.Side==PlayerSide.Hero,$"Free Play takes the profile side ({W.Progression.Data.Side}) and does NOT lock it.");
        Check(Count(NpcRole.Civilian)==d.Civilians&&Count(NpcRole.Cop)==W.Tuning.Heat.FriendlyPatrolCount,$"Population: {Count(NpcRole.Civilian)} civilians, {Count(NpcRole.Cop)} patrol cops.");
        float advance=hero.SpawnInterval*3;
        yield return Advance(session,advance);
        int events=FindObjectsByType<CrimeEvent>().Length,encounters=FindObjectsByType<CrimeEncounter>().Length,criminals=Count(NpcRole.Criminal);
        Check(session.Elapsed>=advance&&!session.Ended&&W.Crimes.Count==0&&events==0&&encounters==0&&criminals==0,
            $"Advanced {session.Elapsed:F0}s (3x Hero SpawnInterval {hero.SpawnInterval}s): crimes={W.Crimes.Count}, CrimeEvent objects={events}, CrimeEncounter objects={encounters}, criminals={criminals}; session not ended.");
        var objectives=PrototypeHUD.ObjectiveLines(W);
        Check(objectives.Count==0&&PrototypeHUD.DirectorLine(W)==null&&(d.Hud&ModeHud.Objectives)==0&&(d.Hud&ModeHud.Heat)!=0,"HUD: no objective/timer lines, no director line; Heat shown.");
        // Death -> respawn, more times than Hero's DefeatLimit.
        for(int i=1;i<=hero.DefeatLimit+1;i++)
        {
            W.DamagePlayer(W.Health);Check(W.PlayerDead,$"Death {i}: player actually dead (health {W.Health}).");
            yield return Respawn();
            Check(!W.PlayerDead&&W.Health==W.Tuning.Movement.Health&&!session.Ended&&SceneManager.GetActiveScene().name==GameFlow.CityScene&&session.Defeats==i,$"Death {i} -> respawned at full health {W.Health:F0}; session NOT ended (defeats={session.Defeats}, DefeatLimit={d.DefeatLimit}).");
        }
        // Heat stays active: a real prop break raises Heat and the police respond on their own interval.
        float heat=W.Heat;int cops=Count(NpcRole.Cop);
        var prop=FindObjectsByType<BreakableProp>().OrderBy(p=>(p.transform.position-W.Hero.transform.position).sqrMagnitude).First();
        prop.TakeDamage(100000,W.Powers);
        Check(W.Heat>heat,$"Real BreakableProp break -> WorldSession.OnDestruction: Heat {heat:F2} -> {W.Heat:F2} ({W.Stars} star).");
        float until=Time.realtimeSinceStartup+W.Tuning.Heat.ResponseInterval+3;
        while(Count(NpcRole.Cop)<=cops&&Time.realtimeSinceStartup<until)yield return null;
        int expectedCops=W.Tuning.Heat.FriendlyPatrolCount+W.Stars*W.Tuning.Heat.CopsPerStar;
        Check(Count(NpcRole.Cop)==expectedCops&&Count(NpcRole.Cop)>cops,$"Police respond in real time: cops {cops} -> {Count(NpcRole.Cop)} (= patrol {W.Tuning.Heat.FriendlyPatrolCount} + {W.Stars} star x {W.Tuning.Heat.CopsPerStar}).");
        // Side switch flips Data.Side AND which NPCs are hostile.
        var far=W.City.Sidewalks.OrderByDescending(p=>(p-W.Hero.transform.position).sqrMagnitude).First();
        var criminal=CityNpc.Spawn(W,far,NpcRole.Criminal);criminal.Freeze(120);
        foreach(var npc in W.Npcs)if(npc!=null)npc.Freeze(120); // keep the newly hostile side from interfering with later steps
        Check(W.Progression.Data.Side==PlayerSide.Hero&&criminal.Hostile&&Hostiles(NpcRole.Cop)==0&&Hostiles(NpcRole.Civilian)==0,$"Before switch (Hero): criminal hostile, 0/{Count(NpcRole.Cop)} cops hostile, 0 civilians hostile.");
        string message=W.RequestSideSwitch();
        Check(message.StartsWith("Side changed")&&W.Progression.Data.Side==PlayerSide.Villain&&Disk().Side==PlayerSide.Villain,$"H action: '{message}' -> Data.Side=Villain, saved to disk.");
        Check(!criminal.Hostile&&Hostiles(NpcRole.Cop)==Count(NpcRole.Cop)&&Count(NpcRole.Cop)>0&&Hostiles(NpcRole.Civilian)==0,$"After switch (Villain): criminal NOT hostile, {Hostiles(NpcRole.Cop)}/{Count(NpcRole.Cop)} cops hostile.");
        message=W.RequestSideSwitch();
        Check(message=="Side switch cooling down"&&W.Progression.Data.Side==PlayerSide.Villain,"Immediate second switch refused by the existing cooldown CONTROL.");
        // Pause: Resume + Return home only.
        session.SetPaused(true);var options=PrototypeHUD.PauseOptions(session);
        Check(options.SequenceEqual(new[]{PrototypeHUD.ResumeOption,PrototypeHUD.HomeOption}),"Free Play pause menu offers exactly: "+string.Join(" | ",options));
        PrototypeHUD.ChoosePause(session,PrototypeHUD.HomeOption);yield return Scene(GameFlow.HomeScene);
        Check(W==null&&Flow.ActiveMode==null&&Disk().LastModeId=="free-play","Pause 'Return home' reached the Home scene; city unloaded.");
        // SideFromProfile + ShowResults=false: relaunch starts on the saved side, and even a results request goes straight Home.
        Check(Flow.Select(freePlay),"Free Play relaunch accepted by GameFlow.Select.");yield return Scene(GameFlow.CityScene);
        Check(W.Progression.Data.Side==PlayerSide.Villain&&!W.Progression.SideLocked,"Relaunch starts as Villain: the side comes from the saved profile.");
        W.Mode.EndToResults();
        float deadline=Time.realtimeSinceStartup+30;while(Flow.Loading||SceneManager.GetActiveScene().name==GameFlow.CityScene){if(Time.realtimeSinceStartup>deadline)throw new Exception("end timeout");yield return null;}
        Check(SceneManager.GetActiveScene().name==GameFlow.HomeScene,"ShowResults=false: an explicit results request lands on "+SceneManager.GetActiveScene().name+", not Results.");
        yield return Scene(GameFlow.HomeScene);
    }

    IEnumerator FreePlayControls()
    {
        // CONTROL: Hero mode, identical accelerated time, DOES spawn encounters and DOES lock the side.
        Check(Flow.Select(hero),"Hero mode selected.");yield return Scene(GameFlow.CityScene);
        var session=W.Mode;int initial=W.Crimes.Count(c=>c!=null&&!c.Resolved);
        yield return Advance(session,hero.SpawnInterval*3);
        int after=W.Crimes.Count(c=>c!=null&&!c.Resolved)+W.Mode.Failures+W.Mode.Successes;
        Check(initial==hero.InitialEncounters&&after>initial&&FindObjectsByType<CrimeEncounter>().Length>0,$"Hero CONTROL: same {session.Elapsed:F0}s advance -> encounters {initial} at start, {after} spawned in total; CrimeEncounter objects={FindObjectsByType<CrimeEncounter>().Length}.");
        string message=W.RequestSideSwitch();
        Check(message.StartsWith("Side is set by this mode")&&W.Progression.Data.Side==PlayerSide.Hero&&W.Progression.SideLocked&&!W.Progression.SwitchSide(),$"Hero CONTROL: side switch BLOCKED ('{message}'); side stays Hero although the saved profile side was Villain.");
        Check(PrototypeHUD.ObjectiveLines(W).Any(l=>l.Contains("remaining")),"Hero CONTROL: HUD shows the objective/timer lines.");
        session.SetPaused(true);var options=PrototypeHUD.PauseOptions(session);
        Check(options.Length==3&&options.Contains(PrototypeHUD.ResultsOption),"Hero CONTROL: pause menu keeps the results option: "+string.Join(" | ",options));
        PrototypeHUD.ChoosePause(session,PrototypeHUD.HomeOption);yield return Scene(GameFlow.HomeScene);
        // GameFlow.Select guard CONTROLS: relaxed only for definitions that spawn no encounters.
        var broken=ScriptableObject.CreateInstance<GameModeDefinition>();broken.Id="verification-broken";broken.Encounters=new EncounterDefinition[0];
        Check(broken.InitialEncounters>0&&broken.Rules==null&&!Flow.Select(broken)&&!Flow.Loading,"Guard CONTROL: an encounter-spawning definition with no Rules/Encounters is still rejected.");
        var hidden=Instantiate(freePlay);hidden.Playable=false;
        Check(!Flow.Select(hidden)&&!Flow.Loading,"Guard CONTROL: Playable=false is still rejected.");
        Destroy(broken);Destroy(hidden);
        yield return Menu();
    }

    // ---------------------------------------------------------------- ENDLESS
    sealed class WaveSample { public int Wave, Alive; public float Health, MaxHealth, Damage; }
    EndlessWaveState State=>W.Mode.Director as EndlessWaveState;
    Vector3 HoverPoint=>State.Arena+new Vector3(0,3.2f,-8);
    void Hover(){Move(HoverPoint);W.Hero.transform.rotation=Quaternion.identity;}
    IEnumerator StartNextWave()
    {
        var s=State;int wave=s.Wave;
        Check(s.Intermission,$"Wave {wave} is in its intermission ({s.IntermissionLeft:F1}s).");
        W.Mode.Tick(s.IntermissionLeft+.001f);
        Check(s.Wave==wave+1&&!s.Intermission,$"Intermission elapsed -> wave {s.Wave} started.");
        yield break; // same frame: spawn positions are sampled before any NPC moves
    }
    IEnumerator ClearWave()
    {
        var s=State;int wave=s.Wave,guard=0;
        while(s.Wave==wave&&!s.Intermission)
        {
            foreach(var npc in s.Alive.ToArray())npc.Damage(100000,W.Powers);
            W.Mode.Tick(.01f);yield return null;
            if(++guard>300)throw new Exception("Wave "+wave+" never cleared");
        }
        Check(s.Intermission&&s.Wave==wave&&s.WaveKills==waves.WaveSize(wave),$"Wave {wave} cleared: {s.WaveKills} kills = WaveSize; score {W.Mode.Score}.");
    }
    WaveSample Sample(string label)
    {
        var s=State;var alive=s.Alive;var first=alive[0];
        var role=W.Progression.Data.Side==PlayerSide.Hero?waves.HeroSideEnemy:waves.VillainSideEnemy;
        foreach(var npc in alive)
        {
            if(npc.Role!=role||!npc.Hostile||!npc.AlwaysAggro||npc.Health!=npc.MaxHealth||!Mathf.Approximately(npc.MaxHealth,first.MaxHealth)||!Mathf.Approximately(npc.ContactDamage,first.ContactDamage))
                throw new Exception($"{label}: inconsistent enemy {npc.name} role={npc.Role} hostile={npc.Hostile} health={npc.Health}/{npc.MaxHealth} damage={npc.ContactDamage}");
            if(Flat(npc.transform.position,W.Hero.transform.position)<waves.MinSpawnDistanceFromPlayer-.01f)throw new Exception($"{label}: {npc.name} spawned {Flat(npc.transform.position,W.Hero.transform.position):F2}m from the player");
            float ring=Flat(npc.transform.position,s.Arena);
            if(ring>waves.SpawnRadius+W.Tuning.Npcs.NavSampleRadius+.01f)throw new Exception($"{label}: {npc.name} {ring:F1}m from arena centre");
        }
        var sample=new WaveSample{Wave=s.Wave,Alive=alive.Count,Health=first.Health,MaxHealth=first.MaxHealth,Damage=first.ContactDamage};
        Log($"RECORD {label}: wave={sample.Wave}, alive={sample.Alive}, per-enemy health={sample.Health:F2} (max {sample.MaxHealth:F2}), contact damage={sample.Damage:F2}, role={first.Role}, Heat stars={W.Stars}; spawn distances from player {string.Join(",",alive.Select(n=>Flat(n.transform.position,W.Hero.transform.position).ToString("F1")))}m; ring radii {string.Join(",",alive.Select(n=>Flat(n.transform.position,s.Arena).ToString("F1")))}m.");
        return sample;
    }
    /// One real NPC contact attack against the player; returns health lost.
    IEnumerator ContactHit(string label)
    {
        var s=State;var attacker=s.Alive.OrderBy(n=>Flat(n.transform.position,HoverPoint)).First();
        foreach(var npc in s.Alive)if(npc!=attacker)npc.Freeze(8);
        float before=W.Health,expected=attacker.ContactDamage;
        Move(attacker.transform.position+attacker.transform.forward*.9f);
        float until=Time.realtimeSinceStartup+6;
        while(W.Health>=before&&Time.realtimeSinceStartup<until)yield return null;
        float lost=before-W.Health;Hover();
        Check(Mathf.Abs(lost-expected)<.001f,$"{label}: real {attacker.Role} contact attack took {lost:F2} health ({before:F2} -> {W.Health:F2}); explicit per-wave damage {expected:F2}.");
    }

    sealed class Timing { public double Fps, Mean, P95, Skinning, BatchMode, PlayerLoop; public int Frames, Humanoids, Visible; }
    sealed class Recorded { public string Name; public ProfilerRecorder Recorder; public double Sum; }
    List<ProfilerRecorderHandle> handles;
    IEnumerator MeasureFps(string label,Timing timing)
    {
        // PerformanceProfileRunner single-render method at the REAL gameplay camera: ThirdPersonCamera enabled
        // (tuning offset, yaw 0 = behind the hero looking +Z into the arena), camera disabled, one manual Render() per frame.
        var cam=Camera.main;var follow=cam.GetComponent<ThirdPersonCamera>();follow.enabled=true;
        if(gameTarget==null){gameTarget=new RenderTexture(1280,720,24){name="Endless 1280x720"};gameTarget.Create();}
        cam.targetTexture=gameTarget;cam.enabled=false;
        var warm=System.Diagnostics.Stopwatch.StartNew();while(warm.Elapsed.TotalSeconds<2){cam.Render();yield return null;}
        var times=new List<double>();var watch=System.Diagnostics.Stopwatch.StartNew();double prior=0;
        while(watch.Elapsed.TotalSeconds<4){cam.Render();yield return null;double now=watch.Elapsed.TotalSeconds;times.Add((now-prior)*1000);prior=now;}
        watch.Stop();
        timing.Frames=times.Count;timing.Fps=times.Count/watch.Elapsed.TotalSeconds;timing.Mean=times.Average();var sorted=times.OrderBy(t=>t).ToList();timing.P95=sorted[Mathf.Min(sorted.Count-1,(int)(sorted.Count*.95f))];
        timing.Humanoids=FindObjectsByType<HumanoidPresentation>().Length;timing.Visible=FindObjectsByType<Renderer>().Count(r=>r.isVisible);
        // Separate profiled sample so recorder overhead never touches the FPS sample.
        string[] wanted={"PostLateUpdate.UpdateAllSkinnedMeshes","PostLateUpdate.BatchModeUpdate","PlayerLoop"};
        if(handles==null)
        {
            var all=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(all);handles=new List<ProfilerRecorderHandle>();
            foreach(var name in wanted){foreach(var h in all)if(ProfilerRecorderHandle.GetDescription(h).Name==name){handles.Add(h);break;}}
            Log("PROFILER recorders found: "+string.Join(", ",handles.Select(h=>ProfilerRecorderHandle.GetDescription(h).Name))+" (requested: "+string.Join(", ",wanted)+")");
        }
        var recorders=handles.Select(h=>new Recorded{Name=ProfilerRecorderHandle.GetDescription(h).Name,Recorder=new ProfilerRecorder(h,1,ProfilerRecorderOptions.Default)}).ToList();
        foreach(var r in recorders)if(!r.Recorder.IsRunning)r.Recorder.Start();
        int frames=0;var profiled=System.Diagnostics.Stopwatch.StartNew();
        while(profiled.Elapsed.TotalSeconds<3){cam.Render();yield return null;foreach(var r in recorders)if(r.Recorder.Valid)r.Sum+=r.Recorder.LastValue;frames++;}
        foreach(var r in recorders)r.Recorder.Dispose();
        double Ms(string name){var r=recorders.Find(x=>x.Name==name);return r!=null&&frames>0?r.Sum/frames/1e6:double.NaN;}
        timing.Skinning=Ms("PostLateUpdate.UpdateAllSkinnedMeshes");timing.BatchMode=Ms("PostLateUpdate.BatchModeUpdate");timing.PlayerLoop=Ms("PlayerLoop");
        Capture(gameTarget,label.ToLowerInvariant().Replace(' ','-')+"-gameplay-camera");
        cam.enabled=true;cam.targetTexture=null;
        Log($"MEASURED {label}: single-render FPS={timing.Fps:F2}, mean={timing.Mean:F2}ms, p95={timing.P95:F2}ms, frames={timing.Frames}; humanoids={timing.Humanoids} (enemies alive {State.Alive.Count} + hero), visible renderers={timing.Visible}; profiled {frames} frames: PostLateUpdate.UpdateAllSkinnedMeshes={timing.Skinning:F3}ms, PostLateUpdate.BatchModeUpdate={timing.BatchMode:F3}ms, PlayerLoop={timing.PlayerLoop:F3}ms; camera {cam.transform.position} fwd {cam.transform.forward}, hero hovering at {W.Hero.transform.position}; 1280x720 Editor Play Mode, {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName}.");
    }

    IEnumerator EndlessHero()
    {
        Check(waves!=null&&endless.Director==waves&&endlessVillain.Director==waves,"endless-fight and endless-fight-villain reference the ONE Resources/ModeDirectors/EndlessWaves asset.");
        Check(endless.Side==PlayerSide.Hero&&endlessVillain.Side==PlayerSide.Villain&&endless.Results==ResultsLayout.Survival&&endless.DefeatLimit==1&&endless.Civilians==0&&!endless.SpawnPolice,"Endless definitions are pure data: Survival layout, DefeatLimit 1, 0 civilians, no police.");
        Submit(ui.ModeButtons["endless-fight"]);yield return Scene(GameFlow.CityScene);
        var session=W.Mode;var s=State;
        Check(session.Definition==endless&&s!=null&&s.Tuning==waves&&W.Progression.Data.Side==PlayerSide.Hero&&W.Progression.SideLocked,"Real Home button launched Endless Fight (Hero): EndlessWaveState runtime component created by the director; side Hero, locked.");
        Check(W.Npcs.Count(n=>n!=null)==0&&W.Crimes.Count==0&&s.Wave==0&&s.Intermission,$"Before wave 1: 0 NPCs (no civilians, no police), 0 encounters; arena centre {s.Arena}.");
        Hover();
        yield return StartNextWave();
        var w1=Sample("WAVE 1");
        Check(w1.Alive==waves.AliveTarget(1)&&w1.Alive==waves.FirstWaveCount&&Mathf.Approximately(w1.MaxHealth,waves.BaseHealth)&&Mathf.Approximately(w1.Damage,waves.BaseDamage),$"Wave 1 = FirstWaveCount {waves.FirstWaveCount} Criminals at base health {waves.BaseHealth} / damage {waves.BaseDamage}.");
        Check(PrototypeHUD.DirectorLine(W)==$"WAVE 1 · LEFT {waves.WaveSize(1)} · SCORE 0 · BEST 0","HUD line: "+PrototypeHUD.DirectorLine(W));
        var t1=new Timing();yield return MeasureFps("Endless wave 1",t1);
        Check(State.Alive.Count==w1.Alive&&!W.PlayerDead,"Hovering hero unreachable during the sample (no deaths, no kills).");
        yield return ContactHit("Wave 1 contact damage");
        // Real punch against a wave enemy.
        var victim=s.Alive.OrderBy(n=>Flat(n.transform.position,HoverPoint)).Last();victim.Freeze(3);float h0=victim.Health;
        Vector3 dir=(victim.transform.position-HoverPoint);dir.y=0;dir.Normalize();
        Move(victim.transform.position-dir*1.2f);W.Hero.transform.rotation=Quaternion.LookRotation(dir);W.Hero.DebugSetResources(6,3,0);
        Check(W.Hero.TryPunch(),"Real E-punch accepted.");yield return new WaitForSeconds(W.Hero.PunchWindupSeconds+.1f);
        Check(victim.Health<h0,$"Real punch impact damaged a wave-1 Criminal: health {h0:F1} -> {victim.Health:F1}.");Hover();
        // Advance CONTROL on wave 2: one enemy left alive -> no advance.
        yield return ClearWave();Check(session.Score==waves.WaveSize(1)*waves.KillScore*1+waves.WaveClearBonus*1,$"Score after wave 1 = {session.Score} (kills x KillScore x wave + WaveClearBonus x wave).");
        yield return StartNextWave();
        var keep=s.Alive[0];foreach(var npc in s.Alive.ToArray())if(npc!=keep)npc.Damage(100000,W.Powers);
        yield return Advance(session,waves.IntermissionSeconds*3);
        Check(s.Wave==2&&!s.Intermission&&s.Alive.Count==1&&s.Remaining==1&&!keep.Dead,$"Advance CONTROL: one wave-2 enemy alive after {waves.IntermissionSeconds*3}s -> still wave {s.Wave}, LEFT {s.Remaining}, no intermission.");
        keep.Damage(100000,W.Powers);session.Tick(.01f);
        Check(s.Intermission&&s.Wave==2,"Last enemy defeated -> intermission begins.");
        yield return StartNextWave();Check(s.Alive.Count==waves.AliveTarget(3),$"Wave 3 spawned {s.Alive.Count} = FirstWaveCount + 2*EnemiesPerWave.");
        yield return ClearWave();yield return StartNextWave();
        // Heat must NOT scale endless enemies: 3 stars are active when wave 5 spawns. (Waited out during wave 4's
        // fight, not its intermission, because the intermission also counts down in real time.)
        W.AddHeat(3-W.Heat);
        yield return WaitRealtime(W.Tuning.Heat.ResponseInterval+.6f);
        Check(W.Stars==3&&Count(NpcRole.Cop)==0&&Count(NpcRole.PursuingHero)==0,$"SpawnPolice=0 CONTROL: {W.Stars} Heat stars for {W.Tuning.Heat.ResponseInterval+.6f:F1}s -> 0 police, 0 pursuing hero.");
        yield return ClearWave();yield return StartNextWave();
        var w5=Sample("WAVE 5");
        float starHealth=W.Tuning.Npcs.CopHealth+W.Stars*W.Tuning.Npcs.HealthPerStar,starDamage=W.Tuning.Npcs.AttackDamage+W.Stars*W.Tuning.Npcs.DamagePerStar;
        Check(w5.Alive==waves.FirstWaveCount+waves.EnemiesPerWave*4&&w5.Alive==waves.AliveTarget(5)&&w5.Alive-w1.Alive==waves.EnemiesPerWave*4,$"Alive count escalates exactly: wave 1 {w1.Alive} -> wave 5 {w5.Alive} (= {waves.FirstWaveCount} + {waves.EnemiesPerWave} x 4, under MaxAlive {waves.MaxAlive}).");
        Check(Mathf.Abs(w5.MaxHealth-waves.BaseHealth*(1+waves.HealthGrowthPerWave*4))<.001f&&Mathf.Abs(w5.MaxHealth/w1.MaxHealth-waves.HealthMultiplier(5))<.0001f,$"Health escalates exactly: {w1.MaxHealth:F2} -> {w5.MaxHealth:F2} (x{waves.HealthMultiplier(5):F2}); Heat-star formula would have given {starHealth:F2}.");
        Check(Mathf.Abs(w5.Damage-waves.BaseDamage*(1+waves.DamageGrowthPerWave*4))<.001f&&Mathf.Abs(w5.Damage/w1.Damage-waves.DamageMultiplier(5))<.0001f,$"Damage escalates exactly: {w1.Damage:F2} -> {w5.Damage:F2} (x{waves.DamageMultiplier(5):F2}); Heat-star formula would have given {starDamage:F2}.");
        var t5=new Timing();yield return MeasureFps("Endless wave 5",t5);
        Log($"FPS SUMMARY gameplay camera: wave 1 ({t1.Humanoids} humanoids) {t1.Fps:F2} FPS / skinning {t1.Skinning:F3}ms / batch-mode overhead {t1.BatchMode:F3}ms  ->  wave 5 ({t5.Humanoids} humanoids) {t5.Fps:F2} FPS / skinning {t5.Skinning:F3}ms / batch-mode overhead {t5.BatchMode:F3}ms; skinning per added humanoid {(t5.Skinning-t1.Skinning)/Mathf.Max(1,t5.Humanoids-t1.Humanoids):F3}ms.");
        yield return ContactHit("Wave 5 contact damage (3 Heat stars active)");
        yield return ClearWave();yield return StartNextWave();
        // MaxAlive cap: wave 6 has WaveSize 13 > MaxAlive 12.
        Check(waves.WaveSize(6)>waves.MaxAlive&&s.Alive.Count==waves.MaxAlive&&s.Spawned==waves.MaxAlive&&s.Remaining==waves.WaveSize(6),$"MaxAlive cap: wave 6 size {waves.WaveSize(6)}, alive {s.Alive.Count}, spawned {s.Spawned}.");
        s.Alive[0].Damage(100000,W.Powers);session.Tick(.01f);
        Check(s.Alive.Count==waves.MaxAlive&&s.Spawned==waves.WaveSize(6),$"Reinforcement after a kill: alive {s.Alive.Count}, spawned {s.Spawned}/{waves.WaveSize(6)}.");
        s.Alive[0].Damage(100000,W.Powers);session.Tick(.01f);
        Check(s.Alive.Count==waves.WaveSize(6)-2&&s.Spawned==waves.WaveSize(6)&&s.Remaining==waves.WaveSize(6)-2,$"Whole wave spawned: no further reinforcements (alive {s.Alive.Count}, LEFT {s.Remaining}).");
        int expected=0;for(int w=1;w<=5;w++)expected+=waves.WaveSize(w)*waves.KillScore*w+waves.WaveClearBonus*w;expected+=2*waves.KillScore*6;
        int kills=Enumerable.Range(1,5).Sum(waves.WaveSize)+2;
        Check(session.Score==expected&&s.Kills==kills,$"Score {session.Score} = sum(kills x {waves.KillScore} x wave) + sum({waves.WaveClearBonus} x cleared wave); {s.Kills} enemies defeated.");
        // Death ends the run: stand in the arena and let the wave-6 crowd's REAL attacks kill the hero.
        Move(s.Arena);float deadline=Time.realtimeSinceStartup+60;
        while(!session.Ended){if(Time.realtimeSinceStartup>deadline)throw new Exception($"Wave-6 enemies did not defeat the player (health {W.Health})");yield return null;}
        Check(session.Defeats==1,"Real wave-6 NPC attacks defeated the player; DefeatLimit 1 ended the run.");
        yield return Scene(GameFlow.ResultsScene);yield return Menu();yield return WaitRealtime(.5f);
        var r=Flow.Result;
        Check(r.Layout==ResultsLayout.Survival&&r.Outcome==SessionOutcome.Lost&&r.Wave==6&&r.EnemiesDefeated==kills&&r.Score==expected&&r.BestScore==expected&&r.NewBest,$"Result: layout={r.Layout}, outcome={r.Outcome}, wave={r.Wave}, defeated={r.EnemiesDefeated}, score={r.Score}, best={r.BestScore}, newBest={r.NewBest}.");
        yield return SurvivalScreen($"WAVE 6 REACHED / NEW BEST",PlayerSide.Hero,r);
        var disk=Disk();var record=disk.ModeRecords.Find(m=>m.Id=="endless-fight");
        Check(record!=null&&record.BestScore==expected&&record.BestWave==6&&record.Runs==1&&disk.BestSessionScore==expected,$"Saved per-mode record endless-fight: best {record?.BestScore}, best wave {record?.BestWave}, runs {record?.Runs}; global BestSessionScore {disk.BestSessionScore}.");
        Capture(uiTarget,"endless-results-new-best");
        // Lower-scoring later run must NOT overwrite the best.
        Submit(ui.ReplayButton);yield return Scene(GameFlow.CityScene);
        session=W.Mode;s=State;Hover();
        Check(session.Definition==endless&&s.Wave==0,"PLAY AGAIN relaunched Endless Fight (Hero) at wave 0.");
        yield return StartNextWave();s.Alive[0].Damage(100000,W.Powers);session.Tick(.01f);
        Check(PrototypeHUD.DirectorLine(W)==$"WAVE 1 · LEFT {waves.WaveSize(1)-1} · SCORE {waves.KillScore} · BEST {expected}","HUD shows the saved best during the next run: "+PrototypeHUD.DirectorLine(W));
        W.DamagePlayer(W.Health);yield return Scene(GameFlow.ResultsScene);yield return Menu();yield return WaitRealtime(.5f);
        r=Flow.Result;
        Check(r.Score==waves.KillScore&&!r.NewBest&&r.BestScore==expected&&r.Wave==1,$"Lower run CONTROL: score {r.Score} < best {r.BestScore}; newBest={r.NewBest}.");
        yield return SurvivalScreen("WAVE 1 REACHED",PlayerSide.Hero,r);
        disk=Disk();record=disk.ModeRecords.Find(m=>m.Id=="endless-fight");
        Check(record.BestScore==expected&&record.BestWave==6&&record.Runs==2&&disk.BestSessionScore==expected,$"Lower run did NOT overwrite the saved best: best {record.BestScore}, best wave {record.BestWave}, runs {record.Runs}.");
        Capture(uiTarget,"endless-results-lower-score");
        Submit(ui.HomeButton);yield return Scene(GameFlow.HomeScene);yield return Menu();
        Check(W==null,"HOME button returned to Home.");
    }

    IEnumerator SurvivalScreen(string headline,PlayerSide side,SessionResult r)
    {
        var palette=Resources.Load<CityPalette>("CityPalette");
        var texts=ui.Root.Query<Label>().ToList().Select(l=>l.text).ToList();
        int wave=texts.IndexOf("WAVE"),defeated=texts.IndexOf("ENEMIES DEFEATED"),score=texts.IndexOf("SCORE"),best=texts.IndexOf("BEST");
        Check(ui.Headline.text==headline&&ui.Headline.style.color.value==palette.Colors[(int)(side==PlayerSide.Hero?CityColor.HeroAccent:CityColor.VillainAccent)],$"Survival headline '{ui.Headline.text}' in the {side} accent.");
        Check(ui.StatCount==4&&wave>=0&&wave<defeated&&defeated<score&&score<best&&texts[wave-1]==r.Wave.ToString()&&texts[defeated-1]==r.EnemiesDefeated.ToString()&&texts[score-1]==r.Score.ToString()&&texts[best-1]==r.BestScore.ToString()&&!texts.Contains("CRIMES STOPPED")&&!texts.Contains("HEISTS COMPLETED"),
            $"Exactly 4 stats in order: {texts[wave-1]} WAVE / {texts[defeated-1]} ENEMIES DEFEATED / {texts[score-1]} SCORE / {texts[best-1]} BEST.");
        Check(ui.ReplayButton.enabledInHierarchy&&ui.HomeButton.enabledInHierarchy,"PLAY AGAIN and HOME enabled.");
        yield return null;
    }

    IEnumerator EndlessVillain()
    {
        Submit(ui.ModeButtons["endless-fight-villain"]);yield return Scene(GameFlow.CityScene);
        var session=W.Mode;var s=State;Hover();
        Check(session.Definition==endlessVillain&&W.Progression.Data.Side==PlayerSide.Villain&&W.Progression.SideLocked&&s.Tuning==waves,"Real Home button launched Endless Fight (Villain): side Villain, locked, same director asset.");
        yield return StartNextWave();
        var w1=Sample("VILLAIN WAVE 1");
        Check(w1.Alive==waves.FirstWaveCount&&s.Alive.All(n=>n.Role==NpcRole.Cop&&n.Hostile)&&Count(NpcRole.Criminal)==0&&Count(NpcRole.Civilian)==0,$"Villain enemies are {w1.Alive} hostile Cops; 0 criminals, 0 civilians.");
        yield return ContactHit("Villain wave 1 contact damage");
        var last=s.Alive[0];foreach(var npc in s.Alive.ToArray())if(npc!=last)npc.Damage(100000,W.Powers);
        yield return WaitRealtime(W.Tuning.Heat.ResponseInterval+.6f);
        Check(W.Heat>0&&Count(NpcRole.Cop)==1&&Count(NpcRole.PursuingHero)==0,$"Defeating cops raised Heat to {W.Heat:F2} ({W.Stars} stars) but SpawnPolice=0 spawned no police (only the 1 remaining enemy cop exists).");
        yield return ClearWave();
        yield return StartNextWave();
        W.DamagePlayer(W.Health);yield return Scene(GameFlow.ResultsScene);yield return Menu();yield return WaitRealtime(.5f);
        var r=Flow.Result;int expected=waves.WaveSize(1)*waves.KillScore+waves.WaveClearBonus;
        Check(r.Layout==ResultsLayout.Survival&&r.Wave==2&&r.Score==expected&&r.NewBest&&r.Side==PlayerSide.Villain,$"Villain result: wave {r.Wave}, score {r.Score}, newBest={r.NewBest}.");
        yield return SurvivalScreen("WAVE 2 REACHED / NEW BEST",PlayerSide.Villain,r);
        Capture(uiTarget,"endless-villain-results");
        var disk=Disk();var heroRecord=disk.ModeRecords.Find(m=>m.Id=="endless-fight");var villainRecord=disk.ModeRecords.Find(m=>m.Id=="endless-fight-villain");
        Check(villainRecord!=null&&villainRecord.BestScore==expected&&villainRecord.BestWave==2&&heroRecord.BestScore>expected,$"Best scores are per definition id: endless-fight {heroRecord.BestScore}, endless-fight-villain {villainRecord.BestScore}.");
        File.Copy(WorldSession.VerificationSavePath,ExpectedPath,true);
        Log("SAVE for second-process reload: "+JsonUtility.ToJson(disk));
        Submit(ui.HomeButton);yield return Scene(GameFlow.HomeScene);
    }

    // ---------------------------------------------------------------- SECOND PROCESS
    IEnumerator ReloadChecks()
    {
        yield return Scene(GameFlow.HomeScene);yield return Menu();
        var expected=JsonUtility.FromJson<ProgressSave>(File.ReadAllText(ExpectedPath));
        Check(JsonUtility.ToJson(ui.Profile.Data)==JsonUtility.ToJson(expected),$"SECOND UNITY PROCESS: Home profile reloaded the exact save ({expected.ModeRecords.Count} mode records, level {expected.Level}, sessions {expected.SessionsPlayed}).");
        var heroBest=expected.ModeRecords.Find(m=>m.Id=="endless-fight");var villainBest=expected.ModeRecords.Find(m=>m.Id=="endless-fight-villain");
        Check(ui.Profile.BestScore("endless-fight")==heroBest.BestScore&&ui.Profile.Record("endless-fight").BestWave==heroBest.BestWave&&ui.Profile.BestScore("endless-fight-villain")==villainBest.BestScore,
            $"Reloaded per-mode bests: endless-fight {ui.Profile.BestScore("endless-fight")} (wave {ui.Profile.Record("endless-fight").BestWave}), endless-fight-villain {ui.Profile.BestScore("endless-fight-villain")}.");
        Submit(ui.ModeButtons["endless-fight"]);yield return Scene(GameFlow.CityScene);
        Check(W.Progression.BestScore("endless-fight")==heroBest.BestScore&&PrototypeHUD.DirectorLine(W).EndsWith("BEST "+heroBest.BestScore),"In-game HUD after reload: "+PrototypeHUD.DirectorLine(W));
        W.Mode.ReturnHome();yield return Scene(GameFlow.HomeScene);
        var probe=new GameObject("Reload probe").AddComponent<PlayerProgression>();var tuning=Resources.Load<GameTuning>("GameTuning");var powers=Resources.LoadAll<PowerDefinition>("Powers");
        probe.Initialize(tuning.Progression,powers,WorldSession.VerificationSavePath);
        Check(probe.BestScore("endless-fight")==heroBest.BestScore&&probe.Record("endless-fight").Runs==heroBest.Runs+1,$"Abandoning a 0-score run after reload keeps best {probe.BestScore("endless-fight")} (runs {heroBest.Runs} -> {probe.Record("endless-fight").Runs}).");
        // Old saves written before ModeRecords existed.
        string fixture=Path.Combine(Folder,"legacy-save-fixture.json"),legacy=Path.Combine(Folder,"save-legacy.json");
        Check(!File.ReadAllText(fixture).Contains("ModeRecords"),"Legacy fixture is a real pre-change save (written 2026-09-20 by MenuPresentationVerification) with no ModeRecords field.");
        File.Copy(fixture,legacy,true);if(File.Exists(legacy+".bak"))File.Delete(legacy+".bak");
        probe.Initialize(tuning.Progression,powers,legacy);
        Check(probe.LastError==null&&probe.Data.Level==2&&probe.Data.Xp==70&&probe.Data.BestSessionScore==420&&probe.Data.Side==PlayerSide.Villain&&probe.Data.ModeRecords!=null&&probe.Data.ModeRecords.Count==0&&probe.BestScore("endless-fight")==0,
            $"Legacy save loads: level {probe.Data.Level}, XP {probe.Data.Xp}, best {probe.Data.BestSessionScore}, side {probe.Data.Side}, ModeRecords empty list (not null).");
        probe.RecordSession("endless-fight",false,55,0,3);probe.Initialize(tuning.Progression,powers,legacy);
        Check(probe.BestScore("endless-fight")==55&&probe.Record("endless-fight").BestWave==3&&probe.Data.Level==2&&probe.Data.BestSessionScore==420&&probe.Data.SessionsPlayed==5,"Legacy save upgraded in place: new endless record 55 / wave 3; old fields intact (global best 420 unchanged).");
        string invalid=Path.Combine(Folder,"save-invalid.json");File.WriteAllText(invalid,"{\"Version\":2,\"Level\":9}");
        probe.Initialize(tuning.Progression,powers,invalid);
        Check(probe.LastError!=null&&probe.Data.Level==1,"Invalid-save CONTROL: version-2 file still rejected ("+probe.LastError+").");
        probe.Initialize(tuning.Progression,powers,Path.Combine(Folder,"save-fresh-"+Guid.NewGuid().ToString("N")+".json"));
        Check(probe.Data.ModeRecords.Count==0&&probe.BestScore("endless-fight")==0,"Fresh-save CONTROL in second process: no mode records, best 0.");
        Destroy(probe.gameObject);
    }
}
#endif
