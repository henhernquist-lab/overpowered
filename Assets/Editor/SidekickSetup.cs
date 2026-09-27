#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Sidekick authoring (Editor only). Reads Sidekick's colour-property table (swatch -> Species/Outfits/Attachments/...)
/// straight from its SQLite file with the system sqlite3 in read-only mode, so no Sidekick runtime/DB code is pulled into
/// gameplay; the result is baked into plain assets.
public static class SidekickSetup
{
    public const string Database="Assets/Synty/SidekickCharacters/Database/Side_Kick_Data.db";
    public struct ColorProperty{public int Id,Group,U,V;public string Name;}
    public static readonly string[] Groups={"?","Species","Outfits","Attachments","Materials","Elements"};
    public static List<ColorProperty> ReadColorProperties()
    {
        var info=new ProcessStartInfo("/usr/bin/sqlite3",$"-readonly -separator \"|\" \"{Path.GetFullPath(Database)}\" \"select id,color_group,u,v,name from sk_color_property order by id\"")
            {RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        using var process=Process.Start(info);string output=process.StandardOutput.ReadToEnd();string error=process.StandardError.ReadToEnd();process.WaitForExit();
        if(process.ExitCode!=0)throw new Exception("sqlite3 read of the Sidekick colour table failed: "+error);
        return output.Split('\n').Where(l=>l.Contains('|')).Select(l=>{var f=l.Split('|');return new ColorProperty{Id=int.Parse(f[0]),Group=int.Parse(f[1]),U=int.Parse(f[2]),V=int.Parse(f[3]),Name=f[4].Trim()};}).ToList();
    }
    /// Swatches actually referenced by a prefab's mesh (UV0 -> 2x2-pixel cell of the 32x32 colour map), with vertex counts.
    public static Dictionary<Vector2Int,int> UsedSwatches(GameObject prefab)
    {
        var used=new Dictionary<Vector2Int,int>();
        foreach(var skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach(var uv in skin.sharedMesh.uv)
            {
                var cell=new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(uv.x*16),0,15),Mathf.Clamp(Mathf.FloorToInt(uv.y*16),0,15));
                used[cell]=used.TryGetValue(cell,out var n)?n+1:1;
            }
        return used;
    }
    public static Texture2D ReadableColorMap(Material material)
    {
        var source=material.GetTexture("_ColorMap") as Texture2D;var path=AssetDatabase.GetAssetPath(source);
        var copy=new Texture2D(2,2,TextureFormat.RGBA32,false);copy.LoadImage(File.ReadAllBytes(path));return copy;
    }
    // Hero roster -> Sidekick prefab (the only three outfitted Starter prefabs; distinct heads/outfits). Data, not ID switches in
    // gameplay: this only fills HeroDefinition.CharacterPrefab/Suit assets.
    public static readonly (string hero,string prefab)[] Heroes={("titan","Starter/Starter_01"),("nova","Starter/Starter_02"),("vector","Starter/Starter_03")};
    public const string SuitFolder="Assets/Resources/Forge/Sidekick";
    // Materials-group swatches that read as light/FX rather than suit fabric keep their authored colour.
    static readonly string[] KeepMaterials={"Glow","Glass","Digital Screen","Transparency FX","Jewellery Gem"};
    public const float TrimLuminance=.30f;
    static float Luminance(Color c)=>.299f*c.r+.587f*c.g+.114f*c.b;
    /// Swatch roles (Sidekick's own colour groups): Species (skin, hair, eyes, mouth, nails...), Elements, unmapped cells and FX
    /// materials (glow/glass/screens/gems) -> Keep. Of the rest, authored dark cells (luminance < TrimLuminance) -> Trim, which
    /// keeps the outfit's light/dark structure; cloth (Outfits group) -> Primary; armour/attachments/material parts -> Secondary.
    public static SidekickSuit.Swatch[] Roles(Dictionary<Vector2Int,int> used,Texture2D map,List<ColorProperty> props)
    {
        var result=new List<SidekickSuit.Swatch>();
        foreach(var pair in used.OrderBy(p=>p.Key.x).ThenBy(p=>p.Key.y))
        {
            var prop=props.FirstOrDefault(x=>x.U==pair.Key.x&&x.V==pair.Key.y);var c=map.GetPixel(pair.Key.x*2,pair.Key.y*2);
            string name=prop.Name==null?"(unmapped)":Groups[prop.Group]+" / "+prop.Name;
            bool keep=prop.Name==null||prop.Group==1||prop.Group==5||prop.Group==4&&KeepMaterials.Any(k=>prop.Name.StartsWith(k));
            var role=keep?SuitRole.Keep:Luminance(c)<TrimLuminance?SuitRole.Trim:prop.Group==2?SuitRole.Primary:SuitRole.Secondary;
            result.Add(new SidekickSuit.Swatch{Cell=pair.Key,Role=role,Name=name,Vertices=pair.Value});
        }
        return result.ToArray();
    }
    // Candidate NPC looks: the lighter base bodies (5.9k-9k vertices, 1 skinned mesh, vs the mannequin's 28k in 2).
    // Used only when HumanoidAnimationTuning.SidekickNpcs is on (off unless measured FPS allows; see STATUS).
    public static readonly string[] NpcPrefabs={"HumanSpecies/HumanSpecies_01","HumanSpecies/HumanSpecies_02","HumanSpecies/HumanSpecies_03","HumanSpecies/HumanSpecies_04"};
    static SidekickSuit Suit(string id,GameObject prefab,List<ColorProperty> props,StringBuilder log,string header)
    {
        var skin=prefab.GetComponentInChildren<SkinnedMeshRenderer>();
        string suitPath=$"{SuitFolder}/{id}-suit.asset";var suit=AssetDatabase.LoadAssetAtPath<SidekickSuit>(suitPath);
        if(suit==null){suit=ScriptableObject.CreateInstance<SidekickSuit>();AssetDatabase.CreateAsset(suit,suitPath);}
        var authored=ReadableColorMap(skin.sharedMaterial);
        if(suit.BaseColorMap==null){suit.BaseColorMap=new Texture2D(authored.width,authored.height,TextureFormat.RGBA32,false);AssetDatabase.AddObjectToAsset(suit.BaseColorMap,suit);}
        else suit.BaseColorMap.Reinitialize(authored.width,authored.height,TextureFormat.RGBA32,false);
        suit.BaseColorMap.name=id+" authored colour map (readable copy of "+Path.GetFileName(AssetDatabase.GetAssetPath(skin.sharedMaterial.GetTexture("_ColorMap")))+")";
        suit.BaseColorMap.filterMode=FilterMode.Point;suit.BaseColorMap.wrapMode=TextureWrapMode.Clamp;suit.BaseColorMap.SetPixels32(authored.GetPixels32());suit.BaseColorMap.Apply(false,false);
        suit.Prefab=prefab;suit.Source=skin.sharedMaterial;suit.Trim=CityColor.Metal;suit.Swatches=Roles(UsedSwatches(prefab),authored,props);
        UnityEngine.Object.DestroyImmediate(authored);EditorUtility.SetDirty(suit.BaseColorMap);EditorUtility.SetDirty(suit);
        log.AppendLine($"== {header} -> {prefab.name}.prefab ({skin.sharedMesh.vertexCount} vertices), source material {suit.Source.name}; trim={suit.Trim}");
        foreach(var role in new[]{SuitRole.Primary,SuitRole.Secondary,SuitRole.Trim,SuitRole.Keep})
        {
            var list=suit.Swatches.Where(x=>x.Role==role).ToArray();
            log.AppendLine($"   {role}: {list.Length} swatches / {list.Sum(x=>x.Vertices)} verts: "+string.Join("; ",list.Select(x=>$"({x.Cell.x},{x.Cell.y}) #{ColorUtility.ToHtmlStringRGB(suit.BaseColorMap.GetPixel(x.Cell.x*2,x.Cell.y*2))} {x.Name}")));
        }
        return suit;
    }
    /// Optimized body mesh: Sidekick's combined prefab mesh carries one skeleton copy per part in bones[]/bindposes (2,992
    /// entries on Starter_03). Entries with the same bone transform AND the same bind pose are merged, bone weights remapped;
    /// blend shapes (Sidekick's editor sliders, unused by the game) are dropped after baking any non-zero prefab weight.
    /// Verified: the same posed skeleton skins both meshes to the same vertices (max error logged).
    static void BuildBody(string id,GameObject prefab,SidekickSuit suit,StringBuilder log)
    {
        var skin=prefab.GetComponentInChildren<SkinnedMeshRenderer>();var src=skin.sharedMesh;var bones=skin.bones;var binds=src.bindposes;
        var unique=new List<int>();var map=new int[bones.Length];
        bool Same(Matrix4x4 a,Matrix4x4 b){for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>1e-5f)return false;return true;}
        for(int i=0;i<bones.Length;i++)
        {
            int k=unique.FindIndex(u=>bones[u]==bones[i]&&Same(binds[u],binds[i]));
            if(k<0){k=unique.Count;unique.Add(i);}map[i]=k;
        }
        var mesh=new Mesh{name=id+" body (deduplicated bones)",indexFormat=src.indexFormat};
        var vertices=src.vertices;var normals=src.normals;var tangents=src.tangents;int baked=0;
        for(int s=0;s<src.blendShapeCount;s++)
        {
            float w=skin.GetBlendShapeWeight(s);if(w==0)continue;int frame=src.GetBlendShapeFrameCount(s)-1;float full=src.GetBlendShapeFrameWeight(s,frame);
            var dv=new Vector3[vertices.Length];var dn=new Vector3[vertices.Length];var dt=new Vector3[vertices.Length];src.GetBlendShapeFrameVertices(s,frame,dv,dn,dt);float f=w/full;baked++;
            for(int v=0;v<vertices.Length;v++){vertices[v]+=dv[v]*f;if(normals.Length>0)normals[v]+=dn[v]*f;if(tangents.Length>0){var t=tangents[v];tangents[v]=new Vector4(t.x+dt[v].x*f,t.y+dt[v].y*f,t.z+dt[v].z*f,t.w);}}
        }
        mesh.vertices=vertices;if(normals.Length>0)mesh.normals=normals.Select(n=>n.normalized).ToArray();if(tangents.Length>0)mesh.tangents=tangents;
        for(int c=0;c<8;c++)if(src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0+c)){var uv=new List<Vector4>();src.GetUVs(c,uv);mesh.SetUVs(c,uv);}
        if(src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))mesh.colors32=src.colors32;
        mesh.subMeshCount=src.subMeshCount;for(int m=0;m<src.subMeshCount;m++)mesh.SetTriangles(src.GetTriangles(m),m);
        var perVertex=src.GetBonesPerVertex();var weights=src.GetAllBoneWeights();
        var remapped=new Unity.Collections.NativeArray<BoneWeight1>(weights.Length,Unity.Collections.Allocator.Temp);
        for(int i=0;i<weights.Length;i++){var w=weights[i];w.boneIndex=map[w.boneIndex];remapped[i]=w;}
        mesh.SetBoneWeights(perVertex,remapped);remapped.Dispose();
        mesh.bindposes=unique.Select(i=>binds[i]).ToArray();mesh.bounds=src.bounds;
        string path=$"{SuitFolder}/{id}-body.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}
        suit.Body=mesh;suit.SourceMesh=src;suit.BodyBones=unique.ToArray();EditorUtility.SetDirty(suit);
        // CONTROL: identical skinning. Two instances, the same non-trivial pose, original vs optimized body.
        var a=UnityEngine.Object.Instantiate(prefab);var b=UnityEngine.Object.Instantiate(prefab);
        var sa=a.GetComponentInChildren<SkinnedMeshRenderer>();var sb=b.GetComponentInChildren<SkinnedMeshRenderer>();suit.ApplyBody(sb);
        foreach(var go in new[]{a,b}){var an=go.GetComponent<Animator>();foreach(var (bone,euler) in new[]{(HumanBodyBones.Spine,new Vector3(20,15,5)),(HumanBodyBones.LeftUpperArm,new Vector3(10,40,-60)),(HumanBodyBones.RightLowerArm,new Vector3(0,-70,0)),(HumanBodyBones.LeftUpperLeg,new Vector3(-50,0,10)),(HumanBodyBones.Head,new Vector3(15,30,0))})an.GetBoneTransform(bone).localRotation*=Quaternion.Euler(euler);}
        var ma=new Mesh();var mb=new Mesh();sa.BakeMesh(ma);sb.BakeMesh(mb);var va=ma.vertices;var vb=mb.vertices;float err=0;for(int v=0;v<va.Length;v++)err=Mathf.Max(err,Vector3.Distance(va[v],vb[v]));
        UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);UnityEngine.Object.DestroyImmediate(ma);UnityEngine.Object.DestroyImmediate(mb);
        log.AppendLine($"   BODY {path}: bones[] {bones.Length} -> {unique.Count} (distinct transforms {bones.Distinct().Count()}), blend shapes {src.blendShapeCount} -> 0 (non-zero baked: {baked}), vertices {src.vertexCount}; CONTROL posed skinning max vertex difference {err*1000:F3} mm");
        if(err>.001f)throw new Exception($"{id}: optimized body skins differently ({err*1000:F2} mm)");
    }
    [MenuItem("Overpowered/Forge/Sidekick heroes (suits + character prefabs)")]
    public static void CreateHeroes()
    {
        var props=ReadColorProperties();var log=new StringBuilder();
        if(!AssetDatabase.IsValidFolder(SuitFolder))AssetDatabase.CreateFolder("Assets/Resources/Forge","Sidekick");
        foreach(var (heroId,prefabPath) in Heroes)
        {
            var hero=AssetDatabase.LoadAssetAtPath<HeroDefinition>($"Assets/Resources/Forge/Heroes/{heroId}.asset");
            if(hero==null)throw new Exception("Missing hero definition "+heroId+" (run Overpowered > Forge > Create missing assets first).");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(SidekickClipCheck.PrefabPath(prefabPath));
            var suit=Suit(heroId,prefab,props,log,$"hero {heroId} ({hero.DisplayName}, default Primary={hero.Primary} Secondary={hero.Secondary})");
            BuildBody(heroId,prefab,suit,log);
            hero.CharacterPrefab=prefab;hero.Suit=suit;EditorUtility.SetDirty(hero);
        }
        var tuning=AssetDatabase.LoadAssetAtPath<HumanoidAnimationTuning>("Assets/Resources/HumanoidAnimationTuning.asset");
        tuning.NpcLooks=NpcPrefabs.Select(p=>Suit("npc-"+Path.GetFileName(p).ToLowerInvariant(),AssetDatabase.LoadAssetAtPath<GameObject>(SidekickClipCheck.PrefabPath(p)),props,log,"NPC look (role colour = Primary, archetype accent = Secondary)")).ToArray();
        EditorUtility.SetDirty(tuning);log.AppendLine($"HumanoidAnimationTuning.SidekickNpcs={tuning.SidekickNpcs} (left as authored), NpcLooks={tuning.NpcLooks.Length}");
        AssetDatabase.SaveAssets();Directory.CreateDirectory("Verification/Sidekick");File.WriteAllText("Verification/Sidekick/suits.txt",log.ToString());
        UnityEngine.Debug.Log("[SIDEKICK SETUP]\n"+log);
    }
    public static void CreateHeroesBatch(){int code=0;try{CreateHeroes();}catch(Exception e){UnityEngine.Debug.LogError(e);code=1;}EditorApplication.Exit(code);}
    /// Batch: shared-controller foot IK + hero suits/prefabs (both idempotent).
    public static void ApplyBatch(){int code=0;try{HumanoidSetup.ApplyFootIK();CreateHeroes();}catch(Exception e){UnityEngine.Debug.LogError(e);code=1;}EditorApplication.Exit(code);}
    /// Idle lineup of all 8 prefabs (front row, back row) for choosing hero bodies. Verification/Sidekick/lineup.png
    public static void Lineup()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.52f,.58f);
        var sun=new GameObject("sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(35,160,0);
        var tuning=AssetDatabase.LoadAssetAtPath<HumanoidAnimationTuning>("Assets/Resources/HumanoidAnimationTuning.asset");
        var cam=new GameObject("cam").AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.16f,.18f,.22f);cam.fieldOfView=24;
        int W=300,H=520;var rt=new RenderTexture(W,H,24){antiAliasing=4};cam.targetTexture=rt;var cell=new Texture2D(W,H,TextureFormat.RGB24,false);
        var sheet=new Texture2D(W*SidekickClipCheck.Prefabs.Length,H*2,TextureFormat.RGB24,false);
        for(int i=0;i<SidekickClipCheck.Prefabs.Length;i++)
        {
            var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SidekickClipCheck.PrefabPath(SidekickClipCheck.Prefabs[i])));
            var animator=go.GetComponent<Animator>();animator.runtimeAnimatorController=tuning.Controller;animator.Update(0);animator.Update(.5f);
            var skin=go.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();skin.BakeMesh(mesh);
            var proxy=new GameObject("proxy");proxy.transform.SetParent(skin.transform,false);proxy.AddComponent<MeshFilter>().sharedMesh=mesh;proxy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
            var b=proxy.GetComponent<MeshRenderer>().bounds;
            for(int v=0;v<2;v++)
            {
                cam.transform.position=b.center+new Vector3(0,.1f,v==0?6.5f:-6.5f)*b.size.y/1.8f;cam.transform.LookAt(b.center);cam.Render();
                RenderTexture.active=rt;cell.ReadPixels(new Rect(0,0,W,H),0,0);cell.Apply();RenderTexture.active=null;sheet.SetPixels(i*W,(1-v)*H,W,H,cell.GetPixels());
            }
            UnityEngine.Object.DestroyImmediate(go);
        }
        sheet.Apply();Directory.CreateDirectory("Verification/Sidekick");File.WriteAllBytes("Verification/Sidekick/lineup.png",sheet.EncodeToPNG());
        EditorApplication.Exit(0);
    }
    /// Look comparison for the shader decision: each hero (default colours) with its suit on Sidekick_ShaderGraph (left) and the
    /// same colour map on Standard + Sidekick's emission map (right), lit like the city. Verification/Sidekick/shader-compare.png
    public static void ShaderCompare()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.45f,.5f,.6f);
        var sun=new GameObject("sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(45,-35+180,0);
        var palette=AssetDatabase.LoadAssetAtPath<CityPalette>("Assets/Resources/CityPalette.asset");
        var tuning=AssetDatabase.LoadAssetAtPath<HumanoidAnimationTuning>("Assets/Resources/HumanoidAnimationTuning.asset");
        var cam=new GameObject("cam").AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.35f,.42f,.5f);cam.fieldOfView=22;
        int W=360,H=520;var rt=new RenderTexture(W,H,24){antiAliasing=4};cam.targetTexture=rt;var cell=new Texture2D(W,H,TextureFormat.RGB24,false);
        var sheet=new Texture2D(W*2*Heroes.Length,H,TextureFormat.RGB24,false);int col=0;
        foreach(var (heroId,_) in Heroes)
        {
            var hero=AssetDatabase.LoadAssetAtPath<HeroDefinition>($"Assets/Resources/Forge/Heroes/{heroId}.asset");var suit=hero.Suit;
            var map=suit.Build(palette.Colors[(int)hero.Primary],palette.Colors[(int)hero.Secondary],palette.Colors[(int)suit.Trim]);
            var sidekick=new Material(suit.Source);sidekick.SetTexture(SidekickSuit.ColorMapProperty,map);
            var standard=new Material(Shader.Find("Standard")){mainTexture=map};standard.SetFloat("_Glossiness",palette.Smoothness);
            var emission=suit.Source.GetTexture("_EmissionMap");if(emission!=null){standard.SetTexture("_EmissionMap",emission);standard.SetColor("_EmissionColor",Color.white);standard.EnableKeyword("_EMISSION");}
            foreach(var mat in new[]{sidekick,standard})
            {
                var go=UnityEngine.Object.Instantiate(hero.CharacterPrefab);var animator=go.GetComponent<Animator>();animator.runtimeAnimatorController=tuning.Controller;animator.Update(0);animator.Update(.5f);
                var skin=go.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();skin.BakeMesh(mesh);
                var proxy=new GameObject("proxy");proxy.transform.SetParent(skin.transform,false);proxy.AddComponent<MeshFilter>().sharedMesh=mesh;proxy.AddComponent<MeshRenderer>().sharedMaterial=mat;skin.enabled=false;
                var b=proxy.GetComponent<MeshRenderer>().bounds;cam.transform.position=b.center+new Vector3(.9f,.25f,5.2f)*b.size.y/1.8f;cam.transform.LookAt(b.center);cam.Render();
                RenderTexture.active=rt;cell.ReadPixels(new Rect(0,0,W,H),0,0);cell.Apply();RenderTexture.active=null;sheet.SetPixels(col*W,0,W,H,cell.GetPixels());col++;
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        sheet.Apply();File.WriteAllBytes("Verification/Sidekick/shader-compare.png",sheet.EncodeToPNG());EditorApplication.Exit(0);
    }
    public static void Analyze()
    {
        var log=new StringBuilder();int code=0;
        try
        {
            var props=ReadColorProperties();log.AppendLine($"sk_color_property rows read (sqlite3 -readonly): {props.Count}");
            foreach(var p in SidekickClipCheck.Prefabs)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(SidekickClipCheck.PrefabPath(p));var skin=prefab.GetComponentInChildren<SkinnedMeshRenderer>();
                var map=ReadableColorMap(skin.sharedMaterial);var used=UsedSwatches(prefab);
                log.AppendLine($"== {Path.GetFileName(p)} mesh={skin.sharedMesh.name} verts={skin.sharedMesh.vertexCount} submeshes={skin.sharedMesh.subMeshCount} materials={skin.sharedMaterials.Length} ({skin.sharedMaterial.name}, shader {skin.sharedMaterial.shader.name}) uvChannels={string.Join(",",Enumerable.Range(0,8).Where(c=>skin.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0+c)))}");
                foreach(var cell in used.OrderBy(c=>c.Key.x).ThenByDescending(c=>c.Key.y))
                {
                    var prop=props.FirstOrDefault(x=>x.U==cell.Key.x&&x.V==cell.Key.y);var c=map.GetPixel(cell.Key.x*2,cell.Key.y*2);
                    log.AppendLine($"   ({cell.Key.x,2},{cell.Key.y,2}) verts={cell.Value,6} #{ColorUtility.ToHtmlStringRGB(c)} {(prop.Name==null?"(no property)":Groups[prop.Group]+" / "+prop.Name)}");
                }
                UnityEngine.Object.DestroyImmediate(map);
            }
        }
        catch(Exception e){log.AppendLine("EXCEPTION "+e);code=1;}
        Directory.CreateDirectory("Verification/Sidekick");File.WriteAllText("Verification/Sidekick/swatch-analysis.txt",log.ToString());
        EditorApplication.Exit(code);
    }
}
#endif
