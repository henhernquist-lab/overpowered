#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class SidekickVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public bool Reload;
    const string Dir="Verification/Sidekick";
    readonly List<string> output=new List<string>();string runtimeFailure;
    WorldSession W=>WorldSession.Instance;
    ForgeCatalog F=>Resources.Load<ForgeCatalog>("ForgeCatalog");
    CityPalette Palette=>Resources.Load<CityPalette>("CityPalette");
    void Awake(){Application.logMessageReceived+=ObserveLog;}
    void OnDestroy(){Application.logMessageReceived-=ObserveLog;}
    void ObserveLog(string message,string trace,LogType type){if((type==LogType.Exception||type==LogType.Error)&&trace.Contains("Assets/Scripts/"))runtimeFailure=message;}
    void Log(string line){output.Add(line);Debug.Log("[SIDEKICK VERIFY] "+line);File.WriteAllLines($"{Dir}/{(Reload?"reload.txt":"results.txt")}",output);}
    void Check(bool ok,string line){if(!ok)throw new Exception(line);Log("PASS "+line);}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Reload?ReloadChecks():Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved=false;
            try{if(runtimeFailure!=null)throw new Exception("Gameplay Console error: "+runtimeFailure);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Finished(0);
    }
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+60;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout "+name);yield return null;}
        yield return new WaitForSecondsRealtime(.5f);
    }
    void Submit(Button button){using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    static Color32 C32(Color c)=>c;
    // Second colour pair per hero: contrasting ForgeCatalog.SuitColors choices (pair 1 = the hero's defaults).
    static (CityColor a,CityColor b) Alternate(string id)=>id=="vector"?(CityColor.Red,CityColor.Cream):id=="titan"?(CityColor.UiPurple,CityColor.Cyan):(CityColor.Amber,CityColor.Teal);

    /// Asserts a renderer's suit material: the exact Primary/Secondary/Trim palette colours on every role swatch, and
    /// every Keep swatch (skin, hair, eyes...) byte-identical to the authored map. Returns the skin swatch colour.
    Color32 AssertSuit(SkinnedMeshRenderer skin,HeroDefinition hero,CityColor a,CityColor b,string where)
    {
        var mat=skin.sharedMaterial;var suit=hero.Suit;
        Check(mat!=null&&mat!=suit.Source&&mat.shader==suit.Source.shader&&mat.name==$"Suit/{suit.name}/{a}+{b}",$"{where}: {hero.DisplayName} renderer uses the cached suit material '{mat?.name}' (Sidekick shader, not the vendor material, not a palette Standard material).");
        var map=mat.GetTexture(SidekickSuit.ColorMapProperty) as Texture2D;
        Check(map!=null&&map!=suit.Source.GetTexture(SidekickSuit.ColorMapProperty)&&map.filterMode==FilterMode.Point,$"{where}: _ColorMap is the generated point-filtered suit map, not the authored texture.");
        int wrong=0,checkedCells=0;
        foreach(var s in suit.Swatches)
        {
            Color32 expected=s.Role==SuitRole.Primary?C32(Palette.Colors[(int)a]):s.Role==SuitRole.Secondary?C32(Palette.Colors[(int)b]):s.Role==SuitRole.Trim?C32(Palette.Colors[(int)suit.Trim]):SidekickSuit.Pixel(suit.BaseColorMap,s.Cell);
            var actual=SidekickSuit.Pixel(map,s.Cell);checkedCells++;
            if(actual.r!=expected.r||actual.g!=expected.g||actual.b!=expected.b){wrong++;if(wrong<4)Log($"   mismatch {s.Name} ({s.Cell.x},{s.Cell.y}) role={s.Role} expected={expected} actual={actual}");}
        }
        int p=suit.Swatches.Count(s=>s.Role==SuitRole.Primary),q=suit.Swatches.Count(s=>s.Role==SuitRole.Secondary),t=suit.Swatches.Count(s=>s.Role==SuitRole.Trim),k=suit.Swatches.Count(s=>s.Role==SuitRole.Keep);
        Check(wrong==0&&p>0&&q>0,$"{where}: all {checkedCells} used swatches correct: {p} Primary={a} {C32(Palette.Colors[(int)a])}, {q} Secondary={b}, {t} Trim={suit.Trim}, {k} Keep (authored).");
        var skinCell=new Vector2Int(0,5);var skinSwatch=suit.Swatches.First(s=>s.Cell==skinCell);
        Check(skinSwatch.Role==SuitRole.Keep&&skinSwatch.Name.Contains("Skin"),$"{where}: swatch (0,5) is '{skinSwatch.Name}' and kept.");
        return SidekickSuit.Pixel(map,skinCell);
    }
    Texture2D Read(RenderTexture rt)
    {
        var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=previous;return image;
    }
    Texture2D CaptureHero(Transform hero)
    {
        var camera=new GameObject("Sidekick capture").AddComponent<Camera>();camera.fieldOfView=30;
        camera.transform.position=hero.position+hero.forward*3.6f+hero.right*1.2f+Vector3.up*1.35f;camera.transform.LookAt(hero.position+Vector3.up*.95f);
        var rt=new RenderTexture(400,500,24){antiAliasing=4};rt.Create();camera.targetTexture=rt;camera.Render();var image=Read(rt);
        camera.targetTexture=null;rt.Release();Destroy(rt);Destroy(camera.gameObject);return image;
    }
    void SaveSheet(Texture2D[,] cells,string file)
    {
        int cols=cells.GetLength(0),rows=cells.GetLength(1),w=cells[0,0].width,h=cells[0,0].height;var sheet=new Texture2D(w*cols,h*rows,TextureFormat.RGB24,false);
        for(int x=0;x<cols;x++)for(int y=0;y<rows;y++)sheet.SetPixels(x*w,(rows-1-y)*h,w,h,cells[x,y].GetPixels());
        sheet.Apply();File.WriteAllBytes($"{Dir}/{file}",sheet.EncodeToPNG());Destroy(sheet);
    }
    int LiveSuitMaterials=>Resources.FindObjectsOfTypeAll<Material>().Count(m=>m!=null&&m.name.StartsWith("Suit/"));

    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);var menu=FindAnyObjectByType<ModeScreens>();
        Check(F.Heroes.Length==3&&F.Heroes.All(h=>h.CharacterPrefab!=null&&h.Suit!=null&&h.CharacterPrefab.GetComponent<Animator>().avatar.isHuman),"ForgeCatalog: 3 heroes, each a Sidekick prefab with a Humanoid avatar and a suit: "+string.Join(", ",F.Heroes.Select(h=>$"{h.DisplayName}={h.CharacterPrefab.name}")));
        Check(F.Heroes.Select(h=>h.CharacterPrefab).Distinct().Count()==3,"Three distinct character prefabs.");
        Submit(menu.ForgeButton);yield return null;var screen=menu.ForgeScreen;
        Check(screen!=null&&screen.Root.resolvedStyle.display!=DisplayStyle.None,"Home button opens Hero Forge.");
        // 1) Forge preview: every hero x two colour pairs.
        var cells=new Texture2D[F.Heroes.Length,2];
        for(int i=0;i<F.Heroes.Length;i++)
        {
            var hero=F.Heroes[i];var pairs=new[]{(hero.Primary,hero.Secondary),Alternate(hero.Id)};var skin=new Color32[2];
            for(int j=0;j<2;j++)
            {
                Check(screen.SelectHero(hero)&&screen.SetColors(pairs[j].Item1,pairs[j].Item2),$"Forge: {hero.DisplayName} {pairs[j].Item1}/{pairs[j].Item2} saved.");
                var skinned=screen.PreviewModel.GetComponentsInChildren<SkinnedMeshRenderer>();
                Check(skinned.Length==1&&skinned[0].sharedMesh==hero.CharacterPrefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh,$"Forge preview instantiates {hero.CharacterPrefab.name} ({skinned[0].sharedMesh.vertexCount} vertices, 1 skinned mesh).");
                skin[j]=AssertSuit(skinned[0],hero,pairs[j].Item1,pairs[j].Item2,"Forge preview");
                cells[i,j]=Read(screen.Preview);
            }
            Check(skin[0].Equals(skin[1])&&skin[0].Equals(SidekickSuit.Pixel(hero.Suit.BaseColorMap,new Vector2Int(0,5))),$"CONTROL: {hero.DisplayName} skin swatch identical for both colour pairs and equal to the authored map: {skin[0]}.");
        }
        SaveSheet(cells,"heroes-recolour.png");Log("CAPTURE heroes-recolour.png: columns = "+string.Join(" | ",F.Heroes.Select(h=>h.DisplayName))+"; top row = hero default colours, bottom row = alternate pair.");
        yield return null;
        Check(LiveSuitMaterials<=1,$"Forge rebuilt the preview 6x: live suit materials = {LiveSuitMaterials} (each preview owns and destroys its one material).");
        Check(F.Heroes.All(h=>h.Suit.Source.GetTexture(SidekickSuit.ColorMapProperty)!=null&&!h.Suit.Source.name.StartsWith("Suit/")),"CONTROL: vendor Sidekick materials untouched (still reference their authored colour maps).");
        // 2) Forge -> SAVE & BACK -> session, per hero.
        var session=new Texture2D[F.Heroes.Length,1];
        for(int i=0;i<F.Heroes.Length;i++)
        {
            var hero=F.Heroes[i];var (a,b)=Alternate(hero.Id);
            if(i>0){menu=FindAnyObjectByType<ModeScreens>();Submit(menu.ForgeButton);yield return null;screen=menu.ForgeScreen;}
            Check(screen.SelectHero(hero)&&screen.SetColors(a,b),$"Forge: choose {hero.DisplayName} {a}/{b}.");
            Submit(screen.Root.Q<Button>("forge-done"));yield return null;
            Check(screen.Root.style.display.value==DisplayStyle.None&&menu.Profile.Data.Loadout.HeroId==hero.Id&&menu.Profile.Data.Loadout.Primary==a&&menu.Profile.Data.Loadout.Secondary==b,"SAVE & BACK closes Forge with the loadout persisted in PlayerProgression.");
            int created=CityMaterials.SuitsCreated;
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero")),"Enter Hero session.");yield return Scene(GameFlow.CityScene);
            yield return new WaitForSeconds(1.5f);
            var p=W.Hero.GetComponent<HumanoidPresentation>();var skins=p.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
            Check(skins.Length==1&&skins[0].sharedMesh==hero.CharacterPrefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh&&p.Animator.avatar==hero.CharacterPrefab.GetComponent<Animator>().avatar&&p.Animator.gameObject.name==hero.DisplayName,$"Session spawns {hero.DisplayName} as {hero.CharacterPrefab.name} (same mesh + Humanoid avatar), shared controller {p.Animator.runtimeAnimatorController.name}, root motion {p.Animator.applyRootMotion}.");
            Check(!p.Animator.applyRootMotion&&p.Animator.transform!=W.Hero.transform&&p.VisualRoot.GetComponentsInChildren<MeshRenderer>().Length==0,"CONTROL: no root motion, Animator not on the physics root, no capsule MeshRenderer.");
            AssertSuit(skins[0],hero,a,b,"Session");
            var suitMaterial=skins[0].sharedMaterial;var suitMap=suitMaterial.GetTexture(SidekickSuit.ColorMapProperty);
            Check(suitMaterial==CityMaterials.Suit(hero.Suit,a,b)&&CityMaterials.Current.SuitCount==1&&CityMaterials.SuitsCreated==created+1,$"ONE suit material for (hero, primary, secondary) cached by the city's CityMaterials (count {CityMaterials.Current.SuitCount}, created this session {CityMaterials.SuitsCreated-created}).");
            for(int f=0;f<60;f++)yield return null;
            Check(CityMaterials.SuitsCreated==created+1&&skins[0].sharedMaterial==suitMaterial&&LiveSuitMaterials==1,$"60 frames later: no material created per frame (live suit materials {LiveSuitMaterials}, NPCs {W.Npcs.Count} unaffected).");
            session[i,0]=CaptureHero(W.Hero.transform);
            // Ice frozen look swaps to the shared Cyan palette material and must restore the suit material.
            bool frozen=true;var look=FrozenLook.Show(W.Hero.gameObject,CityColor.Cyan,()=>frozen);yield return null;
            Check(skins[0].sharedMaterial==CityMaterials.Get(CityColor.Cyan),"Ice frozen look shows the shared Cyan material on the Sidekick body.");
            frozen=false;yield return null;yield return null;
            Check(skins[0].sharedMaterial==suitMaterial&&suitMaterial!=null&&suitMaterial.GetTexture(SidekickSuit.ColorMapProperty)==suitMap,"Thaw restores the same suit material and colour map.");
            Destroy(look);
            // First person hides the body to shadows only, and restores.
            var follow=Camera.main.GetComponent<ThirdPersonCamera>();var before=skins[0].shadowCastingMode;bool wasFirst=follow.FirstPerson;
            Check(follow.ToggleView(),"Toggle view accepted.");yield return null;
            var mode=follow.FirstPerson?UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:before;
            Check(skins[0].shadowCastingMode==mode&&(follow.FirstPerson?skins[0].shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:true),$"First-person={follow.FirstPerson}: Sidekick body renderer shadowCastingMode={skins[0].shadowCastingMode}.");
            Check(follow.ToggleView()&&follow.FirstPerson==wasFirst,"Toggle back.");yield return null;
            Check(skins[0].shadowCastingMode==before,$"Third person restores shadowCastingMode={before}.");
            GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);yield return null;
            Check(suitMaterial==null&&suitMap==null,"Teardown: the session's suit material and colour map were destroyed with the city.");
        }
        SaveSheet(session,"session-heroes.png");Log("CAPTURE session-heroes.png: "+string.Join(" | ",F.Heroes.Select(h=>{var (a,b)=Alternate(h.Id);return $"{h.DisplayName} {a}/{b}";}))+" in a live Hero session.");
        menu=FindAnyObjectByType<ModeScreens>();var last=F.Heroes.Last();var (la,lb)=Alternate(last.Id);
        File.WriteAllText($"{Dir}/saves/expected.txt",$"{last.Id} {la} {lb}");
        Check(menu.Profile.Data.Loadout.HeroId==last.Id&&menu.Profile.Data.Loadout.Primary==la,$"Saved loadout for the separate-process reload: {last.Id} {la}/{lb}.");
        Log("LIMIT: automated renders/asserts only; the look and feel of each hero in motion is a human playtest item.");
    }
    IEnumerator ReloadChecks()
    {
        yield return Scene(GameFlow.HomeScene);var menu=FindAnyObjectByType<ModeScreens>();
        var expected=File.ReadAllText($"{Dir}/saves/expected.txt").Split(' ');var hero=F.Hero(expected[0]);
        var a=(CityColor)Enum.Parse(typeof(CityColor),expected[1]);var b=(CityColor)Enum.Parse(typeof(CityColor),expected[2]);var l=menu.Profile.Data.Loadout;
        Check(l.HeroId==hero.Id&&l.Primary==a&&l.Secondary==b,$"SECOND PROCESS restores {hero.DisplayName} {a}/{b} from the save.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);yield return new WaitForSeconds(1.5f);
        var skin=W.Hero.GetComponent<HumanoidPresentation>().VisualRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        Check(skin.sharedMesh==hero.CharacterPrefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh,$"Reloaded session spawns {hero.CharacterPrefab.name}.");
        AssertSuit(skin,hero,a,b,"Reloaded session");
        var cells=new Texture2D[1,1];cells[0,0]=CaptureHero(W.Hero.transform);SaveSheet(cells,"reload-session.png");
        GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
        var fresh=new GameObject("Fresh control").AddComponent<PlayerProgression>();fresh.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),Path.GetFullPath($"{Dir}/saves/fresh-{Guid.NewGuid():N}.json"));
        var d=fresh.Data.Loadout;var def=F.Hero(d.HeroId);
        Check(!(d.HeroId==hero.Id&&d.Primary==a&&d.Secondary==b)&&d.Primary==def.Primary&&d.Secondary==def.Secondary,$"Fresh-save CONTROL: {d.HeroId} {d.Primary}/{d.Secondary} (hero defaults), not the saved build.");
        Destroy(fresh.gameObject);
    }
}
#endif
