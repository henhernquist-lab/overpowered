using UnityEngine;
public sealed partial class PowerUser
{
    PowerExpansionRuntime expansion;
    public PowerExpansionRuntime Expansion
    {
        get { if(expansion==null){expansion=gameObject.AddComponent<PowerExpansionRuntime>();expansion.Initialize(this);}return expansion; }
    }
}
