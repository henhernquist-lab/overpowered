#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class IntroTimelineVerificationRunner : MonoBehaviour
{
    public bool Reload;public Action<int> Finished;
    const string Folder="Verification/Intro/";
    readonly List<string> lines=new List<string>();
    void Log(string value){lines.Add(value);Debug.Log("[INTRO] "+value);File.WriteAllLines(Folder+(Reload?"reload-results.txt":"results.txt"),lines);}
    void Check(bool valid,string value){if(!valid)throw new Exception(value);Log("PASS "+value);}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}
            if(next is IEnumerator child)stack.Push(child);else yield return next;
        }
        Finished(0);
    }
    IEnumerator Home()
    {
        float until=Time.realtimeSinceStartup+40;
        while(SceneManager.GetActiveScene().name!=GameFlow.HomeScene||GameFlow.Instance==null||GameFlow.Instance.Loading||FindAnyObjectByType<ModeScreens>()==null)
        {if(Time.realtimeSinceStartup>until)throw new Exception("Home scene timeout");yield return null;}
        yield return null;
    }
    void Capture(RenderTexture source,string name)
    {
        var old=RenderTexture.active;RenderTexture.active=source;
        var image=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);
        image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();RenderTexture.active=old;
        File.WriteAllBytes(Folder+name+".png",image.EncodeToPNG());
        int visible=0,minX=source.width,minY=source.height,maxX=0,maxY=0;var pixels=image.GetPixels32();
        for(int y=0;y<source.height;y++)for(int x=0;x<source.width;x++)if(pixels[y*source.width+x].a>32)
        {visible++;minX=Mathf.Min(minX,x);minY=Mathf.Min(minY,y);maxX=Mathf.Max(maxX,x);maxY=Mathf.Max(maxY,y);}
        Destroy(image);Check(visible>1000&&minX>5&&maxX<source.width-5&&minY>5&&maxY<source.height-5,
            $"Intro camera framed the selected hero ({visible} nontransparent pixels, bounds {minX},{minY}–{maxX},{maxY}).");
    }
    void CapturePanel(RenderTexture source,string name)
    {
        var old=RenderTexture.active;RenderTexture.active=source;var image=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,source.width,source.height),0,0);image.Apply();RenderTexture.active=old;
        File.WriteAllBytes(Folder+name+".png",image.EncodeToPNG());Destroy(image);Log("CAPTURE "+name+".png (actual UI Toolkit panel).");
    }
    IEnumerator Checks()
    {
        yield return Home();var menu=FindAnyObjectByType<ModeScreens>();
        if(Reload)
        {
            Check(menu.Profile.Data.Version==1&&menu.Profile.Data.IntroSeen,"SECOND PROCESS reloads the additive IntroSeen flag from a Version 1 legacy save.");
            Check(FindAnyObjectByType<FirstLaunchIntro>()==null,"SECOND PROCESS Home does not replay the intro.");
            yield break;
        }
        var intro=FindAnyObjectByType<FirstLaunchIntro>();
        Check(menu.Profile.Data.Version==1&&!menu.Profile.Data.IntroSeen&&intro!=null&&intro.Playing,"Old Version 1 save without IntroSeen starts the first-launch Timeline.");
        Check(intro.Elapsed<2&&intro.Image!=null,"Timeline begins on the skyline with its hero render target ready.");
        var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");var fallback=forge.Hero(menu.Profile.Data.Loadout?.HeroId);var nova=forge.Hero("nova");
        Check(fallback!=nova&&intro.SelectedHeroId==fallback.Id&&intro.VisualSource==fallback.CharacterPrefab.name,
            $"Legacy save stages its own saved/default hero {fallback.DisplayName} ({intro.VisualSource}), the CONTROL for the NOVA leg below.");
        intro.Skip();yield return null;
        Check(intro.Skipped&&!intro.Playing&&menu.Profile.Data.IntroSeen,"Skip is immediate and persists IntroSeen.");
        Check(menu.Root.Q<VisualElement>("first-launch-intro")==null,"Skip reveals the existing interactive Home screen.");
        GameFlow.Instance.Home();yield return Home();
        Check(FindAnyObjectByType<FirstLaunchIntro>()==null,"Same-process Home revisit does not replay a skipped intro.");
        WorldSession.VerificationSavePath=Path.GetFullPath(Folder+"saves/full.json");
        if(File.Exists(WorldSession.VerificationSavePath))File.Delete(WorldSession.VerificationSavePath);
        var alternate=new GameObject("Alternate intro profile").AddComponent<PlayerProgression>();
        alternate.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),WorldSession.VerificationSavePath);
        Check(alternate.SetLoadout(nova,nova.DefaultA,nova.DefaultB,nova.Primary,nova.Secondary),"Fresh full-playback profile selects NOVA through the normal Forge save gate.");Destroy(alternate.gameObject);
        GameFlow.Instance.Home();yield return Home();menu=FindAnyObjectByType<ModeScreens>();intro=FindAnyObjectByType<FirstLaunchIntro>();
        Check(intro!=null&&intro.Playing&&!menu.Profile.Data.IntroSeen&&intro.SelectedHeroId=="nova","Fresh NOVA profile starts full playback independently of the skip control and default hero.");
        Check(intro.VisualSource==nova.CharacterPrefab.name&&nova.CharacterPrefab!=fallback.CharacterPrefab,
            $"Alternate-hero CONTROL: the Timeline stages NOVA's own character ({intro.VisualSource}), not {fallback.DisplayName}'s ({fallback.CharacterPrefab.name}).");
        var panel=new RenderTexture(1280,720,24){name="Intro actual UI verification"};panel.Create();menu.Panel.targetTexture=panel;
        float began=Time.realtimeSinceStartup;
        while(intro.Playing&&intro.Elapsed<5.8)yield return null;
        Check(intro.Playing&&intro.Elapsed>=5.8,"Timeline camera push and hero entrance are still playing before landing.");
        Log("VIEW "+intro.DebugView());
        Capture(intro.Image,"hero-entrance");
        yield return null;yield return null;CapturePanel(panel,"full-home-intro");
        while(intro.Playing&&Time.realtimeSinceStartup-began<12)yield return null;
        float seconds=Time.realtimeSinceStartup-began;
        Check(!intro.Playing&&!intro.Skipped&&seconds>=8f&&seconds<12,"Full Timeline reaches Home after its authored nine-second sequence ("+seconds.ToString("F2")+" s after the fresh-scene assertion).");
        Check(intro.RenderedFrames>60&&menu.Profile.Data.IntroSeen,"Full playback rendered moving frames and persisted completion.");
        Check(menu.Root.Q<VisualElement>("first-launch-intro")==null,"Full playback releases the overlay and restores Home controls.");
        menu.Panel.targetTexture=null;panel.Release();Destroy(panel);
        GameFlow.Instance.Home();yield return Home();
        Check(FindAnyObjectByType<FirstLaunchIntro>()==null,"Full playback does not replay on the next Home visit.");
    }
}
#endif
