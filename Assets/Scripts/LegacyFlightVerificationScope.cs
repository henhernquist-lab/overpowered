#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

/// Retained Flight/Sonic Slam regression fixture only. Never called by shipping gameplay or setup.
/// Temporarily exposes Flight in the IN-MEMORY hero roster, restores exact original arrays on dispose.
/// Existing equip, ownership and resource gates remain in force. This is not selectable-content evidence.
public sealed class LegacyFlightVerificationScope : IDisposable
{
    readonly HeroDefinition[] heroes;
    readonly PowerDefinition[][] original;
    public LegacyFlightVerificationScope()
    {
        heroes=Resources.Load<ForgeCatalog>("ForgeCatalog").Heroes;
        original=heroes.Select(h=>h.AvailablePowers).ToArray();
        var flight=Resources.Load<PowerDefinition>("Powers/flight");
        for(int i=0;i<heroes.Length;i++)
            if(!heroes[i].AvailablePowers.Contains(flight))heroes[i].AvailablePowers=heroes[i].AvailablePowers.Concat(new[]{flight}).ToArray();
        Debug.Log("LEGACY FIXTURE: temporarily expose retained Flight in memory to test its original gated runtime; not shipping roster evidence.");
    }
    public void Dispose(){for(int i=0;i<heroes.Length;i++)if(heroes[i]!=null)heroes[i].AvailablePowers=original[i];}
}
#endif
