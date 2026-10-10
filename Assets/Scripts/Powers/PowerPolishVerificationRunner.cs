#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

/// Real equipped casts in both views and two lighting conditions. Screenshots are evidence,
/// not an automated aesthetic verdict; the Roster suite owns detailed gameplay math controls.
public sealed class PowerPolishVerificationRunner:SessionVerificationRunner
{
    protected override string Folder=>"Verification/PowerPolish/";
    protected override string ResultFile=>"results.txt";
    ThirdPersonCamera Rig=>Cam.GetComponent<ThirdPersonCamera>();
    WorldLightingDirector Lighting=>WorldLightingDirector.Instance;
    protected override IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        yield return Primary("darkness");
        yield return Primary("lightning");
        yield return Primary("poison");
        yield return Shield();
        yield return CityNightDarkness();
        Log("LIMIT: captures need human assessment for shape, contrast, timing and obstruction; this suite does not declare that visuals look good.");
    }
    IEnumerator EnterPower(string primary,string secondary=null,bool isolated=true)
    {
        yield return Home();var hero=F.Heroes[0];
        Check(Profile.SetLoadout(hero,Power(primary),Power("strength"),
            secondary==null?Profile.EquippedSecondary:Power(secondary),hero.Primary,hero.Secondary),
            "Save legal "+primary+" loadout through progression.");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/free-play"));
        yield return Scene(GameFlow.CityScene);
        Check(W.Powers.IsEquipped(Power(primary))&&
            (secondary==null||W.Powers.SecondaryRuntime.Definition==Power(secondary)),
            "Session equips requested primary and secondary.");
        if(isolated)Isolate();
        Check(Lighting!=null,"Scene lighting director exists for paired day/night captures.");
    }
    void View(bool first)
    {
        if(first)
        {
            Rig.enabled=true;if(!Rig.FirstPerson)Check(Rig.ToggleView(),"Enter first person.");
            Rig.SetLook(0,0);
        }
        else
        {
            if(Rig.FirstPerson){Rig.enabled=true;Check(Rig.ToggleView(),"Return to third person.");}
            Rig.enabled=false;
        }
    }
    IEnumerator Primary(string id)
    {
        yield return EnterPower(id);
        var power=Runtime(id);var status=StatusPowerVfx.Get();var lines=PowerVfx.Get();
        // All palette swatches and fixed pools are warm before resource accounting.
        for(int sample=0;sample<4;sample++)
        {
            bool first=sample>=2,night=(sample&1)!=0;
            View(first);Lighting.SetTime(night?.75f:0f);
            var target=Actor(new Vector3(0,150,first?5:6),NpcRole.Criminal,1000);
            if(first){yield return null;yield return null;}
            else Aim(Chest(target));
            power.Cooldown=0;power.Charges=Mathf.Max(power.Charges,1);
            Check(W.Powers.Use(power),id+" real equipped cast in "+(first?"first":"third")+" person, "+(night?"night":"day")+".");
            if(id=="lightning")Check(LightningEffect.LastVisualStrikes.Count>0&&
                LightningEffect.LastVisualStrikes[0].Target==target,
                "Lightning primary visual points at the actual first recipient.");
            if(id=="darkness")Check(target.Rooted&&status.ActiveRoots>0,
                "Darkness restrained target has a live pooled body cue.");
            if(id=="poison")Check(target.GetComponent<Poisoned>().VisualActive,
                "Poisoned target has a live pooled contamination cue.");
            yield return null;
            Capture(id+"-"+(night?"night":"day")+"-"+(first?"first":"third")+".png");
            UnityEngine.Object.Destroy(target.gameObject);yield return null;
            if(id=="darkness")Check(status.ActiveRoots==0,"Destroyed rooted target returns its restraint lease.");
            if(id=="poison")Check(status.ActivePoisons==0,"Destroyed poisoned target returns its contamination lease.");
            yield return new WaitForSeconds(.55f);
            Check(lines.ActiveLines==0,"Previous short-lived power lines clear before the next camera/lighting capture.");
        }
        View(false);Lighting.SetTime(0);
        yield return new WaitForSeconds(.5f);
        int materials=Resources.FindObjectsOfTypeAll<Material>().Length;
        int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        int objects=FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length;
        int renderers=FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length;
        int pooled=status.GetComponentsInChildren<Transform>(true).Length+lines.GetComponentsInChildren<Transform>(true).Length;
        int misses=status.PoolMisses;
        for(int i=0;i<12;i++)
        {
            var target=Actor(new Vector3(0,150,10),NpcRole.Criminal,1000);
            Aim(Chest(target));W.Powers.Tick(100,true);power.Cooldown=0;power.Charges=Mathf.Max(power.Charges,1);
            bool used=W.Powers.Use(power);
            Check(used,id+" repeated equipped cast "+i+" ("+W.Powers.Message+").");
            UnityEngine.Object.Destroy(target.gameObject);yield return null;
        }
        yield return new WaitForSeconds(.5f);
        Log($"MEASURED {id} warm->stress resources: materials {materials}->{Resources.FindObjectsOfTypeAll<Material>().Length}, meshes {meshes}->{Resources.FindObjectsOfTypeAll<Mesh>().Length}, objects {objects}->{FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length}, renderers {renderers}->{FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length}, particles {FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Length}, pool misses +{status.PoolMisses-misses}, transient line capacity {lines.PoolCount}.");
        Check(Resources.FindObjectsOfTypeAll<Material>().Length==materials&&Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&
            status.GetComponentsInChildren<Transform>(true).Length+lines.GetComponentsInChildren<Transform>(true).Length==pooled&&
            status.PoolMisses==misses&&status.ActiveRoots==0&&status.ActivePoisons==0,
            id+" repeated casts release leases and leave shared resources and fixed pool counts stable.");
    }
    IEnumerator Shield()
    {
        yield return EnterPower("fire","force-field");
        var user=W.Powers;var power=user.SecondaryRuntime;
        for(int sample=0;sample<4;sample++)
        {
            bool first=sample>=2,night=(sample&1)!=0;
            View(first);Lighting.SetTime(night?.75f:0f);
            if(first){yield return null;yield return null;}
            else
            {
                Cam.transform.position=W.Hero.transform.position+new Vector3(2,2.4f,-4.5f);
                Cam.transform.LookAt(W.Hero.transform.position+Vector3.up);
            }
            power.Cooldown=0;power.Charges=Mathf.Max(power.Charges,1);
            Check(user.UseSecondary()&&user.Shield.Up,"Force Field real secondary activation, "+(first?"first":"third")+" person, "+(night?"night":"day")+".");
            yield return null;
            Check(first?user.Shield.FirstPersonEdgesVisible&&!user.Shield.RingsVisible:
                user.Shield.RingsVisible&&!user.Shield.FirstPersonEdgesVisible,
                "Barrier uses the intended view-specific shape.");
            Capture("shield-"+(night?"night":"day")+"-"+(first?"first":"third")+".png");
            int ripples=user.Shield.HitRipples;
            W.DamagePlayer(5);
            Check(user.Shield.HitRipples==ripples+1,"Real absorbed damage causes one local hit response.");
            yield return null;
            Capture("shield-hit-"+(night?"night":"day")+"-"+(first?"first":"third")+".png");
            user.Shield.Drop(false);yield return new WaitForSeconds(.4f);
        }
        View(false);Lighting.SetTime(0);
        int materials=Resources.FindObjectsOfTypeAll<Material>().Length;
        int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        int objects=FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length;
        int renderers=FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length;
        int pooled=user.Shield.GetComponentsInChildren<LineRenderer>(true).Length;
        for(int i=0;i<12;i++)
        {
            user.Tick(100,true);power.Cooldown=0;power.Charges=Mathf.Max(power.Charges,1);
            bool used=user.UseSecondary();
            Check(used,"Repeated Force Field activation "+i+" ("+user.Message+").");
            W.DamagePlayer(5);user.Shield.Drop(false);
        }
        yield return new WaitForSeconds(.4f);
        Log($"MEASURED shield warm->stress resources: materials {materials}->{Resources.FindObjectsOfTypeAll<Material>().Length}, meshes {meshes}->{Resources.FindObjectsOfTypeAll<Mesh>().Length}, objects {objects}->{FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length}, renderers {renderers}->{FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length}.");
        Check(Resources.FindObjectsOfTypeAll<Material>().Length==materials&&Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes&&
            user.Shield.GetComponentsInChildren<LineRenderer>(true).Length==pooled,
            "Repeated Force Field hits and raises reuse one shell and shared resources.");
    }
    IEnumerator CityNightDarkness()
    {
        yield return EnterPower("darkness",null,false);
        PlaceHero(W.City.Spawn+Vector3.up*.05f);
        View(false);Lighting.SetTime(.75f);
        var target=Actor(W.City.Spawn+Vector3.forward*8,NpcRole.Criminal,1000);
        Aim(Chest(target));
        var texture=new RenderTexture(1280,720,24);texture.Create();
        yield return ProfileFrames("city night idle before Darkness",texture,1.5f);
        Check(W.Powers.Use(Runtime("darkness"))&&target.Rooted,
            "Real city-night Darkness cast roots its aimed target.");
        yield return null;
        Check(StatusPowerVfx.Get().ActiveRoots==1,"City-night target owns the close restraint in real street lighting.");
        Capture("darkness-night-city-third.png");
        yield return ProfileFrames("same city night with Darkness root active",texture,1.5f);
        Check(target.Rooted&&StatusPowerVfx.Get().ActiveRoots==1,
            "Active city profile retained the real rooted status through its sample.");
        texture.Release();Destroy(texture);
    }
    IEnumerator ProfileFrames(string label,RenderTexture texture,float seconds)
    {
        var camera=Cam;var previous=camera.targetTexture;bool enabled=camera.enabled;
        camera.enabled=false;camera.targetTexture=texture;
        var timer=System.Diagnostics.Stopwatch.StartNew();int frames=0;
        while(timer.Elapsed.TotalSeconds<seconds){camera.Render();frames++;yield return null;}
        camera.targetTexture=previous;camera.enabled=enabled;
        Log($"MEASURED {label}: {frames/timer.Elapsed.TotalSeconds:F2} FPS, {timer.Elapsed.TotalMilliseconds/Mathf.Max(1,frames):F3} ms/frame, {frames} frames, {FindObjectsByType<Renderer>().Length} active renderers, {FindObjectsByType<ParticleSystem>().Length} particle systems, {Resources.FindObjectsOfTypeAll<Material>().Length} loaded materials.");
    }
}
#endif
