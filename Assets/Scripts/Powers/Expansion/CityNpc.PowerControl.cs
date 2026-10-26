using UnityEngine;
using UnityEngine.AI;
public sealed partial class CityNpc
{
    float nextPowerInterrupt;
    public int PowerInterrupts{get;private set;}
    public float PowerDisplacementFactor=>IsBoss? .12f:HasGuard? .3f:Archetype!=null&&Archetype.Kind==AttackKind.Slam? .4f:1f;
    public bool TryPowerInterrupt(float duration,float refractory)
    {
        if(Dead||Time.time<nextPowerInterrupt)return false;
        duration=Mathf.Clamp(duration,.05f,IsBoss? .15f:.4f);
        nextPowerInterrupt=Time.time+Mathf.Max(duration+.3f,refractory);
        CancelAttack();Root(duration);PowerInterrupts++;return true;
    }
}
