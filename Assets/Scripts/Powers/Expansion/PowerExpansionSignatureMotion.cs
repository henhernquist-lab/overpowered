using UnityEngine;
public sealed partial class PowerExpansionRuntime
{
    public Vector3 AdvanceEffect(Vector3 origin,Vector3 step,float radius)
    {
        if(step.sqrMagnitude<.000001f)return origin;
        int count=Physics.SphereCastNonAlloc(origin,radius,step.normalized,rays,step.magnitude+.05f,~0,QueryTriggerInteraction.Ignore);float distance=step.magnitude;
        if(count>=rays.Length)return origin;
        for(int i=0;i<count;i++)if(rays[i].transform.root!=transform&&rays[i].collider.GetComponentInParent<CityNpc>()==null)distance=Mathf.Min(distance,Mathf.Max(0,rays[i].distance-.05f));
        return origin+step.normalized*distance;
    }
}
