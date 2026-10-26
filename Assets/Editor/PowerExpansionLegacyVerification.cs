#if UNITY_EDITOR
using System.Linq;
using UnityEngine;
public static class PowerExpansionLegacyVerification
{
    public static void RetainedResources()
    {
        var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");
        foreach(string id in new[]{"strength","laser-eyes","flight"})
        {
            var power=Resources.Load<PowerDefinition>("Powers/"+id);power.Selectable=true;
            foreach(var hero in forge.Heroes)if(!hero.AvailablePowers.Contains(power))hero.AvailablePowers=hero.AvailablePowers.Concat(new[]{power}).ToArray();
        }
        Debug.Log("RETAINED-FIXTURE ONLY: retired resource selection temporarily enabled in memory to test unchanged legacy animation/camera behavior. Shipping roster tested separately.");
    }
}
#endif
