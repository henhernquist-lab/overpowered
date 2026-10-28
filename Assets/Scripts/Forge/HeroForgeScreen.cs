using System;
using System.Linq;
using Unity.Cinemachine;
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
    public ForgeChoice SecondarySlot {get;private set;}
    public RenderTexture Preview {get;private set;}
    /// Archetype comparison bars under the preview (rebuilt with the form when the hero changes).
    public VisualElement StatsPanel {get;private set;}
    /// The character instance of the last captured preview (verification reads its recoloured material).
    public GameObject PreviewModel {get;private set;}
    ModeScreens menu;ForgeCatalog catalog;HeroDefinition hero;PowerDefinition a,b,utility;
    HeroAppearanceSave appearance;HeroDefinition previewHero;HeroAppearanceRenderer appearanceRenderer;
    CityColor primary,secondary;CityPalette palette;GameObject previewRoot;bool rebuilding;
    int previewRenderAfterFrame=-1;
    public bool PreviewPending=>previewRenderAfterFrame>=0;
    Camera previewCamera;CinemachineBrain revealBrain;Transform orbitPivot;float revealStarted;bool revealActive;
    public bool RevealActive=>revealActive;
    public int RevealFrames {get;private set;}
    /// Orbit heading (degrees about the hero) of the last rendered reveal frame; 0 is the settled front view.
    public float RevealHeading {get;private set;}
    public bool HasCinemachinePreview=>revealBrain!=null;
    public Vector3 RevealCameraPosition=>previewCamera!=null?previewCamera.transform.position:Vector3.zero;
    VisualElement form;Label feedback;
    Color C(CityColor role)=>palette.Colors[(int)role];
    public void Initialize(ModeScreens owner)
    {
        menu=owner;catalog=Resources.Load<ForgeCatalog>("ForgeCatalog");palette=Resources.Load<CityPalette>("CityPalette");
        Root=new VisualElement{name="hero-forge"};Root.style.position=Position.Absolute;Root.style.left=Length.Percent(5);Root.style.right=Length.Percent(5);Root.style.top=Length.Percent(5);Root.style.bottom=Length.Percent(5);
        Root.style.backgroundColor=C(CityColor.UiNavy);Root.style.paddingLeft=Root.style.paddingRight=32;Root.style.paddingTop=24;
        Root.style.color=C(CityColor.UiInk);menu.Root.Add(Root);
        var title=new Label("HERO FORGE");title.style.fontSize=42;title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.color=C(CityColor.HeroAccent);title.style.flexShrink=0;Root.Add(title);
        var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.flexGrow=1;row.style.flexBasis=0;row.style.minHeight=0;Root.Add(row);
        var column=new VisualElement{name="hero-preview-column"};column.style.minHeight=0;column.style.width=Length.Percent(42);column.style.marginRight=30;row.Add(column);
        var image=new Image{name="hero-preview",scaleMode=ScaleMode.ScaleToFit};image.style.flexGrow=1;image.style.flexBasis=0;image.style.minHeight=0;column.Add(image);
        StatsPanel=new VisualElement{name="forge-stats"};StatsPanel.style.flexShrink=0;StatsPanel.style.backgroundColor=C(CityColor.UiPanel);StatsPanel.style.paddingLeft=StatsPanel.style.paddingRight=12;
        StatsPanel.style.paddingTop=StatsPanel.style.paddingBottom=8;StatsPanel.style.marginTop=8;column.Add(StatsPanel);
        Preview=new RenderTexture(400,500,24){name="Hero Forge preview"};Preview.Create();image.image=Preview;
        var formScroll=new ScrollView(ScrollViewMode.Vertical){name="forge-form-scroll"};formScroll.style.flexGrow=1;formScroll.style.flexBasis=0;formScroll.style.minHeight=0;formScroll.style.overflow=Overflow.Hidden;
        // The Home panel deliberately has no default Unity theme, so size and clip the ScrollView's internal viewport explicitly.
        var scrollBody=formScroll.contentViewport.parent;scrollBody.style.flexDirection=FlexDirection.Row;scrollBody.style.flexGrow=1;scrollBody.style.flexShrink=1;scrollBody.style.flexBasis=0;scrollBody.style.minHeight=0;
        formScroll.contentViewport.style.flexGrow=1;formScroll.contentViewport.style.flexShrink=1;formScroll.contentViewport.style.minHeight=0;formScroll.contentViewport.style.overflow=Overflow.Hidden;
        formScroll.contentContainer.style.flexShrink=0;formScroll.contentContainer.style.flexGrow=0;row.Add(formScroll);
        form=new VisualElement{name="forge-form"};form.style.flexShrink=0;form.style.paddingTop=12;formScroll.Add(form);
        feedback=new Label();feedback.style.flexShrink=0;feedback.style.fontSize=15;Root.Add(feedback);
        var done=new Button(()=>Close()){text="SAVE & BACK",name="forge-done"};done.style.flexShrink=0;done.style.height=48;done.style.marginTop=16;done.style.marginBottom=20;done.style.backgroundColor=C(CityColor.HeroAccent);done.style.color=C(CityColor.UiNavy);done.style.fontSize=18;done.style.unityFontStyleAndWeight=FontStyle.Bold;done.style.unityTextAlign=TextAnchor.MiddleCenter;Root.Add(done);
        Open();
    }
    public void Open()
    {
        var saved=menu.Profile.Data.Loadout;hero=catalog.Hero(saved.HeroId);a=menu.Profile.EquippedA;b=menu.Profile.EquippedB;utility=menu.Profile.EquippedSecondary;appearance=menu.Profile.Appearance(hero);primary=appearance.Primary;secondary=appearance.Secondary;
        Root.style.display=DisplayStyle.Flex;Rebuild();Capture();
    }
    public void Close(){Root.style.display=DisplayStyle.None;revealActive=false;previewRenderAfterFrame=-1;}
    public bool SelectHero(HeroDefinition value)
    {
        if(Array.IndexOf(catalog.Heroes,value)<0)return false;
        if(hero!=null)menu.Profile.SetAppearance(appearance);
        hero=value;appearance=menu.Profile.Appearance(hero);primary=appearance.Primary;secondary=appearance.Secondary;
        var owned=hero.AvailablePowers.Where(p=>p.Selectable&&menu.Profile.Owns(p)&&p.Role==PowerRole.Combat).ToArray();
        a=owned.Contains(hero.DefaultA)?hero.DefaultA:owned[0];
        b=owned.Contains(hero.DefaultB)&&hero.DefaultB!=a?hero.DefaultB:owned.First(p=>p!=a);
        utility=hero.AvailablePowers.First(p=>p.Selectable&&menu.Profile.Owns(p)&&p.Role==PowerRole.Secondary);
        return Save();
    }
    public bool SelectPower(int slot,PowerDefinition power)
    {
        if(!menu.Profile.Owns(power))return false;
        if(slot==2){if(!catalog.AllowedSecondary(hero,power))return false;utility=power;return Save();}
        if(!catalog.AllowedCombat(hero,power))return false;
        if(slot==0){a=power;if(b==a)b=hero.AvailablePowers.First(p=>p.Selectable&&p!=a&&p.Role==PowerRole.Combat&&menu.Profile.Owns(p));}
        else if(slot==1){if(power==a)return false;b=power;}else return false;
        return Save();
    }
    public bool SetColors(CityColor first,CityColor second)=>SetColors(first,second,appearance.Accent);
    public bool SetColors(CityColor first,CityColor second,CityColor accent)
    {
        var next=appearance.Copy();next.Primary=first;next.Secondary=second;next.Accent=accent;return SaveAppearance(next);
    }
    public bool SelectCosmetic(CosmeticSlot slot,string id)
    {
        var cosmetics=Resources.Load<CosmeticCatalog>("CosmeticCatalog");
        var suit=cosmetics?.Resolve(hero,appearance.SuitId,CosmeticSlot.Suit);
        var item=cosmetics?.Find(id,slot);
        if(item==null||!item.Available||!item.Compatible(hero,suit?.Id))return false;
        var next=appearance.Copy();
        if(slot==CosmeticSlot.Suit)next.SuitId=id;else if(slot==CosmeticSlot.Mask)next.MaskId=id;else next.EmblemId=id;
        return SaveAppearance(next);
    }
    bool SaveAppearance(HeroAppearanceSave next)
    {
        if(!menu.Profile.SetAppearance(next))return false;
        appearance=menu.Profile.Appearance(hero);primary=appearance.Primary;secondary=appearance.Secondary;
        feedback.text="APPEARANCE SAVED";Rebuild();Capture();return true;
    }
    bool Save()
    {
        bool ok=menu.Profile.SetLoadout(hero,a,b,utility,primary,secondary);
        if(ok){feedback.text="BUILD SAVED";Rebuild();Capture();BeginReveal();}else feedback.text="Choose two different attack powers and one secondary.";
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
        var appearanceHeading=new Label("APPEARANCE"){name="forge-appearance-heading"};appearanceHeading.style.fontSize=22;appearanceHeading.style.color=C(CityColor.HeroAccent);form.Add(appearanceHeading);
        BuildCosmetics();
        Dropdown("PRIMARY SUIT",catalog.SuitColors.Select(c=>c.ToString()).ToArray(),Array.IndexOf(catalog.SuitColors,primary),i=>SetColors(catalog.SuitColors[i],secondary));
        Dropdown("SECONDARY SUIT",catalog.SuitColors.Select(c=>c.ToString()).ToArray(),Array.IndexOf(catalog.SuitColors,secondary),i=>SetColors(primary,catalog.SuitColors[i]));
        Dropdown("ACCENT",catalog.SuitColors.Select(c=>c.ToString()).ToArray(),Array.IndexOf(catalog.SuitColors,appearance.Accent),i=>SetColors(primary,secondary,catalog.SuitColors[i]));
        var first=hero.AvailablePowers.Where(p=>p.Selectable&&menu.Profile.Owns(p)&&p.Role==PowerRole.Combat).ToArray();
        SlotA=Dropdown("ATTACK POWER 1",first.Select(p=>p.DisplayName).ToArray(),Array.IndexOf(first,a),i=>SelectPower(0,first[i]));
        var second=first.Where(p=>p!=a).ToArray();
        SlotB=Dropdown("ATTACK POWER 2",second.Select(p=>p.DisplayName).ToArray(),Array.IndexOf(second,b),i=>SelectPower(1,second[i]));
        var secondaries=hero.AvailablePowers.Where(p=>p.Selectable&&menu.Profile.Owns(p)&&p.Role==PowerRole.Secondary).ToArray();
        SecondarySlot=Dropdown("SECONDARY POWER / F",secondaries.Select(p=>p.DisplayName).ToArray(),Array.IndexOf(secondaries,utility),i=>SelectPower(2,secondaries[i]));
        var signatures=Resources.Load<SignatureCatalog>("SignatureCatalog");
        bool preview=signatures!=null&&signatures.Enabled;
        var difficulty=new SessionDifficulty(menu.Profile.SelectedDifficulty);
        foreach(var power in new[]{a,b,utility})
        {
            if(power==null)continue;var stats=power.GetStats(Mathf.Max(0,menu.Profile.Tier(power)));
            float cooldown=stats.Cooldown*difficulty.Cooldown*(power.Effect is PunchEffect?1:(hero.Stats??HeroStats.Baseline).CooldownMultiplier);
            var information=new Label($"{power.DisplayName} / {stats.Charges} charges · {cooldown:0.##}s base cooldown · {power.ResourceCost*difficulty.EnergyUsage:0.##} energy\n{power.Description}"){name="forge-power-info-"+power.Id};
            information.style.whiteSpace=WhiteSpace.Normal;information.style.fontSize=12;information.style.color=C(CityColor.UiInk);information.style.marginBottom=10;form.Add(information);
        }
        var synergy=preview?menu.Profile.EquippedSignature:catalog.Resolve(a,b,utility);
        if(preview)
        {
            var heading=new Label("SIGNATURE ABILITY — CHOOSE ONE"){name="forge-signature-heading"};
            heading.style.unityFontStyleAndWeight=FontStyle.Bold;
            heading.style.color=C(CityColor.UiPurple);form.Add(heading);
            var accent=menu.Profile.Data.Side==PlayerSide.Villain?CityColor.VillainAccent:CityColor.HeroAccent;
            foreach(var option in signatures.Choices(a,b))
            {
                var captured=option;bool chosen=option==synergy;
                var choice=new Button(()=>{if(menu.Profile.SetSignature(captured.Id))Rebuild();}){name="signature-"+option.Id,
                    text=$"{(chosen?"SELECTED · ":"")}{option.DisplayName} / {option.OwnerPrimary.DisplayName}\n{option.Description}\n{option.Cooldown*difficulty.Cooldown:0.##}s cooldown · {option.EnergyCost*difficulty.EnergyUsage:0.##} energy"};
                choice.style.whiteSpace=WhiteSpace.Normal;choice.style.minHeight=78;choice.style.marginBottom=6;
                choice.style.backgroundColor=C(chosen?accent:CityColor.UiPanel);
                choice.style.color=C(chosen?CityColor.UiNavy:CityColor.UiInk);form.Add(choice);
            }
            if(synergy==null)form.Add(new Label("No implemented signature for these primaries. Disable preview to use the legacy system."));
        }
        SynergyLabel=new Label("SYNERGY / "+(synergy?.DisplayName??"No synergy")){name="forge-synergy"};
        SynergyLabel.style.fontSize=24;SynergyLabel.style.whiteSpace=WhiteSpace.Normal;SynergyLabel.style.color=C(CityColor.HeroAccent);form.Add(SynergyLabel);
        // Show the same selected-difficulty resource/cooldown requirements used by the session runner.
        SynergyStatus=new Label(synergy!=null?$"READY WHEN EQUIPPED  ·  {synergy.Cooldown*difficulty.Cooldown:0.##} S COOLDOWN{(synergy.EnergyCost>0?$"  ·  {synergy.EnergyCost*difficulty.EnergyUsage:0.##} ENERGY":"")}  ·  {HudBindings.KeyName(catalog.SynergyKey)} TO USE":""){name="forge-synergy-status"};
        SynergyStatus.style.fontSize=13;SynergyStatus.style.unityFontStyleAndWeight=FontStyle.Bold;SynergyStatus.style.color=C(CityColor.UiMuted);SynergyStatus.style.marginBottom=4;form.Add(SynergyStatus);
        var detail=new Label(synergy?.Description??"");detail.style.whiteSpace=WhiteSpace.Normal;detail.style.marginBottom=12;form.Add(detail);
        BuildStats();
        foreach(var child in form.Children())child.style.flexShrink=0;
        rebuilding=false;
    }
    void BuildCosmetics()
    {
        var cosmetics=Resources.Load<CosmeticCatalog>("CosmeticCatalog");if(cosmetics==null)return;
        var suit=cosmetics.Resolve(hero,appearance.SuitId,CosmeticSlot.Suit);
        foreach(CosmeticSlot slot in Enum.GetValues(typeof(CosmeticSlot)))
        {
            var available=cosmetics.Items.Where(x=>x!=null&&x.Slot==slot&&x.Available&&x.Compatible(hero,suit?.Id)).ToArray();
            string saved=slot==CosmeticSlot.Suit?appearance.SuitId:slot==CosmeticSlot.Mask?appearance.MaskId:appearance.EmblemId;
            var resolved=cosmetics.Resolve(hero,saved,slot,suit?.Id);
            if(available.Length>0)
            {
                var field=Dropdown(slot.ToString().ToUpperInvariant(),available.Select(x=>x.DisplayName).ToArray(),Array.IndexOf(available,resolved),i=>SelectCosmetic(slot,available[i].Id));
                field.name="forge-cosmetic-"+slot.ToString().ToLowerInvariant();
            }
            if(saved!=null&&saved!=resolved?.Id)
            {
                var fallback=new Label("Saved "+slot.ToString().ToLowerInvariant()+" unavailable; showing "+(resolved?.DisplayName??"legacy appearance")+". Selection retained.");fallback.style.whiteSpace=WhiteSpace.Normal;fallback.style.fontSize=12;form.Add(fallback);
            }

        }
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
        if(PreviewModel!=null&&previewHero==hero&&appearanceRenderer!=null)
        {
            appearanceRenderer.Apply(appearance);PreviewModel.GetComponent<Animator>().Update(0);previewRenderAfterFrame=Time.frameCount+1;return;
        }
        if(previewRoot!=null){previewRoot.SetActive(false);Destroy(previewRoot);}
        previewRoot=new GameObject("Forge preview only");previewRoot.transform.SetParent(transform,false);previewRoot.transform.position=new Vector3(10000,-1000,10000);
        // Preview owns one palette cache, since the skyline's transient cache is disposed after capture.
        var materials=previewRoot.AddComponent<CityMaterials>();materials.Initialize(palette);
        var tuning=hero.Animation!=null?hero.Animation:Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning");
        var model=Instantiate(hero.CharacterPrefab!=null?hero.CharacterPrefab:tuning.Model,previewRoot.transform);PreviewModel=model;previewHero=hero;
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
        appearanceRenderer=HeroAppearanceRenderer.Install(model,hero,materials,true);appearanceRenderer.Apply(appearance);animator.Update(0);
        foreach(var node in previewRoot.GetComponentsInChildren<Transform>())node.gameObject.layer=30;
        var camera=new GameObject("Forge preview camera").AddComponent<Camera>();camera.transform.SetParent(previewRoot.transform,false);camera.enabled=false;camera.cullingMask=1<<30;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=C(CityColor.UiPanel);camera.fieldOfView=32;camera.targetTexture=Preview;previewCamera=camera;
        bounds=Posed();
        camera.transform.position=bounds.center+new Vector3(0,.1f,4.2f);camera.transform.LookAt(bounds.center);
        var pivot=new GameObject("Forge orbit pivot").transform;pivot.SetParent(previewRoot.transform,false);pivot.position=bounds.center;orbitPivot=pivot;
        var focus=new GameObject("Forge reveal focus").transform;focus.SetParent(previewRoot.transform,false);focus.position=bounds.center;
        var virtualCamera=new GameObject("Forge Cinemachine reveal").AddComponent<CinemachineCamera>();virtualCamera.transform.SetParent(previewRoot.transform,false);
        virtualCamera.Follow=pivot;virtualCamera.LookAt=focus;
        var lens=virtualCamera.Lens;lens.FieldOfView=32;virtualCamera.Lens=lens;
        var track=virtualCamera.gameObject.AddComponent<CinemachineFollow>();track.FollowOffset=new Vector3(0,.1f,4.2f);
        var tracking=track.TrackerSettings;tracking.BindingMode=Unity.Cinemachine.TargetTracking.BindingMode.LockToTarget;tracking.PositionDamping=Vector3.zero;tracking.RotationDamping=Vector3.zero;track.TrackerSettings=tracking;
        var aim=virtualCamera.gameObject.AddComponent<CinemachineRotationComposer>();aim.Damping=Vector2.zero;
        revealBrain=camera.gameObject.AddComponent<CinemachineBrain>();revealBrain.UpdateMethod=CinemachineBrain.UpdateMethods.ManualUpdate;revealBrain.IgnoreTimeScale=true;
        var sun=new GameObject("Forge preview light").AddComponent<Light>();sun.transform.SetParent(previewRoot.transform,false);sun.type=LightType.Directional;sun.cullingMask=1<<30;sun.intensity=1.4f;sun.color=C(CityColor.Cream);sun.transform.rotation=Quaternion.Euler(35,150,0);
        camera.targetTexture=null;previewRenderAfterFrame=Time.frameCount+1;
        // No preview animation/camera work per frame while home gameplay cards are shown.
        animator.enabled=false;
    }
    void BeginReveal(){revealStarted=Time.unscaledTime;revealActive=true;RevealFrames=0;RevealHeading=-28;if(orbitPivot!=null)orbitPivot.rotation=Quaternion.Euler(0,-28,0);}
    void LateUpdate()
    {
        // Unity refreshes skinning buffers after mesh/bone reassignment. Keep the last complete image until the next
        // frame, coalescing rapid controls into one render instead of capturing partially updated geometry.
        if(previewRenderAfterFrame>=0)
        {
            if(Time.frameCount<previewRenderAfterFrame)return;
            if(previewCamera!=null&&Root.style.display!=DisplayStyle.None){previewCamera.targetTexture=Preview;previewCamera.Render();previewCamera.targetTexture=null;previewRenderAfterFrame=-1;}
        }
        if(!revealActive||previewCamera==null||revealBrain==null||Root.style.display==DisplayStyle.None)return;
        float t=Mathf.Clamp01((Time.unscaledTime-revealStarted)/.85f);
        // Quick orbit and settle. The Forge form stays interactive throughout; no gameplay camera is touched.
        float heading=t<.7f?Mathf.Lerp(-28,18,Mathf.SmoothStep(0,1,t/.7f)):Mathf.Lerp(18,0,Mathf.SmoothStep(0,1,(t-.7f)/.3f));
        orbitPivot.rotation=Quaternion.Euler(0,heading,0);RevealHeading=heading;
        revealBrain.ManualUpdate();previewCamera.targetTexture=Preview;previewCamera.Render();previewCamera.targetTexture=null;RevealFrames++;
        if(t>=1)revealActive=false;
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
