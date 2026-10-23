using System;
using UnityEngine;

/// A bounded visual-only assembler. Canonical fitting happens before this is installed; changing outfits never refits
/// the character or touches physics, animation, powers or stats. Shared baked meshes, one optional renderer per slot.
public sealed class HeroAppearanceRenderer : MonoBehaviour
{
    public string SuitId {get;private set;}
    public string MaskId {get;private set;}
    public string EmblemId {get;private set;}
    HeroDefinition hero;CosmeticCatalog catalog;CityMaterials materials;SkinnedMeshRenderer body;
    Mesh legacyMesh;Transform[] legacyBones;Material[] legacyMaterials;
    SkinnedMeshRenderer mask,emblem;bool preview;
    Texture2D map;Material previewMaterial;
    public Material ColorMaterial {get;private set;}
    public static HeroAppearanceRenderer Install(GameObject model,HeroDefinition hero,CityMaterials materials,bool preview)
    {
        var r=model.AddComponent<HeroAppearanceRenderer>();r.hero=hero;r.materials=materials;r.preview=preview;
        r.catalog=Resources.Load<CosmeticCatalog>("CosmeticCatalog");r.body=model.GetComponentInChildren<SkinnedMeshRenderer>(true);
        r.legacyMesh=r.body.sharedMesh;r.legacyBones=r.body.bones;r.legacyMaterials=r.body.sharedMaterials;
        return r;
    }
    Transform[] Bind(CosmeticDefinition item)
    {
        if(item.Mesh==null)return null;
        if(item.Bones==null||item.Bones.Length!=item.Mesh.bindposes.Length)throw new InvalidOperationException("Invalid cosmetic bind poses: "+item.Id);
        var bones=new Transform[item.Bones.Length];
        for(int i=0;i<bones.Length;i++)bones[i]=transform.Find(item.Bones[i])??throw new InvalidOperationException("Missing cosmetic bone: "+item.Bones[i]);
        return bones;
    }
    public void Apply(HeroAppearanceSave appearance)
    {
        var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");
        var primary=forge.ColorOrDefault(appearance.Primary,hero.Primary);var secondary=forge.ColorOrDefault(appearance.Secondary,hero.Secondary);
        var accent=forge.ColorOrDefault(appearance.Accent,hero.Suit.Trim);
        if(preview)
        {
            if(map==null){map=hero.Suit.Build(materials.Palette.Colors[(int)primary],materials.Palette.Colors[(int)secondary],materials.Palette.Colors[(int)accent]);previewMaterial=hero.Suit.CreateMaterial(map,$"Suit/{hero.Suit.name}/{primary}+{secondary}",materials.Palette.Smoothness);}
            else hero.Suit.Write(map,materials.Palette.Colors[(int)primary],materials.Palette.Colors[(int)secondary],materials.Palette.Colors[(int)accent]);
            previewMaterial.name=$"Suit/{hero.Suit.name}/{primary}+{secondary}"+(accent==hero.Suit.Trim?"":"+"+accent);
            if(!hero.Suit.AuthoredShader)materials.Surface(previewMaterial,Color.white,map,true);
            ColorMaterial=previewMaterial;
        }
        else ColorMaterial=materials.SuitMaterial(hero.Suit,primary,secondary,accent);
        var suit=catalog!=null?catalog.Resolve(hero,appearance.SuitId,CosmeticSlot.Suit):null;
        string resolvedSuit=suit?.Id;
        if(SuitId!=resolvedSuit||body.sharedMesh==null)
        {
            body.sharedMesh=suit?.Mesh!=null?suit.Mesh:legacyMesh;body.bones=suit?.Mesh!=null?Bind(suit):legacyBones;
        }
        SuitId=resolvedSuit;
        var mats=(Material[])legacyMaterials.Clone();for(int i=0;i<mats.Length;i++)if(mats[i]==hero.Suit.Source)mats[i]=ColorMaterial;
        body.sharedMaterials=mats;
        var resolvedMask=catalog!=null?catalog.Resolve(hero,appearance.MaskId,CosmeticSlot.Mask,SuitId):null;
        var resolvedEmblem=catalog!=null?catalog.Resolve(hero,appearance.EmblemId,CosmeticSlot.Emblem,SuitId):null;
        ApplySlot(ref mask,resolvedMask,MaskId);MaskId=resolvedMask?.Id;
        ApplySlot(ref emblem,resolvedEmblem,EmblemId);EmblemId=resolvedEmblem?.Id;
    }
    void ApplySlot(ref SkinnedMeshRenderer renderer,CosmeticDefinition item,string previous)
    {
        if(item?.Mesh==null){if(renderer!=null)renderer.gameObject.SetActive(false);return;}
        if(renderer==null)
        {
            var go=new GameObject("Cosmetic "+item.Slot);go.transform.SetParent(body.transform.parent,false);
            go.transform.localPosition=body.transform.localPosition;go.transform.localRotation=body.transform.localRotation;go.transform.localScale=body.transform.localScale;
            go.layer=body.gameObject.layer;renderer=go.AddComponent<SkinnedMeshRenderer>();renderer.rootBone=body.rootBone;
            renderer.localBounds=body.localBounds;renderer.updateWhenOffscreen=body.updateWhenOffscreen;
        }
        renderer.gameObject.SetActive(true);
        if(item.Id!=previous){renderer.sharedMesh=item.Mesh;renderer.bones=Bind(item);}
        renderer.sharedMaterial=ColorMaterial;
    }
    void OnDestroy(){if(previewMaterial!=null)Destroy(previewMaterial);if(map!=null)Destroy(map);}
}
