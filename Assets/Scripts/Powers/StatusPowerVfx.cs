using UnityEngine;
using UnityEngine.Rendering;

/// Fixed, session-owned presentation for live Darkness and Poison states.
/// Each state leases a prebuilt line group; no target allocates geometry or materials when afflicted.
public sealed class StatusPowerVfx : MonoBehaviour
{
    public const int RootCapacity = 8, RootLinesPerSlot = 4, PoisonCapacity = 32, PoisonPoints = 12;
    sealed class RootSlot
    {
        public CityNpc Owner;
        public Transform Pivot;
        public readonly LineRenderer[] Bands = new LineRenderer[RootLinesPerSlot];
    }
    sealed class PoisonSlot
    {
        public Poisoned Owner;
        public LineRenderer Ring;
        public float PulseUntil;
    }
    static StatusPowerVfx instance;
    public static StatusPowerVfx Existing => instance;
    readonly RootSlot[] roots = new RootSlot[RootCapacity];
    readonly PoisonSlot[] poisons = new PoisonSlot[PoisonCapacity];
    public int RootLeases { get; private set; }
    public int PoisonLeases { get; private set; }
    public int PoisonPulses { get; private set; }
    public int PoolMisses { get; private set; }
    public int ActiveRoots { get { int n=0;foreach(var s in roots)if(s.Owner!=null)n++;return n; } }
    public int ActivePoisons { get { int n=0;foreach(var s in poisons)if(s.Owner!=null)n++;return n; } }
    public static StatusPowerVfx Get()
    {
        if(instance!=null)return instance;
        var root=new GameObject("Pooled status power VFX");
        if(WorldSession.Instance!=null)root.transform.SetParent(WorldSession.Instance.transform,false);
        instance=root.AddComponent<StatusPowerVfx>();instance.Build();return instance;
    }
    LineRenderer Create(string name,Transform parent,CityColor swatch,int points,bool loop,float width)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=loop;
        line.positionCount=points;line.widthMultiplier=width;line.numCapVertices=2;
        line.sharedMaterial=CityMaterials.GetVfx(swatch);
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
        go.SetActive(false);return line;
    }
    void Build()
    {
        // Resolve all shared swatches during pool construction, never on a cast or damage tick.
        CityMaterials.GetVfx(CityColor.UiNavy);
        CityMaterials.GetVfx(CityColor.UiMuted);
        CityMaterials.GetVfx(CityColor.Leaf);
        for(int i=0;i<RootCapacity;i++)
        {
            var pivot=new GameObject("Shadow restraint "+i).transform;pivot.SetParent(transform,false);
            var slot=roots[i]=new RootSlot{Pivot=pivot};
            for(int b=0;b<RootLinesPerSlot;b++)
            {
                bool accent=b>=2;
                var line=slot.Bands[b]=Create("Shadow band "+i+"/"+b,pivot,
                    accent?CityColor.UiMuted:CityColor.UiNavy,9,false,accent?.018f:.065f);
                for(int p=0;p<9;p++)
                {
                    float t=p/8f;
                    float angle=(b%2)*Mathf.PI + t*Mathf.PI*2.2f;
                    float radius=Mathf.Lerp(.46f,.31f,t)+(accent?.012f:0);
                    line.SetPosition(p,new Vector3(Mathf.Cos(angle)*radius,.03f+t*1.32f,Mathf.Sin(angle)*radius));
                }
            }
        }
        for(int i=0;i<PoisonCapacity;i++)
        {
            var ring=Create("Toxic contamination "+i,transform,CityColor.Leaf,PoisonPoints,true,.028f);
            poisons[i]=new PoisonSlot{Ring=ring};
        }
    }
    public int AcquireRoot(CityNpc npc)
    {
        for(int i=0;i<RootCapacity;i++)if(roots[i].Owner==npc)return i;
        for(int i=0;i<RootCapacity;i++)if(roots[i].Owner==null)
        {
            var s=roots[i];s.Owner=npc;RootLeases++;
            foreach(var line in s.Bands)line.gameObject.SetActive(true);
            return i;
        }
        PoolMisses++;return -1;
    }
    public void ReleaseRoot(CityNpc npc,int slot)
    {
        if(slot<0||slot>=RootCapacity||roots[slot].Owner!=npc)return;
        var s=roots[slot];s.Owner=null;
        foreach(var line in s.Bands)line.gameObject.SetActive(false);
    }
    public int AcquirePoison(Poisoned status)
    {
        for(int i=0;i<PoisonCapacity;i++)if(poisons[i].Owner==status)return i;
        for(int i=0;i<PoisonCapacity;i++)if(poisons[i].Owner==null)
        {
            var s=poisons[i];s.Owner=status;PoisonLeases++;s.PulseUntil=Time.time+.22f;
            s.Ring.gameObject.SetActive(true);return i;
        }
        PoolMisses++;return -1;
    }
    public void PulsePoison(Poisoned status,int slot)
    {
        if(slot<0||slot>=PoisonCapacity||poisons[slot].Owner!=status)return;
        poisons[slot].PulseUntil=Time.time+.24f;PoisonPulses++;
    }
    public void ReleasePoison(Poisoned status,int slot)
    {
        if(slot<0||slot>=PoisonCapacity||poisons[slot].Owner!=status)return;
        var s=poisons[slot];s.Owner=null;s.Ring.gameObject.SetActive(false);
    }
    void LateUpdate()
    {
        for(int i=0;i<RootCapacity;i++)
        {
            var s=roots[i];if(s.Owner==null)continue;
            if(s.Owner.Dead||!s.Owner.Rooted){ReleaseRoot(s.Owner,i);continue;}
            s.Pivot.position=s.Owner.transform.position;
            s.Pivot.rotation=Quaternion.Euler(0,Time.time*40f,0);
        }
        for(int i=0;i<PoisonCapacity;i++)
        {
            var s=poisons[i];if(s.Owner==null)continue;
            if(!s.Owner.Active){ReleasePoison(s.Owner,i);continue;}
            float pulse=Mathf.Clamp01((s.PulseUntil-Time.time)/.24f);
            float radius=.31f+.13f*(1f-pulse)*pulse;
            Vector3 foot=s.Owner.transform.position;
            s.Ring.transform.position=foot+Vector3.up*.8f;
            s.Ring.widthMultiplier=.024f+.035f*pulse;
            for(int p=0;p<PoisonPoints;p++)
            {
                float a=p*Mathf.PI*2/PoisonPoints;
                float organic=1f+.13f*Mathf.Sin(a*3+Time.time*3);
                s.Ring.SetPosition(p,new Vector3(Mathf.Cos(a)*radius*organic,
                    .075f*Mathf.Sin(a*2+Time.time*2),Mathf.Sin(a)*radius*organic));
            }
        }
    }
    void OnDestroy(){if(instance==this)instance=null;}
}
