#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed class LaserPresentationRunner : SessionVerificationRunner
{
    protected override string Folder=>"Verification/LaserPresentation/";
    protected override string ResultFile=>"results.txt";
    protected override IEnumerator Checks()
    {
        yield return Enter("laser-eyes","strength");
        Isolate();
        var rt=Runtime("laser-eyes");var effect=(LaserEyesEffect)rt.Definition.Effect;
        var target=Actor(new Vector3(0,150,10),NpcRole.Cop,1000);
        var aside=Actor(new Vector3(3,150,10),NpcRole.Cop,1000);
        yield return new WaitForSeconds(.2f);
        Aim(Chest(target));
        Check(W.Powers.FindTarget(W.Powers.Stats(rt).Range,out var actual)&&actual.collider.GetComponentInParent<CityNpc>()==target,
            "Third-person center aim selects the intended real target.");
        float health=target.Health;
        Check(W.Powers.Use(rt),"Laser channel starts through the equipped power gate.");
        W.Powers.Channel(true,.11f);
        var state=BeamState.For(W.Powers);var vfx=PowerVfx.Get();
        Check(target.Health<health&&aside.Health==1000,"Real damage reaches the center target, not the off-axis CONTROL.");
        Check(state.HasHit&&Vector3.Distance(state.LastHitPoint,actual.point)<.03f&&vfx.LaserImpactVisible,
            "Impact flare appears at the real third-person ray contact.");
        Check(vfx.BeamVisible&&vfx.LaserRight.gameObject.activeSelf&&vfx.LaserHaloLeft.gameObject.activeSelf&&
            vfx.LaserHaloRight.gameObject.activeSelf&&vfx.LaserRendererCount==7,
            "Third-person travel uses two pooled eye cores, two halos and bounded contact lines.");
        Check(state.Head!=null&&Mathf.Abs(Vector3.Distance(state.VisualLeft,state.VisualRight)-effect.EyeSeparation)<.01f&&
            Vector3.Distance(state.VisualLeft,state.Head.position)<.4f&&
            Vector3.Distance(vfx.Beam.GetPosition(1),state.LastHitPoint)<.001f&&
            Vector3.Distance(vfx.LaserRight.GetPosition(1),state.LastHitPoint)<.001f,
            "Both visible rays begin near the animated head and converge on the true hit point.");
        Capture("third-person-eye-beams.png");
        var oldCameraPosition=Cam.transform.position;var oldCameraRotation=Cam.transform.rotation;
        Cam.transform.position=W.Hero.transform.position+new Vector3(2.5f,2f,-3f);
        Cam.transform.LookAt(Chest(target));Capture("third-person-side.png");
        Cam.transform.SetPositionAndRotation(oldCameraPosition,oldCameraRotation);
        int materials=Resources.FindObjectsOfTypeAll<Material>().Length;
        int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
        int lines=FindObjectsByType<LineRenderer>().Length;
        int particles=FindObjectsByType<ParticleSystem>().Length;
        int pool=vfx.PoolCount;
        for(int i=0;i<4;i++)
        {
            W.Powers.Channel(false,0);
            Check(!vfx.BeamVisible&&!vfx.LaserImpactVisible,"Release hides every laser layer, repeat "+i+".");
            rt.Cooldown=0;
            Check(W.Powers.Use(rt),"Repeat cast starts through the normal gate, repeat "+i+".");
            W.Powers.Channel(true,.05f);
        }
        Check(materials==Resources.FindObjectsOfTypeAll<Material>().Length&&meshes==Resources.FindObjectsOfTypeAll<Mesh>().Length&&
            lines==FindObjectsByType<LineRenderer>().Length&&particles==FindObjectsByType<ParticleSystem>().Length&&pool==vfx.PoolCount,
            "Repeated casts keep material, mesh, LineRenderer, ParticleSystem and pool counts stable.");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)vfx.ShowLaser(state.VisualLeft,state.VisualRight,state.LastHitPoint,state.LastHitPoint,false,true,
            effect.BeamWidth,effect.HaloWidth,effect.ImpactRadius,Cam.transform.right,Cam.transform.up);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Log($"MEASURED pooled laser presentation: {bytes} managed bytes /1000 visual updates; materials={materials}, meshes={meshes}, LineRenderers={lines}, ParticleSystems={particles}, generic pool={pool}.");
        Check(bytes==0,"Laser presentation update makes no per-tick managed allocation after warmup.");
        W.Powers.Channel(false,0);

        var rig=Cam.GetComponent<ThirdPersonCamera>();rig.enabled=true;
        if(!rig.FirstPerson)Check(rig.ToggleView(),"Switch to first-person Laser Eyes.");
        rig.SetLook(0,0);yield return null;yield return null;
        Check(rig.FirstPerson&&W.Powers.AimOrigin==Cam.transform.position,"First-person aim still originates at the protected eye camera.");
        Check(W.Powers.FindTarget(W.Powers.Stats(rt).Range,out actual)&&actual.collider.GetComponentInParent<CityNpc>()==target,
            "First-person center ray selects the same intended target.");
        rt.Cooldown=0;Check(W.Powers.Use(rt),"First-person channel starts.");W.Powers.Channel(true,.11f);
        Check(state.HasHit&&Vector3.Distance(state.LastHitPoint,actual.point)<.03f&&vfx.LaserImpactVisible,
            "First-person impact stays on the real center-ray contact.");
        Check(vfx.Beam.positionCount==2&&vfx.LaserRight.gameObject.activeSelf&&
            Vector3.Distance(vfx.Beam.GetPosition(1),vfx.LaserRight.GetPosition(0))<.001f&&
            Vector3.Distance(vfx.LaserRight.GetPosition(1),state.LastHitPoint)<.001f,
            "First-person peripheral entry merges into a thin beam on the real aim ray, without head origins.");
        var screen=Cam.WorldToViewportPoint(state.VisualLeft);
        Check(screen.z>Cam.nearClipPlane&&screen.x>.25f&&screen.x<.75f&&screen.y<.5f,
            $"First-person beam begins in the lower-center view without head geometry ({screen}).");
        Check(W.Hero.GetComponent<HumanoidPresentation>().VisualRoot.GetComponentsInChildren<Renderer>(true).All(
            r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),
            "First-person view keeps player visuals in shadow-only mode without disabling their renderers.");
        Capture("first-person-beam.png");

        var wall=Wall(Cam.transform.position+Cam.transform.forward*.8f,new Vector3(2,2,.2f));
        float behind=target.Health;Physics.SyncTransforms();W.Powers.Channel(true,.11f);
        Check(W.Powers.FindTarget(W.Powers.Stats(rt).Range,out actual)&&actual.collider==wall.GetComponent<Collider>()&&
            Vector3.Distance(state.LastHitPoint,actual.point)<.03f&&target.Health==behind,
            "Nearby wall intercepts the real beam before the enemy CONTROL.");
        Check(Vector3.Dot(state.VisualLeft-W.Powers.AimOrigin,W.Powers.AimDirection)<actual.distance,
            "First-person visible origin remains on the camera side of the nearby wall.");
        Capture("first-person-close-wall.png");Destroy(wall);yield return null;
        foreach(float pitch in new[]{-75f,75f})
        {
            rig.SetLook(0,pitch);yield return null;yield return null;
            W.Powers.Channel(true,.05f);
            bool found=W.Powers.FindTarget(W.Powers.Stats(rt).Range,out actual);
            Vector3 expected=found?actual.point:W.Powers.AimOrigin+W.Powers.AimDirection*W.Powers.Stats(rt).Range;
            Check(Vector3.Angle(Cam.ViewportPointToRay(new Vector3(.5f,.5f)).direction,W.Powers.AimDirection)<.01f&&
                Vector3.Distance(state.LastHitPoint,expected)<.03f&&vfx.BeamVisible,
                "Extreme first-person pitch "+pitch+" keeps visual endpoint on the unchanged center aim ray.");
            Capture(pitch<0?"first-person-look-up.png":"first-person-look-down.png");
        }
        W.Powers.Channel(false,0);
        Check(!vfx.BeamVisible&&!vfx.LaserImpactVisible,"Release immediately hides the first-person beam and impact.");
        rig.SetLook(0,0);yield return null;
        rt.Cooldown=0;Check(W.Powers.Use(rt),"Start held channel for pause control.");
        W.Powers.ScriptedHold=true;W.Hero.enabled=true;yield return null;
        W.Mode.SetPaused(true);yield return null;
        Check(W.Powers.Channeling==null&&!vfx.BeamVisible,"Pause ends the channel and all beam layers.");
        W.Mode.SetPaused(false);W.Powers.ScriptedHold=false;
        rt.Cooldown=0;Check(W.Powers.Use(rt),"Start channel for death control.");
        W.Powers.ScriptedHold=true;yield return null;W.DamagePlayer(W.Health,true);yield return null;
        Check(W.PlayerDead&&W.Powers.Channeling==null&&!vfx.BeamVisible,"Death ends the channel and all beam layers.");
    }
}
#endif
