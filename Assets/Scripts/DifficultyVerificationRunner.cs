#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class DifficultyVerificationRunner : SessionVerificationRunner
{
    protected override string Folder=>"Verification/Difficulty/";
    protected override string ResultFile=>MenuOnly?"menu.txt":Reload?"reload.txt":"results.txt";
    public bool Reload,MenuOnly;
    GameHud Hud=>FindAnyObjectByType<GameHud>();
    readonly List<NpcDamageResult> hits=new List<NpcDamageResult>();
    void Hit(NpcDamageResult h){if(W!=null&&h.Attacker==W.Powers)hits.Add(h);}
    void Near(float actual,float wanted,string why,float tolerance=.003f)=>Check(Mathf.Abs(actual-wanted)<tolerance,$"{why}: {actual:F5}, expected {wanted:F5}.");
    IEnumerator Frames(int n){for(int i=0;i<n;i++)yield return null;}
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if(Reload)
        {
            Check(Profile.Data.DifficultyId=="extreme","Separate process retained Extreme.");
            yield return Enter(F.Hero("vector"),"fire","strength","free-play");
            Check(W.Difficulty.Id=="extreme","Reload starts Extreme session.");Near(W.MaxHealth,92.5f,"Reload player maximum");
            yield return Home();Check(Profile.Data.DifficultyId=="extreme","Reload return Home keeps choice.");yield break;
        }
        Check(Profile.Data.DifficultyId=="normal","Fresh profile defaults Normal.");
        Check(!Profile.SetDifficulty("invalid"),"Invalid selection rejected.");
        var old=JsonUtility.ToJson(Profile.Data).Replace("\"DifficultyId\":\"normal\",","");
        Check(!old.Contains("DifficultyId"),"Legacy fixture genuinely lacks field.");
        File.WriteAllText(Profile.SavePath,old);Profile.Load();Check(Profile.Data.DifficultyId=="normal","Old save defaults Normal.");
        foreach(var id in new[]{"normal","hard","extreme"})
        {
            var m=FindAnyObjectByType<ModeScreens>();
            using(var e=NavigationSubmitEvent.GetPooled()){e.target=m.DifficultyButtons[id];m.DifficultyButtons[id].SendEvent(e);}
            yield return Frames(3);
            Check(m.DifficultyButtons.Count==3&&m.DifficultyButtons[id].ClassListContains("difficulty-selected"),"Three buttons; selected class for "+id);
            Check(m.Profile.Data.DifficultyId==id&&m.DifficultyDetails.text.Contains(new SessionDifficulty(m.Profile.SelectedDifficulty).Details),"Button saves choice and shows actual percentages "+id);
            yield return MenuCapture(m,id);
        }
        if(MenuOnly)
        {
            var menu=FindAnyObjectByType<ModeScreens>();var button=menu.DifficultyButtons["extreme"];
            bool state=button.ClassListContains("difficulty-pulse"), changed=false;
            for(int i=0;i<12;i++){yield return new WaitForSecondsRealtime(.1f);changed|=state!=button.ClassListContains("difficulty-pulse");}
            Check(changed,"Selected Extreme pulses via low-frequency UI schedule");
            menu.SelectDifficulty("normal");Check(!button.ClassListContains("difficulty-pulse"),"Deselection stops pulse immediately");
            yield return MenuCapture(menu,"normal-wide",2560,1080);yield break;
        }
        var hard=new SessionDifficulty(DifficultyDefinition.Resolve("hard"));var extreme=new SessionDifficulty(DifficultyDefinition.Resolve("extreme"));
        var hv=new[]{hard.PlayerDamage,hard.EnemyHealth,hard.PlayerHealth,hard.SpawnRate,hard.EnergyUsage,hard.Cooldown};
        var ev=new[]{extreme.PlayerDamage,extreme.EnemyHealth,extreme.PlayerHealth,extreme.SpawnRate,extreme.EnergyUsage,extreme.Cooldown};
        for(int i=0;i<hv.Length;i++)Near(ev[i]-1,(hv[i]-1)*1.5f,"Extreme deviation "+i);
        CityNpc.DamageApplied+=Hit;
        try
        {
            foreach(string id in new[]{"normal","hard","extreme"})
            {
                foreach(string mode in new[]{"hero","villain","free-play","endless-fight"})yield return Mode(id,mode);
                foreach(string hero in new[]{"vector","titan","nova"})yield return Hero(id,hero);
                yield return Beam(id);
            }
        }
        finally{CityNpc.DamageApplied-=Hit;}
        yield return Home();Check(Profile.SetDifficulty("extreme"),"Save Extreme for separate-process reload.");
        Log("LIMIT: short Editor measurements, no human balance/feel acceptance or standalone performance claim.");
    }
    IEnumerator StartSession(string id,string mode,string hero="vector",string a="fire",string b="strength")
    {
        yield return Home();Check(Profile.SetDifficulty(id),"Choose "+id+" for next "+mode);
        Profile.Data.Points=100;
        foreach(var key in new[]{a,b})if(!Profile.Owns(Power(key)))Check(Profile.Buy(Power(key)),"Unlock "+key);
        yield return Enter(F.Hero(hero),a,b,mode);
        Check(W.Difficulty.Id==id&&W.Mode.Definition.Id==mode,"Difficulty preserves mode identity "+id+" / "+mode);
        W.Rolls.FixedUnit=null;W.Rolls.Seed(101);
    }
    IEnumerator Mode(string id,string mode)
    {
        yield return StartSession(id,mode);
        var d=W.Difficulty;var definition=W.Mode.Definition;
        float dm=id=="hard"?.97f:id=="extreme"?.955f:1;
        float hp=id=="hard"?1.05f:id=="extreme"?1.075f:1;
        Near(d.PlayerDamage,dm,"Authored player damage");Near(d.EnemyHealth,hp,"Authored enemy health");Near(d.EnemyDamage,1,"Enemy outgoing unchanged");
        Check(W.Progression.SetDifficulty(id=="normal"?"hard":"normal")&&W.Difficulty.Id==id,"Saving next selection cannot mutate live session");W.Progression.SetDifficulty(id);
        Near(W.Mode.EncounterSpawnInterval,definition.SpawnInterval/d.SpawnRate,"Encounter interval");
        Near(W.PoliceResponseInterval,W.Tuning.Heat.ResponseInterval*W.Response.ArrivalMultiplier/d.SpawnRate,"Police response interval");
        Log($"MEASURED {id}/{mode}: encounter {W.Mode.EncounterSpawnInterval:F5}s; police {W.PoliceResponseInterval:F5}s; legacy {W.CrimeSpawnInterval:F5}s.");
        if(mode=="free-play")Check(definition.SuccessGoal==0&&definition.FailureLimit==0&&definition.DefeatLimit==0&&definition.SessionSeconds==0,"Free Play keeps no automatic failure/win limits.");
        W.enabled=false;W.Mode.enabled=false;
        foreach(var npc in W.Npcs.ToArray())if(npc!=null)Destroy(npc.gameObject);yield return null;
        var expected=new CombatRolls(101);
        foreach(string archetype in new[]{"rusher","gunner","brute"})
        {
            var a=Resources.Load<EnemyArchetype>("Enemies/"+archetype);Check(a!=null,"Archetype asset "+archetype);
            for(int i=0;i<8;i++)
            {
                float mean=(W.Tuning.Npcs.CopHealth+W.Stars*W.Tuning.Npcs.HealthPerStar)*a.HealthMultiplier;
                var n=CityNpc.Spawn(W,W.City.Sidewalks[0],NpcRole.Criminal,a);n.enabled=false;
                Near(n.MaxHealth,expected.Health(mean*hp,W.Tuning.CombatRanges),"Seeded actual spawn "+archetype);
                Near(n.Health,n.MaxHealth,"Current starts at rolled max");
                float outgoing=(W.Tuning.Npcs.AttackDamage+W.Stars*W.Tuning.Npcs.DamagePerStar)*a.DamageMultiplier;
                Near(n.ContactDamage,outgoing,"Enemy per-hit damage unchanged");Destroy(n.gameObject);
            }
        }
        var wave=definition.Director as EndlessWaveDirector;
        if(wave!=null)
        {
            var state=W.Mode.Director as EndlessWaveState;
            state.Tick(wave.IntermissionSeconds+.1f);
            Check(state.Alive.Count>0&&state.Alive.Count<=wave.MaxAlive,"Actual Endless wave spawns and respects cap.");
            foreach(var npc in state.Alive)
            {
                float mean=wave.HealthFor(1)*npc.Archetype.HealthMultiplier;
                Check(npc.MaxHealth>=mean*hp*.9f-.001f&&npc.MaxHealth<=mean*hp*1.1f+.001f,"Wave health and difficulty applied once.");
            }
            var spec=wave.Compose(5,2,W.Progression.Data.Side);
            var scaled=CityNpc.Spawn(W,W.City.Sidewalks[0],spec.Role,spec.Archetype,spec.Health,spec.Damage);
            Check(scaled.MaxHealth>=spec.Health*hp*.9f-.001f&&scaled.MaxHealth<=spec.Health*hp*1.1f+.001f,"Wave-five final mean passes through difficulty exactly once");
            Near(scaled.ContactDamage,spec.Damage,"Wave-five enemy damage scaling unchanged");Destroy(scaled.gameObject);
            int count=state.Spawned;state.Tick(.001f);Check(state.Spawned==count,"Full budget cannot double-spawn.");
            Log("MEASURED Endless reinforcement interval: 0 (immediate), unchanged for "+id+"; cap "+wave.MaxAlive);
        }
        // Actual timed mode spawning: clean authored encounters, advance just below/above the resolved interval.
        if(definition.MaximumEncounters>0)
        {
            foreach(var crime in W.Crimes)if(crime.Encounter!=null)crime.Encounter.enabled=false;
            W.Crimes.Clear();
            int spawned=0;Action<CrimeEncounter> on=e=>spawned++;W.EncounterSpawned+=on;
            // Existing elapsed time is below a second. Reset timer only, without changing gameplay rules.
            typeof(GameModeSession).GetField("spawnClock",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(W.Mode,0f);
            W.Mode.Tick(W.Mode.EncounterSpawnInterval-.01f);Check(spawned==0,"No early recurring encounter");
            W.Mode.Tick(.02f);Check(spawned==1,"Exactly one encounter at resolved cadence");W.EncounterSpawned-=on;
        }
        if(mode=="villain")
        {
            W.AddHeat(2);int policeSpawns=0;float firstAt=-1,start=Time.time;
            Action<CityNpc> spawn=n=>{if(n.Role==NpcRole.Cop){policeSpawns++;if(firstAt<0)firstAt=Time.time;}};
            typeof(WorldSession).GetField("responseTimer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(W,0f);
            CityNpc.Spawned+=spawn;W.enabled=true;
            float interval=W.PoliceResponseInterval;yield return new WaitForSeconds(interval*.75f);
            Check(policeSpawns==0,"No early police reinforcement");
            yield return new WaitForSeconds(interval*.25f+.12f);W.enabled=false;CityNpc.Spawned-=spawn;
            Check(policeSpawns>0&&firstAt-start>=interval-.03f&&firstAt-start<interval+.15f,"Real police arrival obeys adjusted timer");
            Log($"MEASURED {id} actual police arrival {firstAt-start:F5}s, interval {interval:F5}s, spawned {policeSpawns}.");
        }
        Isolate();Hud.DismissBriefing("difficulty verification");
        var rolled=CityNpc.Spawn(W,W.City.Sidewalks[0],W.Progression.Data.Side==PlayerSide.Villain?NpcRole.Cop:NpcRole.Criminal,Resources.Load<EnemyArchetype>("Enemies/Brute"));
        rolled.enabled=false;rolled.Agent.enabled=false;rolled.transform.position=new Vector3(4,150.05f,9);rolled.AlwaysAggro=true;Physics.SyncTransforms();
        Aim(Chest(rolled));float rolledMax=rolled.MaxHealth;rolled.Damage(1,W.Powers);yield return Frames(2);
        Check(Hud.EnemyBars.Any(b=>b.Target==rolled&&Mathf.Abs(b.ShownMaxHealth-rolledMax)<.003f),"Bar displays actual spawned difficulty-scaled rolled maximum");Destroy(rolled.gameObject);
        var target=Actor(new Vector3(0,150.05f,9),W.Progression.Data.Side==PlayerSide.Villain?NpcRole.Cop:NpcRole.Criminal,1000);target.AlwaysAggro=true;Aim(Chest(target));hits.Clear();
        W.Rolls.Seed(101);var roll=new CombatRolls(101).Damage(27,W.Tuning.CombatRanges);
        Check(W.Powers.Use(Runtime("fire")),"Paid Fire cast "+id);yield return new WaitForSeconds(.6f);
        var hit=hits.Last(h=>h.Target==target);Near(hit.ResolvedDamage,roll*dm,"Actual final Fire hit");
        Near(1000-target.Health,roll*dm,"Health loss matches final damage");
        Check(Hud.DamageNumbers.Any(n=>n.Active&&n.Target==target&&Mathf.Abs(n.Amount-hit.ResolvedDamage)<.003f),"Floating number reads final difficulty damage");
        Check(Hud.EnemyBars.Any(b=>b.Target==target&&Mathf.Abs(b.ShownMaxHealth-target.MaxHealth)<.003f),"Bar reads stored max");
        W.DamagePlayer(5);yield return null;
        Near(Hud.HealthFill.style.width.value.value,W.Health/W.MaxHealth*100,"HUD health fraction uses difficulty denominator",.02f);
        // Final recipient covers independent synergy and shatter paths without introducing new variance.
        float before=target.Health;using(W.Powers.Credit("synergy:control"))target.Damage(40,W.Powers);Near(before-target.Health,40*dm,"Independent authored synergy damage scales once");
        float enemyBefore=W.Health;W.DamagePlayer(5);Near(enemyBefore-W.Health,5,"Incoming enemy damage not multiplied");
        if(mode=="free-play")
        {
            W.enabled=true;W.DamagePlayer(99999,true);yield return new WaitForSeconds(W.Tuning.Movement.RespawnDelay+.3f);
            Near(W.Health,W.MaxHealth,"Respawn restores difficulty-adjusted max");W.enabled=false;
        }
        Destroy(target.gameObject);
        if(definition.ShowResults){W.Mode.EndToResults();yield return Scene(GameFlow.ResultsScene);Check(FindAnyObjectByType<ModeScreens>().Profile.Data.DifficultyId==id,"Results retains difficulty");}
        yield return Home();Check(Profile.Data.DifficultyId==id,"Return Home retains difficulty");
    }
    IEnumerator Hero(string id,string hero)
    {
        yield return StartSession(id,"free-play",hero,"ice","strength");
        W.enabled=false;W.Mode.enabled=false;Isolate();Hud.DismissBriefing("difficulty hero");
        var d=W.Difficulty;var h=F.Hero(hero).Stats;
        Near(W.MaxHealth,100*h.MaxHealth*d.PlayerHealth,id+" "+hero+" actual max health");
        Near(W.Powers.MaxEnergy,100*h.MaxEnergy,"Hero energy maximum preserved");Near(W.Powers.EnergyRegen,12*h.EnergyRegen,"Hero regen preserved");
        var target=Actor(new Vector3(0,150.05f,9),NpcRole.Criminal,1000);Aim(Chest(target));
        var power=Runtime("ice");float energy=W.Powers.Energy;Check(W.Powers.Use(power),"Real Ice activation "+hero);
        Near(energy-W.Powers.Energy,power.Definition.ResourceCost*d.EnergyUsage,"Instant energy paid once");
        Near(power.Cooldown,power.Definition.Cooldown*h.CooldownMultiplier*d.Cooldown,"Actual cooldown assigned once");
        int charges=power.Charges;float recharge=power.Definition.ChargeRecharge*d.Cooldown;
        W.Powers.Tick(recharge-.001f,true);Check(power.Charges==charges,"Charge not recharged early");
        W.Powers.Tick(.002f,true);Check(power.Charges==charges+1,"Charge recharges at adjusted threshold");
        Log($"MEASURED {id}/{hero}: health {W.MaxHealth:F3}; energy {energy:F3}; regen {W.Powers.EnergyRegen:F3}; Ice cost {W.Powers.EnergyCost(power):F3}, cooldown {W.Powers.Stats(power).Cooldown:F3}, recharge {recharge:F3}.");
        Destroy(target.gameObject);
        // Real melee uses the hero multiplier and final difficulty; do not merely inspect stats.
        target=Actor(new Vector3(0,150.05f,2.7f),NpcRole.Criminal,1000);hits.Clear();W.Powers.Tick(100,true);W.Rolls.FixedUnit=.5f;
        Check(W.Hero.TryPunch(),"Real hero punch");yield return new WaitForSeconds(.5f);
        Near(hits.Last(x=>x.Target==target).ResolvedDamage,35*h.MeleeDamage*d.PlayerDamage,"Hero melee identity preserved");Destroy(target.gameObject);
    }
    IEnumerator Beam(string id)
    {
        yield return StartSession(id,"free-play","vector","laser-eyes","fire");
        W.enabled=false;W.Mode.enabled=false;Isolate();Hud.DismissBriefing("difficulty beam");
        foreach(var n in W.Npcs.ToArray())if(n!=null)Destroy(n.gameObject);yield return null;
        var crowd=new List<CityNpc>();for(int i=0;i<12;i++)crowd.Add(Actor(new Vector3((i%4-1.5f)*2,150.05f,8+i/4*2),NpcRole.Criminal,10000));
        var target=crowd[1];Aim(Chest(target));W.Rolls.FixedUnit=.5f;hits.Clear();
        var laser=Runtime("laser-eyes");Check(W.Powers.Select(laser)&&W.Powers.Use(laser),"Real Laser channel begins");
        float energy=W.Powers.Energy;W.Powers.Channel(true,.25f);
        Near(energy-W.Powers.Energy,64*.25f*W.Difficulty.EnergyUsage,"Laser actual gross drain");
        Near(hits.Last(x=>x.Target==target).ResolvedDamage,42*.25f*W.Difficulty.PlayerDamage,"Stable channel DPS difficulty once");
        W.Powers.EndChannel();Near(laser.Cooldown,laser.Definition.Cooldown*W.Difficulty.Cooldown,"Laser stop cooldown");
        yield return Frames(20);W.Powers.Select(Runtime("fire"));
        double elapsed=0;int peakVfx=0,peakLabels=0;var particles=FindObjectsByType<ParticleSystem>();
        for(int i=0;i<180;i++)
        {
            if(i%30==0)
            {
                W.Powers.Tick(100,true);Check(W.Powers.Use(Runtime("fire")),"Dense sample real Fire cast");
                foreach(var n in crowd)using(W.Powers.Credit("fire"))n.Damage(1,W.Powers);
            }
            yield return null;elapsed+=Time.unscaledDeltaTime;
            peakVfx=Mathf.Max(peakVfx,particles.Count(p=>p!=null&&p.isPlaying));peakLabels=Mathf.Max(peakLabels,Hud.DamageNumbers.Count(n=>n.Active));
        }
        Log($"MEASURED dense {id}: {180/elapsed:F2} FPS / {elapsed/180*1000:F3} ms; alive {crowd.Count(n=>n!=null&&!n.Dead)}; peak active particle systems {peakVfx}; bars {Hud.EnemyBars.Count}, labels {Hud.DamageNumbers.Count}, peak active labels {peakLabels}. 180-frame fixed12-target Editor workload; not standalone pressure benchmark.");
        Check(crowd.Count==12&&Hud.EnemyBars.Count==20&&Hud.DamageNumbers.Count==96,"Dense workload keeps fixed enemy/UI budgets");
    }
    IEnumerator MenuCapture(ModeScreens menu,string id,int width=1280,int height=720)
    {
        var rt=new RenderTexture(width,height,24);rt.Create();menu.Panel.targetTexture=rt;
        yield return Frames(5);menu.Root.MarkDirtyRepaint();yield return Frames(2);
        var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        foreach(string mode in new[]{"hero","villain"})
        {
            var glyph=menu.Root.Q<VisualElement>("mode-glyph-"+mode);var title=menu.Root.Q<Label>("mode-title-"+mode);
            Check(glyph.worldBound.yMax<title.worldBound.yMin,$"Mode icon/title do not overlap at {width}x{height}: "+mode);
        }
        Check(menu.DifficultyDetails.worldBound.yMax<menu.ModeButtons["hero"].worldBound.yMin,"Difficulty details clear mode cards at "+width+"x"+height);
        image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(Folder+id+".png",image.EncodeToPNG());RenderTexture.active=old;
        menu.Panel.targetTexture=null;Destroy(image);rt.Release();Destroy(rt);
    }
}
#endif
