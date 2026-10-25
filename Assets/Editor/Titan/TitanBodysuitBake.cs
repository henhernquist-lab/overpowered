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
public static class TitanBodysuitBake
{
    const string HeroId="titan",Folder="Assets/Resources/Forge/Cosmetics/Titan";
    const string SuitId="titan_suit_guardian";
    static readonly string[] Codes={"17HIPS","18LEGL","19LEGR"};
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
        try{Build(log);}catch(Exception e){log.AppendLine("FAIL "+e);exit=1;}
        Directory.CreateDirectory("Verification/TitanCustomization");File.WriteAllText("Verification/TitanCustomization/native-bake.txt",log.ToString());Debug.Log(log.ToString());EditorApplication.Exit(exit);
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
        // Keep fitted face, armor torso/arms/gauntlets and boots. Remove plumed helmet, beard, skirt and accessory geometry.
        var allFiles=Directory.GetFiles("Assets/Synty/SidekickCharacters/Resources/Meshes","*.fbx",SearchOption.AllDirectories);
        var recipe=Regex.Matches(File.ReadAllText("Assets/Synty/SidekickCharacters/Characters/Starter/Starter_01/Starter_01.sk"),@"^- Name: (.+)$",RegexOptions.Multiline).Cast<Match>().Select(x=>x.Groups[1].Value.Trim()).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var src=suit.Body;var sv=src.vertices;var sn=src.normals;var su=src.uv;var sw=src.boneWeights;var st=src.triangles;var keep=new List<int>();int cursor=0;
        foreach(var name in recipe)
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(allFiles.Single(f=>System.IO.Path.GetFileNameWithoutExtension(f)==name)).GetComponentInChildren<SkinnedMeshRenderer>();int count=p.sharedMesh.vertexCount;var pu=p.sharedMesh.uv;
            int type=int.Parse(name.Split('_')[4].Substring(0,2));bool retainedPart=type<=8&&type!=2||type>=35||type>=10&&type<=16||type==20||type==21;
            Check(cursor+count<=sv.Length,"source part vertex range "+name,log);
            bool Match(int start)=>start>=0&&start+count<=su.Length&&pu.Where((v,i)=>(v-su[start+i]).sqrMagnitude>=1e-10f).Any()==false;
            // The installed combined Starter_01 has different shoulder-attachment vertex counts from its .sk source recipe.
            // After those excluded attachments, recover the head by its ENTIRE exact UV sequence; never guess a face range.
            if(type==1&&!Match(cursor))
            {
                var candidates=Enumerable.Range(Mathf.Max(0,cursor-64),129).Where(Match).ToArray();
                Check(candidates.Length==1,"unique exact full head UV sequence after excluded source-version attachment drift",log);
                log.AppendLine("MEASURED excluded attachment recipe drift="+(candidates[0]-cursor)+" vertices; recovered head start="+candidates[0]);cursor=candidates[0];
            }
            int matched=pu.Where((v,i)=>(v-su[cursor+i]).sqrMagnitude<1e-10f).Count();
            if(retainedPart){Check(matched==count,"exact fitted retained UV range "+name,log);keep.AddRange(Enumerable.Range(cursor,count));sources.Add("FITTED "+name);}
            else log.AppendLine("REMOVED "+name+"; source UV matches="+matched+"/"+count+"; no excluded surface copied");
            cursor+=count;
        }
        Check(cursor==sv.Length,"all original geometry accounted for before accessory removal",log);
        var remap=Enumerable.Repeat(-1,sv.Length).ToArray();for(int i=0;i<keep.Count;i++)remap[keep[i]]=i;
        var retained=new List<int>();for(int i=0;i<st.Length;i+=3){int a=remap[st[i]],b=remap[st[i+1]],c=remap[st[i+2]];if(a<0&&b<0&&c<0)continue;if(a<0||b<0||c<0)throw new Exception("Part boundary crosses triangle");retained.Add(a);retained.Add(b);retained.Add(c);}
        Add(keep.Select(i=>sv[i]).ToArray(),keep.Select(i=>sn[i]).ToArray(),keep.Select(i=>su[i]).ToArray(),keep.Select(i=>sw[i]).ToArray(),retained.ToArray(),legacyBones,src.bindposes);
        foreach(var code in Codes)
        {
            string name="SK_HUMN_BASE_01_"+code+"_HU01";var part=AssetDatabase.LoadAssetAtPath<GameObject>(allFiles.Single(f=>System.IO.Path.GetFileNameWithoutExtension(f)==name)).GetComponentInChildren<SkinnedMeshRenderer>();var mesh=part.sharedMesh;var v=mesh.vertices;var n=mesh.normals;
            // TITAN uses the neutral masculine Sidekick fit; no blendshape offsets are needed.
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
            int type=int.Parse(code.Substring(0,2));var u=v.Select(p=>colorCells[type==17||type==13||type==14||type==15||type==16||type==18&&p.y<.45f||type==19&&p.y<.45f?SuitRole.Secondary:type==10&&Mathf.Abs(p.y-.96f)<.025f?SuitRole.Trim:type==10&&Mathf.Abs(p.x)>.115f?SuitRole.Secondary:SuitRole.Primary]).ToArray();
            Add(v,n,u,mesh.boneWeights,mesh.triangles,targetBones,binds);sources.Add("SUIT "+name+" (masculine, neutral body shape; clothing-only UVs)");
        }
        // Compact to weighted entries only; keep the original character's full skeleton/Avatar in its prefab.
        var used=weights.SelectMany(w=>new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}).Where(x=>x.Item2>0).Select(x=>x.Item1).Distinct().OrderBy(x=>x).ToArray();var compact=new int[bones.Count];for(int i=0;i<used.Length;i++)compact[used[i]]=i;
        for(int i=0;i<weights.Count;i++){var w=weights[i];w.boneIndex0=compact[w.boneIndex0];w.boneIndex1=compact[w.boneIndex1];w.boneIndex2=compact[w.boneIndex2];w.boneIndex3=compact[w.boneIndex3];weights[i]=w;}
        var body=new Mesh{name=SuitId};body.SetVertices(vertices);body.SetNormals(normals);body.SetUVs(0,uv);body.boneWeights=weights.ToArray();body.bindposes=used.Select(i=>bindposes[i]).ToArray();body.SetTriangles(triangles,0);body.RecalculateBounds();body=Save(body,SuitId);
        var module=Resources.Load<CosmeticCatalog>(TitanCosmeticContent.ResourcePath);var definition=module.Find(SuitId,CosmeticSlot.Suit);definition.Mesh=body;definition.Bones=used.Select(i=>PathOf(bones[i],hero.CharacterPrefab.transform)).ToArray();definition.DisplayName="Guardian · fitted suit";
        Check(body.vertexCount<suit.Body.vertexCount&&body.bindposes.Length<=88,"optimized suit budget "+body.vertexCount+" vertices / "+body.bindposes.Length+" bones",log);
        foreach(var source in sources)log.AppendLine("ASSETS "+source);
        BakeEmblems(body,definition.Bones,hero,module,log);EditorUtility.SetDirty(module);AssetDatabase.SaveAssets();
    }
    static void BakeEmblems(Mesh body,string[] bodyBones,HeroDefinition hero,CosmeticCatalog module,StringBuilder log)
    {
        var v=body.vertices;var tri=body.triangles;float Surface(float x,float y)
        {
            float z=float.NegativeInfinity;for(int i=0;i<tri.Length;i+=3){var a=v[tri[i]];var b=v[tri[i+1]];var c=v[tri[i+2]];float d=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);if(Mathf.Abs(d)<1e-9f)continue;float u=((b.y-c.y)*(x-c.x)+(c.x-b.x)*(y-c.y))/d,w=((c.y-a.y)*(x-c.x)+(a.x-c.x)*(y-c.y))/d;if(u>=-1e-5f&&w>=-1e-5f&&u+w<=1.00001f)z=Mathf.Max(z,u*a.z+w*b.z+(1-u-w)*c.z);}
            if(float.IsNegativeInfinity(z))throw new Exception("Badge outside suit");return z;
        }
        var cell=(Vector2)hero.Suit.Swatches.First(s=>s.Role==SuitRole.Trim).Cell/16+Vector2.one/32;
        foreach(string kind in new[]{"shield","core"})
        {
            var p=new List<Vector2>();var t=new List<int>();float cy=1.3f;
            if(kind=="shield"){p.Add(new Vector2(0,cy));p.AddRange(new[]{new Vector2(-.045f,cy+.041f),new Vector2(-.041f,cy-.011f),new Vector2(0,cy-.056f),new Vector2(.041f,cy-.011f),new Vector2(.045f,cy+.041f)});for(int i=0;i<5;i++)t.AddRange(new[]{0,1+i,1+(i+1)%5});}
            else{foreach(float r in new[]{.042f,.024f})for(int i=0;i<16;i++){float a=i*Mathf.PI/8;p.Add(new Vector2(Mathf.Cos(a)*r,cy+Mathf.Sin(a)*r));}for(int i=0;i<16;i++)t.AddRange(new[]{i,(i+1)%16,16+i,(i+1)%16,16+(i+1)%16,16+i});}
            Vector3[] points=null;float min=0;
            for(int refinement=0;refinement<3;refinement++)
            {
                points=p.Select(q=>new Vector3(q.x,q.y,Surface(q.x,q.y)+.006f)).ToArray();min=float.PositiveInfinity;
                for(int i=0;i<t.Count;i+=3)foreach(var bary in new[]{new Vector3(1f/3,1f/3,1f/3),new Vector3(.5f,.5f,0),new Vector3(.5f,0,.5f),new Vector3(0,.5f,.5f)}){var q=points[t[i]]*bary.x+points[t[i+1]]*bary.y+points[t[i+2]]*bary.z;min=Mathf.Min(min,q.z-Surface(q.x,q.y));}
                if(min>.001f)break;
                if(refinement==2)
                {
                    // Sharp molded breastplate ridges can remain between projection samples. Keep badge complexity bounded;
                    // lift the finished tessellation as a whole by the measured residual rather than burying its center.
                    float lift=.002f-min;Check(lift<.015f,"bounded badge stand-off correction "+lift+"m",log);
                    for(int j=0;j<points.Length;j++)points[j]+=Vector3.forward*lift;min+=lift;
                    log.AppendLine("MEASURED "+kind+" residual ridge stand-off correction="+lift+"m");break;
                }
                var edges=new Dictionary<(int,int),int>();int Mid(int a,int b){var key=a<b?(a,b):(b,a);if(edges.TryGetValue(key,out int j))return j;j=p.Count;p.Add((p[a]+p[b])*.5f);edges[key]=j;return j;}
                var next=new List<int>();for(int i=0;i<t.Count;i+=3){int a=t[i],b=t[i+1],c=t[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);next.AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});}t=next;
            }
            Check(min>.001f,"authored "+kind+" triangle/corner torso clearance >= "+min+"m",log);int spine=Array.FindIndex(bodyBones,b=>b.EndsWith("/spine_03"));if(spine<0)throw new Exception("No chest bone");
            var mesh=new Mesh{name="titan_emblem_"+kind};mesh.vertices=points;mesh.normals=Enumerable.Repeat(Vector3.forward,points.Length).ToArray();mesh.uv=Enumerable.Repeat(cell,points.Length).ToArray();mesh.boneWeights=Enumerable.Repeat(new BoneWeight{weight0=1},points.Length).ToArray();mesh.bindposes=new[]{body.bindposes[spine]};mesh.triangles=t.ToArray();mesh.RecalculateBounds();mesh=Save(mesh,mesh.name);
            var d=module.Find(mesh.name,CosmeticSlot.Emblem);d.Mesh=mesh;d.Bones=new[]{bodyBones[spine]};log.AppendLine($"MEASURED {mesh.name}: {mesh.vertexCount} vertices / {t.Count/3} triangles / 1 existing spine bone");
        }
    }
}
#endif
