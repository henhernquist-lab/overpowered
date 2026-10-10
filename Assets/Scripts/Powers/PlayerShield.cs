using UnityEngine;
using UnityEngine.Rendering;

/// Finite capacity Force Field. Damage math and WorldSession route are unchanged.
/// One session-owned shell, hit ripple and first-person edge set are built on first raise and reused.
[DefaultExecutionOrder(50)]
public sealed class PlayerShield : MonoBehaviour
{
    public const int RingCount=5, RingPoints=40;
    public float Capacity { get; private set; }
    public float Remaining { get; private set; }
    public float Until { get; private set; }
    public float AbsorbFraction { get; private set; }=1f;
    public float TotalAbsorbed { get; private set; }
    public int HitsAbsorbed { get; private set; }
    public int Raised { get; private set; }
    public int HitRipples { get; private set; }
    public int BreakPops { get; private set; }
    public int SoftExpiries { get; private set; }
    public Vector3 LastHitDirection { get; private set; }
    public string LastEnd { get; private set; }="";
    public bool Up => raised&&Remaining>0f&&Time.time<Until;
    public bool RingsVisible => raised&&rings[0]!=null&&rings[0].gameObject.activeSelf;
    public bool HitRippleVisible => ripple!=null&&ripple.gameObject.activeSelf;
    public bool FirstPersonEdgesVisible => edges[0]!=null&&edges[0].gameObject.activeSelf;
    public event System.Action<bool> Ended;
    readonly LineRenderer[] rings=new LineRenderer[RingCount];
    readonly LineRenderer[] edges=new LineRenderer[2];
    Transform pivot;LineRenderer ripple;
    CityColor color;bool raised,hasHitOrigin;Vector3 hitOrigin;
    float radius=1.1f,rippleUntil,rippleStart,fadeUntil;
    public void Raise(float capacity,float seconds,float absorbFraction,float ringRadius,CityColor ringColor)
    {
        Capacity=Remaining=Mathf.Max(0,capacity);Until=Time.time+Mathf.Max(0,seconds);
        AbsorbFraction=Mathf.Clamp01(absorbFraction);radius=ringRadius;color=ringColor;
        raised=true;Raised++;fadeUntil=0;hasHitOrigin=false;
        if(pivot==null)Build();
        var material=CityMaterials.GetVfx(color);
        for(int i=0;i<rings.Length;i++)
        {
            rings[i].sharedMaterial=material;rings[i].widthMultiplier=i<3?.043f:.025f;
            rings[i].gameObject.SetActive(true);
        }
        pivot.localScale=Vector3.one*radius;
        FeelDirector.Instance?.Particles.Burst(transform.position+Vector3.up,color,10);
    }
    /// Called only by a real enemy release that intersected the player.
    public void PrimeHitFrom(Vector3 worldPoint){hitOrigin=worldPoint;hasHitOrigin=true;}
    /// Returns the portion of damage that reaches health. Visual response uses the amount really absorbed.
    public float Absorb(float damage)
    {
        if(!Up||damage<=0f){hasHitOrigin=false;return damage;}
        float taken=Mathf.Min(Remaining,damage*AbsorbFraction);
        Remaining-=taken;TotalAbsorbed+=taken;HitsAbsorbed++;
        if(taken>0f)
        {
            Vector3 direction=(hasHitOrigin?hitOrigin-transform.position:transform.forward);
            direction.y=0f;if(direction.sqrMagnitude<.001f)direction=transform.forward;
            LastHitDirection=direction.normalized;ShowRipple(false);
            FeelDirector.Instance?.Particles.Burst(transform.position+Vector3.up+LastHitDirection*radius*.7f,color,6);
        }
        hasHitOrigin=false;
        if(Remaining<=0f)Drop(true);
        return damage-taken;
    }
    public void Drop(bool broken)
    {
        if(!raised)return;
        raised=false;Remaining=0f;hasHitOrigin=false;LastEnd=broken?"broken":"expired";
        if(broken)
        {
            BreakPops++;
            for(int i=0;i<rings.Length;i++)rings[i].gameObject.SetActive(false);
            ShowRipple(true);
            FeelDirector.Instance?.Particles.Burst(transform.position+Vector3.up,color,14);
        }
        else{SoftExpiries++;fadeUntil=Time.time+.24f;}
        Ended?.Invoke(broken);
    }
    LineRenderer Line(string name,Transform parent,CityColor swatch,int count,bool loop,float width)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;
        line.positionCount=count;line.loop=loop;line.widthMultiplier=width;
        line.sharedMaterial=CityMaterials.GetVfx(swatch);
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
        go.SetActive(false);return line;
    }
    void Build()
    {
        CityMaterials.GetVfx(CityColor.Cream);
        pivot=new GameObject("Shield shell").transform;pivot.SetParent(transform,false);pivot.localPosition=Vector3.up*.95f;
        for(int i=0;i<RingCount;i++)
        {
            var line=rings[i]=Line("Shield great circle "+i,pivot,color,RingPoints,true,i<3?.043f:.025f);
            if(i==1)line.transform.localRotation=Quaternion.Euler(90,0,0);
            else if(i==2)line.transform.localRotation=Quaternion.Euler(0,0,90);
            else if(i>=3)line.transform.localPosition=Vector3.up*(i==3?.47f:-.47f);
            float scale=i>=3?.9f:1f;
            for(int n=0;n<RingPoints;n++)
            {
                float a=n*Mathf.PI*2/RingPoints;
                line.SetPosition(n,new Vector3(Mathf.Cos(a)*scale,0,Mathf.Sin(a)*scale));
            }
        }
        ripple=Line("Shield local hit ripple",transform,CityColor.Cream,RingPoints,true,.07f);
        for(int i=0;i<RingPoints;i++)
        {
            float a=i*Mathf.PI*2/RingPoints;
            ripple.SetPosition(i,new Vector3(Mathf.Cos(a),Mathf.Sin(a),0));
        }
        for(int i=0;i<2;i++)edges[i]=Line("Shield peripheral edge "+i,transform,color,3,false,.003f);
    }
    void ShowRipple(bool breaking)
    {
        if(ripple==null)return;
        ripple.sharedMaterial=CityMaterials.GetVfx(breaking?color:CityColor.Cream);
        if(LastHitDirection.sqrMagnitude<.001f)LastHitDirection=transform.forward;
        ripple.transform.position=transform.position+Vector3.up*.95f+LastHitDirection*radius*.75f;
        ripple.transform.rotation=Quaternion.LookRotation(LastHitDirection);
        rippleStart=Time.time;rippleUntil=Time.time+(breaking?.38f:.22f);
        ripple.widthMultiplier=breaking?.13f:.075f;ripple.gameObject.SetActive(true);
        if(!breaking)HitRipples++;
    }
    void Update()
    {
        if(raised&&!Up)Drop(Remaining<=0f);
        if(ripple!=null&&ripple.gameObject.activeSelf)
        {
            float duration=LastEnd=="broken"&&!raised?.38f:.22f;
            float t=Mathf.Clamp01((Time.time-rippleStart)/duration);
            ripple.transform.localScale=Vector3.one*Mathf.Lerp(.18f,LastEnd=="broken"&&!raised?1.3f:.55f,t);
            if(Time.time>=rippleUntil)ripple.gameObject.SetActive(false);
        }
        if(fadeUntil>0)
        {
            float left=Mathf.Clamp01((fadeUntil-Time.time)/.24f);
            if(left<=0){fadeUntil=0;foreach(var r in rings)r.gameObject.SetActive(false);}
            else for(int i=0;i<rings.Length;i++)rings[i].widthMultiplier=(i<3?.043f:.025f)*left;
        }
        if(raised&&pivot!=null)
        {
            // Capacity is communicated by line weight; no shared material property is modified.
            float strength=.5f+.5f*Mathf.Clamp01(Remaining/Mathf.Max(.01f,Capacity));
            for(int i=0;i<rings.Length;i++)rings[i].widthMultiplier=(i<3?.043f:.025f)*strength;
        }
    }
    void LateUpdate()
    {
        if(pivot==null)return;
        var camera=Camera.main;var rig=camera!=null?camera.GetComponent<ThirdPersonCamera>():null;
        bool first=rig!=null&&rig.isActiveAndEnabled&&rig.FirstPerson;
        bool shell=raised||fadeUntil>0;
        bool showShell=shell&&!first;
        for(int i=0;i<rings.Length;i++)if(rings[i].gameObject.activeSelf!=showShell)rings[i].gameObject.SetActive(showShell);
        if(ripple!=null&&first)ripple.enabled=false;else if(ripple!=null)ripple.enabled=true;
        for(int i=0;i<edges.Length;i++)
        {
            var line=edges[i];bool showEdge=(shell||Time.time<rippleUntil)&&first;
            if(line.gameObject.activeSelf!=showEdge)line.gameObject.SetActive(showEdge);
            if(!showEdge)continue;
            line.useWorldSpace=true;
            bool impact=Time.time<rippleUntil&&
                (i==0?Vector3.Dot(LastHitDirection,camera.transform.right)<0:Vector3.Dot(LastHitDirection,camera.transform.right)>=0);
            line.sharedMaterial=CityMaterials.GetVfx(impact?CityColor.Cream:color);
            float x=i==0?.02f:.98f;
            line.SetPosition(0,camera.ViewportToWorldPoint(new Vector3(x,.35f,.35f)));
            line.SetPosition(1,camera.ViewportToWorldPoint(new Vector3(x+(i==0?-.01f:.01f),.5f,.35f)));
            line.SetPosition(2,camera.ViewportToWorldPoint(new Vector3(x,.65f,.35f)));
            float strength=raised?(.5f+.5f*Mathf.Clamp01(Remaining/Mathf.Max(.01f,Capacity))):
                Mathf.Max(Mathf.Clamp01((fadeUntil-Time.time)/.24f),Mathf.Clamp01((rippleUntil-Time.time)/.38f));
            line.widthMultiplier=.003f*strength+(impact?.007f*Mathf.Clamp01((rippleUntil-Time.time)/.22f):0f);
        }
    }
    void OnDisable()
    {
        raised=false;fadeUntil=0;
        foreach(var r in rings)if(r!=null)r.gameObject.SetActive(false);
        foreach(var e in edges)if(e!=null)e.gameObject.SetActive(false);
        if(ripple!=null)ripple.gameObject.SetActive(false);
    }
}
