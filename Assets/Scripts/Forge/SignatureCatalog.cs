using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Forge/Signature Catalog")]
public sealed class SignatureCatalog : ScriptableObject
{
    public PowerSynergyDefinition[] Abilities=Array.Empty<PowerSynergyDefinition>();
    [Tooltip("Opt-in only in Editor/development builds. Normal gameplay keeps the existing synergies.")]
    public bool DeveloperPreview;
    /// Runtime-only flag for automated Play Mode verification so tests never dirty the asset on disk.
    public static bool VerificationPreview;
    [Tooltip("Shipping activation additionally requires a complete, unique mapping for every primary.")]
    public bool EnableWhenComplete=true;
    public int ExpectedPrimaries=8;
    public bool Enabled => ((DeveloperPreview||VerificationPreview)&&(Application.isEditor||Debug.isDebugBuild))||(EnableWhenComplete&&Complete);
    public bool Complete
    {
        get
        {
            int count=0;
            foreach(var power in Resources.LoadAll<PowerDefinition>("Powers"))
            {
                if(power.Role!=PowerRole.Combat||!power.Selectable)continue;
                count++;int matches=0;
                foreach(var d in Abilities)if(Implemented(d)&&d.OwnerPrimary==power)matches++;
                if(matches!=1)return false;
            }
            return count==ExpectedPrimaries&&Abilities.Length==ExpectedPrimaries;
        }
    }
    public bool Implemented(PowerSynergyDefinition d)
    {
        if(d==null||string.IsNullOrWhiteSpace(d.Id)||d.OwnerPrimary==null||d.OwnerPrimary.Role!=PowerRole.Combat||!d.OwnerPrimary.Selectable||d.Effect==null)return false;
        foreach(var other in Abilities)if(other!=null&&other!=d&&(other.Id==d.Id||other.OwnerPrimary==d.OwnerPrimary))return false;
        return true;
    }
    public PowerSynergyDefinition ForPrimary(PowerDefinition primary)
        => primary==null?null:Array.Find(Abilities,d=>Implemented(d)&&d.OwnerPrimary==primary);
    public bool Eligible(PowerSynergyDefinition d,PowerDefinition a,PowerDefinition b)
        => a!=null&&b!=null&&a!=b&&Array.IndexOf(Abilities,d)>=0&&Implemented(d)&&(d.OwnerPrimary==a||d.OwnerPrimary==b);
    public List<PowerSynergyDefinition> Choices(PowerDefinition a,PowerDefinition b)
    {
        var list=new List<PowerSynergyDefinition>(2);
        if(a==null||b==null||a==b)return list;
        var first=ForPrimary(a);if(first!=null)list.Add(first);
        var second=ForPrimary(b);if(second!=null&&second!=first)list.Add(second);
        return list;
    }
    public PowerSynergyDefinition Resolve(string id,PowerDefinition a,PowerDefinition b)
        => string.IsNullOrEmpty(id)?null:Array.Find(Abilities,d=>d!=null&&d.Id==id&&Eligible(d,a,b));
    public PowerSynergyDefinition First(PowerDefinition a,PowerDefinition b)
        => a==null||b==null||a==b?null:ForPrimary(a)??ForPrimary(b);
}
