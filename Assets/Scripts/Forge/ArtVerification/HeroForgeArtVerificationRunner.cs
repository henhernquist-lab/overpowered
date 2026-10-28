#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
public sealed partial class HeroForgeArtVerificationRunner : MonoBehaviour
{
    public bool Reload;public Action<int> Finished;
    const string Dir="Verification/HeroForgeArt";
    readonly List<string> lines=new List<string>();string error;
    ForgeCatalog Forge=>Resources.Load<ForgeCatalog>("ForgeCatalog");CosmeticCatalog Catalog=>Resources.Load<CosmeticCatalog>("CosmeticCatalog");WorldSession W=>WorldSession.Instance;
    readonly Dictionary<string,HeroAppearanceSave> appearances=new Dictionary<string,HeroAppearanceSave>();
    readonly Dictionary<string,(float,float,Vector3,Vector3)> capsules=new Dictionary<string,(float,float,Vector3,Vector3)>();
    string Suit(string hero)=>hero=="vector"?"vector_suit_tactical":hero=="titan"?"titan_suit_guardian":"nova_suit_energyweave";
    CosmeticDefinition[] Masks(HeroDefinition h)=>Catalog.Items.Where(d=>d.HeroId==h.Id&&d.Slot==CosmeticSlot.Mask&&d.Available&&d.Mesh!=null&&d.Id.StartsWith(h.Id+"_")&&d.Compatible(h,Suit(h.Id))).ToArray();
    CosmeticDefinition[] Emblems(HeroDefinition h)=>Catalog.Items.Where(d=>d.HeroId==h.Id&&d.Slot==CosmeticSlot.Emblem&&d.Available&&d.Mesh!=null&&d.Compatible(h,Suit(h.Id))).ToArray();
    void Awake(){Application.logMessageReceived+=Observe;}
    void OnDestroy(){Application.logMessageReceived-=Observe;}
    void Observe(string text,string trace,LogType type){if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&trace.Contains("Assets/Scripts/"))error=text;}
    void Log(string message){lines.Add(message);Debug.Log(message);File.WriteAllLines(Dir+(Reload?"/reload.txt":"/results.txt"),lines);}
    void Check(bool value,string message){if(!value)throw new Exception(message);Log("PASS "+message);}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{if(error!=null)throw new Exception(error);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Finished(0);
    }
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+120;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name!="Home"&&W==null)){if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout "+name);yield return null;}
        yield return new WaitForSecondsRealtime(.5f);
    }
    void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    HeroForgeScreen OpenForge(){var menu=FindAnyObjectByType<ModeScreens>();Submit(menu.ForgeButton);return menu.ForgeScreen;}
    Texture2D Capture(RenderTexture rt,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Dir+"/"+path));var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=old;File.WriteAllBytes(Dir+"/"+path,image.EncodeToPNG());return image;
    }
    int Difference(Texture2D a,Texture2D b)=>a.GetPixels32().Zip(b.GetPixels32(),(x,y)=>!x.Equals(y)).Count(x=>x);
    string Protected(ProgressSave data){var copy=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(data));copy.HeroAppearances=null;copy.Loadout.Primary=copy.Loadout.Secondary=default;return JsonUtility.ToJson(copy);}
    void Colors(HeroAppearanceRenderer renderer,HeroDefinition hero,HeroAppearanceSave value)
    {
        var map=(Texture2D)SidekickSuit.MapOf(renderer.ColorMaterial);var palette=Resources.Load<CityPalette>("CityPalette");
        foreach(var cell in hero.Suit.Swatches){Color32 expected=cell.Role==SuitRole.Keep?SidekickSuit.Pixel(hero.Suit.BaseColorMap,cell.Cell):palette.Colors[(int)(cell.Role==SuitRole.Primary?value.Primary:cell.Role==SuitRole.Secondary?value.Secondary:value.Accent)];if(!SidekickSuit.Pixel(map,cell.Cell).Equals(expected))throw new Exception("Protected/palette swatch mismatch "+hero.Id+"/"+cell.Name);}
        Check(true,hero.Id+": three palette channels and protected facial/eye cells match the authored map");
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        Check(Catalog.Items.Select(d=>d.Id).Distinct().Count()==Catalog.Items.Length,"Production disk catalog has unique stable IDs; no temporary registration scope");
        var menu=FindAnyObjectByType<ModeScreens>();var profile=menu.Profile;
        if(Reload)
        {
            Check(JsonUtility.ToJson(profile.Data)==File.ReadAllText(Dir+"/saves/expected.json"),"SECOND PROCESS exact XP, levels, tiers, powers, secondary, signature, difficulty, records, parkour and per-hero appearances survive");
            foreach(var hero in Forge.Heroes){var a=profile.Appearance(hero);Check(a.SuitId==Suit(hero.Id)&&a.MaskId==Masks(hero)[1].Id&&a.EmblemId==Emblems(hero)[1].Id,hero.Id+": SECOND PROCESS independent cosmetic IDs restored");appearances[hero.Id]=a;}
            foreach(var hero in Forge.Heroes){var screen=OpenForge();Check(screen.SelectHero(hero),hero.Id+": SECOND PROCESS select restored hero through actual Forge");screen.Close();Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")),hero.Id+": SECOND PROCESS enter actual Hero Mode");yield return Scene(GameFlow.CityScene);Live(hero,appearances[hero.Id],"reload Hero Mode");GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);}
            yield break;
        }
        Check(profile.Data.Version==1&&(profile.Data.HeroAppearances==null||profile.Data.HeroAppearances.Count==0),"Fresh Version 1 save keeps legacy defaults without forced appearance migration");
        foreach(var hero in Forge.Heroes)Check(profile.Appearance(hero).SuitId==hero.Id+"-legacy",hero.Id+": missing records resolve the original legacy suit");
        profile.AddXp(500);profile.Data.ModeRecords.Add(new ModeRecord{Id="future-mode",BestScore=75,BestWave=4,Runs=3});profile.Data.ParkourCourses.Add(new ParkourCourseRecord{Id="future-course",BestSeconds=38.5f,Completions=2,RewardClaimed=true,Completed=true});profile.Save();
        Check(profile.Buy(Resources.Load<PowerDefinition>("Powers/fire")),"Seed a real purchased upgrade through PlayerProgression before cosmetic preservation controls");
        yield return PreviewChecks();
        foreach(var hero in Forge.Heroes){var screen=OpenForge();screen.SelectHero(hero);Check(JsonUtility.ToJson(profile.Appearance(hero))==JsonUtility.ToJson(appearances[hero.Id]),hero.Id+": cycling all three heroes retains independent outfit/colors");screen.Close();}
        yield return PlayChecks();
        menu=FindAnyObjectByType<ModeScreens>();profile=menu.Profile;
        foreach(var hero in Forge.Heroes){var screen=OpenForge();screen.SelectHero(hero);screen.SelectCosmetic(CosmeticSlot.Mask,Masks(hero)[1].Id);screen.SelectCosmetic(CosmeticSlot.Emblem,Emblems(hero)[1].Id);appearances[hero.Id]=profile.Appearance(hero);screen.Close();}
        profile.Save();File.WriteAllText(Dir+"/saves/expected.json",JsonUtility.ToJson(profile.Data));Check(profile.Data.Version==1,"Outer save version remains 1 after all actual modes and character choices");
    }
}
#endif
