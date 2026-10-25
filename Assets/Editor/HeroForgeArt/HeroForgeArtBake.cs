#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// Character-specific authoring, not a runtime assembler. Existing Synty anatomy supplies the properly rigged suit;
/// clothing UVs address costume swatches, while fitted legacy face/eyes retain their protected authored UVs.
public static class HeroForgeArtBake
{
    static string HeroId,Folder,SuitId,ModulePath,Starter;static float Feminine,Skinny;static int[] Retained;
    static string[] Codes;
    static readonly string[] BodyCodes={"10TORS","11AUPL","12AUPR","13ALWL","14ALWR","15HNDL","16HNDR","17HIPS","18LEGL","19LEGR"};
    static void Configure(string id,string starter,float feminine,float skinny,int[] retained,string suit)
    {
        HeroId=id;Starter=starter;Feminine=feminine;Skinny=skinny;Retained=retained;SuitId=suit;
        Folder="Assets/Resources/Forge/Cosmetics/"+char.ToUpper(id[0])+id.Substring(1);ModulePath="Forge/Cosmetics/"+char.ToUpper(id[0])+id.Substring(1)+"/"+char.ToUpper(id[0])+id.Substring(1)+"Cosmetics";
        Codes=BodyCodes.Where(c=>!retained.Contains(int.Parse(c.Substring(0,2)))).ToArray();
    }
    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;var parent=System.IO.Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(path));
    }
    static CosmeticCatalog Module()
    {
        EnsureFolder(Folder);string path="Assets/Resources/"+ModulePath+".asset";var module=AssetDatabase.LoadAssetAtPath<CosmeticCatalog>(path);
        if(module==null){module=ScriptableObject.CreateInstance<CosmeticCatalog>();AssetDatabase.CreateAsset(module,path);}
        var ids=new List<(string,CosmeticSlot,string)>{(SuitId,CosmeticSlot.Suit,HeroId=="vector"?"Tactical Weave":HeroId=="titan"?"Guardian plated suit":"Energy Weave"),(HeroId+"_mask_unmasked",CosmeticSlot.Mask,"Unmasked")};
        if(HeroId=="vector"){ids.Add(("vector_mask_aegis",CosmeticSlot.Mask,"Aegis Visor"));ids.Add(("vector_mask_sentinel",CosmeticSlot.Mask,"Sentinel Cowl"));ids.Add(("vector-emblem-chevron",CosmeticSlot.Emblem,"Chevron"));ids.Add(("vector-emblem-bolt",CosmeticSlot.Emblem,"Bolt"));}
        else if(HeroId=="titan"){ids.Add(("titan_mask_guardian",CosmeticSlot.Mask,"Guardian Helmet"));ids.Add(("titan_mask_vanguard",CosmeticSlot.Mask,"Vanguard Mask"));ids.Add(("titan_emblem_shield",CosmeticSlot.Emblem,"Shield Crest"));ids.Add(("titan_emblem_core",CosmeticSlot.Emblem,"Power Core"));}
        else{ids.Add(("nova_mask_spectrum",CosmeticSlot.Mask,"Spectrum Visor"));ids.Add(("nova_mask_phantom",CosmeticSlot.Mask,"Phantom Helmet"));ids.Add(("nova_emblem_star",CosmeticSlot.Emblem,"Nova Star"));ids.Add(("nova_emblem_core",CosmeticSlot.Emblem,"Energy Core"));}
        var list=new List<CosmeticDefinition>();foreach(var entry in ids){var d=module.Find(entry.Item1,entry.Item2)??new CosmeticDefinition();d.Id=entry.Item1;d.Slot=entry.Item2;d.DisplayName=entry.Item3;d.HeroId=HeroId;d.Available=true;d.UnavailableReason="";d.Suits=d.Slot==CosmeticSlot.Suit?Array.Empty<string>():new[]{SuitId};list.Add(d);}
        module.Items=list.ToArray();module.Defaults=Array.Empty<CosmeticDefaults>();EditorUtility.SetDirty(module);return module;
    }
    static string PathOf(Transform bone,Transform root){string p=bone.name;while(bone.parent!=root){bone=bone.parent;if(bone==null)throw new Exception("Bone outside model");p=bone.name+"/"+p;}return p;}
    static void Check(bool ok,string text,StringBuilder log){log.AppendLine((ok?"PASS ":"FAIL ")+text);if(!ok)throw new Exception(text);}
    static Mesh Save(Mesh mesh,string id)
    {
        string path=Folder+"/"+id+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old==null)AssetDatabase.CreateAsset(mesh,path);else{old.Clear(false);old.name=mesh.name;old.indexFormat=mesh.indexFormat;old.vertices=mesh.vertices;old.normals=mesh.normals;old.uv=mesh.uv;old.boneWeights=mesh.boneWeights;old.bindposes=mesh.bindposes;old.triangles=mesh.triangles;old.bounds=mesh.bounds;UnityEngine.Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(mesh);}return mesh;
    }
    public static void Run()
    {
        int exit=0;var log=new StringBuilder();
        try
        {
            Configure("vector","Starter_03",0,26.6f,new[]{20,21},"vector_suit_tactical");Module();Build(log);
            Configure("titan","Starter_01",0,0,new[]{11,12,13,14,15,16,20,21},"titan_suit_guardian");Module();Build(log);
            Configure("nova","Starter_02",100,14.5f,new[]{20,21},"nova_suit_energyweave");Module();Build(log);
            var target=Resources.Load<CosmeticCatalog>("CosmeticCatalog");var all=target.Items.ToList();
            foreach(string hero in new[]{"Vector","Titan","Nova"})foreach(var d in Resources.Load<CosmeticCatalog>("Forge/Cosmetics/"+hero+"/"+hero+"Cosmetics").Items)
            {if(all.Any(x=>x.Id==d.Id&&x.HeroId!=d.HeroId))throw new Exception("Foreign ID collision "+d.Id);all.RemoveAll(x=>x.Id==d.Id);all.Add(d);}
            target.Items=all.ToArray();EditorUtility.SetDirty(target);AssetDatabase.SaveAssets();
            Check(target.Items.Where(x=>x.Available&&x.Mesh!=null).All(x=>x.Bones.Length==x.Mesh.bindposes.Length),"registered disk catalog contains valid real mesh bindings",log);
        }
        catch(Exception e){log.AppendLine("FAIL "+e);exit=1;}
        Directory.CreateDirectory("Verification/HeroForgeArt");File.WriteAllText("Verification/HeroForgeArt/native-bake.txt",log.ToString());Debug.Log(log.ToString());EditorApplication.Exit(exit);
    }
    static void Build(StringBuilder log)
    {
        var hero=Resources.Load<ForgeCatalog>("ForgeCatalog").Hero(HeroId);var suit=hero.Suit;var original=hero.CharacterPrefab.GetComponentInChildren<SkinnedMeshRenderer>();
        var legacyBones=suit.BodyBones.Select(i=>original.bones[i]).ToArray();var byName=hero.CharacterPrefab.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var weights=new List<BoneWeight>();var triangles=new List<int>();var bones=new List<Transform>();var bindposes=new List<Matrix4x4>();var sources=new List<string>();
        var colorCells=new[]{SuitRole.Primary,SuitRole.Secondary,SuitRole.Trim}.ToDictionary(role=>role,role=>(Vector2)suit.Swatches.First(x=>x.Role==role).Cell/16f+Vector2.one/32f);
        int Bone(Transform t,Matrix4x4 bind)
        {
            int k=bones.IndexOf(t);if(k>=0)return k;bones.Add(t);bindposes.Add(bind);return bones.Count-1;
        }
        void Add(Vector3[] v,Vector3[] n,Vector2[] u,BoneWeight[] w,int[] tris,Transform[] sb,Matrix4x4[] binds)
        {
            int offset=vertices.Count;var map=new int[sb.Length];for(int i=0;i<map.Length;i++)map[i]=Bone(sb[i],binds[i]);
            foreach(var bw in w){var b=bw;b.boneIndex0=map[b.boneIndex0];b.boneIndex1=map[b.boneIndex1];b.boneIndex2=map[b.boneIndex2];b.boneIndex3=map[b.boneIndex3];weights.Add(b);}
            vertices.AddRange(v);normals.AddRange(n);uv.AddRange(u);triangles.AddRange(tris.Select(i=>i+offset));
        }
        // Exact fitted face + boots from the optimized legacy body. No old fox mask, hair, robe, belts or accessory chains.
        var allFiles=Directory.GetFiles("Assets/Synty/SidekickCharacters/Resources/Meshes","*.fbx",SearchOption.AllDirectories);
        var recipe=Regex.Matches(File.ReadAllText("Assets/Synty/SidekickCharacters/Characters/Starter/"+Starter+"/"+Starter+".sk"),@"^- Name: (.+)$",RegexOptions.Multiline).Cast<Match>().Select(x=>x.Groups[1].Value.Trim()).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var src=suit.Body;var sv=src.vertices;var sn=src.normals;var su=src.uv;var sw=src.boneWeights;var st=src.triangles;var keep=new List<int>();int cursor=0;
        foreach(var name in recipe)
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(allFiles.Single(f=>System.IO.Path.GetFileNameWithoutExtension(f)==name)).GetComponentInChildren<SkinnedMeshRenderer>();int count=p.sharedMesh.vertexCount;var pu=p.sharedMesh.uv;
            int type=int.Parse(name.Split('_')[4].Substring(0,2));bool retainedPart=type<=8&&type!=2||type>=35||Retained.Contains(type);
            bool Match(int start)=>start>=0&&start+count<=su.Length&&pu.Where((q,i)=>(q-su[start+i]).sqrMagnitude>=1e-10f).Any()==false;
            if(type==1&&!Match(cursor))
            {
                var candidates=Enumerable.Range(Mathf.Max(0,cursor-64),129).Where(Match).ToArray();Check(candidates.Length==1,"unique exact head recovery "+HeroId,log);
                log.AppendLine("MEASURED "+HeroId+" excluded source-recipe drift="+(candidates[0]-cursor));cursor=candidates[0];
            }
            Check(cursor+count<=sv.Length,"source range "+HeroId+"/"+name,log);
            if(retainedPart)
            {
                Check(Match(cursor),"exact retained fitted range "+HeroId+"/"+name,log);
                if(type>=10&&type<=21)for(int j=cursor;j<cursor+count;j++)
                {
                    var cell=new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(su[j].x*16),0,15),Mathf.Clamp(Mathf.FloorToInt(su[j].y*16),0,15));var swatch=suit.Swatches.First(x=>x.Cell==cell);
                    var role=swatch.Name.EndsWith("Metal 01")||swatch.Name.EndsWith("Metal 02")?SuitRole.Trim:swatch.Name.EndsWith("Metal 04")?SuitRole.Primary:SuitRole.Secondary;su[j]=colorCells[role];
                }
                keep.AddRange(Enumerable.Range(cursor,count));sources.Add("FITTED "+name);
            }
            cursor+=count;
        }
        Check(cursor==sv.Length,"all original geometry accounted for before accessory removal",log);
        var remap=Enumerable.Repeat(-1,sv.Length).ToArray();for(int i=0;i<keep.Count;i++)remap[keep[i]]=i;
        var retained=new List<int>();for(int i=0;i<st.Length;i+=3){int a=remap[st[i]],b=remap[st[i+1]],c=remap[st[i+2]];if(a<0&&b<0&&c<0)continue;if(a<0||b<0||c<0)throw new Exception("Part boundary crosses triangle");retained.Add(a);retained.Add(b);retained.Add(c);}
        Add(keep.Select(i=>sv[i]).ToArray(),keep.Select(i=>sn[i]).ToArray(),keep.Select(i=>su[i]).ToArray(),keep.Select(i=>sw[i]).ToArray(),retained.ToArray(),legacyBones,src.bindposes);
        foreach(var code in Codes)
        {
            string name="SK_HUMN_BASE_01_"+code+"_HU01";var part=AssetDatabase.LoadAssetAtPath<GameObject>(allFiles.Single(f=>System.IO.Path.GetFileNameWithoutExtension(f)==name)).GetComponentInChildren<SkinnedMeshRenderer>();var mesh=part.sharedMesh;var v=mesh.vertices;var n=mesh.normals;
            for(int s=0;s<mesh.blendShapeCount;s++)
            {
                string shape=mesh.GetBlendShapeName(s);float amount=shape.Contains("masculineFeminine")?Feminine:shape.Contains("defaultSkinny")?Skinny:0;if(amount==0)continue;
                int frame=mesh.GetBlendShapeFrameCount(s)-1;var dv=new Vector3[v.Length];var dn=new Vector3[v.Length];var dt=new Vector3[v.Length];mesh.GetBlendShapeFrameVertices(s,frame,dv,dn,dt);float f=amount/mesh.GetBlendShapeFrameWeight(s,frame);
                for(int i=0;i<v.Length;i++){v[i]+=dv[i]*f;n[i]=(n[i]+dn[i]*f).normalized;}
            }
            var transform=original.transform.worldToLocalMatrix*part.transform.localToWorldMatrix;for(int i=0;i<v.Length;i++){v[i]=transform.MultiplyPoint3x4(v[i]);n[i]=transform.MultiplyVector(n[i]).normalized;}
            var targetBones=part.bones.Select(b=>byName.TryGetValue(b.name,out var t)?t:throw new Exception("Missing clothing bone "+b.name)).ToArray();var binds=targetBones.Select(b=>b.worldToLocalMatrix*original.transform.localToWorldMatrix).ToArray();
            float error=0;for(int i=0;i<binds.Length;i++)for(int k=0;k<16;k++)error=Mathf.Max(error,Mathf.Abs(binds[i][k]-(part.bones[i].worldToLocalMatrix*part.transform.localToWorldMatrix*transform.inverse)[k]));
            float pivotError=0;for(int i=0;i<targetBones.Length;i++)pivotError=Mathf.Max(pivotError,Vector3.Distance(targetBones[i].position,part.bones[i].position));
            log.AppendLine("MEASURED source imported axes/bind difference "+error+"; joint pivot distance "+pivotError+" for "+name);
            Check(pivotError<.05f,"source belongs to the compatible Sidekick fit family "+name+" joint delta="+pivotError,log);
            // Match the existing fitted rig's anatomical pivots by the source's own skin weights. Do not import its
            // differently oriented bones, replace the Avatar, or move gameplay/character skeleton transforms.
            var shift=targetBones.Select((b,i)=>original.transform.worldToLocalMatrix.MultiplyVector(b.position-part.bones[i].position)).ToArray();var fittedWeights=mesh.boneWeights;
            for(int i=0;i<v.Length;i++){var w=fittedWeights[i];v[i]+=shift[w.boneIndex0]*w.weight0+shift[w.boneIndex1]*w.weight1+shift[w.boneIndex2]*w.weight2+shift[w.boneIndex3]*w.weight3;}
            if(HeroId=="titan"&&code=="10TORS")
            {
                // Remodel the rigged anatomical torso into a broad faceted breastplate. No knight lacing or duplicated chest.
                for(int i=0;i<v.Length;i++)
                {
                    float band=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.02f,1.12f,v[i].y))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.39f,1.49f,v[i].y)));
                    float front=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.01f,.07f,v[i].z));v[i].x*=1+band*.09f;v[i].z+=band*front*.024f;
                }
            }
            int type=int.Parse(code.Substring(0,2));var u=v.Select(p=>colorCells[type==17||type==13||type==14||type==15||type==16||type==18&&p.y<.45f||type==19&&p.y<.45f?SuitRole.Secondary:type==10&&Mathf.Abs(p.y-.96f)<.025f?SuitRole.Trim:type==10&&Mathf.Abs(p.x)>.115f?SuitRole.Secondary:SuitRole.Primary]).ToArray();
            Add(v,n,u,mesh.boneWeights,mesh.triangles,targetBones,binds);sources.Add("SUIT "+name+" (feminine="+Feminine+", skinny="+Skinny+"; clothing-only UVs)");
        }
        // Compact to weighted entries only; keep the original character's full skeleton/Avatar in its prefab.
        var used=weights.SelectMany(w=>new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}).Where(x=>x.Item2>0).Select(x=>x.Item1).Distinct().OrderBy(x=>x).ToArray();var compact=new int[bones.Count];for(int i=0;i<used.Length;i++)compact[used[i]]=i;
        for(int i=0;i<weights.Count;i++){var w=weights[i];w.boneIndex0=compact[w.boneIndex0];w.boneIndex1=compact[w.boneIndex1];w.boneIndex2=compact[w.boneIndex2];w.boneIndex3=compact[w.boneIndex3];weights[i]=w;}
        var body=new Mesh{name=SuitId};body.SetVertices(vertices);body.SetNormals(normals);body.SetUVs(0,uv);body.boneWeights=weights.ToArray();body.bindposes=used.Select(i=>bindposes[i]).ToArray();body.SetTriangles(triangles,0);body.RecalculateNormals();body.RecalculateBounds();body=Save(body,SuitId);
        var module=Resources.Load<CosmeticCatalog>(ModulePath);var definition=module.Find(SuitId,CosmeticSlot.Suit);definition.Mesh=body;definition.Bones=used.Select(i=>PathOf(bones[i],hero.CharacterPrefab.transform)).ToArray();definition.DisplayName=HeroId=="vector"?"Tactical Weave":HeroId=="titan"?"Guardian plated suit":"Energy Weave";
        Check(body.vertexCount<suit.Body.vertexCount&&body.bindposes.Length<=88,"optimized suit budget "+body.vertexCount+" vertices / "+body.bindposes.Length+" bones",log);
        foreach(var source in sources)log.AppendLine("ASSETS "+source);
        BakeEmblems(body,definition.Bones,hero,module,log);ForgeHeadgearAuthoring.Bake(hero,module,definition,log);EditorUtility.SetDirty(module);AssetDatabase.SaveAssets();
    }
    static void BakeEmblems(Mesh body,string[] bodyBones,HeroDefinition hero,CosmeticCatalog module,StringBuilder log)
    {
        var v=body.vertices;var tri=body.triangles;float Surface(float x,float y)
        {
            float z=float.NegativeInfinity;for(int i=0;i<tri.Length;i+=3){var a=v[tri[i]];var b=v[tri[i+1]];var c=v[tri[i+2]];float d=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);if(Mathf.Abs(d)<1e-9f)continue;float u=((b.y-c.y)*(x-c.x)+(c.x-b.x)*(y-c.y))/d,w=((c.y-a.y)*(x-c.x)+(a.x-c.x)*(y-c.y))/d;if(u>=-1e-5f&&w>=-1e-5f&&u+w<=1.00001f)z=Mathf.Max(z,u*a.z+w*b.z+(1-u-w)*c.z);}
            if(float.IsNegativeInfinity(z))throw new Exception("Badge outside suit");return z;
        }
        var cell=(Vector2)hero.Suit.Swatches.First(s=>s.Role==SuitRole.Trim).Cell/16+Vector2.one/32;
        foreach(string kind in HeroId=="vector"?new[]{"chevron","bolt"}:HeroId=="titan"?new[]{"shield","core"}:new[]{"star","core"})
        {
            var p=new List<Vector2>();var t=new List<int>();float cy=HeroId=="nova"?1.245f:1.285f;
            if(kind=="chevron"){p.AddRange(new[]{new Vector2(-.06f,cy+.035f),new Vector2(0,cy-.025f),new Vector2(.06f,cy+.035f),new Vector2(.06f,cy+.005f),new Vector2(0,cy-.055f),new Vector2(-.06f,cy+.005f)});t.AddRange(ForgeHeadgearAuthoring.Triangulate(p));}
            else if(kind=="bolt"){p.AddRange(new[]{new Vector2(.015f,cy+.065f),new Vector2(-.045f,cy-.005f),new Vector2(-.008f,cy-.005f),new Vector2(-.025f,cy-.065f),new Vector2(.045f,cy+.012f),new Vector2(.009f,cy+.012f)});t.AddRange(ForgeHeadgearAuthoring.Triangulate(p));}
            else if(kind=="shield"){p.Add(new Vector2(0,cy));p.AddRange(new[]{new Vector2(-.055f,cy+.05f),new Vector2(-.05f,cy-.012f),new Vector2(0,cy-.065f),new Vector2(.05f,cy-.012f),new Vector2(.055f,cy+.05f)});for(int i=0;i<5;i++)t.AddRange(new[]{0,1+i,1+(i+1)%5});}
            else if(kind=="star"){p.Add(new Vector2(0,cy));for(int i=0;i<16;i++){float a=i*Mathf.PI/8,r=i%2==0?.055f:.025f;p.Add(new Vector2(Mathf.Cos(a)*r,cy+Mathf.Sin(a)*r));t.AddRange(new[]{0,1+i,1+(i+1)%16});}}
            else{foreach(float r in new[]{.052f,.030f})for(int i=0;i<16;i++){float a=i*Mathf.PI/8;p.Add(new Vector2(Mathf.Cos(a)*r,cy+Mathf.Sin(a)*r));}for(int i=0;i<16;i++)t.AddRange(new[]{i,(i+1)%16,16+i,(i+1)%16,16+(i+1)%16,16+i});}
            Vector3[] points=null;float min=0;
            for(int refinement=0;refinement<5;refinement++)
            {
                points=p.Select(q=>new Vector3(q.x,q.y,Surface(q.x,q.y)+.006f)).ToArray();min=float.PositiveInfinity;
                for(int i=0;i<t.Count;i+=3)foreach(var bary in new[]{new Vector3(1f/3,1f/3,1f/3),new Vector3(.5f,.5f,0),new Vector3(.5f,0,.5f),new Vector3(0,.5f,.5f)}){var q=points[t[i]]*bary.x+points[t[i+1]]*bary.y+points[t[i+2]]*bary.z;min=Mathf.Min(min,q.z-Surface(q.x,q.y));}
                if(min>.001f)break;
                if(refinement==4){float lift=.002f-min;Check(lift<.015f,"bounded emblem stand-off "+HeroId+"/"+kind,log);for(int j=0;j<points.Length;j++)points[j]+=Vector3.forward*lift;min+=lift;break;}
                var edges=new Dictionary<(int,int),int>();int Mid(int a,int b){var key=a<b?(a,b):(b,a);if(edges.TryGetValue(key,out int j))return j;j=p.Count;p.Add((p[a]+p[b])*.5f);edges[key]=j;return j;}
                var next=new List<int>();for(int i=0;i<t.Count;i+=3){int a=t[i],b=t[i+1],c=t[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);next.AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});}t=next;
            }
            Check(min>.001f,"authored "+kind+" triangle/corner torso clearance >= "+min+"m",log);int spine=Array.FindIndex(bodyBones,b=>b.EndsWith("/spine_03"));if(spine<0)throw new Exception("No chest bone");
            string emblemId=HeroId=="vector"?"vector-emblem-"+kind:HeroId+"_emblem_"+kind;var mesh=new Mesh{name=emblemId};mesh.vertices=points;mesh.normals=Enumerable.Repeat(Vector3.forward,points.Length).ToArray();mesh.uv=Enumerable.Repeat(cell,points.Length).ToArray();mesh.boneWeights=Enumerable.Repeat(new BoneWeight{weight0=1},points.Length).ToArray();mesh.bindposes=new[]{body.bindposes[spine]};mesh.triangles=t.ToArray();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);
            var d=module.Find(mesh.name,CosmeticSlot.Emblem);d.Mesh=mesh;d.Bones=new[]{bodyBones[spine]};log.AppendLine($"MEASURED {mesh.name}: {mesh.vertexCount} vertices / {t.Count/3} triangles / 1 existing spine bone");
        }
    }
}
#endif
