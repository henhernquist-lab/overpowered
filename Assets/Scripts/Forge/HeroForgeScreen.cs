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
    /// Archetype comparison bars under the preview (rebuilt with the form when the hero changes).
    public VisualElement StatsPanel {get;private set;}
    /// The character instance of the last captured preview (verification reads its recoloured material).
    public GameObject PreviewModel {get;private set;}
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
        var column=new VisualElement{name="hero-preview-column"};column.style.width=Length.Percent(42);column.style.marginRight=30;row.Add(column);
        var image=new Image{name="hero-preview",scaleMode=ScaleMode.ScaleToFit};image.style.flexGrow=1;column.Add(image);
        StatsPanel=new VisualElement{name="forge-stats"};StatsPanel.style.backgroundColor=C(CityColor.UiPanel);StatsPanel.style.paddingLeft=StatsPanel.style.paddingRight=12;
        StatsPanel.style.paddingTop=StatsPanel.style.paddingBottom=8;StatsPanel.style.marginTop=8;column.Add(StatsPanel);
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
        BuildStats();
        rebuilding=false;
    }
    /// One row per archetype stat: the selected hero's real value and a bar filled relative to the best value across the
    /// roster (for cooldown, lower is better), so heroes compare at a glance. Values come from HeroDefinition.Stats x the
    /// shared GameTuning baseline; named "forge-stat-<key>" (value label "forge-stat-<key>-value").
    public static readonly string[] StatKeys={"health","energy","regen","speed","melee","power","cooldown","knockback"};
    public static float StatValue(HeroDefinition h,string key)
    {
        var s=h!=null&&h.Stats!=null?h.Stats:HeroStats.Baseline;var t=Resources.Load<GameTuning>("GameTuning").Movement;
        switch(key)
        {
            case "health":return t.Health*s.MaxHealth;
            case "energy":return t.Energy*s.MaxEnergy;
            case "regen":return t.EnergyRecharge*s.EnergyRegen;
            case "speed":return t.RunSpeed*s.MoveSpeed;
            case "melee":return s.MeleeDamage;
            case "power":return s.PowerDamage;
            case "cooldown":return s.CooldownMultiplier;
            default:return s.KnockbackResistance;
        }
    }
    /// 0..1 bar fill: value / roster best (cooldown: roster best / value; knockback: the resistance itself).
    public float StatFill(HeroDefinition h,string key)
    {
        float v=StatValue(h,key);
        if(key=="knockback")return Mathf.Clamp01(v);
        if(key=="cooldown"){float best=catalog.Heroes.Where(x=>x!=null).Min(x=>StatValue(x,key));return Mathf.Clamp01(best/Mathf.Max(.0001f,v));}
        float max=catalog.Heroes.Where(x=>x!=null).Max(x=>StatValue(x,key));return Mathf.Clamp01(v/Mathf.Max(.0001f,max));
    }
    public static string StatText(HeroDefinition h,string key)
    {
        float v=StatValue(h,key);
        switch(key)
        {
            case "health":return $"{v:0} HP";
            case "energy":return $"{v:0}";
            case "regen":return $"{v:0.#}/S";
            case "speed":return $"{v:0.#} M/S";
            case "knockback":return $"{v*100:0}%";
            default:return $"x{v:0.00}";
        }
    }
    static readonly string[] StatTitles={"HEALTH","ENERGY","ENERGY REGEN","MOVE SPEED","MELEE DAMAGE","POWER DAMAGE","POWER COOLDOWN","KNOCKBACK RESIST"};
    void BuildStats()
    {
        StatsPanel.Clear();
        var title=new Label(hero.DisplayName+"  ·  ARCHETYPE");title.style.fontSize=12;title.style.color=C(CityColor.UiMuted);title.style.marginBottom=4;StatsPanel.Add(title);
        for(int i=0;i<StatKeys.Length;i++)
        {
            string key=StatKeys[i];float fill=StatFill(hero,key);bool better=key=="cooldown"?StatValue(hero,key)<1f:key=="knockback"?StatValue(hero,key)>0f:StatValue(hero,key)>StatValue(null,key)+.0001f;
            bool worse=key=="cooldown"?StatValue(hero,key)>1f:key!="knockback"&&StatValue(hero,key)<StatValue(null,key)-.0001f;
            var row=new VisualElement{name="forge-stat-"+key};row.style.flexDirection=FlexDirection.Row;row.style.alignItems=Align.Center;row.style.height=18;
            var label=new Label(StatTitles[i]);label.style.width=130;label.style.fontSize=11;label.style.color=C(CityColor.UiMuted);row.Add(label);
            var track=new VisualElement();track.style.flexGrow=1;track.style.height=8;track.style.backgroundColor=C(CityColor.UiNavy);row.Add(track);
            var bar=new VisualElement{name="forge-stat-"+key+"-fill"};bar.style.width=Length.Percent(fill*100f);bar.style.height=8;
            bar.style.backgroundColor=C(better?CityColor.HeroAccent:worse?CityColor.Red:CityColor.Cream);track.Add(bar);
            var value=new Label(StatText(hero,key)){name="forge-stat-"+key+"-value"};value.style.width=78;value.style.fontSize=11;value.style.unityTextAlign=TextAnchor.MiddleRight;row.Add(value);
            StatsPanel.Add(row);
        }
    }
    void Capture()
    {
        if(previewRoot!=null){previewRoot.SetActive(false);Destroy(previewRoot);}
        previewRoot=new GameObject("Forge preview only");previewRoot.transform.SetParent(transform,false);previewRoot.transform.position=new Vector3(10000,-1000,10000);
        // Preview owns one palette cache, since the skyline's transient cache is disposed after capture.
        var materials=previewRoot.AddComponent<CityMaterials>();materials.Initialize(palette);
        var tuning=hero.Animation!=null?hero.Animation:Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning");
        var model=Instantiate(hero.CharacterPrefab!=null?hero.CharacterPrefab:tuning.Model,previewRoot.transform);PreviewModel=model;
        var animator=model.GetComponent<Animator>();animator.runtimeAnimatorController=tuning.Controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        animator.Play("Locomotion");animator.Update(0);
        var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        if(hero.Suit!=null)foreach(var r in renderers)hero.Suit.ApplyBody(r);
        // Frame by the posed skinned vertices (as HumanoidPresentation fits the body): skinned-renderer bounds are conservative
        // envelopes, far larger than a Sidekick character, which left it small in the frame.
        Bounds Posed()
        {
            Bounds result=new Bounds();bool any=false;var mesh=new Mesh();
            foreach(var r in renderers){r.BakeMesh(mesh);foreach(var v in mesh.vertices){var p=r.transform.TransformPoint(v);if(!any){result=new Bounds(p,Vector3.zero);any=true;}else result.Encapsulate(p);}}
            Destroy(mesh);return result;
        }
        Bounds bounds=Posed();
        float fit=1.8f/Mathf.Max(.01f,bounds.size.y);model.transform.localScale=Vector3.Scale(model.transform.localScale*fit,hero.VisualScale);
        foreach(var renderer in renderers)
        {
            var mats=renderer.sharedMaterials;
            for(int i=0;i<mats.Length;i++)mats[i]=hero.Suit!=null&&mats[i]==hero.Suit.Source?materials.SuitMaterial(hero.Suit,primary,secondary):CityMaterials.Get(renderer.name.Contains("Joints")?secondary:primary);
            renderer.sharedMaterials=mats;
        }
        foreach(var node in previewRoot.GetComponentsInChildren<Transform>())node.gameObject.layer=30;
        var camera=new GameObject("Forge preview camera").AddComponent<Camera>();camera.transform.SetParent(previewRoot.transform,false);camera.enabled=false;camera.cullingMask=1<<30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=C(CityColor.UiPanel);camera.fieldOfView=32;camera.targetTexture=Preview;
        bounds=Posed();
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
