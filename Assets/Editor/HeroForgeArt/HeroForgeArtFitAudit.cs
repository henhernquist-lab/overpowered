#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
public static class HeroForgeArtFitAudit
{
    static float Hit(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c)
    {
        Vector3 e1=b-a,e2=c-a,p=Vector3.Cross(direction,e2);float det=Vector3.Dot(e1,p);if(Mathf.Abs(det)<1e-8f)return -1;
        float inverse=1/det;var t=origin-a;float u=Vector3.Dot(t,p)*inverse;if(u<-.00001f||u>1.00001f)return -1;
        var q=Vector3.Cross(t,e1);float v=Vector3.Dot(direction,q)*inverse;if(v<-.00001f||u+v>1.00001f)return -1;float distance=Vector3.Dot(e2,q)*inverse;return distance>0?distance:-1;
    }
    public static void Run()
    {
        int code=0;var log=new StringBuilder();
        try
        {
            var catalog=Resources.Load<CosmeticCatalog>("CosmeticCatalog");
            foreach(var hero in Resources.Load<ForgeCatalog>("ForgeCatalog").Heroes)
            {
                string id=hero.Id=="vector"?"vector_suit_tactical":hero.Id=="titan"?"titan_suit_guardian":"nova_suit_energyweave";var suit=catalog.Find(id,CosmeticSlot.Suit);int bone=Array.FindIndex(suit.Bones,p=>p.EndsWith("/head"));
                var vertices=suit.Mesh.vertices;var weights=suit.Mesh.boneWeights;
                var head=vertices.Where((p,i)=>weights[i].boneIndex0==bone&&weights[i].weight0>.99f).ToArray();var bounds=new Bounds(head[0],Vector3.zero);foreach(var p in head)bounds.Encapsulate(p);var crown=head.Where(p=>p.y>bounds.max.y-.045f).ToArray();
                foreach(var mask in catalog.Items.Where(d=>d.HeroId==hero.Id&&d.Slot==CosmeticSlot.Mask&&d.Mesh!=null&&d.Compatible(hero,id)&&new[]{"sentinel","guardian","vanguard","phantom"}.Any(n=>d.Id.EndsWith(n))))
                {
                    var mv=mask.Mesh.vertices;var triangles=mask.Mesh.triangles;float clearance=float.PositiveInfinity;int misses=0;
                    foreach(var p in crown)
                    {
                        var ray=p-bounds.center;float length=ray.magnitude;ray/=length;float hit=float.PositiveInfinity;
                        for(int i=0;i<triangles.Length;i+=3){float d=Hit(bounds.center,ray,mv[triangles[i]],mv[triangles[i+1]],mv[triangles[i+2]]);if(d>0)hit=Mathf.Min(hit,d);}
                        if(float.IsPositiveInfinity(hit))misses++;else clearance=Mathf.Min(clearance,hit-length);
                    }
                    bool pass=misses==0&&clearance>.0005f;log.AppendLine((pass?"PASS ":"FAIL ")+mask.Id+": "+crown.Length+" actual skull vertices, missing shell rays="+misses+", minimum radial crown clearance="+clearance+"m");
                    if(!pass)throw new Exception("Crown fit failed "+mask.Id);
                }
            }
        }
        catch(Exception e){log.AppendLine("FAIL "+e);code=1;}
        Directory.CreateDirectory("Verification/HeroForgeArt");File.WriteAllText("Verification/HeroForgeArt/crown-fit-audit.txt",log.ToString());Debug.Log(log.ToString());EditorApplication.Exit(code);
    }
}
#endif
