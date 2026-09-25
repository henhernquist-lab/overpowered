using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// One persistent bounded voice pool. No per-NPC components, audio coroutines or per-frame queries/allocations.
[DefaultExecutionOrder(100)]
public sealed class AudioDirector : MonoBehaviour
{
    public static AudioDirector Instance {get;private set;}
    public AudioTuning Tuning {get;private set;}
    public AudioSource[] Sources {get;private set;}
    public float HeatIntensity {get;private set;}
    public WorldSession BoundWorld=>world;
    const int Reserved=4;
    AudioCue[] playing;
    AudioCueDefinition[] cues;
    int cursor=Reserved;
    WorldSession world;
    HumanoidPresentation player;
    Camera listenerCamera;
    float refreshClock,sampleClock,playerDistance;
    bool wasFlying;
    VisualElement menuRoot;
    int lastUiFrame=-1;Button lastUiButton;
    System.Random random=new System.Random(); // Do not perturb gameplay's UnityEngine.Random sequence.
    sealed class Actor
    {
        public CityNpc Npc;public HumanoidPresentation Pose;public int Hits,Deaths;public float Distance;
        public System.Action Attack;
    }
    Actor[] actors;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic(){Instance=null;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install(){if(Instance==null)new GameObject("Audio Director (pooled)").AddComponent<AudioDirector>();}
    void Awake()
    {
        if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;DontDestroyOnLoad(gameObject);
        Tuning=Resources.Load<AudioTuning>("AudioTuning");
        if(Tuning==null){Debug.LogError("AudioTuning missing. Run Overpowered/Audio/Create missing audio assets.");enabled=false;return;}
        cues=new AudioCueDefinition[System.Enum.GetValues(typeof(AudioCue)).Length];foreach(var cue in Tuning.Cues)cues[(int)cue.Id]=cue;
        Sources=new AudioSource[Mathf.Max(Reserved+4,Tuning.PoolSize)];playing=new AudioCue[Sources.Length];actors=new Actor[Mathf.Max(1,Tuning.MaximumTrackedActors)];
        for(int i=0;i<Sources.Length;i++){var go=new GameObject("Voice "+i);go.transform.SetParent(transform,false);var s=go.AddComponent<AudioSource>();s.playOnAwake=false;s.dopplerLevel=0;Sources[i]=s;}
    }
    void OnEnable(){SceneManager.sceneLoaded+=SceneChanged;BreakableProp.Destroyed+=PropDestroyed;CityNpc.Spawned+=NpcSpawned;}
    void Start(){ApplyMixerVolumes();}
    public void ApplyMixerVolumes()
    {
        if(Tuning==null||Tuning.Mixer==null)return;
        Tuning.Mixer.SetFloat("MasterVolume",Tuning.MasterDb);Tuning.Mixer.SetFloat("MusicVolume",Tuning.MusicDb);Tuning.Mixer.SetFloat("SFXVolume",Tuning.SfxDb);Tuning.Mixer.SetFloat("UIVolume",Tuning.UiDb);Tuning.Mixer.SetFloat("AmbientVolume",Tuning.AmbientDb);
    }
    void SceneChanged(Scene scene,LoadSceneMode mode)
    {
        Unbind();listenerCamera=null;refreshClock=0;
        // Preserve UI click tails and music across scene loads, but never leave world loops behind.
        if(Sources!=null)for(int i=0;i<Sources.Length;i++)if(i<3||i>=Reserved&&cues[(int)playing[i]].Spatial)Sources[i].Stop();
    }
    void Unbind()
    {
        if(world!=null){world.Powers.Activated-=PowerActivated;world.PlayerDamaged-=PlayerDamage;world.PlayerRespawned-=Respawn;world.Hero.PunchImpacted-=Punch;world.Hero.HurricaneKickImpacted-=Punch;world.Hero.BackflipStarted-=Jump;world.Hero.Jumped-=Jump;world.Hero.Landed-=Land;}
        if(actors!=null)for(int i=0;i<actors.Length;i++)RemoveActor(i);
        if(menuRoot!=null){menuRoot.UnregisterCallback<ClickEvent>(UiClicked,TrickleDown.TrickleDown);menuRoot.UnregisterCallback<NavigationSubmitEvent>(UiSubmit,TrickleDown.TrickleDown);menuRoot.UnregisterCallback<PointerOverEvent>(UiOver,TrickleDown.TrickleDown);menuRoot.UnregisterCallback<FocusInEvent>(UiFocus,TrickleDown.TrickleDown);menuRoot=null;}
        world=null;player=null;playerDistance=0;wasFlying=false;HeatIntensity=0;lastUiButton=null;lastUiFrame=-1;
    }
    void Bind()
    {
        var current=WorldSession.Instance;
        if(current!=null&&current.Powers!=null&&world!=current)
        {
            Unbind();world=current;player=world.Hero.GetComponent<HumanoidPresentation>();
            world.Powers.Activated+=PowerActivated;world.PlayerDamaged+=PlayerDamage;world.PlayerRespawned+=Respawn;world.Hero.PunchImpacted+=Punch;world.Hero.HurricaneKickImpacted+=Punch;world.Hero.BackflipStarted+=Jump;world.Hero.Jumped+=Jump;world.Hero.Landed+=Land;
        }
        // The hero's presentation may be created after the world starts spawning NPCs (Hero Forge bootstrap order),
        // so a Bind() triggered by an early spawn can see none yet: keep resolving it until it exists.
        if(world!=null&&player==null)player=world.Hero.GetComponent<HumanoidPresentation>();
        if(listenerCamera==null)listenerCamera=Camera.main;
        if(world==null&&menuRoot==null)
        {
            var screen=FindAnyObjectByType<ModeScreens>();if(screen!=null&&screen.Root!=null)
            {menuRoot=screen.Root;menuRoot.RegisterCallback<ClickEvent>(UiClicked,TrickleDown.TrickleDown);menuRoot.RegisterCallback<NavigationSubmitEvent>(UiSubmit,TrickleDown.TrickleDown);menuRoot.RegisterCallback<PointerOverEvent>(UiOver,TrickleDown.TrickleDown);menuRoot.RegisterCallback<FocusInEvent>(UiFocus,TrickleDown.TrickleDown);}
        }
    }
    void RemoveActor(int i){var actor=actors[i];if(actor!=null&&actor.Npc!=null)actor.Npc.Attacked-=actor.Attack;actors[i]=null;}
    void RefreshActors()
    {
        for(int i=0;i<actors.Length;i++)if(actors[i]!=null&&actors[i].Npc==null)RemoveActor(i);
        if(world==null)return;
        foreach(var npc in world.Npcs)
        {
            if(npc==null)continue;bool found=false;int empty=-1;
            for(int i=0;i<actors.Length;i++){if(actors[i]==null){if(empty<0)empty=i;}else if(actors[i].Npc==npc){found=true;break;}}
            if(found||empty<0)continue;
            var pose=npc.GetComponent<HumanoidPresentation>();if(pose==null)continue;
            var actor=new Actor{Npc=npc,Pose=pose,Hits=pose.HitCount,Deaths=pose.DeathCount};
            actor.Attack=()=>{if(actor.Npc!=null)Play(AttackCue(actor.Npc),actor.Npc.transform.position);};npc.Attacked+=actor.Attack;actors[empty]=actor;
        }
    }
    float Jitter(float min,float max)=>Mathf.Lerp(min,max,(float)random.NextDouble());
    void Configure(int index,AudioCueDefinition cue,Vector3 position,bool loop,float gain)
    {
        var source=Sources[index];source.Stop();playing[index]=cue.Id;source.transform.position=position;
        source.clip=cue.Clips[random.Next(cue.Clips.Length)];source.outputAudioMixerGroup=cue.Group;source.spatialBlend=cue.Spatial?1:0;
        source.rolloffMode=cue.Rolloff;source.minDistance=cue.MinDistance;source.maxDistance=Mathf.Max(cue.MinDistance,cue.MaxDistance);source.priority=cue.Priority;
        source.pitch=loop?1:Jitter(cue.Pitch.x,cue.Pitch.y);source.volume=loop?gain:Mathf.Clamp01(cue.Volume+Jitter(-cue.VolumeJitter,cue.VolumeJitter));source.loop=loop;source.ignoreListenerPause=!cue.Spatial;source.Play();
    }
    public AudioSource Play(AudioCue id,Vector3 position)
    {
        if(!isActiveAndEnabled||cues==null)return null;var cue=cues[(int)id];
        if(cue==null||cue.Clips==null||cue.Clips.Length==0||cue.MaxConcurrent<=0)return null;
        if(cue.Spatial&&(world==null||world.Mode!=null&&(world.Mode.Paused||world.Mode.Ended)))return null;
        if(cue.Spatial&&listenerCamera!=null&&(position-listenerCamera.transform.position).sqrMagnitude>cue.MaxDistance*cue.MaxDistance)return null;
        int active=0;for(int i=0;i<Sources.Length;i++)if(playing[i]==id&&Sources[i].isPlaying)active++;
        if(active>=cue.MaxConcurrent)return null;
        for(int n=0;n<Sources.Length-Reserved;n++)
        {
            int index=cursor;cursor++;if(cursor>=Sources.Length)cursor=Reserved;
            if(Sources[index].isPlaying)continue;Configure(index,cue,position,false,1);return Sources[index];
        }
        return null; // Saturated: drop, never allocate or steal a persistent bed.
    }
    void Bed(int index,AudioCue id,float gain,Vector3 position)
    {
        var cue=cues[(int)id];var source=Sources[index];float target=cue.Volume*Mathf.Clamp01(gain);
        if(target>0&&!source.isPlaying)Configure(index,cue,position,true,0);
        source.transform.position=position;source.volume=Mathf.MoveTowards(source.volume,target,Tuning.LoopFadePerSecond*Time.unscaledDeltaTime);
        if(target==0&&source.volume==0&&source.isPlaying)source.Stop();
    }
    void Update()
    {
        if(Sources==null)return;
        refreshClock-=Time.unscaledDeltaTime;if(refreshClock<=0){refreshClock=Mathf.Max(.1f,Tuning.ActorRefreshInterval);Bind();RefreshActors();}
        bool live=world!=null&&(world.Mode==null||!world.Mode.Ended&&!world.Mode.Paused);
        float heat=live?world.Heat/Mathf.Max(1,world.Tuning.Heat.MaximumStars):0;
        HeatIntensity=Mathf.MoveTowards(HeatIntensity,Mathf.Clamp01(heat),Tuning.HeatResponse*Time.unscaledDeltaTime);
        Vector3 position=world!=null?world.Hero.transform.position:Vector3.zero;
        Bed(0,AudioCue.CityBed,live?Mathf.Lerp(Tuning.CalmBedGain.x,Tuning.CalmBedGain.y,HeatIntensity):0,position);
        Bed(1,AudioCue.SirenBed,live?Mathf.Lerp(Tuning.SirenBedGain.x,Tuning.SirenBedGain.y,HeatIntensity):0,position);
        bool flying=live&&!world.PlayerDead&&world.Hero.PresentationState.Flying;
        if(flying&&!wasFlying)Play(AudioCue.FlightStart,position);wasFlying=flying;Bed(2,AudioCue.FlightLoop,flying?1:0,position);
        Bed(3,AudioCue.Music,live?Mathf.Lerp(Tuning.MusicGain.x,Tuning.MusicGain.y,HeatIntensity):Tuning.MenuMusicGain,position);
        sampleClock+=Time.deltaTime;if(sampleClock<Tuning.ActorSampleInterval)return;float dt=sampleClock;sampleClock=0;
        if(!live)return;
        if(player!=null&&!world.PlayerDead)Step(player,ref playerDistance,dt);
        for(int i=0;i<actors.Length;i++)
        {
            var actor=actors[i];if(actor==null||actor.Npc==null||actor.Pose==null)continue;
            var pose=actor.Pose;var pos=actor.Npc.transform.position;
            if(pose.DeathCount>actor.Deaths)Play(AudioCue.Death,pos);else if(pose.HitCount>actor.Hits)Play(AudioCue.Hit,pos);
            actor.Hits=pose.HitCount;actor.Deaths=pose.DeathCount;
            if(!actor.Npc.Dead&&(pos-position).sqrMagnitude<Tuning.ActorAudibleDistance*Tuning.ActorAudibleDistance)Step(pose,ref actor.Distance,dt);
            else actor.Distance=0;
        }
    }
    void Step(HumanoidPresentation pose,ref float distance,float dt)
    {
        string state=pose.State;bool ground=state=="Locomotion"||state=="Panic"||state=="Armed"||state=="Back";
        if(!ground||pose.MeasuredSpeed<Tuning.MinimumFootstepSpeed){distance=0;return;}
        distance+=pose.MeasuredSpeed*dt;if(distance<Mathf.Max(.1f,Tuning.FootstepDistance))return;
        distance%=Mathf.Max(.1f,Tuning.FootstepDistance);Play(AudioCue.Footstep,pose.transform.position);
    }
    void PowerActivated(PowerDefinition definition)
    {if(definition.Effect is PunchEffect)return;foreach(var binding in Tuning.Powers)if(binding.Effect==definition.Effect){Play(binding.Cue,world.Hero.transform.position);return;}}
    static AudioCue AttackCue(CityNpc npc)=>npc.Archetype!=null?(npc.Archetype.Kind==AttackKind.Ranged?AudioCue.Gunshot:AudioCue.Punch):(npc.Role==NpcRole.Cop?AudioCue.Gunshot:AudioCue.Punch);
    // Without this, an enemy attacking within one ActorRefreshInterval of spawning was silent (Endless gunners can).
    void NpcSpawned(CityNpc npc){Bind();RefreshActors();}
    /// True when this director is listening to the NPC's attacks.
    public bool Tracks(CityNpc npc){foreach(var a in actors)if(a!=null&&a.Npc==npc)return true;return false;}
    void Punch(){Play(AudioCue.Punch,world.Hero.transform.position);}
    void Jump(){Play(AudioCue.Jump,world.Hero.transform.position);}
    void Land(float speed){if(speed>=Tuning.MinimumLandingSpeed)Play(AudioCue.Land,world.Hero.transform.position);}
    void PlayerDamage(bool died){Play(died?AudioCue.Death:AudioCue.Hit,world.Hero.transform.position);}
    void Respawn(){Sources[2].Stop();wasFlying=false;playerDistance=0;}
    void PropDestroyed(Vector3 position){Play(AudioCue.Destruction,position);}
    static Button ButtonAt(object target){var e=target as VisualElement;return e as Button??e?.GetFirstAncestorOfType<Button>();}
    void UiClick(Button button)
    {
        if(button==null||!button.enabledInHierarchy||lastUiFrame==Time.frameCount&&lastUiButton==button)return;
        lastUiFrame=Time.frameCount;lastUiButton=button;Play(AudioCue.UiClick,Vector3.zero);
    }
    void UiClicked(ClickEvent evt){UiClick(ButtonAt(evt.target));}
    void UiSubmit(NavigationSubmitEvent evt){UiClick(ButtonAt(evt.target));}
    void UiOver(PointerOverEvent evt){var b=ButtonAt(evt.target);if(b!=null&&b.enabledInHierarchy)Play(AudioCue.UiHover,Vector3.zero);}
    void UiFocus(FocusInEvent evt){var b=ButtonAt(evt.target);if(b!=null&&b.enabledInHierarchy)Play(AudioCue.UiHover,Vector3.zero);}
    public void StopAll(){if(Sources!=null)foreach(var source in Sources)source.Stop();}
    void OnDisable(){SceneManager.sceneLoaded-=SceneChanged;BreakableProp.Destroyed-=PropDestroyed;CityNpc.Spawned-=NpcSpawned;Unbind();StopAll();}
    void OnDestroy(){if(Instance==this)Instance=null;}
}
