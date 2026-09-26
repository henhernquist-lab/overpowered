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

/// Real retained UI events/rendering; isolated save. Session completion is deliberately accelerated.
public sealed class MenuPresentationVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;
    readonly List<string> output=new List<string>();
    ModeScreens ui;
    RenderTexture target;
    string Folder=>Path.GetFullPath("Verification/Menus");
    WorldSession W=>WorldSession.Instance;
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        var stack=new Stack<IEnumerator>();stack.Push(Checks());
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
    void Check(bool valid,string text){if(!valid)throw new Exception(text);Log("PASS "+text);}
    void Write(){File.WriteAllLines(Path.Combine(Folder,"results.txt"),output);}
    IEnumerator Scene(string name)
    {
        float deadline=Time.realtimeSinceStartup+30;
        while(GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null)){if(Time.realtimeSinceStartup>deadline)throw new Exception("Scene timeout "+name);yield return null;}
        yield return null;
        if(W!=null){W.Hero.enabled=false;var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=new Vector3(0,100,0);cc.enabled=true;}
    }
    IEnumerator Menu()
    {
        ui=UnityEngine.Object.FindAnyObjectByType<ModeScreens>();Check(ui!=null,"Menu component exists in "+SceneManager.GetActiveScene().name);
        if(target!=null){target.Release();Destroy(target);}target=new RenderTexture(1280,720,24){name="Verification UI 1280x720"};target.Create();ui.Panel.targetTexture=target;
        for(int i=0;i<15;i++)yield return null;
        Check(ui.Root.resolvedStyle.width>1000&&ui.Root.resolvedStyle.height>600,$"Actual retained UI layout {ui.Root.resolvedStyle.width:0}x{ui.Root.resolvedStyle.height:0} logical pixels.");
    }
    void Submit(Button button){using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}}
    void Capture(string name)
    {
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());Destroy(image);Log("CAPTURE "+name+".png (actual UI Toolkit target texture, not a mockup)");
    }
    IEnumerator Benchmark(bool moving,string label)
    {
        ui.MotionEnabled=moving;yield return new WaitForSecondsRealtime(.7f);
        var watch=System.Diagnostics.Stopwatch.StartNew();var frames=new List<double>();double last=0;int draws=0;
        while(watch.Elapsed.TotalSeconds<4){yield return null;double now=watch.Elapsed.TotalSeconds;frames.Add((now-last)*1000);last=now;draws+=UnityStats.drawCalls;}
        frames.Sort();Log($"MEASURED {label}: motion={moving}, frames={frames.Count}, seconds={watch.Elapsed.TotalSeconds:F3}, FPS={frames.Count/watch.Elapsed.TotalSeconds:F2}, mean={frames.Average():F3}ms, p95={frames[(int)(frames.Count*.95)]:F3}ms, drawCalls={draws/(float)frames.Count:F1}, 1280x720 Editor UI render.");
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);yield return Menu();
        var palette=Resources.Load<CityPalette>("CityPalette");var hero=ui.ModeButtons["hero"];var villain=ui.ModeButtons["villain"];
        Check(hero.worldBound.xMax<villain.worldBound.xMin&&Mathf.Abs(hero.worldBound.y-villain.worldBound.y)<1,"Hero/Villain cards are genuinely side by side.");
        Check(hero.Query<MenuIcon>().ToList().Any(i=>i.Glyph==MenuGlyph.Shield&&i.Ink==ui.ColorFor(PlayerSide.Hero))&&villain.Query<MenuIcon>().ToList().Any(i=>i.Glyph==MenuGlyph.Flame&&i.Ink==ui.ColorFor(PlayerSide.Villain)),"Original shield/city and angular flame use distinct shared Hero/Villain palette colors.");
        Check(ui.Profile.Data.Level==1&&ui.Profile.Data.Xp==0&&ui.Profile.Data.Points==0,"Fresh-save CONTROL starts level 1 / XP 0 / points 0.");
        // Free Play and both Endless Fight variants are now playable data-defined modes (intentional change, Step 2-3):
        // the extras row shows them ENABLED below the side cards, and no Coming Soon placeholder remains.
        foreach(var id in new[]{"free-play","endless-fight","endless-fight-villain"})
        {
            var button=ui.ModeButtons[id];
            Check(Resources.Load<GameModeDefinition>("Modes/"+id).Playable&&button.enabledInHierarchy&&button.worldBound.width>0&&button.worldBound.yMin>hero.worldBound.yMax,"Visible ENABLED extras-row button "+id+" below the side cards.");
        }
        Check(ui.Root.Query<Label>().ToList().All(l=>l.text!="COMING SOON"),"Coming Soon strip is empty: no non-playable mode definitions remain.");
        var hidden=UnityEngine.Object.Instantiate(Resources.Load<GameModeDefinition>("Modes/free-play"));hidden.Playable=false;
        Check(!GameFlow.Instance.Select(hidden)&&!GameFlow.Instance.Loading&&SceneManager.GetActiveScene().name==GameFlow.HomeScene,"Flow-guard CONTROL: a Playable=false definition still does not launch.");Destroy(hidden);
        Capture("home");
        using(var enter=PointerEnterEvent.GetPooled()){enter.target=hero;hero.SendEvent(enter);}yield return new WaitForSecondsRealtime(.4f);
        Check(hero.style.translate.value.y.value<-7,$"Hover lifts Hero card {hero.style.translate.value.y.value:F2} logical pixels; configured -8.");Capture("home-hover");
        var skyline=GameFlow.Instance.GetComponent<MenuSkyline>();int plannedBuildings=Resources.Load<CityLayout>("CityLayout").Generate(Resources.Load<GameTuning>("GameTuning").City).Count;Check(skyline.BuildingCount==plannedBuildings&&plannedBuildings>=40&&skyline.Texture.IsCreated(),$"Skyline uses {skyline.BuildingCount} actual seeded buildings; one-time capture {skyline.CaptureMilliseconds:F1}ms; cached {skyline.Texture.width}x{skyline.Texture.height}.");
        yield return Benchmark(false,"home static A");yield return Benchmark(true,"home animated B");yield return Benchmark(true,"home animated B2");yield return Benchmark(false,"home static A2");ui.MotionEnabled=true;
        Submit(ui.ModeButtons["free-play"]);yield return Scene(GameFlow.CityScene);Check(W.Mode.Definition.Id=="free-play"&&!W.Progression.SideLocked,"Actual Free Play extras-button submit loads Free Play (side not locked).");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);yield return Menu();hero=ui.ModeButtons["hero"];
        Submit(hero);yield return Scene(GameFlow.CityScene);Check(W.Mode.Definition.Side==PlayerSide.Hero,"Actual Hero card submit loads Hero gameplay.");
        W.Mode.AddScore(240);W.AddHeat(2);W.Mode.RecordRescue();W.Progression.AddXp(W.Progression.RequiredXp+40);W.Mode.Finish(SessionOutcome.Won,"UI verification accelerated completion");
        yield return Scene(GameFlow.ResultsScene);yield return Menu();
        float early=ui.DisplayedXpFraction;int earlyLevel=ui.DisplayedLevel;yield return new WaitForSecondsRealtime(2);
        var result=GameFlow.Instance.Result;
        Check(ui.Headline.text=="CITY SAVED"&&ui.Headline.style.color.value==palette.Colors[(int)CityColor.HeroAccent]&&ui.StatCount==4,"Hero result: CITY SAVED, shared cyan, exactly four stats.");
        Check(ui.DisplayedLevel==ui.Profile.Data.Level&&Mathf.Abs(ui.DisplayedXpFraction-ui.Profile.Data.Xp/(float)ui.Profile.RequiredXp)<.001f,$"Animated XP: early L{earlyLevel} {early:F3} -> final L{ui.DisplayedLevel} {ui.DisplayedXpFraction:F3}; saved {ui.Profile.Data.Xp}/{ui.Profile.RequiredXp}.");
        Check(ui.RewardVisible&&ui.UpgradeButtons.Count==3,"Actual XP level-up exposes three upgrade choices.");Capture("hero-results-level-up");
        var strength=Resources.Load<PowerDefinition>("Powers/strength");int tier=ui.Profile.Tier(strength),points=ui.Profile.Data.Points;float force=strength.GetStats(tier).Force;
        Submit(ui.UpgradeButtons.First(b=>b.name=="upgrade-"+strength.Id));yield return null;
        Check(ui.Profile.Tier(strength)==tier+1&&ui.Profile.Data.Points==points-strength.Upgrades[tier].PointCost,$"Real upgrade button -> PlayerProgression.Buy: Strength tier {tier}->{ui.Profile.Tier(strength)}, points {points}->{ui.Profile.Data.Points}, force {force}->{strength.GetStats(ui.Profile.Tier(strength)).Force}.");
        Check(!ui.ChooseUpgrade(strength)&&ui.Profile.Tier(strength)==tier+1,"Zero-point repeated purchase CONTROL rejected.");
        Capture("hero-results-upgraded");Submit(ui.ReplayButton);yield return Scene(GameFlow.CityScene);Check(W.Mode.Definition.Side==PlayerSide.Hero&&W.Progression.Tier(strength)==tier+1,"PLAY AGAIN loads Hero; purchased upgrade reloaded from save.");W.Mode.Finish(SessionOutcome.Lost,"UI loss presentation control");
        yield return Scene(GameFlow.ResultsScene);yield return Menu();yield return new WaitForSecondsRealtime(2);Check(ui.Headline.text=="MISSION FAILED"&&!ui.RewardVisible&&ui.UpgradeButtons.Count==0,"Hero loss / no-level-up CONTROL: MISSION FAILED, no reward choices.");Capture("hero-results-loss");Submit(ui.HomeButton);yield return Scene(GameFlow.HomeScene);yield return Menu();
        Check(ui.Profile.Data.Level==2&&ui.Profile.Tier(strength)==tier+1,"HOME progression preview reloads real level and power tier.");Submit(ui.ModeButtons["villain"]);yield return Scene(GameFlow.CityScene);Check(W.Mode.Definition.Side==PlayerSide.Villain,"Actual Villain card submit loads Villain gameplay.");
        W.Mode.AddScore(420);W.AddHeat(3.5f);W.Progression.AddXp(30);W.Mode.Finish(SessionOutcome.Won,"UI verification accelerated completion");yield return Scene(GameFlow.ResultsScene);yield return Menu();yield return new WaitForSecondsRealtime(2);
        Check(ui.Headline.text=="ESCAPED THE HEAT"&&ui.Headline.style.color.value==palette.Colors[(int)CityColor.VillainAccent]&&ui.StatCount==4,"Villain result: ESCAPED THE HEAT, shared orange, same component/layout, four stats.");Check(GameFlow.Instance.Result.PeakHeat>=3.5f,"Peak Heat snapshot records actual WorldSession.AddHeat updates.");Capture("villain-results");yield return Benchmark(true,"villain results animated");
        Submit(ui.ReplayButton);yield return Scene(GameFlow.CityScene);Check(W.Mode.Definition.Side==PlayerSide.Villain,"PLAY AGAIN loads Villain.");W.Mode.Finish(SessionOutcome.Lost,"UI loss presentation control");yield return Scene(GameFlow.ResultsScene);yield return Menu();yield return new WaitForSecondsRealtime(2);Check(ui.Headline.text=="CAUGHT","Villain loss shows CAUGHT.");Capture("villain-results-loss");Submit(ui.HomeButton);yield return Scene(GameFlow.HomeScene);yield return Menu();
        Check(ui.Profile.Data.Level==2&&ui.Profile.Tier(strength)==tier+1,"Both modes complete Home -> Play -> Results -> Home; saved upgrade remains across mode switch.");Capture("home-progress");
        Log("LIMIT: UI events are automated navigation-submit/pointer-enter events, not hardware mouse input. Gameplay completion/XP grants accelerated via existing methods; no claim of full hands-on sessions. Performance is isolated Editor UI throughput, not standalone gameplay FPS.");
    }
}
#endif
