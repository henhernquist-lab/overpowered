using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// An overlay on the existing home screen, sharing its panel, profile and palette.
public sealed class HeroForgeScreen : MonoBehaviour
{
    public VisualElement Root {get;private set;}
    public Label SynergyLabel {get;private set;}
    public Label SynergyStatus {get;private set;}
    public ForgeChoice SlotA {get;private set;}
    public ForgeChoice SlotB {get;private set;}
    public RenderTexture Preview {get;private set;}
    ModeScreens menu;ForgeCatalog catalog;HeroDefinition hero;PowerDefinition a,b;
    CityColor primary,secondary;CityPalette palette;GameObject previewRoot;bool rebuilding;
    VisualElement form;Label feedback;
    Color C(CityColor role)=>palette.Colors[(int)role];
    public void Initialize(ModeScreens owner)
    {
        menu=owner;catalog=Resources.Load<ForgeCatalog>("ForgeCatalog");palette=Resources.Load<CityPalette>("CityPalette");
        Root=new VisualElement{name="hero-forge"};Root.style.position=Position.Absolute;Root.style.left=Length.Percent(5);Root.style.right=Length.Percent(5);Root.style.top=Length.Percent(5);Root.style.bottom=Length.Percent(5);
        Root.style.backgroundColor=C(CityColor.UiNavy);Root.style.paddingLeft=Root.style.paddingRight=32;Root.style.paddingTop=24;
        Root.style.color=C(CityColor.UiInk);menu.Root.Add(Root);
        var title=new Label("HERO FORGE");title.style.fontSize=42;title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.color=C(CityColor.HeroAccent);Root.Add(title);
        var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.flexGrow=1;Root.Add(row);
        var image=new Image{name="hero-preview",scaleMode=ScaleMode.ScaleToFit};image.style.width=Length.Percent(42);image.style.marginRight=30;row.Add(image);
        Preview=new RenderTexture(400,500,24){name="Hero Forge preview"};Preview.Create();image.image=Preview;
        form=new VisualElement();form.style.flexGrow=1;form.style.paddingTop=12;row.Add(form);
        feedback=new Label();feedback.style.fontSize=15;Root.Add(feedback);
        var done=new Button(()=>Close()){text="SAVE & BACK",name="forge-done"};done.style.height=48;done.style.marginTop=16;done.style.marginBottom=20;done.style.backgroundColor=C(CityColor.HeroAccent);done.style.color=C(CityColor.UiNavy);done.style.fontSize=18;done.style.unityFontStyleAndWeight=FontStyle.Bold;done.style.unityTextAlign=TextAnchor.MiddleCenter;Root.Add(done);
        Open();
    }
    public void Open()
    {
        var saved=menu.Profile.Data.Loadout;hero=catalog.Hero(saved.HeroId);a=menu.Profile.EquippedA;b=menu.Profile.EquippedB;primary=saved.Primary;secondary=saved.Secondary;
        Root.style.display=DisplayStyle.Flex;Rebuild();Capture();
    }
    public void Close(){Root.style.display=DisplayStyle.None;}
    public bool SelectHero(HeroDefinition value)
    {
        if(Array.IndexOf(catalog.Heroes,value)<0)return false;
        hero=value;primary=hero.Primary;secondary=hero.Secondary;
        var owned=hero.AvailablePowers.Where(menu.Profile.Owns).ToArray();
        a=owned.Contains(hero.DefaultA)?hero.DefaultA:owned[0];
        b=owned.Contains(hero.DefaultB)&&hero.DefaultB!=a?hero.DefaultB:owned.First(p=>p!=a);
        return Save();
    }
    public bool SelectPower(int slot,PowerDefinition power)
    {
        if(!catalog.Allowed(hero,power)||!menu.Profile.Owns(power))return false;
        if(slot==0){a=power;if(b==a)b=hero.AvailablePowers.First(p=>p!=a&&menu.Profile.Owns(p));}
        else if(slot==1){if(power==a)return false;b=power;}else return false;
        return Save();
    }
    public bool SetColors(CityColor first,CityColor second){primary=first;secondary=second;return Save();}
    bool Save()
    {
        bool ok=menu.Profile.SetLoadout(hero,a,b,primary,secondary);
        if(ok){feedback.text="BUILD SAVED";Rebuild();Capture();}else feedback.text="Choose two different powers.";
        return ok;
    }
    ForgeChoice Dropdown(string title,string[] values,int selected,Action<int> changed)
    {
        var field=new ForgeChoice(title,values,Mathf.Max(0,selected),i=>{if(!rebuilding)changed(i);},palette);
        form.Add(field);return field;
    }
    void Rebuild()
    {
        rebuilding=true;form.Clear();
        Dropdown("HERO",catalog.Heroes.Select(h=>h.DisplayName).ToArray(),Array.IndexOf(catalog.Heroes,hero),i=>SelectHero(catalog.Heroes[i]));
        Dropdown("PRIMARY SUIT",catalog.SuitColors.Select(c=>c.ToString()).ToArray(),Array.IndexOf(catalog.SuitColors,primary),i=>SetColors(catalog.SuitColors[i],secondary));
        Dropdown("SECONDARY SUIT",catalog.SuitColors.Select(c=>c.ToString()).ToArray(),Array.IndexOf(catalog.SuitColors,secondary),i=>SetColors(primary,catalog.SuitColors[i]));
        var first=hero.AvailablePowers.Where(menu.Profile.Owns).ToArray();
        SlotA=Dropdown("POWER SLOT 1",first.Select(p=>p.DisplayName).ToArray(),Array.IndexOf(first,a),i=>SelectPower(0,first[i]));
        var second=first.Where(p=>p!=a).ToArray();
        SlotB=Dropdown("POWER SLOT 2",second.Select(p=>p.DisplayName).ToArray(),Array.IndexOf(second,b),i=>SelectPower(1,second[i]));
        var synergy=catalog.Resolve(a,b);
        SynergyLabel=new Label("SYNERGY / "+(synergy?.DisplayName??"No synergy")){name="forge-synergy"};
        SynergyLabel.style.fontSize=24;SynergyLabel.style.whiteSpace=WhiteSpace.Normal;SynergyLabel.style.color=C(CityColor.HeroAccent);form.Add(SynergyLabel);
        // Synergies have no unlock step: the equipped pair's synergy is available at once, gated only by its cooldown.
        SynergyStatus=new Label(synergy!=null?$"READY WHEN EQUIPPED  ·  {synergy.Cooldown:0} S COOLDOWN  ·  {HudBindings.KeyName(catalog.SynergyKey)} TO USE":""){name="forge-synergy-status"};
        SynergyStatus.style.fontSize=13;SynergyStatus.style.unityFontStyleAndWeight=FontStyle.Bold;SynergyStatus.style.color=C(CityColor.UiMuted);SynergyStatus.style.marginBottom=4;form.Add(SynergyStatus);
        var detail=new Label(synergy?.Description??"");detail.style.whiteSpace=WhiteSpace.Normal;detail.style.marginBottom=12;form.Add(detail);
        rebuilding=false;
    }
    void Capture()
    {
        if(previewRoot!=null){previewRoot.SetActive(false);Destroy(previewRoot);}
        previewRoot=new GameObject("Forge preview only");previewRoot.transform.SetParent(transform,false);previewRoot.transform.position=new Vector3(10000,-1000,10000);
        // Preview owns one palette cache, since the skyline's transient cache is disposed after capture.
        var materials=previewRoot.AddComponent<CityMaterials>();materials.Initialize(palette);
        var tuning=hero.Animation!=null?hero.Animation:Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning");
        var model=Instantiate(hero.CharacterPrefab!=null?hero.CharacterPrefab:tuning.Model,previewRoot.transform);
        var animator=model.GetComponent<Animator>();animator.runtimeAnimatorController=tuning.Controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        animator.Play("Locomotion");animator.Update(0);
        var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        float fit=1.8f/Mathf.Max(.01f,bounds.size.y);model.transform.localScale=Vector3.Scale(model.transform.localScale*fit,hero.VisualScale);
        foreach(var renderer in renderers)
        {
            var mats=renderer.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=CityMaterials.Get(renderer.name.Contains("Joints")?secondary:primary);renderer.sharedMaterials=mats;
        }
        foreach(var node in previewRoot.GetComponentsInChildren<Transform>())node.gameObject.layer=30;
        var camera=new GameObject("Forge preview camera").AddComponent<Camera>();camera.transform.SetParent(previewRoot.transform,false);camera.enabled=false;camera.cullingMask=1<<30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=C(CityColor.UiPanel);camera.fieldOfView=32;camera.targetTexture=Preview;
        bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        camera.transform.position=bounds.center+new Vector3(0,.1f,4.2f);camera.transform.LookAt(bounds.center);
        var sun=new GameObject("Forge preview light").AddComponent<Light>();sun.transform.SetParent(previewRoot.transform,false);sun.type=LightType.Directional;sun.cullingMask=1<<30;sun.intensity=1.4f;sun.color=C(CityColor.Cream);sun.transform.rotation=Quaternion.Euler(35,150,0);
        camera.Render();camera.targetTexture=null;
        // No preview animation/camera work per frame while home gameplay cards are shown.
        animator.enabled=false;
    }
    void OnDestroy(){if(Preview!=null){Preview.Release();Destroy(Preview);}if(previewRoot!=null)Destroy(previewRoot);}
}

// Explicit previous/next choices: no dependence on the menu's deliberately absent default dropdown theme.
public sealed class ForgeChoice : VisualElement
{
    public readonly System.Collections.Generic.List<string> choices;
    public readonly Button Previous,Next;
    public ForgeChoice(string title,string[] values,int selected,Action<int> changed,CityPalette palette)
    {
        choices=values.ToList();style.flexDirection=FlexDirection.Row;style.height=55;style.marginBottom=12;
        style.backgroundColor=palette.Colors[(int)CityColor.UiPanel];style.alignItems=Align.Center;
        var label=new Label(title);label.style.width=155;label.style.paddingLeft=12;label.style.fontSize=12;label.style.color=palette.Colors[(int)CityColor.UiMuted];Add(label);
        Previous=new Button(()=>changed((selected+values.Length-1)%values.Length)){text="‹"};
        Next=new Button(()=>changed((selected+1)%values.Length)){text="›"};
        var value=new Label(values[selected]);value.style.flexGrow=1;value.style.fontSize=20;value.style.unityTextAlign=TextAnchor.MiddleCenter;
        Add(Previous);Add(value);Add(Next);
        foreach(var button in new[]{Previous,Next}){button.style.width=40;button.style.height=45;button.style.fontSize=30;button.style.unityTextAlign=TextAnchor.MiddleCenter;button.style.color=palette.Colors[(int)CityColor.HeroAccent];button.SetEnabled(values.Length>1);}
    }
}
