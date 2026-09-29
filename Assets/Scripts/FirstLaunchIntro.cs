using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

/// Home-only, skippable Timeline presentation. The selected hero is a visual duplicate; the gameplay root is never animated.
public sealed class FirstLaunchIntro : MonoBehaviour
{
    public static bool AllowBatchVerification;
    public bool Playing => director!=null&&director.state==PlayState.Playing&&!finished;
    public bool Skipped {get;private set;}
    public double Elapsed => director!=null?director.time:0;
    public RenderTexture Image => image;
    public string SelectedHeroId {get;private set;}
    /// Name of the prefab the staged duplicate was instantiated from (the selected hero's own character, not a fixed one).
    public string VisualSource {get;private set;}
    public int RenderedFrames {get;private set;}
    ModeScreens menu;PlayableDirector director;VisualElement overlay;Image heroImage;Label caption;
    GameObject stage,visual;Camera stageCamera;RenderTexture image;Animator heroAnimator;bool landed,finished;
    public string DebugView()
    {
        var r=visual!=null?visual.GetComponentInChildren<SkinnedMeshRenderer>():null;
        return $"stage={stage?.transform.position} hero={stage?.transform.Find("Hero entrance")?.position} model={visual?.transform.position} mesh={r?.bounds} layer={r?.gameObject.layer} camera={stageCamera?.transform.position} forward={stageCamera?.transform.forward} viewport={(r!=null&&stageCamera!=null?stageCamera.WorldToViewportPoint(r.bounds.center):Vector3.zero)} mask={stageCamera?.cullingMask} frames={RenderedFrames}";
    }
    public void Initialize(ModeScreens owner)
    {
        menu=owner;
        if(owner.Profile.Data.IntroSeen){Destroy(this);return;}
        var timeline=Resources.Load<TimelineAsset>("Intro/FirstLaunch");
        if(timeline==null)throw new InvalidOperationException("Run IntroTimelineSetup.Configure to author the first-launch Timeline.");
        BuildOverlay();BuildStage();
        director=stage.AddComponent<PlayableDirector>();director.playableAsset=timeline;director.timeUpdateMode=DirectorUpdateMode.UnscaledGameTime;
        foreach(var track in timeline.GetOutputTracks())
        {
            if(track.name=="Hero entrance")director.SetGenericBinding(track,stage.transform.Find("Hero entrance").GetComponent<Animator>());
            else if(track.name=="Camera push")director.SetGenericBinding(track,stage.transform.Find("Camera push").GetComponent<Animator>());
        }
        director.stopped+=Stopped;director.Play();
    }
    void BuildOverlay()
    {
        var palette=Resources.Load<CityPalette>("CityPalette");
        overlay=new VisualElement{name="first-launch-intro"};overlay.style.position=Position.Absolute;
        overlay.style.left=overlay.style.right=overlay.style.top=overlay.style.bottom=0;
        overlay.style.backgroundColor=palette.Colors[(int)CityColor.UiNavy];
        var skyline=GameFlow.Instance.GetComponent<MenuSkyline>().Get(Resources.Load<MenuPresentationTuning>("MenuPresentationTuning"));
        overlay.style.backgroundImage=Background.FromRenderTexture(skyline);overlay.style.backgroundSize=new BackgroundSize(BackgroundSizeType.Cover);
        menu.Root.Add(overlay);
        heroImage=new Image{name="intro-hero",scaleMode=ScaleMode.StretchToFill};heroImage.style.position=Position.Absolute;
        heroImage.style.left=heroImage.style.right=heroImage.style.top=heroImage.style.bottom=0;heroImage.style.opacity=0;overlay.Add(heroImage);
        caption=new Label("THE CITY IS WAITING"){name="intro-caption"};caption.style.position=Position.Absolute;caption.style.left=Length.Percent(7);caption.style.bottom=Length.Percent(12);
        caption.style.fontSize=38;caption.style.unityFontStyleAndWeight=FontStyle.Bold;caption.style.color=palette.Colors[(int)CityColor.Cream];overlay.Add(caption);
        var skip=new Button(Skip){name="intro-skip",text="SKIP  ›"};skip.style.position=Position.Absolute;skip.style.right=Length.Percent(5);skip.style.top=Length.Percent(5);
        skip.style.width=130;skip.style.height=48;skip.style.fontSize=17;skip.style.unityTextAlign=TextAnchor.MiddleCenter;skip.style.unityFontStyleAndWeight=FontStyle.Bold;skip.style.color=palette.Colors[(int)CityColor.UiInk];skip.style.backgroundColor=palette.Colors[(int)CityColor.UiPanel];overlay.Add(skip);
    }
    static void Layer(GameObject root,int layer){foreach(var node in root.GetComponentsInChildren<Transform>(true))node.gameObject.layer=layer;}
    void BuildStage()
    {
        stage=new GameObject("First launch intro stage");stage.transform.position=new Vector3(20000,-1000,0);
        var palette=Resources.Load<CityPalette>("CityPalette");stage.AddComponent<CityMaterials>().Initialize(palette);
        var hero=menu.Profile.SelectedHero;SelectedHeroId=hero.Id;var look=hero.Animation!=null?hero.Animation:Resources.Load<HumanoidAnimationTuning>("HumanoidAnimationTuning");
        var entrance=new GameObject("Hero entrance");entrance.transform.SetParent(stage.transform,false);
        var entryAnimator=entrance.AddComponent<Animator>();entryAnimator.applyRootMotion=false;
        var motion=new GameObject("Motion").transform;motion.SetParent(entrance.transform,false);
        var source=hero.CharacterPrefab!=null?hero.CharacterPrefab:look.Model;VisualSource=source.name;
        var model=Instantiate(source,motion);model.name="Selected intro hero";model.transform.localRotation=Quaternion.Euler(0,180,0);visual=model;
        heroAnimator=model.GetComponent<Animator>();heroAnimator.runtimeAnimatorController=look.Controller;heroAnimator.applyRootMotion=false;
        heroAnimator.Play("Locomotion");heroAnimator.Update(0);
        var loadout=menu.Profile.Data.Loadout;
        foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if(hero.Suit!=null)hero.Suit.ApplyBody(renderer);
            var mats=renderer.sharedMaterials;
            for(int i=0;i<mats.Length;i++)
            {
                if(hero.Suit!=null)
                {
                    if(mats[i]==hero.Suit.Source)mats[i]=CityMaterials.Current.SuitMaterial(hero.Suit,loadout.Primary,loadout.Secondary);
                }
                else mats[i]=CityMaterials.Get(renderer.name.Contains("Joints")?loadout.Secondary:loadout.Primary);
            }
            renderer.sharedMaterials=mats;
        }
        // Fit the visual duplicate to the intro frame, keeping the separate animated wrapper free of physics.
        var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();Bounds bounds=Posed(renderers);
        if(bounds.size.y>.01f)
        {
            model.transform.localScale=Vector3.Scale(model.transform.localScale*(2.5f/bounds.size.y),hero.VisualScale);
            bounds=Posed(renderers);model.transform.position+=motion.position-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        }
        heroAnimator.enabled=false;
        var cameraRig=new GameObject("Camera push");cameraRig.transform.SetParent(stage.transform,false);
        var cameraAnimator=cameraRig.AddComponent<Animator>();cameraAnimator.applyRootMotion=false;
        var dolly=new GameObject("Dolly").transform;dolly.SetParent(cameraRig.transform,false);dolly.localPosition=new Vector3(0,1.5f,-9);
        stageCamera=new GameObject("Intro camera").AddComponent<Camera>();stageCamera.transform.SetParent(dolly,false);
        stageCamera.transform.localPosition=Vector3.up*1.5f;stageCamera.transform.localRotation=Quaternion.Euler(4,0,0);stageCamera.enabled=false;stageCamera.cullingMask=1<<29;
        stageCamera.clearFlags=CameraClearFlags.SolidColor;stageCamera.backgroundColor=new Color(0,0,0,0);stageCamera.fieldOfView=32;
        image=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32){name="First launch Timeline image"};image.Create();heroImage.image=image;
        var light=new GameObject("Intro hero light").AddComponent<Light>();light.transform.SetParent(stage.transform,false);light.type=LightType.Directional;light.cullingMask=1<<29;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,150,0);
        Layer(model,29);entrance.layer=29;cameraRig.layer=29;stageCamera.gameObject.layer=29;light.gameObject.layer=29;
    }
    static Bounds Posed(SkinnedMeshRenderer[] renderers)
    {
        Bounds bounds=new Bounds();bool any=false;var baked=new Mesh();
        foreach(var r in renderers){r.BakeMesh(baked);foreach(var v in baked.vertices){var at=r.transform.TransformPoint(v);if(!any){bounds=new Bounds(at,Vector3.zero);any=true;}else bounds.Encapsulate(at);}}
        Destroy(baked);return bounds;
    }
    void Update()
    {
        if(finished||director==null)return;
        if(Input.GetKeyDown(KeyCode.Escape)||Input.GetKeyDown(KeyCode.Space)||Input.GetMouseButtonDown(0)){Skip();return;}
        double t=director.time;
        heroImage.style.opacity=Mathf.SmoothStep(0,1,Mathf.Clamp01(((float)t-2.0f)/1.3f));
        caption.text=t<3?"THE CITY IS WAITING":t<6.5?"A HERO ARRIVES":"YOUR STORY STARTS HERE";
        if(t>=6.15&&!landed){landed=true;heroAnimator.enabled=true;heroAnimator.Play("Land",0,0);}
        stageCamera.targetTexture=image;stageCamera.Render();stageCamera.targetTexture=null;RenderedFrames++;
    }
    void Stopped(PlayableDirector stopped){Complete(false);}
    public void Skip(){Complete(true);}
    void Complete(bool skip)
    {
        if(finished)return;finished=true;Skipped=skip;
        if(director!=null){director.stopped-=Stopped;if(director.state==PlayState.Playing)director.Stop();}
        menu.Profile.MarkIntroSeen();overlay?.RemoveFromHierarchy();
        if(stage!=null)Destroy(stage);if(image!=null){image.Release();Destroy(image);}
    }
    void OnDestroy()
    {
        if(director!=null)director.stopped-=Stopped;
        overlay?.RemoveFromHierarchy();if(stage!=null)Destroy(stage);if(image!=null){image.Release();Destroy(image);}
    }
}
