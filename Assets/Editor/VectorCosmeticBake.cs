#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// Extracts authored part ranges from the already fitted, optimized VECTOR mesh. No vendor/database edits, re-rigging,
/// runtime mesh combination or body-shape approximation. Refuses a recipe/order change instead of guessing boundaries.
public static class VectorCosmeticBake
{
    const string Folder="Assets/Resources/Forge/Cosmetics";
    sealed class Part {public string Name;public int Start,Count;}
    static string BonePath(Transform bone,Transform model)
    {
        string path=bone.name;while(bone.parent!=model){bone=bone.parent;if(bone==null)throw new Exception("Bone outside character");path=bone.name+"/"+path;}return path;
    }
    static void Check(bool value,string message,StringBuilder log){log.AppendLine((value?"PASS ":"FAIL ")+message);if(!value)throw new Exception(message);}
    static CosmeticDefinition Extract(string id,string title,CosmeticSlot slot,Part[] parts,Func<Part,bool> keep,SidekickSuit suit,GameObject prefab,StringBuilder log)
    {
        var src=suit.Body;var chosen=parts.Where(keep).ToArray();var indices=chosen.SelectMany(p=>Enumerable.Range(p.Start,p.Count)).ToArray();
        var oldToNew=Enumerable.Repeat(-1,src.vertexCount).ToArray();for(int i=0;i<indices.Length;i++)oldToNew[indices[i]]=i;
        var mesh=new Mesh{name=id,indexFormat=src.indexFormat};
        var vertices=src.vertices;var normals=src.normals;mesh.vertices=indices.Select(i=>vertices[i]).ToArray();mesh.normals=indices.Select(i=>normals[i]).ToArray();
        if(src.tangents.Length>0){var tangents=src.tangents;mesh.tangents=indices.Select(i=>tangents[i]).ToArray();}
        for(int c=0;c<8;c++)if(src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0+c))
        {var uv=new List<Vector4>();src.GetUVs(c,uv);mesh.SetUVs(c,indices.Select(i=>uv[i]).ToList());}
        if(src.colors32.Length>0){var colors=src.colors32;mesh.colors32=indices.Select(i=>colors[i]).ToArray();}
        var triangles=new List<int>();var sourceTriangles=src.triangles;
        for(int t=0;t<sourceTriangles.Length;t+=3)
        {
            int a=oldToNew[sourceTriangles[t]],b=oldToNew[sourceTriangles[t+1]],c=oldToNew[sourceTriangles[t+2]];
            if(a<0&&b<0&&c<0)continue;
            Check(a>=0&&b>=0&&c>=0,"triangle stays within authored part",new StringBuilder());triangles.Add(a);triangles.Add(b);triangles.Add(c);
        }
        mesh.SetTriangles(triangles,0);
        var counts=src.GetBonesPerVertex();var weights=src.GetAllBoneWeights();var starts=new int[src.vertexCount];int cursor=0;
        for(int i=0;i<starts.Length;i++){starts[i]=cursor;cursor+=counts[i];}
        var used=indices.SelectMany(i=>Enumerable.Range(starts[i],counts[i])).Select(i=>weights[i].boneIndex).Distinct().OrderBy(i=>i).ToArray();
        var remap=used.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
        var selected=indices.SelectMany(i=>Enumerable.Range(starts[i],counts[i])).Select(i=>{var w=weights[i];w.boneIndex=remap[w.boneIndex];return w;}).ToArray();
        using(var n=new Unity.Collections.NativeArray<byte>(indices.Select(i=>counts[i]).ToArray(),Unity.Collections.Allocator.Temp))
        using(var w=new Unity.Collections.NativeArray<BoneWeight1>(selected,Unity.Collections.Allocator.Temp))mesh.SetBoneWeights(n,w);
        var bindposes=src.bindposes;mesh.bindposes=used.Select(i=>bindposes[i]).ToArray();mesh.bounds=src.bounds;
        var skin=prefab.GetComponentInChildren<SkinnedMeshRenderer>();var bones=suit.BodyBones.Select(i=>skin.bones[i]).ToArray();
        string path=Folder+"/"+id+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing!=null){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
        // Positive control: source and extracted meshes deform identically under the same non-trivial pose.
        var instance=UnityEngine.Object.Instantiate(prefab);var original=instance.GetComponentInChildren<SkinnedMeshRenderer>();suit.ApplyBody(original);
        var node=new GameObject("extraction control");node.transform.SetParent(original.transform.parent,false);node.transform.localPosition=original.transform.localPosition;node.transform.localRotation=original.transform.localRotation;node.transform.localScale=original.transform.localScale;
        var extracted=node.AddComponent<SkinnedMeshRenderer>();extracted.sharedMesh=mesh;extracted.bones=used.Select(i=>original.bones[i]).ToArray();extracted.rootBone=original.rootBone;
        var animator=instance.GetComponent<Animator>();animator.enabled=false;
        foreach(var pair in new[]{(HumanBodyBones.Spine,new Vector3(17,13,5)),(HumanBodyBones.Head,new Vector3(20,30,0)),(HumanBodyBones.LeftUpperArm,new Vector3(0,0,-65)),(HumanBodyBones.RightLowerLeg,new Vector3(40,0,0))})animator.GetBoneTransform(pair.Item1).localRotation*=Quaternion.Euler(pair.Item2);
        var aMesh=new Mesh();var bMesh=new Mesh();original.BakeMesh(aMesh);extracted.BakeMesh(bMesh);var va=aMesh.vertices;var vb=bMesh.vertices;float error=0;
        for(int i=0;i<indices.Length;i++)error=Mathf.Max(error,Vector3.Distance(va[indices[i]],vb[i]));
        UnityEngine.Object.DestroyImmediate(aMesh);UnityEngine.Object.DestroyImmediate(bMesh);UnityEngine.Object.DestroyImmediate(instance);
        Check(error<.00001f,$"{id}: posed source/extraction error {error*1000:0.000000}mm; vertices={mesh.vertexCount}; bones={used.Length}; blendshapes={mesh.blendShapeCount}",log);
        log.AppendLine("ASSETS "+id+": "+string.Join(", ",chosen.Select(p=>p.Name)));
        return new CosmeticDefinition{Id=id,DisplayName=title,HeroId="vector",Slot=slot,Available=true,Mesh=mesh,Bones=used.Select(i=>BonePath(bones[i],prefab.transform)).ToArray(),Suits=slot==CosmeticSlot.Suit?Array.Empty<string>():new[]{"vector-starter-modular"}};
    }
    public static void Run()
    {
        int exit=0;var log=new StringBuilder();
        try
        {
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Resources/Forge","Cosmetics");
            var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");var vector=forge.Hero("vector");var suit=vector.Suit;var prefab=vector.CharacterPrefab;
            var recipe=File.ReadAllText("Assets/Synty/SidekickCharacters/Characters/Starter/Starter_03/Starter_03.sk");
            var names=Regex.Matches(recipe,@"^- Name: (.+)$",RegexOptions.Multiline).Cast<Match>().Select(m=>m.Groups[1].Value.Trim()).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var files=Directory.GetFiles("Assets/Synty/SidekickCharacters/Resources/Meshes","*.fbx",SearchOption.AllDirectories);int offset=0;var list=new List<Part>();var sourceUV=suit.Body.uv;
            foreach(var name in names)
            {
                var part=AssetDatabase.LoadAssetAtPath<GameObject>(files.Single(f=>Path.GetFileNameWithoutExtension(f)==name)).GetComponentInChildren<SkinnedMeshRenderer>();var uv=part.sharedMesh.uv;
                Check(uv.Length>0&&offset+uv.Length<=sourceUV.Length&&uv.Select((v,i)=>(v-sourceUV[offset+i]).sqrMagnitude<1e-10f).All(x=>x),"exact UV/vertex range "+name,log);
                list.Add(new Part{Name=name,Start=offset,Count=uv.Length});offset+=uv.Length;
            }
            Check(offset==suit.Body.vertexCount,"recipe accounts for every source vertex",log);
            var parts=list.ToArray();var items=new List<CosmeticDefinition>();var defaults=new List<CosmeticDefaults>();
            foreach(var hero in forge.Heroes)
            {
                string id=hero.Id+"-legacy";items.Add(new CosmeticDefinition{Id=id,DisplayName="Original "+hero.DisplayName+" suit",HeroId=hero.Id,Slot=CosmeticSlot.Suit,Available=true});
                items.Add(new CosmeticDefinition{Id=hero.Id+"-integrated",DisplayName="Original headgear",HeroId=hero.Id,Slot=CosmeticSlot.Mask,Available=true,Suits=new[]{id}});
                items.Add(new CosmeticDefinition{Id=hero.Id+"-no-emblem",DisplayName="None",HeroId=hero.Id,Slot=CosmeticSlot.Emblem,Available=true});
                defaults.Add(new CosmeticDefaults{HeroId=hero.Id,SuitId=id,MaskId=hero.Id+"-integrated",EmblemId=hero.Id+"-no-emblem"});
            }
            int Type(Part p)=>int.Parse(p.Name.Split('_')[4].Substring(0,2));
            items.Add(Extract("vector-starter-modular","Starter armor · light fittings",CosmeticSlot.Suit,parts,p=>Type(p)<22||Type(p)>34,suit,prefab,log));
            items.Add(Extract("vector-hood","Hood",CosmeticSlot.Mask,parts,p=>Type(p)==22,suit,prefab,log));
            items.Add(Extract("vector-hood-respirator","Hood + face cover",CosmeticSlot.Mask,parts,p=>Type(p)==22||Type(p)==23,suit,prefab,log));
            foreach(string id in new[]{"vector-emblem-chevron","vector-emblem-bolt"})items.Add(new CosmeticDefinition{Id=id,DisplayName=id.EndsWith("bolt")?"Bolt emblem":"Chevron emblem",HeroId="vector",Slot=CosmeticSlot.Emblem,Available=false,UnavailableReason="No authored chest-emblem mesh is included in the installed Sidekick Starter art. Requires an artist-fitted chest mesh with UVs and binding to VECTOR spine_03; no placeholder is rendered."});
            string catalogPath="Assets/Resources/CosmeticCatalog.asset";var catalog=AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(catalogPath);
            if(catalog==null){catalog=ScriptableObject.CreateInstance<CosmeticCatalog>();AssetDatabase.CreateAsset(catalog,catalogPath);}
            catalog.Items=items.ToArray();catalog.Defaults=defaults.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            Check(items.Count(x=>x.HeroId=="vector"&&x.Slot==CosmeticSlot.Mask&&x.Mesh!=null)==2,"two fitted hood packages; emblems explicitly unavailable",log);
        }
        catch(Exception e){log.AppendLine("FAIL "+e);exit=1;}
        Directory.CreateDirectory("Verification/VectorCustomization");File.WriteAllText("Verification/VectorCustomization/bake.txt",log.ToString());Debug.Log(log.ToString());EditorApplication.Exit(exit);
    }
}
#endif
