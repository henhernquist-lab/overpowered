#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class TitanCosmeticIntegration
{
    public static void Validate()
    {
        var hero=Resources.Load<ForgeCatalog>("ForgeCatalog").Hero("titan");var module=Resources.Load<CosmeticCatalog>(TitanCosmeticContent.ResourcePath);
        if(module==null)throw new Exception("Missing TITAN module");
        if(hero.CharacterPrefab.GetComponentsInChildren<Animator>(true).Length!=1)throw new Exception("TITAN must keep one Animator");
        foreach(var item in module.Items.Where(x=>x.Available&&x.Mesh!=null))
        {
            var mesh=item.Mesh;if(mesh.vertexCount==0||mesh.bindposes.Length!=item.Bones.Length||item.Bones.Length>hero.Suit.BodyBones.Length)throw new Exception("Invalid mesh/bone budget: "+item.Id);
            foreach(var bone in item.Bones)if(hero.CharacterPrefab.transform.Find(bone)==null)throw new Exception("Missing TITAN bone "+bone);
            var weights=mesh.boneWeights;
            if(weights.Length!=mesh.vertexCount||weights.Any(w=>w.weight0+w.weight1+w.weight2+w.weight3<.99f))throw new Exception("Invalid skin weights "+item.Id);
        }
    }
    [MenuItem("Overpowered/Forge/Titan/Register content after integration")]
    public static void Register()
    {
        Validate();var target=Resources.Load<CosmeticCatalog>("CosmeticCatalog");target.Items=TitanCosmeticContent.Combined(target);EditorUtility.SetDirty(target);AssetDatabase.SaveAssets();
        Debug.Log("TITAN content registered in the existing cosmetic catalog; legacy defaults and other heroes retained.");
    }
}
#endif
