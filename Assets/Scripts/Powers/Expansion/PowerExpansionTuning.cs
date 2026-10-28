using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Power Expansion/Tuning")]
public sealed class PowerExpansionTuning : ScriptableObject
{
    [Range(1,16)]public int MaxTargets=8;
    public float WindHalfAngle=24,WindDistance=3,WindSeconds=.3f,WindInterrupt=.1f;
    public float SonicHalfAngle=38,SonicStagger=.28f,InterruptRefractory=1.2f,BossInterruptRefractory=6;
    public KeyCode MorphFormKey=KeyCode.V,PlasmaChargeKey=KeyCode.G;
}
