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
        var pool=Final?FireProjectilePool.Instance:null;
        if(Final)Check(pool!=null&&FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length==FireProjectilePool.Capacity&&pool.ActiveCount==0,
            "Fire projectile objects are all prebuilt before the first cast.");
        var target=Wall(new Vector3(0,151,16),new Vector3(3,3,.3f));
        Aim(target.transform.position);yield return null;
        int before=FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length;
        int materials=Resources.FindObjectsOfTypeAll<Material>().Length,meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        Check(W.Powers.Use(fire),"Real Fire Blast activation through the equip gate.");
        var shot=FindObjectsByType<PowerProjectile>().FirstOrDefault();Check(shot!=null,"Physical Fire projectile exists in travel.");
        var body=shot.GetComponent<Rigidbody>();var collider=shot.GetComponent<SphereCollider>();
        if(Final)
        {
            var visual=shot.Visual;
            Check(shot.transform.localScale==Vector3.one&&Mathf.Abs(collider.radius-.175f)<.001f&&
                !body.useGravity&&body.collisionDetectionMode==CollisionDetectionMode.ContinuousDynamic,
                "Pooled projectile retains the original 0.175m physics sphere and continuous collision.");
            Check(visual!=null&&visual.Body!=null&&visual.Core!=null&&visual.Taper!=null&&
                visual.Body.sharedMaterial==CityMaterials.Get(CityColor.Fire)&&
                visual.Core.sharedMaterial==CityMaterials.GetVfx(CityColor.Cream)&&
                visual.Taper.sharedMaterial==CityMaterials.GetVfx(CityColor.Red)&&
                visual.Body.GetComponent<MeshFilter>().sharedMesh==visual.Core.GetComponent<MeshFilter>().sharedMesh,
                "Warm body, hot leading core and red taper use shared palette materials and shared bolt mesh.");
            Check(Vector3.Dot(shot.transform.forward,body.linearVelocity.normalized)>.999f&&
                visual.Core.transform.localPosition.z>visual.Body.transform.localPosition.z&&
                visual.Taper.transform.localPosition.z<visual.Body.transform.localPosition.z,
                "Directional visible layers follow physical travel, with core ahead and taper behind.");
        }
        var rootRenderer=shot.GetComponent<Renderer>();
        Log($"MEASURED fire launch: instances {before}->{FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length}, size={shot.transform.localScale.x:F3}, colliderRadius={collider.radius:F3}, speed={body.linearVelocity.magnitude:F3}, material={(rootRenderer!=null?rootRenderer.sharedMaterial.name:"visual children")}, meshes={meshes}->{Resources.FindObjectsOfTypeAll<Mesh>().Length}, materials={materials}->{Resources.FindObjectsOfTypeAll<Material>().Length}.");
        Capture(Final?"final-third-person.png":"baseline-third-person.png");
        if(Final)for(int i=0;i<4;i++)yield return new WaitForFixedUpdate();
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
        if(Final&&shot!=null)
        {
            Check(shot.Exploded&&pool.ImpactCount==1,"Impact presentation starts only after the physical collision.");
            var impact=FindObjectsByType<FireImpactRing>(FindObjectsInactive.Include).First(x=>x.RingsShown>0);
            Check(Vector3.Distance(impact.Contact,shot.LastImpactPoint)<.001f&&impact.gameObject.activeSelf,
                "Pooled expanding ring is placed at the real collision contact point.");
            yield return new WaitForSeconds(.07f);
            Cam.transform.position=shot.LastImpactPoint+new Vector3(2f,1f,-3f);
            Cam.transform.LookAt(shot.LastImpactPoint);Capture("final-impact.png");
        }
        Log($"MEASURED fire after impact: projectile objects={FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length}, materials={Resources.FindObjectsOfTypeAll<Material>().Length}, meshes={Resources.FindObjectsOfTypeAll<Mesh>().Length}.");
        if(!Final)yield break;

        // Criminal controls are not culled by the live Heat-based police reconciliation.
        var near=Actor(new Vector3(1.2f,150,14.5f),NpcRole.Criminal,1000);
        var far=Actor(new Vector3(4.5f,150,14.5f),NpcRole.Criminal,1000);
        yield return null;
        int fixedMaterials=Resources.FindObjectsOfTypeAll<Material>().Length;
        int fixedMeshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        int fixedRenderers=FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length;
        int fixedParticles=FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length;
        int recycled=pool.Recycled;float nearHealth=near.Health,farHealth=far.Health;
        for(int i=0;i<4;i++)
        {
            Aim(target.transform.position);fire.Cooldown=0;fire.Charges=stats.Charges;W.Powers.Tick(100,true);
            int impacts=pool.ImpactCount;
            Check(W.Powers.Use(fire),"Repeated real Fire cast accepted through PowerUser, cast "+i+".");
            var live=FindObjectsByType<PowerProjectile>().FirstOrDefault(p=>!p.Exploded);
            Check(live!=null&&near.Health==nearHealth&&far.Health==farHealth,
                "Damage waits for physical impact; near and outside-radius controls are unchanged during travel, cast "+i+".");
            until=Time.time+2;while(live.gameObject.activeInHierarchy&&Time.time<until)yield return null;
            Check(live.Exploded&&pool.ImpactCount==impacts+1&&pool.ActiveCount==0,
                "One real wall collision, one pooled contact response, and projectile returned, cast "+i+".");
            if(i==0)
            {
                Log($"MEASURED Fire radius control: impact={live.LastImpactPoint}, near={near.transform.position} health {nearHealth}->{near.Health}, outside={far.transform.position} health {farHealth}->{far.Health}.");
                Check(near.Health<nearHealth&&far.Health==farHealth,
                    "Real 3m Fire blast damages the nearby NPC only at impact; outside-radius NPC untouched.");
            }
            nearHealth=near.Health;farHealth=far.Health;
        }
        Check(pool.Recycled>=recycled+4&&FindObjectsByType<PowerProjectile>(FindObjectsInactive.Include).Length==FireProjectilePool.Capacity&&
            Resources.FindObjectsOfTypeAll<Material>().Length==fixedMaterials&&Resources.FindObjectsOfTypeAll<Mesh>().Length==fixedMeshes&&
            FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length==fixedRenderers&&
            FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length==fixedParticles,
            "Four repeated casts reuse all projectile, renderer, particle, material and mesh resources.");

        var rig=Cam.GetComponent<ThirdPersonCamera>();rig.enabled=true;yield return null;
        Check(rig.ToggleView()&&rig.FirstPerson,"Switch to real first-person Fire Blast camera.");
        rig.SetLook(0,0);yield return null;yield return null;
        Check(W.Powers.AimOrigin==Cam.transform.position,"First-person gameplay aim still begins at the eye camera.");
        fire.Cooldown=0;fire.Charges=stats.Charges;W.Powers.Tick(100,true);
        Check(W.Powers.Use(fire),"First-person real Fire cast accepted.");
        var firstShot=FindObjectsByType<PowerProjectile>().First(p=>!p.Exploded);
        var viewport=Cam.WorldToViewportPoint(firstShot.Visual.transform.position);
        Check(viewport.z>Cam.nearClipPlane&&viewport.x>.52f&&viewport.y<.5f&&
            Vector3.Dot(firstShot.transform.forward,W.Powers.AimDirection)>.999f,
            $"First-person visible entry is camera-safe and below/right of the unchanged center aim ({viewport}).");
        Capture("final-first-person-launch.png");
        until=Time.time+2;while(firstShot.gameObject.activeInHierarchy&&Time.time<until)yield return null;
        var close=Wall(Cam.transform.position+Cam.transform.forward*.65f,new Vector3(2,2,.15f));
        fire.Cooldown=0;fire.Charges=stats.Charges;W.Powers.Tick(100,true);
        int beforeClose=pool.ImpactCount;Check(W.Powers.Use(fire),"Clear eye permits near-wall Fire cast.");
        var closeShot=FindObjectsByType<PowerProjectile>().First(p=>!p.Exploded);
        float face=close.GetComponent<Collider>().bounds.min.z;
        Check(closeShot.transform.position.z<face&&closeShot.Visual.Body.bounds.max.z<=face+.02f&&
            closeShot.Visual.Core.bounds.max.z<=face+.02f,
            "Near-wall visual and physics launch stay on the camera side of the obstacle.");
        until=Time.time+1;while(closeShot.gameObject.activeInHierarchy&&Time.time<until)yield return null;
        Check(closeShot.Exploded&&pool.ImpactCount==beforeClose+1&&closeShot.LastImpactPoint.z<=face+.2f,
            "Nearby wall intercepts the real projectile and receives the contact effect.");
        Capture("final-first-person-close-wall.png");
    }
}
#endif
