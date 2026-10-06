using UnityEngine;
using UnityEngine.Rendering;

/// Session-owned Fire Blast projectiles and contact flashes. The spherical collider is gameplay;
/// every visible mesh, renderer and impact line is created once and reused for the session.
public sealed class FireProjectilePool:MonoBehaviour
{
    public const int Capacity=12,ImpactCapacity=4;
    PowerProjectile[] shots;FireImpactRing[] impacts;Mesh bolt,tail;int impactCursor;
    public static FireProjectilePool Instance{get;private set;}
    public int Recycled{get;set;}
    public int ActiveCount {get {int n=0;if(shots!=null)foreach(var shot in shots)if(shot.gameObject.activeSelf)n++;return n;}}
    public int ImpactCount{get;private set;}
    public int ActiveImpacts {get {int n=0;if(impacts!=null)foreach(var impact in impacts)if(impact.gameObject.activeSelf)n++;return n;}}
    public PowerProjectile Slot(int i)=>shots[i];
    public static FireProjectilePool Create(WorldSession world)
    {
        if(Instance!=null)return Instance;
        var root=new GameObject("Pooled Fire Blast projectiles");root.transform.SetParent(world.transform,false);
        var pool=root.AddComponent<FireProjectilePool>();pool.Build();return pool;
    }
    void Build()
    {
        Instance=this;
        bolt=Shape("Fire compressed bolt",new[]{-.42f,-.30f,-.08f,.12f,.34f,.44f},new[]{0f,.065f,.17f,.15f,.075f,0f});
        tail=Shape("Fire short taper",new[]{-.68f,-.49f,-.28f,-.08f,.06f},new[]{0f,.035f,.10f,.09f,0f});
        var warm=CityMaterials.Get(CityColor.Fire);
        var impactWarm=CityMaterials.GetVfx(CityColor.Fire);
        var hot=CityMaterials.GetVfx(CityColor.Cream);
        var ember=CityMaterials.GetVfx(CityColor.Red);
        shots=new PowerProjectile[Capacity];
        for(int i=0;i<shots.Length;i++)
        {
            var root=new GameObject("Pooled Fire projectile "+i);root.transform.SetParent(transform,false);
            root.SetActive(false);
            var sphere=root.AddComponent<SphereCollider>();sphere.radius=.175f;
            var body=root.AddComponent<Rigidbody>();body.useGravity=false;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var visual=new GameObject("Directional fire bolt");visual.transform.SetParent(root.transform,false);
            var presentation=visual.AddComponent<FireProjectileVisual>();
            presentation.Build(bolt,tail,warm,hot,ember);
            var projectile=root.AddComponent<PowerProjectile>();projectile.Bind(this,presentation);
            shots[i]=projectile;
        }
        impacts=new FireImpactRing[ImpactCapacity];
        for(int i=0;i<impacts.Length;i++)
        {
            var root=new GameObject("Pooled Fire impact "+i);root.transform.SetParent(transform,false);
            var ring=root.AddComponent<FireImpactRing>();ring.Build(hot,impactWarm);root.SetActive(false);impacts[i]=ring;
        }
    }
    public bool Launch(PowerUser user,PowerRuntime power,Vector3 position,Vector3 direction)
    {
        foreach(var shot in shots)if(!shot.gameObject.activeSelf)
        {
            shot.transform.SetPositionAndRotation(position,Quaternion.LookRotation(direction));
            shot.Visual.gameObject.SetActive(true);shot.gameObject.SetActive(true);
            shot.Initialize(user,power,direction);
            Physics.IgnoreCollision(shot.GetComponent<SphereCollider>(),user.GetComponent<CharacterController>());
            var camera=Camera.main;
            // The close-wall first-person spawn is inside a metre of the eye; its point light would wash out the whole wall.
            if(camera==null||Vector3.Distance(camera.transform.position,position)>=1f)
                WorldLightingDirector.Instance?.FollowPower(power.Definition.Light,shot.transform);
            return true;
        }
        Debug.LogWarning("Fire projectile pool capacity reached; cast refused before resource payment.");
        return false;
    }
    public void Impact(Vector3 position,Vector3 normal,PowerLightSpec light)
    {
        var slot=impacts[impactCursor];impactCursor=(impactCursor+1)%impacts.Length;
        slot.Show(position,normal);ImpactCount++;
        var camera=Camera.main;float cameraDistance=camera!=null?Vector3.Distance(camera.transform.position,position):float.PositiveInfinity;
        FeelDirector.Instance?.Particles?.Burst(position,CityColor.Fire,cameraDistance<2f?2:5);
        // A near-eye wall hit already has the projectile light; a second full-strength flash washes out the view.
        if(cameraDistance>=2f)WorldLightingDirector.Instance?.FlashPower(light,position);
    }
    static Mesh Shape(string name,float[] z,float[] radius)
    {
        const int sides=8;var vertices=new Vector3[z.Length*sides];var triangles=new int[(z.Length-1)*sides*6];
        for(int i=0;i<z.Length;i++)for(int j=0;j<sides;j++)
        {float a=j*Mathf.PI*2f/sides;vertices[i*sides+j]=new Vector3(Mathf.Cos(a)*radius[i],Mathf.Sin(a)*radius[i],z[i]);}
        int t=0;for(int i=0;i<z.Length-1;i++)for(int j=0;j<sides;j++)
        {
            int a=i*sides+j,b=i*sides+(j+1)%sides,c=(i+1)*sides+j,d=(i+1)*sides+(j+1)%sides;
            triangles[t++]=a;triangles[t++]=b;triangles[t++]=c;
            triangles[t++]=b;triangles[t++]=d;triangles[t++]=c;
        }
        var mesh=new Mesh{name=name};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    void OnDestroy()
    {
        if(Instance==this)Instance=null;
        if(bolt!=null)Destroy(bolt);if(tail!=null)Destroy(tail);
    }
}

public sealed class FireProjectileVisual:MonoBehaviour
{
    float size=1f;bool firstPerson;
    public MeshRenderer Body{get;private set;}
    public MeshRenderer Core{get;private set;}
    public MeshRenderer Taper{get;private set;}
    static MeshRenderer Layer(string name,Transform parent,Mesh mesh,Material material,Vector3 position,Vector3 scale)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return renderer;
    }
    public void Build(Mesh bolt,Mesh tail,Material body,Material core,Material ember)
    {
        Taper=Layer("Red tapered wake",transform,tail,ember,new Vector3(0,0,-.24f),Vector3.one);
        Body=Layer("Warm flame body",transform,bolt,body,Vector3.zero,Vector3.one);
        Core=Layer("Hot leading core",transform,bolt,core,new Vector3(0,0,.34f),Vector3.one*.46f);
    }
    public void SetSize(float diameter){size=diameter/.35f;transform.localScale=Vector3.one*size;}
    public void SetFirstPerson(bool value){firstPerson=value;transform.localPosition=value?new Vector3(.22f,-.15f,-.08f):Vector3.zero;}
    public void Present(float age,float clearance)
    {
        float t=Mathf.Clamp01(age/.18f);
        transform.localScale=Vector3.one*(size*clearance*(firstPerson?Mathf.Lerp(.6f,1f,t):1f));
        if(firstPerson)transform.localPosition=Vector3.Lerp(new Vector3(.22f,-.15f,-.08f),Vector3.zero,t);
    }
}

public sealed class FireImpactRing:MonoBehaviour
{
    LineRenderer ring,flashH,flashV;float began,viewScale;Vector3 right,up;
    public Vector3 Contact{get;private set;}
    public int RingsShown{get;private set;}
    public void Build(Material bright,Material flame)
    {
        ring=Line("Expanding fire ring",13,bright);
        flashH=Line("Fire contact flash horizontal",2,flame);
        flashV=Line("Fire contact flash vertical",2,flame);
    }
    LineRenderer Line(string name,int points,Material material)
    {
        var go=new GameObject(name);go.transform.SetParent(transform,false);
        var line=go.AddComponent<LineRenderer>();line.useWorldSpace=true;line.positionCount=points;line.loop=points==13;
        line.numCapVertices=2;line.sharedMaterial=material;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
        return line;
    }
    public void Show(Vector3 contact,Vector3 normal)
    {
        Contact=contact;transform.position=contact;began=Time.time;RingsShown++;
        var camera=Camera.main;viewScale=camera!=null?Mathf.Clamp(Vector3.Distance(camera.transform.position,contact)/2f,.25f,1f):1f;
        normal=normal.sqrMagnitude>.01f?normal.normalized:Vector3.up;
        right=Vector3.Cross(normal,Mathf.Abs(normal.y)>.8f?Vector3.forward:Vector3.up).normalized;
        up=Vector3.Cross(normal,right).normalized;
        gameObject.SetActive(true);Draw(0);
    }
    void Update()
    {
        float t=(Time.time-began)/.24f;
        if(t>=1f){gameObject.SetActive(false);return;}
        Draw(t);
    }
    void Draw(float t)
    {
        float radius=Mathf.Lerp(.12f,.68f,t)*viewScale;
        ring.widthMultiplier=Mathf.Lerp(.075f,.012f,t)*viewScale;
        for(int i=0;i<13;i++)
        {float a=i*Mathf.PI*2f/12;ring.SetPosition(i,Contact+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*radius);}
        flashH.widthMultiplier=flashV.widthMultiplier=Mathf.Lerp(.10f,0f,Mathf.Clamp01(t*3f))*viewScale;
        float arm=Mathf.Lerp(.23f,.38f,t)*viewScale;
        flashH.SetPosition(0,Contact-right*arm);flashH.SetPosition(1,Contact+right*arm);
        flashV.SetPosition(0,Contact-up*arm);flashV.SetPosition(1,Contact+up*arm);
    }
}
