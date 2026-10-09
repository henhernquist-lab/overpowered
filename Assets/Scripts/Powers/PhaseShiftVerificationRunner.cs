#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed class PhaseShiftVerificationRunner : SessionVerificationRunner
{
    public string Stage;
    protected override string Folder=>"Verification/PhaseShift/";
    protected override string ResultFile=>Stage+".txt";
    protected override IEnumerator Checks()
    {
        yield return Enter("fire","strength");
        yield return DenseCombat();
    }
    IEnumerator DenseCombat()
    {
        // Same seeded city, camera and 18 real attacking actors before and after. Restore health only in this harness.
        foreach(var n in W.Npcs.ToArray())if(n!=null)Destroy(n.gameObject);
        yield return null;
        PlaceHero(W.City.Spawn+Vector3.up*.1f,true);
        var crowd=new CityNpc[18];
        for(int i=0;i<crowd.Length;i++)
        {
            var a=Resources.Load<EnemyArchetype>("Enemies/"+(i%3==0?"Gunner":i%3==1?"Brute":"Rusher"));
            crowd[i]=CityNpc.Spawn(W,W.City.Spawn+Quaternion.Euler(0,i*20,0)*Vector3.forward*6,NpcRole.Criminal,a);
            Check(crowd[i]!=null,"Dense actor spawned "+i);crowd[i].AlwaysAggro=true;
        }
        var health=typeof(WorldSession).GetProperty("Health");
        float until=Time.time+4;while(Time.time<until){health.SetValue(W,W.MaxHealth);yield return null;}
        var target=new RenderTexture(1280,720,24);target.Create();var previous=Cam.targetTexture;Cam.targetTexture=target;
        int frames=0;double start=Time.realtimeSinceStartupAsDouble;long alloc=GC.GetAllocatedBytesForCurrentThread();
        int releases=crowd.Sum(n=>n.Releases);
        while(Time.realtimeSinceStartupAsDouble-start<8)
        {
            health.SetValue(W,W.MaxHealth);Cam.Render();frames++;yield return null;
        }
        double seconds=Time.realtimeSinceStartupAsDouble-start;long bytes=GC.GetAllocatedBytesForCurrentThread()-alloc;
        Cam.targetTexture=previous;target.Release();Destroy(target);
        Check(crowd.Sum(n=>n.Releases)>releases,"Dense real enemy attacks occurred during measurement.");
        Log($"MEASURED {Stage} dense combat 18 enemies 1280x720: FPS={frames/seconds:F2}, mean={seconds*1000/frames:F3}ms, whole-frame GC={bytes/(double)frames:F0} B/frame, frames={frames}, releases={crowd.Sum(n=>n.Releases)-releases}, materials={Resources.FindObjectsOfTypeAll<Material>().Length}, meshes={Resources.FindObjectsOfTypeAll<Mesh>().Length}, renderers={FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length}, objects={FindObjectsByType<Transform>(FindObjectsInactive.Include).Length}, particles={FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length}.");
        Capture(Stage+"-dense.png");
    }
}
#endif
