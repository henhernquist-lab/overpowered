#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Original, fitted low-poly headgear. Cross-sections come from the actual fitted head surface, not a primitive collider.
// All authoring happens once in the Editor; the resulting native meshes use one existing head bone at runtime.
public static class ForgeHeadgearAuthoring
{
    public static int[] Triangulate(List<Vector2> polygon)
    {
        float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        var ids=Enumerable.Range(0,polygon.Count).ToList();float area=0;for(int i=0;i<ids.Count;i++)area+=Cross(polygon[i],polygon[(i+1)%ids.Count]);if(area<0)ids.Reverse();
        var result=new List<int>();int guard=100;
        while(ids.Count>3&&guard-->0)
        {
            bool found=false;for(int i=0;i<ids.Count;i++)
            {
                int a=ids[(i+ids.Count-1)%ids.Count],b=ids[i],c=ids[(i+1)%ids.Count];if(Cross(polygon[b]-polygon[a],polygon[c]-polygon[b])<=0)continue;
                bool inside=ids.Any(j=>j!=a&&j!=b&&j!=c&&Cross(polygon[b]-polygon[a],polygon[j]-polygon[a])>=0&&Cross(polygon[c]-polygon[b],polygon[j]-polygon[b])>=0&&Cross(polygon[a]-polygon[c],polygon[j]-polygon[c])>=0);
                if(inside)continue;result.AddRange(new[]{a,b,c});ids.RemoveAt(i);found=true;break;
            }
            if(!found)throw new Exception("Non-simple emblem contour");
        }
        if(ids.Count!=3)throw new Exception("Emblem triangulation failed");result.AddRange(ids);return result.ToArray();
    }
    static Mesh Save(Mesh mesh,string path)
    {
        var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(old==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        old.Clear(false);old.name=mesh.name;old.vertices=mesh.vertices;old.normals=mesh.normals;old.uv=mesh.uv;old.boneWeights=mesh.boneWeights;old.bindposes=mesh.bindposes;old.triangles=mesh.triangles;old.bounds=mesh.bounds;UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);return old;
    }
    public static void Bake(HeroDefinition hero,CosmeticCatalog module,CosmeticDefinition suit,StringBuilder log)
    {
        var body=suit.Mesh;var v=body.vertices;var weights=body.boneWeights;int head=Array.FindIndex(suit.Bones,b=>b.EndsWith("/head"));if(head<0)throw new Exception("Missing fitted head bone");
        var faceBones=suit.Bones.Select((p,i)=>(p,i)).Where(x=>x.p.Contains("/head")).Select(x=>x.i).ToHashSet();
        bool IsHead(BoneWeight w)=>(faceBones.Contains(w.boneIndex0)?w.weight0:0)+(faceBones.Contains(w.boneIndex1)?w.weight1:0)+(faceBones.Contains(w.boneIndex2)?w.weight2:0)+(faceBones.Contains(w.boneIndex3)?w.weight3:0)>.49f;
        var selected=weights.Select(IsHead).ToArray();var headPoints=v.Where((p,i)=>selected[i]).ToArray();var bounds=new Bounds(headPoints[0],Vector3.zero);foreach(var p in headPoints)bounds.Encapsulate(p);
        if(bounds.size.y<.15f||bounds.size.y>.5f||bounds.size.x>.4f)throw new Exception("Invalid anatomical head bounds "+bounds);
        var indices=body.triangles;var headTriangles=new List<(Vector3,Vector3,Vector3)>();for(int i=0;i<indices.Length;i+=3)if(selected[indices[i]]&&selected[indices[i+1]]&&selected[indices[i+2]])headTriangles.Add((v[indices[i]],v[indices[i+1]],v[indices[i+2]]));
        float centerZ=(bounds.min.z+bounds.max.z)*.5f;
        var source=hero.CharacterPrefab.GetComponentInChildren<SkinnedMeshRenderer>();var eye=hero.CharacterPrefab.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftEye);
        float eyeY=eye!=null?source.transform.InverseTransformPoint(eye.position).y:Mathf.Lerp(bounds.min.y,bounds.max.y,.63f);float eyeT=Mathf.Clamp(Mathf.InverseLerp(bounds.min.y,bounds.max.y,eyeY),.48f,.76f);
        List<Vector2> Section(float y)
        {
            var points=new List<Vector2>();void Edge(Vector3 a,Vector3 b){if((a.y-y)*(b.y-y)>0||Mathf.Abs(a.y-b.y)<1e-7f)return;var p=Vector3.Lerp(a,b,(y-a.y)/(b.y-a.y));points.Add(new Vector2(p.x,p.z-centerZ));}
            foreach(var tri in headTriangles){Edge(tri.Item1,tri.Item2);Edge(tri.Item2,tri.Item3);Edge(tri.Item3,tri.Item1);}
            if(points.Count<3)points=headPoints.OrderBy(p=>Mathf.Abs(p.y-y)).Take(Mathf.Max(12,headPoints.Length/20)).Select(p=>new Vector2(p.x,p.z-centerZ)).ToList();return points;
        }
        Vector3 Surface(float t,float angle,float outset,float bulk)
        {
            float y=Mathf.LerpUnclamped(bounds.min.y,bounds.max.y,t);var dir=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));var section=Section(Mathf.Clamp(y,bounds.min.y+.001f,bounds.max.y-.001f));
            float radius=Mathf.Max(.008f,section.Max(p=>Vector2.Dot(p,dir)));float crown=t>.94f?Mathf.Lerp(1,.15f,Mathf.InverseLerp(.94f,1.035f,t)):1;
            float r=(radius+outset)*crown;return new Vector3(dir.x*r*bulk,y,centerZ+dir.y*r);
        }
        foreach(var definition in module.Items.Where(d=>d.Slot==CosmeticSlot.Mask&&d.Id!=hero.Id+"_mask_unmasked"))
        {
            string id=definition.Id;bool band=id.EndsWith("aegis")||id.EndsWith("spectrum");bool spectrum=id.EndsWith("spectrum"),guardian=id.EndsWith("guardian"),vanguard=id.EndsWith("vanguard"),phantom=id.EndsWith("phantom");
            var points=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();var roles=hero.Suit.Swatches.GroupBy(s=>s.Role).ToDictionary(g=>g.Key,g=>(Vector2)g.First().Cell/16+Vector2.one/32);
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,SuitRole role)
            {
                int k=points.Count;points.AddRange(new[]{a,b,c,d});uv.AddRange(Enumerable.Repeat(roles[role],4));triangles.AddRange(new[]{k,k+1,k+3,k+1,k+2,k+3});
            }
            float outset=guardian ? .018f:phantom ? .012f:.011f;float bulk=guardian?1.09f:phantom?1.015f:1.025f;
            if(band)
            {
                int sectors=spectrum?20:16;float halfAngle=spectrum?1.72f:1.87f;var rows=new[]{0f,.16f,.84f,1f};
                Vector3 Point(int ring,int sector)
                {
                    float angle=Mathf.Lerp(-halfAngle,halfAngle,sector/(float)sectors);float side=Mathf.Abs(Mathf.Sin(angle));
                    float low=eyeT-(spectrum ? .065f:.115f)+(spectrum ? .015f:.055f)*side,high=eyeT+(spectrum ? .07f:.105f)+(spectrum ? .025f:.055f)*side;
                    float t=Mathf.Lerp(low,high,rows[ring]);var p=Surface(t,angle,outset,bulk);p.z+=Mathf.Max(0,Mathf.Cos(angle))*(spectrum ? .010f:.006f);return p;
                }
                for(int j=0;j<3;j++)for(int i=0;i<sectors;i++)Quad(Point(j,i),Point(j,i+1),Point(j+1,i+1),Point(j+1,i),j==1?SuitRole.Secondary:SuitRole.Trim);
            }
            else
            {
                int sectors=24;float[] rows={.015f,.23f,.40f,eyeT-.065f,eyeT+.065f,.82f,.94f,1.025f};rows=rows.Distinct().OrderBy(x=>x).ToArray();
                Vector3 Point(int ring,int sector)
                {
                    float a=sector*Mathf.PI*2/sectors;var p=Surface(rows[ring],a,outset,bulk);float front=Mathf.Max(0,Mathf.Cos(a));
                    if(guardian)p.z+=front*(rows[ring]<.4f?.012f:rows[ring]>.75f?.008f:0);
                    if(phantom)p.z+=front*Mathf.Max(0,1-Mathf.Abs(Mathf.Sin(a))*3)*.008f;
                    return p;
                }
                for(int j=0;j<rows.Length-1;j++)for(int i=0;i<sectors;i++)
                {
                    float angle=(i+.5f)*Mathf.PI*2/sectors;float front=Mathf.Cos(angle);float t=(rows[j]+rows[j+1])*.5f;
                    if(vanguard&&front>.15f&&t<eyeT-.05f)continue;
                    if(!guardian&&!phantom&&!vanguard&&front>.35f&&t<.36f)continue;
                    bool visor=front>.35f&&Mathf.Abs(t-eyeT)<.08f;bool rim=Mathf.Abs(t-eyeT)<.16f&&front>.35f;
                    SuitRole role=visor?SuitRole.Secondary:rim||guardian&&t<.24f||phantom&&Mathf.Abs(Mathf.Sin(angle))<.15f?SuitRole.Trim:SuitRole.Primary;
                    Quad(Point(j,i),Point(j,(i+1)%sectors),Point(j+1,(i+1)%sectors),Point(j+1,i),role);
                }
                int cap=points.Count;points.Add(new Vector3(0,bounds.max.y+.014f,centerZ));uv.Add(roles[SuitRole.Primary]);
                for(int i=0;i<sectors;i++){int k=points.Count;points.Add(Point(rows.Length-1,i));points.Add(Point(rows.Length-1,(i+1)%sectors));uv.Add(roles[SuitRole.Primary]);uv.Add(roles[SuitRole.Primary]);triangles.AddRange(new[]{k,k+1,cap});}
            }
            var mesh=new Mesh{name=id};mesh.SetVertices(points);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.boneWeights=Enumerable.Repeat(new BoneWeight{weight0=1},points.Count).ToArray();mesh.bindposes=new[]{body.bindposes[head]};mesh.RecalculateNormals();mesh.RecalculateBounds();
            string folder="Assets/Resources/Forge/Cosmetics/"+char.ToUpper(hero.Id[0])+hero.Id.Substring(1);definition.Mesh=Save(mesh,folder+"/"+id+".asset");definition.Bones=new[]{suit.Bones[head]};definition.Available=true;definition.UnavailableReason="";
            log.AppendLine($"MEASURED {id}: original fitted headgear, {definition.Mesh.vertexCount} vertices / {triangles.Count/3} triangles / one existing head bone; anatomical bounds={bounds}; eye t={eyeT}");
        }
    }
}
#endif
