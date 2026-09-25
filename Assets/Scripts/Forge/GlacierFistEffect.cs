using System.Collections;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Effects/Melee buff")]
public sealed class GlacierFistEffect : SynergyEffect
{
    public override bool CanBegin(SynergyRunner r)=>true;
    public override IEnumerator Execute(SynergyRunner r){r.EnableGlacier();yield break;}
}
