#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

public sealed class FireVisualVerificationRunner:SessionVerificationRunner
{
    public bool Final;
    protected override string Folder=>"Verification/FireRedesign/";
    protected override string ResultFile=>Final?"final-results.txt":"baseline-results.txt";
    protected override IEnumerator Checks()
    {
        yield return Enter("fire","strength");Isolate();
        var fire=Runtime("fire");var stats=W.Powers.Stats(fire);
        Check(fire.Definition.ProjectileSize==.35f&&fire.Definition.ProjectileSpeed==28f&&stats.Radius==3f&&fire.Definition.Damage==45f,
            "Fire data baseline: 0.35m sphere collider, 28m/s, 3m blast, 45 damage.");
        var target=Wall(new Vector3(0,151,16),new Vector3(3,3,.3f));
        Aim(target.transform.position);yield return null;
        int before=FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length;
        int materials=Resources.FindObjectsOfTypeAll<Material>().Length,meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        Check(W.Powers.Use(fire),"Real Fire Blast activation through the equip gate.");
        var shot=FindObjectsByType<PowerProjectile>().FirstOrDefault();Check(shot!=null,"Physical Fire projectile exists in travel.");
        var body=shot.GetComponent<Rigidbody>();var collider=shot.GetComponent<SphereCollider>();
        Log($"MEASURED fire launch: instances {before}->{FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length}, size={shot.transform.localScale.x:F3}, colliderRadius={collider.radius:F3}, speed={body.linearVelocity.magnitude:F3}, material={shot.GetComponent<Renderer>()?.sharedMaterial?.name}, meshes={meshes}->{Resources.FindObjectsOfTypeAll<Mesh>().Length}, materials={materials}->{Resources.FindObjectsOfTypeAll<Material>().Length}.");
        Capture(Final?"final-third-person.png":"baseline-third-person.png");
        Cam.transform.position=shot.transform.position+new Vector3(2.3f,.55f,-.7f);
        Cam.transform.LookAt(shot.transform.position+body.linearVelocity.normalized*2f);
        Capture(Final?"final-side.png":"baseline-side.png");
        var render=new RenderTexture(1280,720,24);render.Create();var old=Cam.targetTexture;Cam.targetTexture=render;
        var watch=Stopwatch.StartNew();int frames=0;long alloc=GC.GetAllocatedBytesForCurrentThread();
        while(watch.Elapsed.TotalSeconds<.4){Cam.Render();frames++;yield return null;}
        long bytes=GC.GetAllocatedBytesForCurrentThread()-alloc;Cam.targetTexture=old;render.Release();Destroy(render);
        Log($"MEASURED fire active 1280x720: FPS={frames/watch.Elapsed.TotalSeconds:F2}, mean={watch.Elapsed.TotalMilliseconds/frames:F3}ms, whole-frame GC={bytes/(double)frames:F0}B/frame, frames={frames}, renderers={FindObjectsByType<Renderer>().Length}, particles={FindObjectsByType<ParticleSystem>().Length}, materials={Resources.FindObjectsOfTypeAll<Material>().Length}, meshes={Resources.FindObjectsOfTypeAll<Mesh>().Length}.");
        float until=Time.time+2;while(shot!=null&&shot.gameObject.activeInHierarchy&&Time.time<until)yield return null;
        Check(shot==null||!shot.gameObject.activeInHierarchy,"Fire projectile finishes on the real wall collision.");
        Log($"MEASURED fire after impact: projectile objects={FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length}, materials={Resources.FindObjectsOfTypeAll<Material>().Length}, meshes={Resources.FindObjectsOfTypeAll<Mesh>().Length}.");
    }
}
#endif
