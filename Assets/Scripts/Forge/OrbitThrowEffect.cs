using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Effects/Orbit throw")]
public sealed class OrbitThrowEffect : SynergyEffect
{
    public bool Ignite;
    public override bool Repeat(SynergyRunner r){r.RequestRelease();return true;}
    public override bool CanBegin(SynergyRunner r)=>r.GatherBodies()>0;
    public override IEnumerator Execute(SynergyRunner r)
    {
        r.HoldGathered();
        float elapsed=0,nextFx=0;
        while(elapsed<r.Definition.Duration&&!r.ReleaseRequested)
        {
            r.Orbit(elapsed);
            if(elapsed>=nextFx){r.OrbitVfx();nextFx=elapsed+.2f;}
            elapsed+=Time.fixedDeltaTime;yield return SynergyRunner.FixedStep;
        }
        for(int i=0;i<r.HeldCount;i++)
        {
            r.LaunchHeld(i,Ignite);yield return new WaitForSeconds(r.Definition.LaunchInterval);
        }
        r.ReleaseHeld();
    }
}
