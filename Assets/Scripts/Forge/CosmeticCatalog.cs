using System;
using UnityEngine;

public enum CosmeticSlot { Suit, Mask, Emblem }

/// Appearance is deliberately independent of the reconstructed HeroLoadout and gameplay statistics.
[Serializable] public sealed class HeroAppearanceSave
{
    public int Version=1;
    public string HeroId, SuitId, MaskId, EmblemId;
    public CityColor Primary, Secondary, Accent;
    public HeroAppearanceSave Copy()=>(HeroAppearanceSave)MemberwiseClone();
}

[Serializable] public sealed class CosmeticDefinition
{
    public string Id, DisplayName, HeroId;
    public CosmeticSlot Slot;
    public string[] Suits=Array.Empty<string>();
    public bool Available;
    [TextArea] public string UnavailableReason;
    public Mesh Mesh;
    [Tooltip("Paths relative to the existing character model. Ordered to match Mesh.bindposes. No extra skeleton or Animator.")]
    public string[] Bones=Array.Empty<string>();
    public bool Compatible(HeroDefinition hero,string suit)=>hero!=null&&HeroId==hero.Id&&
        (Slot==CosmeticSlot.Suit||Suits==null||Suits.Length==0||Array.IndexOf(Suits,suit)>=0);
}

[Serializable] public sealed class CosmeticDefaults
{
    public string HeroId, SuitId, MaskId, EmblemId;
}

[CreateAssetMenu(menuName="Overpowered/Forge/Cosmetic Catalog")]
public sealed class CosmeticCatalog : ScriptableObject
{
    public CosmeticDefinition[] Items=Array.Empty<CosmeticDefinition>();
    public CosmeticDefaults[] Defaults=Array.Empty<CosmeticDefaults>();
    public CosmeticDefinition Find(string id,CosmeticSlot slot)=>Array.Find(Items,x=>x!=null&&x.Id==id&&x.Slot==slot);
    public CosmeticDefinition Resolve(HeroDefinition hero,string id,CosmeticSlot slot,string suit=null)
    {
        var item=Find(id,slot);
        if(item!=null&&item.Available&&item.Compatible(hero,suit))return item;
        var defaults=Array.Find(Defaults,x=>x!=null&&x.HeroId==hero.Id);
        string fallback=slot==CosmeticSlot.Suit?defaults?.SuitId:slot==CosmeticSlot.Mask?defaults?.MaskId:defaults?.EmblemId;
        item=Find(fallback,slot);
        if(item!=null&&item.Available&&item.Compatible(hero,suit))return item;
        return Array.Find(Items,x=>x!=null&&x.Slot==slot&&x.Available&&x.Compatible(hero,suit));
    }
    public HeroAppearanceSave Legacy(HeroDefinition hero,HeroLoadout loadout)
    {
        var d=Array.Find(Defaults,x=>x!=null&&x.HeroId==hero.Id);
        bool selected=loadout!=null&&loadout.HeroId==hero.Id;
        return new HeroAppearanceSave{HeroId=hero.Id,SuitId=d?.SuitId,MaskId=d?.MaskId,EmblemId=d?.EmblemId,
            Primary=selected?loadout.Primary:hero.Primary,Secondary=selected?loadout.Secondary:hero.Secondary,Accent=hero.Suit!=null?hero.Suit.Trim:CityColor.Metal};
    }
}
