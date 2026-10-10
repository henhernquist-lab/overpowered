using UnityEngine;

/// <summary>A short blink that keeps the existing CharacterController and its collision rules.</summary>
[CreateAssetMenu(menuName = "Overpowered/Effects/Teleport")]
public sealed class TeleportEffect : PowerEffect
{
    static readonly Vector3[] pop=new Vector3[9];
    public override bool Execute(PowerUser user, PowerRuntime power)
    {
        Vector3 departure=user.Hero!=null?user.Hero.transform.position:Vector3.zero;
        if (user.Hero != null && user.Hero.TryTeleport(user.Stats(power).Range))
        {
            // Two local pops mark the instantaneous disappearance and arrival. No streak connects the endpoints.
            Pop(departure+Vector3.up*.55f,power.Definition.PaletteColor,.55f);
            Pop(user.Hero.transform.position+Vector3.up*.55f,power.Definition.PaletteColor,.8f);
            FeelDirector.Instance?.Particles.Burst(departure+Vector3.up*.25f,power.Definition.PaletteColor,7);
            FeelDirector.Instance?.Particles.Burst(user.Hero.transform.position+Vector3.up*.25f,power.Definition.PaletteColor,7);
            return true;
        }
        user.Message = "Teleport needs a clear path and landing";
        return false;
    }
    static void Pop(Vector3 center,CityColor color,float radius)
    {
        // A sharp diamond in a vertical plane remains legible at range; the first-person eye stays above it.
        for(int i=0;i<9;i++)
        {
            float a=i*Mathf.PI*.25f;
            pop[i]=center+new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,0);
        }
        var line=PowerVfx.Get().Line(pop,pop.Length,color,.045f,.16f);
        if(line!=null)line.sharedMaterial=CityMaterials.GetVfx(color);
    }
}
