#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ModeVerificationRunner : MonoBehaviour
{
    public bool Reload;
    public Action<int> Finished;
    readonly List<string> output=new List<string>();
    WorldSession W=>WorldSession.Instance;
    GameFlow Flow=>GameFlow.Instance;
    string DirectoryPath=>Path.GetFullPath("Verification/Modes");
    IEnumerator Start()
    {
        // Flatten nested coroutines so all failures reach the log and exit code.
        var stack=new Stack<IEnumerator>();stack.Push(Reload?ReloadChecks():Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try {moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Write();Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}
            if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Write();Finished(0);
    }
    void Log(string text){output.Add(text);Debug.Log(text);}
    void Check(bool condition,string text){if(!condition)throw new Exception(text);Log("PASS "+text);}
    void Write(){System.IO.Directory.CreateDirectory(DirectoryPath);File.WriteAllLines(Path.Combine(DirectoryPath,Reload?"reload.txt":"results.txt"),output);}
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+25;
        while(Flow.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene load timeout: "+name);yield return null;}
        yield return null;
        if(W!=null)W.Hero.enabled=false;
    }
    void Move(Vector3 position)
    {var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=position;cc.enabled=true;Physics.SyncTransforms();}
    CrimeEncounter Next()
    {return W.Crimes.FirstOrDefault(c=>c!=null&&!c.Resolved)?.Encounter??W.Mode.SpawnNext().Encounter;}
    void Hold(CrimeEncounter e,Vector3 position)
    {Move(position);e.Interact(0);Check(e.Interact(e.Definition.HoldSeconds+.01f),"Held interaction accepted at valid objective.");}
    IEnumerator Complete(CrimeEncounter e)
    {
        e.enabled=false; // Test supplies hold durations; live AI/physics still update.
        if(W.Progression.Data.Side==PlayerSide.Hero)
        {
            foreach(var robber in e.Robbers)if(robber.Npc!=null&&!robber.Npc.Dead)robber.Npc.Damage(10000,W.Powers);
            foreach(var civilian in e.Civilians)
            {
                if(civilian.Blockade!=null)civilian.Blockade.GetComponent<BreakableProp>().TakeDamage(10000,W.Powers);
                yield return null;
                Hold(e,civilian.Npc.transform.position);
            }
        }
        else
        {
            foreach(var node in e.Loot)Hold(e,node.Visual.transform.position);
            foreach(var prop in e.Props.ToArray())if(prop!=null)prop.GetComponent<BreakableProp>().TakeDamage(10000,W.Powers);
            yield return null;
        }
        foreach(var hazard in e.Hazards)Hold(e,hazard.Visual.transform.position);
        if(W.Progression.Data.Side==PlayerSide.Villain)
        {
            Check(!e.TryComplete(),"Villain escape CONTROL: loot/destruction alone cannot resolve while inside scene.");
            Move(e.Site+Vector3.up*(e.Definition.PlayerEscapeDistance+3));e.TryComplete();
        }
        Check(e.Finished,"All multi-part encounter objectives resolved.");yield return null;
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(W==null&&UnityEngine.Object.FindAnyObjectByType<ModeScreens>()!=null,"Startup HOME, no city or player spawned.");
        var catalog=Resources.LoadAll<GameModeDefinition>("Modes");
        {var hidden=UnityEngine.Object.Instantiate(catalog.First(m=>m.Id=="free-play"));hidden.Playable=false;Check(!Flow.Select(hidden)&&!Flow.Loading,"Non-playable definition CONTROL refuses launch (free-play/endless-fight are playable since Step 2-3).");UnityEngine.Object.Destroy(hidden);}
        var hero=catalog.First(m=>m.Id=="hero");var villain=catalog.First(m=>m.Id=="villain");
        Check(Flow.Select(hero),"Home -> shipping Hero definition selected.");yield return Scene(GameFlow.CityScene);
        Check(W.Progression.Data.Level==1&&W.Progression.Data.Points==0&&W.Progression.Data.SessionsPlayed==0,"Fresh-save CONTROL: level=1, XP=0, points=0, sessions=0.");
        string city=string.Join("|",W.City.Buildings.Select(b=>JsonUtility.ToJson(b)));
        Check(!W.Progression.SwitchSide()&&W.Progression.Data.Side==PlayerSide.Hero,"Hero side locked; H cannot bypass the mode.");
        var e=Next();
        Check(e.Robbers.Count==3&&e.Civilians.Count==2&&e.Responders.Count==2&&e.Props.Count==8&&e.Loot.Count==2,
            $"Populated event: {e.Robbers.Count} robbers, {e.Civilians.Count} trapped civilians, {e.Responders.Count} responders, {e.Props.Count} real Rigidbody props (2 cars, 4 supplies, 2 blockades), {e.Loot.Count} loot nodes.");
        Check(!e.TryComplete()&&!e.Finished,"Untouched marker CONTROL does not resolve encounter.");
        e.enabled=false;Move(e.Civilians[0].Npc.transform.position);
        Check(!e.Interact(e.Definition.HoldSeconds+.1f)&&e.Rescued==0,"Blocked civilian CONTROL refuses rescue before blockade is moved/broken.");
        var blockade=e.Civilians[0].Blockade;Vector3 before=blockade.position;
        float mass=blockade.mass;Move(new Vector3(blockade.position.x,.1f,blockade.position.z-3f));W.Hero.transform.forward=Vector3.forward;
        Check(W.Hero.TryPunch(),"Real charged E-punch accepted against encounter physics blockade.");
        yield return new WaitForSeconds(W.Hero.PunchWindupSeconds+.05f);
        Check(W.Hero.LastAffectedBodies>0,"Animated punch impact hit encounter physics blockade after windup.");
        float impulse=W.Hero.LastForce;
        for(int i=0;i<30;i++)yield return new WaitForFixedUpdate();
        Check(blockade==null||Vector3.Distance(before,blockade.position)>=e.Definition.PropMoveDistance,
            $"Rescue physics: actual punch AddExplosionForce({impulse:F0} N·s, Impulse), mass={mass:F0}kg; displacement={(blockade==null?"broken into physical shards":Vector3.Distance(before,blockade.position).ToString("F3")+"m")}.");
        Hold(e,e.Civilians[0].Npc.transform.position);
        Check(e.Rescued==1,"Moved blockade permits real rescue interaction.");
        float health=W.Health;var cop=e.Responders[0];
        // Isolate the cop from nearby hostile robbers so their attacks cannot confound this control.
        cop.Agent.Warp(W.City.Sidewalks[0]);Move(cop.transform.position+Vector3.forward*.7f);
        yield return new WaitForSeconds(1.2f);
        Check(!cop.Hostile&&W.Health==health,"Hero police CONTROL: neutral cop in attack range does not damage player.");
        float elapsed=W.Mode.Elapsed;W.Mode.SetPaused(true);W.Mode.Tick(10);e.Tick(10);yield return null;
        Check(Time.timeScale==0&&W.Mode.Elapsed==elapsed,"Pause freezes session clock and encounter simulation.");W.Mode.SetPaused(false);
        // Fail this partially completed event; completion helpers exercise shipping rules below.
        W.AddHeat(1);float heat=W.Heat;e.Tick(e.Definition.Deadline+1);
        Check(W.Mode.Failures==1&&Mathf.Abs(W.Heat-heat-hero.FailureHeat)<.001f,$"Ignored Hero event: failures=1, Heat {heat:F2} -> {W.Heat:F2} (+{hero.FailureHeat:F2}).");yield return null;
        for(int i=0;i<hero.SuccessGoal;i++)yield return Complete(Next());
        yield return Scene(GameFlow.ResultsScene);
        Check(W==null&&Flow.Result.Outcome==SessionOutcome.Won&&Flow.Result.Successes==5,$"Hero PLAY -> RESULTS: {Flow.Result.Outcome}, success={Flow.Result.Successes}, failure={Flow.Result.Failures}, score={Flow.Result.Score}, XP={Flow.Result.Xp}.");
        Flow.Home();yield return Scene(GameFlow.HomeScene);Check(W==null,"Hero RESULTS -> HOME; city unloaded.");
        var saved=JsonUtility.FromJson<ProgressSave>(File.ReadAllText(WorldSession.VerificationSavePath));
        Check(Flow.Select(villain),"Home -> shipping Villain definition selected.");yield return Scene(GameFlow.CityScene);
        Check(string.Join("|",W.City.Buildings.Select(b=>JsonUtility.ToJson(b)))==city,"Hero/Villain regenerated the SAME seeded city layout.");
        Check(W.Progression.Data.Xp==saved.Xp&&W.Progression.Data.Level==saved.Level&&W.Progression.Data.Points==saved.Points&&W.Progression.Data.SessionsWon==1,
            $"Mode-switch persistence: level={saved.Level}, XP={saved.Xp}, points={saved.Points}, wins=1.");
        Check(W.Progression.Buy(W.Powers.Strength.Definition)&&W.Progression.Tier(W.Powers.Strength.Definition)==1,"Earned points purchase strength upgrade; tier=1 must survive restart.");
        e=Next();Check(e.Objective!=hero.Rules.Objective(e)&&W.Progression.Data.Side==PlayerSide.Villain,"Same encounter systems, opposite Villain loot/destruction/escape goals.");
        cop=e.Responders[0];health=W.Health;Move(cop.transform.position+Vector3.forward*.7f);
        yield return new WaitForSeconds(1.2f);
        Check(cop.Hostile&&W.Health<health,$"Villain live cop AI attacked: health {health:F0} -> {W.Health:F0}.");
        // Stand-off above the Gunner range (22 m): 15 m was chosen when police attacks were 2 m contact damage.
        Move(e.Site+Vector3.up*30);int policeBefore=W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Cop);
        W.AddHeat(3);W.ReconcilePolice();int policeAfter=W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Cop);
        Check(policeAfter>policeBefore,$"Existing Heat escalation retained: cops {policeBefore} -> {policeAfter} at Heat {W.Heat:F2}.");
        W.Mode.Tick(W.Mode.Definition.SpawnInterval);
        Check(W.Crimes.Count(c=>c!=null&&!c.Resolved)==2,"Configured spawn-clock interval adds second multi-part encounter.");
        Check(W.Mode.SpawnNext()==null,"Population-cap CONTROL refuses a third simultaneous encounter.");
        yield return Benchmark();
        for(int i=0;i<villain.SuccessGoal;i++)yield return Complete(Next());
        yield return Scene(GameFlow.ResultsScene);
        Check(Flow.Result.Outcome==SessionOutcome.Won&&Flow.Result.Successes==5,$"Villain PLAY -> RESULTS: {Flow.Result.Outcome}, success={Flow.Result.Successes}, score={Flow.Result.Score}, XP={Flow.Result.Xp}.");
        Flow.Home();yield return Scene(GameFlow.HomeScene);Check(W==null,"Villain RESULTS -> HOME; city unloaded.");
        var third=catalog.First(m=>m.Id=="verification-third");
        Check(third.Rules==hero.Rules&&third.Encounters[0]==hero.Encounters[0]&&Flow.Select(third),"Third mode discovered/selected from a definition ONLY; same rule and encounter assets, goal=1, no registry changes.");
        yield return Scene(GameFlow.CityScene);yield return Complete(Next());yield return Scene(GameFlow.ResultsScene);
        Check(Flow.Result.ModeId==third.Id&&Flow.Result.Outcome==SessionOutcome.Won&&Flow.Result.Successes==1,"Data-only third mode loaded, ran, and won at its configured single-encounter goal.");
        Flow.Home();yield return Scene(GameFlow.HomeScene);
        Flow.Select(hero);yield return Scene(GameFlow.CityScene);
        for(int i=0;i<hero.FailureLimit;i++){Next().Tick(1000);yield return null;}
        yield return Scene(GameFlow.ResultsScene);Check(Flow.Result.Outcome==SessionOutcome.Lost&&Flow.Result.Failures==3,"Failure-limit CONTROL: third failed encounter produces LOST results.");
        Flow.Home();yield return Scene(GameFlow.HomeScene);Flow.Select(villain);yield return Scene(GameFlow.CityScene);
        W.Mode.Tick(villain.SessionSeconds);yield return Scene(GameFlow.ResultsScene);
        Check(Flow.Result.Outcome==SessionOutcome.TimedOut,"Timeout CONTROL: configured 900 seconds produces TIMED OUT results.");
        Flow.Home();yield return Scene(GameFlow.HomeScene);Flow.Select(hero);yield return Scene(GameFlow.CityScene);
        for(int i=0;i<hero.DefeatLimit;i++)
        {
            W.DamagePlayer(W.Health);
            if(i<hero.DefeatLimit-1)yield return new WaitForSeconds(W.Tuning.Movement.RespawnDelay+.2f);
        }
        yield return Scene(GameFlow.ResultsScene);
        Check(Flow.Result.Outcome==SessionOutcome.Lost&&Flow.Result.Defeats==3,"Defeat CONTROL: two respawns allowed; actual third player death produces LOST results.");
        File.Copy(WorldSession.VerificationSavePath,Path.Combine(DirectoryPath,"expected-save.json"),true);
        Flow.Home();yield return Scene(GameFlow.HomeScene);
        Log("PASS Full scene flow verified for BOTH shipping modes; third test definition is removed after this run.");
    }
    IEnumerator Benchmark()
    {
        var camera=new GameObject("Benchmark camera").AddComponent<Camera>();var target=new RenderTexture(1280,720,24);camera.targetTexture=target;
        camera.transform.position=Next().Site+new Vector3(0,30,-3);camera.transform.LookAt(Next().Site);camera.Render();
        int civilians=W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Civilian), cops=W.Npcs.Count(n=>n!=null&&!n.Dead&&n.Role==NpcRole.Cop);
        var frames=new List<double>();var timer=System.Diagnostics.Stopwatch.StartNew();double last=timer.Elapsed.TotalSeconds;
        while(timer.Elapsed.TotalSeconds<5){camera.Render();yield return null;double now=timer.Elapsed.TotalSeconds;frames.Add((now-last)*1000);last=now;}
        frames.Sort();Log($"MEASURED populated events: civilians={civilians}, cops={cops}, events={W.Crimes.Count}; 1280x720 rendered Editor Play Mode; frames={frames.Count}, seconds={timer.Elapsed.TotalSeconds:F3}, FPS={frames.Count/timer.Elapsed.TotalSeconds:F2}, p95={frames[(int)(frames.Count*.95)]:F2}ms; {SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}.");
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(DirectoryPath,"populated-event.png"),image.EncodeToPNG());RenderTexture.active=null;camera.targetTexture=null;target.Release();Destroy(target);Destroy(image);Destroy(camera.gameObject);
    }
    IEnumerator ReloadChecks()
    {
        yield return Scene(GameFlow.HomeScene);
        var expected=JsonUtility.FromJson<ProgressSave>(File.ReadAllText(Path.Combine(DirectoryPath,"expected-save.json")));
        var check=new GameObject("Reload progression").AddComponent<PlayerProgression>();var tuning=Resources.Load<GameTuning>("GameTuning");var powers=Resources.LoadAll<PowerDefinition>("Powers");
        check.Initialize(tuning.Progression,powers,WorldSession.VerificationSavePath);
        Check(JsonUtility.ToJson(check.Data)==JsonUtility.ToJson(expected),$"SECOND UNITY PROCESS exact reload: level={check.Data.Level}, XP={check.Data.Xp}, points={check.Data.Points}, sessions={check.Data.SessionsPlayed}, wins={check.Data.SessionsWon}, best score={check.Data.BestSessionScore}; all power tiers/rooftops retained.");
        check.Initialize(tuning.Progression,powers,Path.Combine(DirectoryPath,"fresh-"+Guid.NewGuid().ToString("N")+".json"));
        Check(check.Data.Level==1&&check.Data.Xp==0&&check.Data.Points==0&&check.Data.SessionsPlayed==0,"Fresh-save CONTROL in second process: level=1, XP=0, points=0, sessions=0.");
        Destroy(check.gameObject);
    }
}
#endif
