using System;
using System.Linq;
using UnityEngine;

/// TITAN content adapter only. Uses the existing catalog, renderer, UI and per-hero progression APIs.
/// Registration is explicit; importing this branch does not mutate the shared catalog or install a second manager.
public static class TitanCosmeticContent
{
    public const string ResourcePath="Forge/Cosmetics/Titan/TitanCosmetics";
    public static CosmeticDefinition[] Combined(CosmeticCatalog target)
    {
        var module=Resources.Load<CosmeticCatalog>(ResourcePath);
        if(target==null||module==null)throw new InvalidOperationException("TITAN cosmetic catalog is missing.");
        var items=module.Items;
        if(items==null||items.Length==0||items.Any(x=>x==null||x.HeroId!="titan"||string.IsNullOrEmpty(x.Id)||!x.Id.StartsWith("titan_"))||items.Select(x=>x.Id).Distinct().Count()!=items.Length)
            throw new InvalidOperationException("Invalid or duplicate TITAN content identifiers.");
        foreach(var item in items)
            if(target.Items.Any(x=>x!=null&&x.Id==item.Id&&x.HeroId!="titan"))throw new InvalidOperationException("Foreign cosmetic ID collision: "+item.Id);
        return target.Items.Where(x=>x==null||!items.Any(n=>n.Id==x.Id)).Concat(items).ToArray();
    }
    /// Tests temporarily register the module, then restore the actual asset's original array before exit.
    public static IDisposable RegisterForVerification()
    {
        var target=Resources.Load<CosmeticCatalog>("CosmeticCatalog");var original=target.Items;target.Items=Combined(target);return new Restore(target,original);
    }
    sealed class Restore : IDisposable
    {
        CosmeticCatalog target;readonly CosmeticDefinition[] original;
        public Restore(CosmeticCatalog target,CosmeticDefinition[] original){this.target=target;this.original=original;}
        public void Dispose(){if(target!=null){target.Items=original;target=null;}}
    }
}
