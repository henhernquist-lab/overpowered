#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

/// Uses actual Home callbacks and unmodified shipping mode/director/event assets. Only explicit Editor round-jump and
/// intermission time advancement speed up the checks; no replacement boss prefab, cloned schedule or fake health.
public sealed class TwoModeVerificationRunner : SessionVerificationRunner
{
    protected override string Folder=>"Verification/TwoModes/";
    protected override string ResultFile=>Reload?"reload.txt":"results.txt";
    public bool Reload;
    EndlessWaveState State=>W?.Mode?.Director as EndlessWaveState;
    PulsebreakerBoss Boss=>PulsebreakerBoss.Active;
    GameHud Hud=>FindAnyObjectByType<GameHud>();
    ModeScreens Menu=>FindAnyObjectByType<ModeScreens>();
    readonly HashSet<string> seen=new HashSet<string>();
    int arrivals,diagnostics;
    void Submit(Button b){using(var e=NavigationSubmitEvent.GetPooled()){e.target=b;b.SendEvent(e);}}
    static bool Near(float a,float b)=>Mathf.Abs(a-b)<.002f;
    void Observe(string message,string trace,LogType type){if(type==LogType.Warning&&message.Contains("Pulsebreaker slot reserved"))diagnostics++;}
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        if(Reload)
        {
            Check(Profile.LastError==null&&JsonUtility.ToJson(Profile.Data)==File.ReadAllText(Folder+"saves/expected.json"),"SECOND PROCESS exact progression, legacy mode records, cosmetics, signature, difficulty and loadout restored");
            MenuChecks();
            Submit(Menu.ModeButtons["endless-fight"]);yield return Scene(GameFlow.CityScene);
            Check(State!=null&&State.Tuning.Events==Resources.Load<EndlessWaveEvents>("ModeDirectors/EndlessWaveEvents"),"SECOND PROCESS actual Home Endless route uses shipping events");
            Check(State.JumpToRoundForDevelopment(5),"SECOND PROCESS development jump accepted");yield return AwaitRound(5);yield return AwaitBoss();
            Check(Boss.Npc.IsBoss&&Hud.BossBarVisible,"SECOND PROCESS actual round-5 boss and bar present");
            yield return Home();Check(Boss==null,"SECOND PROCESS Home disposes boss");yield break;
        }
        MenuChecks();yield return MenuCapture();
        Profile.AddXp(350);Profile.Data.LastModeId="villain";
        foreach(string id in new[]{"hero","villain","endless-fight-villain"})
        {
            var old=Resources.Load<GameModeDefinition>("Modes/"+id);
            Check(old!=null&&old.Playable&&!old.ShowOnHome,"Legacy "+id+" remains internally loadable without Home selection");
            Profile.Data.ModeRecords.Add(new ModeRecord{Id=id,BestScore=123,BestWave=7,Runs=2});
        }
        Profile.Data.Side=PlayerSide.Villain;Profile.Save();
        Submit(Menu.ForgeButton);yield return null;
        var forge=Menu.ForgeScreen;
        Check(forge!=null&&forge.SelectHero(F.Heroes[0]),"Existing Hero Forge opens through its real Home button");
        Check(forge.SelectCosmetic(CosmeticSlot.Suit,"vector_suit_tactical")&&forge.SelectCosmetic(CosmeticSlot.Mask,"vector_mask_sentinel")&&forge.SelectCosmetic(CosmeticSlot.Emblem,"vector-emblem-bolt"),"Existing actual modular outfit selections still work");forge.Close();
        Check(Profile.SetLoadout(F.Heroes[0],Power("ice"),Power("strength"),F.Heroes[0].Primary,F.Heroes[0].Secondary),"Save current hero and two equipped powers through existing ownership/equip gates");
        Check(Menu.SelectDifficulty("normal"),"Normal difficulty selection preserved");
        Submit(Menu.ModeButtons["endless-fight"]);yield return Scene(GameFlow.CityScene);
        Check(State!=null&&ReferenceEquals(State.Tuning,Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves")),"Actual Home callback starts the SHIPPING Endless director, not a fixture clone");
        Check(State.Tuning.Events!=null&&State.Tuning.Events.MinibossArchetype==Resources.Load<EnemyArchetype>("Enemies/Pulsebreaker"),"Actual shipping director references the actual Pulsebreaker schedule/archetype");
        Check(W.Progression.Data.Side==PlayerSide.Villain&&W.Progression.SideLocked,"Single Endless choice retains saved allegiance and locks it for that run");
        Check(W.Powers.IsEquipped(Power("ice"))&&W.Powers.IsEquipped(Power("strength")),"Current equipped powers carry through actual menu spawn");
        Check(W.Hero.GetComponentInChildren<HeroAppearanceRenderer>().SuitId=="vector_suit_tactical","Actual saved Forge suit carries into Endless");
        W.Rolls.FixedUnit=.5f;
        for(int round=1;round<=15;round++)
        {
            if(State.Intermission)State.Tick(State.Tuning.IntermissionSeconds+.01f);
            yield return AwaitRound(round);
            Check(State.Plan.Miniboss==(round>=5&&round%5==0),"Shipping round "+round+": exact guaranteed boss formula");
            if(round%5==0){yield return AwaitBoss();yield return BossRound(round,round==5);}
            else
            {
                Check(Boss==null,"CONTROL round "+round+": no automatic boss");
                yield return ClearRegulars(false);
            }
            for(float end=Time.realtimeSinceStartup+5;!State.Intermission&&Time.realtimeSinceStartup<end;)yield return null;
            Check(State.Intermission&&State.Wave==round,"Round "+round+" completes exactly once after authoritative defeats");
        }
        Check(arrivals==3,"Shipping rounds 5/10/15 each delivered one functional Pulsebreaker");
        Check(new[]{"Rusher","Gunner","Brute","Skirmisher","Bulwark"}.All(seen.Contains),"All five regular archetypes actually spawned through round 15: "+string.Join(",",seen));
        Check(State.PeakAlive<=State.Tuning.MaxAlive,"Live enemy count remains within existing MaxAlive cap");
        yield return RetryAndLoss();
        yield return CapReservation();
        foreach(string difficulty in new[]{"hard","extreme"})
        {
            yield return Home();Check(Menu.SelectDifficulty(difficulty),difficulty+": Home difficulty selection works");
            Submit(Menu.ModeButtons["endless-fight"]);yield return Scene(GameFlow.CityScene);W.Rolls.FixedUnit=.5f;
            foreach(int round in new[]{5,10,15})
            {
                Check(State.JumpToRoundForDevelopment(round),difficulty+": jump to "+round);yield return AwaitRound(round);yield return AwaitBoss();
                float expected=State.Tuning.HealthFor(round)*State.Plan.Health*State.Tuning.Events.MinibossArchetype.HealthMultiplier*State.Tuning.Events.MinibossHealthMultiplier*W.Difficulty.EnemyHealth;
                Check(Near(Boss.Npc.MaxHealth,expected),difficulty+" round "+round+": enemy health difficulty applied exactly once ("+Boss.Npc.MaxHealth+" / "+expected+")");
                Check(Near(Boss.Npc.ContactDamage,State.Tuning.DamageFor(round)*State.Plan.Damage*State.Tuning.Events.MinibossDamageMultiplier),difficulty+" round "+round+": attack damage remains unscaled until shared player damage boundary");
                yield return BossRound(round,false);
            }
        }
        yield return Home();Submit(Menu.ModeButtons["free-play"]);yield return Scene(GameFlow.CityScene);
        Check(State==null&&W.Mode.Definition.Director==null&&W.Mode.Definition.InitialEncounters==0&&W.Mode.Definition.MaximumEncounters==0,"Free Play has no wave director, forced combat encounters or rounds");
        W.Mode.Tick(10000);W.AddHeat(3);W.ReconcilePolice();yield return null;
        Check(Boss==null&&!Hud.BossBarVisible&&W.Npcs.Any(n=>n!=null&&n.Role==NpcRole.Cop),"Free Play advances independently, preserves Heat/police and never automatically creates Pulsebreaker");
        Check(W.Progression.SwitchSide(),"Free Play keeps working internal allegiance switching");
        yield return Home();
        Check(Menu.ModeButtons.Count==2&&Profile.Data.Version==1&&Profile.Data.ModeRecords.Count(r=>new[]{"hero","villain","endless-fight-villain"}.Contains(r.Id))==3,"Old mode records and Version 1 survive the overhaul without destructive migration");
        Check(Profile.Appearance(F.Heroes[0]).SuitId=="vector_suit_tactical","Cosmetics persist across both modes and all difficulty runs");
        Profile.Save();File.WriteAllText(Folder+"saves/expected.json",JsonUtility.ToJson(Profile.Data));
        Log("LIMIT: deterministic native Play Mode controls, not hardware-input, human pacing/readability or a sustained standalone performance test.");
    }
    void MenuChecks()
    {
        Check(Menu!=null&&Menu.ModeButtons.Keys.OrderBy(x=>x).SequenceEqual(new[]{"endless-fight","free-play"}),"Home exposes exactly two gameplay choices: ENDLESS FIGHT and FREE PLAY; no campaign/variant/Lab route");
        Check(Menu.Root.Q<Label>("mode-title-endless-fight").text=="ENDLESS FIGHT"&&Menu.Root.Q<Label>("mode-title-free-play").text=="FREE PLAY","Actual card titles match the two-mode design");
        Check(Menu.ForgeButton!=null&&Menu.ForgeButton.enabledSelf&&Menu.DifficultyButtons.Count==3,"Hero Forge and all three difficulty choices remain accessible");
        Check(Profile.LastError==null,"Existing Version 1 profile loads without mode errors");
    }
    IEnumerator AwaitRound(int round)
    {
        float end=Time.realtimeSinceStartup+8;
        while(State!=null&&(State.Intermission||State.Wave!=round)&&Time.realtimeSinceStartup<end)yield return null;
        Check(State!=null&&State.Wave==round&&!State.Intermission,"Actual round "+round+" started");yield return null;
    }
    IEnumerator AwaitBoss()
    {
        float end=Time.realtimeSinceStartup+8;
        while((Boss==null||!Hud.BossBarVisible)&&Time.realtimeSinceStartup<end)yield return null;
        Check(Boss!=null&&Boss.Npc!=null&&!Boss.Npc.Dead&&Boss.Npc.IsBoss,"Actual functioning boss brain attached to live wave actor");
        Check(W.Npcs.Count(n=>n!=null&&!n.Dead&&n.IsBoss)==1,"Maximum one active Pulsebreaker");
        Check(Boss.Npc.Agent.isOnNavMesh&&Mathf.Abs(Boss.transform.position.y-State.Arena.y)<1f,"Boss spawned on valid ground NavMesh, not a rooftop");
        Check(Boss.Npc.Hostile&&Boss.Npc.AlwaysAggro,"Boss is hostile to current allegiance and permanently aggroed");
    }
    void ObserveMix()
    {
        for(int i=0;i<State.Alive.Count;i++)
        {
            var n=State.Alive[i];if(n==null||n.IsBoss)continue;
            seen.Add(n.Archetype.DisplayName);
            if(n.Archetype.DisplayName=="Skirmisher")Check(State.Wave>=3,"Skirmisher eligibility starts at round 3");
            if(n.Archetype.DisplayName=="Bulwark")Check(State.Wave>=5,"Bulwark eligibility starts at round 5");
        }
    }
    IEnumerator ClearRegulars(bool keepBoss)
    {
        float end=Time.realtimeSinceStartup+15;
        while(State.WaveKills<State.Size-(keepBoss?1:0)&&Time.realtimeSinceStartup<end)
        {
            ObserveMix();foreach(var n in State.Alive.ToArray())if(n!=null&&!n.Dead&&!(keepBoss&&n.IsBoss))n.Damage(1000000,W.Powers);
            yield return null;
        }
        Check(State.WaveKills>=State.Size-(keepBoss?1:0),"All intended regular wave actors defeated through existing NPC damage path");
    }
    IEnumerator BossRound(int round,bool captures)
    {
        arrivals++;var boss=Boss;var hud=Hud;int kills=State.MinibossKills;
        Check(hud.BossBarVisible&&hud.BossNameShown=="PULSEBREAKER"&&Near(hud.BossFraction,boss.Npc.HealthFraction),"Boss bar names actual boss and uses authoritative HP/max");
        yield return ClearRegulars(true);
        Check(State.Wave==round&&!State.Intermission&&!State.BossDefeated&&State.Remaining==1,"Boss defeat is REQUIRED; regular kills cannot clear round "+round);
        int updates=hud.BossBarUpdates;boss.Npc.Damage(.25f,null);
        Check(hud.BossBarUpdates==updates+1&&Mathf.Abs(hud.BossFraction-boss.Npc.HealthFraction)<.000001f,"Existing NPC health event updates even a sub-.25% hit immediately; no epsilon filtering");
        Check(!string.IsNullOrWhiteSpace(hud.BossRoot.Q<Label>("boss-value").text),"Boss bar displays actual current / maximum HP text");
        if(captures)yield return Composite("round-5-phase-1-third",1280,720);
        var view=Camera.main.GetComponent<ThirdPersonCamera>();bool wasFirst=view.FirstPerson;if(!wasFirst)view.ToggleView();yield return null;
        Check(hud.BossBarVisible,"Boss bar remains visible in first-person");
        if(captures)yield return Composite("round-5-phase-1-first",1920,1080);
        if(!wasFirst)view.ToggleView();
        W.Mode.SetPaused(true);yield return null;Check(!hud.BossBarVisible,"Boss bar hides while pause/menu controls are open");W.Mode.SetPaused(false);yield return null;
        Check(hud.BossBarVisible,"Boss bar restores after pause");
        boss.Npc.Damage(boss.Npc.CurrentHealth-boss.Npc.MaxHealth*.399f,null);yield return null;yield return null;
        Check(boss.Phase2&&hud.BossPhaseShown=="PHASE 2"&&hud.BossRoot.Q<Label>("boss-phase").text.Contains("OVERDRIVE"),"Authoritative <=40% threshold enters actual Overdrive and explicit phase UI");
        if(captures)yield return Composite("round-5-overdrive",1280,720);
        boss.Npc.Damage(1000000,W.Powers);Check(!hud.BossBarVisible,"Boss death event immediately removes bar");yield return null;
        Check(State.BossDefeated&&State.MinibossKills==kills+1,"Boss defeat accounts exactly once in wave and miniboss rewards");
        Check(State.Intermission&&State.Wave==round,"Defeating final boss completes its round");
        Check(Hud.OverlayRoot.Query<VisualElement>("hud-boss").ToList().Count==1,"Repeated boss rounds reuse one bounded HUD bar");
    }
    IEnumerator RetryAndLoss()
    {
        Check(State.JumpToRoundForDevelopment(5),"Retry/lost-boss control starts a real scheduled boss round");
        var wall=Wall(State.Arena+Vector3.up*3,new Vector3(160,5.8f,160));Physics.SyncTransforms();
        Application.logMessageReceived+=Observe;yield return AwaitRound(5);yield return new WaitForSeconds(1.1f);
        Check(Boss==null&&State.BossPending&&!State.BossDefeated&&State.Spawned==0&&State.Alive.Count==0&&!State.Intermission,"Blocked candidates reserve boss before all regulars and cannot silently clear the round");
        Check(State.BossSpawnAttempts>=2&&State.BossSpawnAttempts<=5&&diagnostics>0,"Safe bounded candidates retry on cooldown and emit useful reserved-slot diagnostic");
        Destroy(wall);yield return null;yield return AwaitBoss();Application.logMessageReceived-=Observe;
        int lost=State.Lost,kills=State.MinibossKills;var lostBoss=Boss.Npc;Destroy(Boss.Npc.gameObject);yield return null;yield return null;yield return AwaitBoss();
        Check(!ReferenceEquals(Boss.Npc,lostBoss)&&State.Lost==lost+1&&State.MinibossKills==kills&&!State.BossDefeated,"Unexpected despawn is NOT a defeat; reserved slot reconstructs actual Pulsebreaker instead of a regular enemy");
        yield return BossRound(5,false);
    }
    IEnumerator CapReservation()
    {
        yield return Home();var mode=Instantiate(Resources.Load<GameModeDefinition>("Modes/endless-fight"));var director=Instantiate((EndlessWaveDirector)mode.Director);mode.Director=director;director.MaxAlive=1;
        Check(GameFlow.Instance.Select(mode),"Start isolated cap=1 control without mutating shipping asset");yield return Scene(GameFlow.CityScene);
        State.JumpToRoundForDevelopment(5);yield return AwaitRound(5);yield return AwaitBoss();
        Check(State.Alive.Count==1&&State.Alive[0]==Boss.Npc&&State.Spawned==1,"Even cap=1 admits reserved boss first; no regular enemy silently occupies the slot");
        yield return Home();Destroy(mode);Destroy(director);
    }
    IEnumerator MenuCapture()
    {
        var rt=new RenderTexture(1280,720,24);rt.Create();Menu.Panel.targetTexture=rt;
        for(int i=0;i<5;i++)yield return null;
        SaveImage(rt,"home-two-modes.png");Menu.Panel.targetTexture=null;rt.Release();Destroy(rt);
        var a=Menu.ModeButtons["endless-fight"].worldBound;var b=Menu.ModeButtons["free-play"].worldBound;
        Check(a.width>300&&b.width>300&&!a.Overlaps(b),"Both main mode cards have usable, nonoverlapping rendered bounds");
    }
    IEnumerator Composite(string name,int width,int height)
    {
        var hud=Hud;var cam=Camera.main;
        hud.DismissBriefing("verification: existing briefing dismiss control");
        for(float end=Time.realtimeSinceStartup+2;GameHud.Shown(hud.Briefing)&&Time.realtimeSinceStartup<end;)yield return null;
        Check(!GameHud.Shown(hud.Briefing),"Capture uses the existing dismissed briefing, so actual boss combat is visible");
        var rig=cam.GetComponent<ThirdPersonCamera>();var delta=Boss.transform.position+Vector3.up*1.5f-(W.Hero.transform.position+Vector3.up*1.5f);
        rig.SetLook(Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,rig.FirstPerson?-Mathf.Atan2(delta.y,new Vector2(delta.x,delta.z).magnitude)*Mathf.Rad2Deg:10f);
        var rt=new RenderTexture(width,height,24);rt.Create();hud.Panel.targetTexture=rt;hud.OverlayPanel.targetTexture=rt;cam.aspect=width/(float)height;
        for(int i=0;i<4;i++)yield return null;
        var bounds=hud.BossRoot.worldBound;var outer=hud.OverlayRoot.worldBound;
        Check(bounds.width>200&&bounds.xMin>=outer.xMin&&bounds.xMax<=outer.xMax&&bounds.yMin>=outer.yMin&&bounds.yMax<=outer.yMax,"Boss bar fits actual "+width+"x"+height+" layout");
        Check(!bounds.Overlaps(hud.DirectorGroup.worldBound),"Boss bar clears the round/score HUD");
        bool enabled=cam.enabled;cam.enabled=false;cam.targetTexture=rt;cam.Render();cam.targetTexture=null;hud.Root.MarkDirtyRepaint();hud.OverlayRoot.MarkDirtyRepaint();yield return null;
        SaveImage(rt,name+".png");cam.enabled=enabled;cam.ResetAspect();hud.Panel.targetTexture=null;hud.OverlayPanel.targetTexture=null;rt.Release();Destroy(rt);yield return null;
    }
    void SaveImage(RenderTexture rt,string name)
    {
        var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Folder+name,image.EncodeToPNG());RenderTexture.active=old;Destroy(image);Log("IMAGE "+name);
    }
}
#endif
