using UnityEngine;
public sealed partial class PowerUser
{
    PowerExpansionRuntime expansion;
    public void EndExpansionTraversal(){if(expansion!=null)expansion.ReleaseGrapple(false);}
    public string ExpansionStatus=>expansion!=null?expansion.Status:"";
    public bool ExpansionDrivesMotion=>expansion!=null&&expansion.DrivesMotion;
    public PowerExpansionRuntime Expansion
    {
        get { if(expansion==null){expansion=gameObject.AddComponent<PowerExpansionRuntime>();expansion.Initialize(this);}return expansion; }
    }
}
