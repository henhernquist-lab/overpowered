using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// Isolated developer scene. No city generation, mode director or persistent profile is created.
public sealed class CombatLab : MonoBehaviour
{
    public const string SceneName="CombatLab";
    public static CombatLab Active {get;private set;}
    static ProgressSave pendingSnapshot;
    public CombatLabSettings Settings {get;private set;}
    public WorldSession World {get;private set;}
    public readonly List<CityNpc> Owned=new List<CityNpc>();
    public int SelectedIndex {get;private set;}
    public int ActiveCount {get {int n=0;foreach(var npc in Owned)if(npc!=null&&!npc.Dead)n++;return n;}}
    public string Feedback {get;private set;}="F2 opens controls. Your equipped loadout is copied; progress is discarded.";
    public ProgressSave StartingSnapshot {get;private set;}
    NavMeshData data; NavMeshDataInstance nav;
    readonly NavMeshPath spawnPath=new NavMeshPath();
    readonly List<GameObject> props=new List<GameObject>();
    int spawnSerial;
    public static void Build()
    {
        if(Active!=null)return;
        new GameObject("Combat Lab").AddComponent<CombatLab>().Initialize();
    }
    void Initialize()
    {
        Active=this; Settings=Resources.Load<CombatLabSettings>("CombatLabSettings");
        var tuning=Resources.Load<GameTuning>("GameTuning");
        Physics.gravity=Vector3.down*tuning.Movement.Gravity;
        new GameObject("Lab palette").AddComponent<CityMaterials>().Initialize(Resources.Load<CityPalette>("CityPalette"));
        var arena=new GameObject("Practice arena (no procedural city)").AddComponent<CityDistrict>();
        arena.Sidewalks.Add(Vector3.zero); arena.SidewalkDistrict.Add(0);
        float size=Mathf.Max(40,Settings.ArenaSize);
        Box("Arena floor",new Vector3(0,-.5f,0),new Vector3(size,1,size),CityColor.Pavement);
        for(int i=0;i<4;i++)
        {
            var rotation=Quaternion.Euler(0,i*90,0);
            Box("Arena boundary",rotation*new Vector3(0,1.5f,size*.5f),i%2==0?new Vector3(size,3,1):new Vector3(1,3,size),CityColor.Slate);
        }
        // Static cover sits outside the central test lane. Shared primitive mesh and palette material.
        Box("Left cover",new Vector3(-14,1,7),new Vector3(3,2,5),CityColor.Metal);
        Box("Right cover",new Vector3(14,1,-7),new Vector3(3,2,5),CityColor.Metal);
        Physics.SyncTransforms(); BuildNav();
        var hero=new GameObject("Practice hero");hero.transform.position=Vector3.up*.1f;
        var cc=hero.AddComponent<CharacterController>();cc.height=tuning.Movement.Height;cc.radius=tuning.Movement.Radius;cc.center=Vector3.up*cc.height*.5f;
        var controller=hero.AddComponent<SuperHeroController>();
        World=new GameObject("Practice session").AddComponent<WorldSession>();
        string readPath=WorldSession.VerificationSavePath;
        if(readPath==null&&!Application.isBatchMode)readPath=Path.Combine(Application.persistentDataPath,tuning.Progression.SaveFilename);
        World.InitializePractice(tuning,arena,controller,pendingSnapshot,readPath);pendingSnapshot=null;
        StartingSnapshot=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(World.Progression.Data));
        HumanoidPresentation.Create(hero,cc.height,controller,null,World.Progression.SelectedHero);
        var camera=GameCamera.Ensure(gameObject.scene,true);var follow=camera.GetComponent<ThirdPersonCamera>()??camera.gameObject.AddComponent<ThirdPersonCamera>();
        follow.target=hero.transform;camera.fieldOfView=tuning.Camera.FieldOfView;camera.farClipPlane=180;
        RenderSettings.fog=false;RenderSettings.ambientLight=new Color(.6f,.65f,.72f);
        var sun=new GameObject("Lab daylight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.transform.rotation=Quaternion.Euler(45,-35,0);
        new GameObject("Lab frost pool").AddComponent<FreezeIndicatorPool>().Initialize();
        FireProjectilePool.Create(World);
        new GameObject("Lab feel").AddComponent<FeelDirector>().Initialize(World);
        new GameObject("Lab combat HUD").AddComponent<GameHud>();
        gameObject.AddComponent<CombatLabPanel>().Initialize(this);
        SpawnProps(); SetControls(false);
    }
    GameObject Box(string label,Vector3 position,Vector3 scale,CityColor color)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=label;go.transform.SetParent(transform,false);
        go.transform.position=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(color);return go;
    }
    void BuildNav()
    {
        var sources=new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(transform,~0,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
        var settings=NavMesh.GetSettingsByIndex(0);
        // Largest lab enemy must fit through the same baked walkable space.
        settings.agentRadius=WorldSession.Instance!=null?WorldSession.Instance.Tuning.Npcs.Radius:.5f;
        settings.agentHeight=2.4f;
        data=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(Vector3.zero,new Vector3(Settings.ArenaSize+4,12,Settings.ArenaSize+4)),Vector3.zero,Quaternion.identity);
        nav=NavMesh.AddNavMeshData(data);
    }
    void SpawnProps()
    {
        var tuning=World.Tuning;
        for(int i=0;i<3;i++)
        {
            var go=Box("Lab throwable crate",new Vector3(-5+i*5,.6f,20),Vector3.one*1.2f,CityColor.Wood);
            var body=go.AddComponent<Rigidbody>();body.mass=tuning.Props.CrateMass;
            go.AddComponent<BreakableProp>().Configure(tuning.Props);props.Add(go);
        }
    }
    public bool Select(int index)
    {
        if(index<0||index>=Settings.Archetypes.Length||Settings.Archetypes[index]==null)return false;
        SelectedIndex=index;return true;
    }
    public CityNpc SpawnOne()
    {
        Owned.RemoveAll(n=>n==null);
        // Bound corpses as well as living actors; clearing never awards a defeat.
        for(int i=Owned.Count-1;i>=0;i--)if(Owned[i].Dead){Destroy(Owned[i].gameObject);Owned.RemoveAt(i);}
        if(Owned.Count>=Mathf.Clamp(Settings.MaxEnemies,1,24)){Feedback="Enemy cap reached. Clear or defeat an enemy.";return null;}
        if(Settings.Archetypes.Length==0)return null;
        for(int i=0;i<24;i++)
        {
            float angle=(spawnSerial+i)*137.5f;
            var point=Quaternion.Euler(0,angle,0)*Vector3.forward*Settings.SpawnRadius;
            if(!NavMesh.SamplePosition(point,out var hit,1,NavMesh.AllAreas)||Vector3.Distance(hit.position,World.Hero.transform.position)<Settings.MinimumSpawnDistance)continue;
            if(!NavMesh.CalculatePath(hit.position,Vector3.zero,NavMesh.AllAreas,spawnPath)||spawnPath.status!=NavMeshPathStatus.PathComplete)continue;
            bool occupied=false;foreach(var npc in World.Npcs)if(npc!=null&&!npc.Dead&&Vector3.Distance(npc.transform.position,hit.position)<1.5f){occupied=true;break;}
            if(occupied)continue;
            var role=World.Progression.Data.Side==PlayerSide.Hero?NpcRole.Criminal:NpcRole.Cop;
            var actor=CityNpc.Spawn(World,hit.position,role,Settings.Archetypes[SelectedIndex]);
            if(actor==null)continue;
            actor.AlwaysAggro=true;Owned.Add(actor);spawnSerial+=i+1;
            Feedback="Spawned "+actor.Archetype.DisplayName;return actor;
        }
        Feedback="No clear connected spawn point; move away from the ring.";return null;
    }
    public void SpawnGroup(){for(int i=0;i<Mathf.Clamp(Settings.GroupSize,1,6);i++)if(SpawnOne()==null)break;}
    public void Clear()
    {
        foreach(var npc in Owned)if(npc!=null){npc.gameObject.SetActive(false);Destroy(npc.gameObject);}
        Owned.Clear();Feedback="Lab enemies cleared.";
    }
    public bool ChangeDifficulty(string id)
    {
        if(DifficultyDefinition.Find(id)==null)return false;
        // The normal API saves to null here, then a scene restart creates the usual immutable difficulty snapshot.
        World.Progression.SetDifficulty(id);StartingSnapshot.DifficultyId=id;ResetArena();return true;
    }
    public void ResetArena()
    {
        pendingSnapshot=JsonUtility.FromJson<ProgressSave>(JsonUtility.ToJson(StartingSnapshot));
        SceneManager.LoadScene(SceneName,LoadSceneMode.Single);
    }
    public void SetControls(bool open)
    {
        World.MenuOpen=open;Cursor.lockState=open?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=open;
    }
    public void Exit(){pendingSnapshot=null;GameFlow.Instance.Home();}
    void OnDestroy()
    {
        Clear();if(nav.valid)nav.Remove();if(data!=null)Destroy(data);
        if(Active==this)Active=null;
    }
}
