#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEditor;

public sealed class FirstPersonVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;public bool Reload;
    const string Folder="Verification/FirstPerson/";
    readonly List<string> output=new List<string>();string error;
    WorldSession W=>WorldSession.Instance;
    Camera Cam=>Camera.main;
    ThirdPersonCamera Rig=>Cam.GetComponent<ThirdPersonCamera>();
    PowerDefinition Power(string id)=>Resources.Load<PowerDefinition>("Powers/"+id);
    void Awake(){Application.logMessageReceived+=Observe;}
    void OnDestroy(){Application.logMessageReceived-=Observe;}
    void Observe(string text,string trace,LogType type)
    {if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&trace.Contains("Assets/Scripts/"))error=text;}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0)
        {
            object next=null;bool moved;
            try{if(error!=null)throw new Exception(error);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Write();Finished(1);yield break;}
            if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
        }
        Write();Finished(0);
    }
    void Log(string line){output.Add(line);Debug.Log(line);}
    void Check(bool ok,string line){if(!ok)throw new Exception(line);Log("PASS "+line);}
    void Write(){File.WriteAllLines(Folder+(Reload?"reload.txt":"results.txt"),output);}
    IEnumerator Scene(string name)
    {
        float until=Time.realtimeSinceStartup+45;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null))
        {if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout");yield return null;}
        yield return null;yield return null;
    }
    void Equip(string a,string b)
    {
        var p=FindAnyObjectByType<ModeScreens>().Profile;p.Data.Points=20;
        foreach(string id in new[]{a,b})if(!p.Owns(Power(id)))Check(p.Buy(Power(id)),"Unlock "+id+" through progression.");
        Check(p.SetLoadout(Resources.Load<ForgeCatalog>("ForgeCatalog").Heroes[0],Power(a),Power(b),CityColor.Blue,CityColor.Cyan),"Equip "+a+" / "+b+" through saved loadout.");
    }
    void Place(Vector3 at)
    {var cc=W.Hero.GetComponent<CharacterController>();cc.enabled=false;W.Hero.transform.position=at;cc.enabled=true;W.Hero.ResetMotion();Physics.SyncTransforms();}
    GameObject Box(string name,Vector3 at,Vector3 size,CityColor color)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.position=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=CityMaterials.Get(color);return go;}
    Rigidbody Target(Vector3 at)
    {var go=Box("Crosshair physics target",at,Vector3.one*.7f,CityColor.Amber);var rb=go.AddComponent<Rigidbody>();rb.useGravity=false;rb.mass=45;go.AddComponent<FirstPersonImpactProbe>();return rb;}
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var profile=FindAnyObjectByType<ModeScreens>().Profile;
        if(Reload)
        {
            Check(profile.Data.FirstPerson,"SECOND PROCESS restores first-person preference in existing save.");
            Check(profile.Data.Loadout.PowerA=="flight"&&profile.Data.Loadout.PowerB=="strength"&&profile.Data.Level>=1,"Existing loadout/progression preserved.");
            var fresh=new GameObject("Fresh control").AddComponent<PlayerProgression>();
            fresh.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),Path.GetFullPath(Folder+"fresh-"+Guid.NewGuid()+".json"));
            Check(!fresh.Data.FirstPerson,"Fresh save CONTROL defaults to third-person.");
            string old=Path.GetFullPath(Folder+"old-"+Guid.NewGuid()+".json");File.WriteAllText(old,"{\"Version\":1,\"Level\":2,\"Xp\":4,\"Points\":0,\"Powers\":[],\"Rooftops\":[]}");
            fresh.Initialize(Resources.Load<GameTuning>("GameTuning").Progression,Resources.LoadAll<PowerDefinition>("Powers"),old);
            Check(!fresh.Data.FirstPerson&&fresh.Data.Level==2&&fresh.Data.Xp==4,"Old save without camera field CONTROL keeps progression and defaults third-person.");
            GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/villain"));yield return Scene(GameFlow.CityScene);
            Check(Rig.FirstPerson,"SECOND PROCESS gameplay camera actually starts first-person.");yield break;
        }
        Check(!profile.Data.FirstPerson,"Fresh initial preference is third-person.");
        foreach(string mode in new[]{"hero","villain"})
        foreach(string second in new[]{"ice","telekinesis"})
        {
            Equip("fire",second);Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/"+mode)),"Launch "+mode);
            yield return Scene(GameFlow.CityScene);W.Hero.enabled=false;
            Box("Isolated aim platform",new Vector3(0,149.5f,0),new Vector3(80,1,80),CityColor.Road);Place(new Vector3(0,150,0));
            if(Rig.FirstPerson)Check(Rig.ToggleView(),mode+" first -> third toggle.");
            var renderers=W.Hero.GetComponent<HumanoidPresentation>().VisualRoot.GetComponentsInChildren<Renderer>();
            Check(renderers.All(r=>r.shadowCastingMode!=ShadowCastingMode.ShadowsOnly),"Third-person restores body renderers.");
            foreach(bool first in new[]{false,true})
            {
                if(first)Check(Rig.ToggleView()&&Rig.FirstPerson,mode+" third -> first toggle.");
                Rig.SetLook(23,first?-12:-10);yield return null;yield return null;
                foreach(string id in new[]{"fire",second})yield return Aim(id,mode+(first?"-first-":"-third-")+id);
            }
            Check(renderers.All(r=>r.shadowCastingMode==ShadowCastingMode.ShadowsOnly),"First-person hides own body; Animator and shadows retained.");
            W.Mode.SetPaused(true);Check(!Rig.ToggleView()&&Rig.FirstPerson,"Paused toggle CONTROL refused.");W.Mode.SetPaused(false);
            yield return WallControls();
            Check(Rig.ToggleView()&&!Rig.FirstPerson,mode+" first -> third restores view.");
            Check(renderers.All(r=>r.shadowCastingMode!=ShadowCastingMode.ShadowsOnly),"Body visible again after reverse toggle.");
            Check(Rig.ToggleView()&&W.Progression.Data.FirstPerson,"Persist first-person through existing profile.");
            GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);
            Check(FindAnyObjectByType<ModeScreens>().Profile.Data.FirstPerson,"Mode -> Home retains preference.");
        }
        Equip("flight","strength");GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));yield return Scene(GameFlow.CityScene);
        Check(Rig.FirstPerson,"Hero reload starts in saved view.");W.Hero.enabled=false;Place(new Vector3(0,153,0));Rig.SetLook(0,0);
        for(int i=0;i<50;i++){W.Hero.MoveAbility(Vector3.zero,true);yield return null;}
        Check(Rig.FlightFov<.1f,"Hover CONTROL has no forward-flight FOV increase.");
        float untilFlight=Time.time+1.5f;
        while(Time.time<untilFlight){CheckFuel();W.Hero.MoveAbility(Vector3.forward*8,true);yield return null;}
        Log($"MEASURED forward flight FOV={Cam.fieldOfView:F3}, additive={Rig.FlightFov:F3} degrees; base={W.Tuning.Camera.FieldOfView}.");
        Check(Rig.FlightFov>4.8f&&Cam.fieldOfView<70.1f,"Actual forward motion drives bounded flight FOV.");
        Rig.KickFov(3,.16f);yield return null;Check(Cam.fieldOfView>70,"Synergy FOV adds to flight instead of fighting it.");
        float until=Time.time+1.5f;while(Time.time<until){W.Hero.MoveAbility(Vector3.zero,false);yield return null;}
        Check(Mathf.Abs(Cam.fieldOfView-65)<.1f,"Non-flight recovery restores baseline FOV after flight/kick.");
        yield return Performance();
        if(!Rig.FirstPerson)Rig.ToggleView();W.Progression.Save();
        Log("LIMIT: gameplay/UI/collision entry points and actual rendered frames tested; no hardware B-key injection, human feel or standalone FPS claim.");
    }
    void CheckFuel(){if(!W.Powers.ConsumeFlight(Time.deltaTime))throw new Exception("Flight fuel failed during motion test");}
    IEnumerator Aim(string id,string name)
    {
        var power=W.Powers.Powers.Find(p=>p.Definition.Id==id);W.Powers.Tick(100,true);
        Check(W.Powers.Select(power),name+" select equipped power.");
        var ray=Cam.ViewportPointToRay(new Vector3(.5f,.5f));var target=Target(ray.GetPoint(9));var off=Target(ray.GetPoint(9)+Cam.transform.right*5);
        Physics.SyncTransforms();var center=Physics.RaycastAll(ray,12,~0,QueryTriggerInteraction.Ignore).Where(h=>h.transform.root!=W.Hero.transform).OrderBy(h=>h.distance).FirstOrDefault();
        Check(center.rigidbody==target,name+" crosshair world ray targets intended rigidbody (self excluded).");
        Check(W.Powers.FindTarget(20,out var hit)&&hit.rigidbody==target,name+" gameplay ray hits same target.");
        float pixels=(Cam.WorldToViewportPoint(hit.point)-new Vector3(.5f,.5f,Cam.WorldToViewportPoint(hit.point).z)).magnitude*1280;
        Check(pixels<1,name+$" aim error={pixels:F4}px.");
        yield return Capture(name+".png");
        Check(W.Powers.Use(power),name+" real cast accepted.");
        if(id=="fire")
        {
            float until=Time.time+2;var probe=target.GetComponent<FirstPersonImpactProbe>();while(!probe.Hit&&Time.time<until)yield return null;
            Check(probe.Hit&&!off.GetComponent<FirstPersonImpactProbe>().Hit,name+" physical projectile hits crosshair target; off-axis CONTROL untouched.");
            float error=Vector3.Distance(probe.Point,center.point);Log($"MEASURED {name} contact error={error:F4}m, target speed={target.linearVelocity.magnitude:F3}m/s.");Check(error<.3f,name+" contact within projectile radius tolerance.");
        }
        else if(id=="ice")Check(target.constraints==RigidbodyConstraints.FreezeAll&&off.constraints!=RigidbodyConstraints.FreezeAll,name+" aimed target frozen; off-axis CONTROL not frozen.");
        else {Check(W.Powers.HeldBody==target&&off.useGravity==false,name+" aimed target held, not off-axis control.");W.Powers.Release(false);}
        Destroy(target.gameObject);Destroy(off.gameObject);yield return null;
    }
    IEnumerator WallControls()
    {
        Place(new Vector3(0,150,0));Rig.SetLook(0,0);yield return null;yield return null;
        var wall=Box("Close wall",new Vector3(0,151,.65f),new Vector3(2,2,.2f),CityColor.Teal);var probe=wall.AddComponent<FirstPersonImpactProbe>();
        var left=Box("Tight left wall",new Vector3(-.72f,151,0),new Vector3(.2f,2,4),CityColor.UiPurple);
        var right=Box("Tight right wall",new Vector3(.72f,151,0),new Vector3(.2f,2,4),CityColor.UiPurple);
        var ceiling=Box("Low ceiling",new Vector3(0,152,0),new Vector3(2,.2f,4),CityColor.Cream);
        Physics.SyncTransforms();yield return null;
        foreach(float angle in new[]{-85f,0,85})
        {
            Rig.SetLook(35,angle);yield return null;yield return null;
            Check(!Physics.OverlapSphere(Cam.transform.position,.15f,~0,QueryTriggerInteraction.Ignore).Any(c=>c.transform.root!=W.Hero.transform),"Tight corner eye volume clear at pitch "+angle);
        }
        Rig.SetLook(45,-10);yield return null;yield return Capture("tight-space-first.png");
        // Move an overhang into the requested eye sphere: the sweep must compress, not clip.
        ceiling.transform.position=new Vector3(0,151.8f,0);Physics.SyncTransforms();yield return null;yield return null;
        Check(Cam.transform.position.y<151.55f,"Low-overhang positive control compresses eye below requested 1.62m height.");
        Check(!Physics.OverlapSphere(Cam.transform.position,.15f,~0,QueryTriggerInteraction.Ignore).Any(c=>c.transform.root!=W.Hero.transform),"Compressed eye remains outside overhang/walls.");
        ceiling.transform.position=new Vector3(0,152,0);Physics.SyncTransforms();
        Rig.SetLook(0,85);yield return null;yield return Capture("tight-space-look-down.png");
        Rig.SetLook(0,0);yield return null;yield return null;
        var fire=W.Powers.Powers.Find(p=>p.Definition.Id=="fire");W.Powers.Tick(100,true);W.Powers.Select(fire);
        Check(W.Powers.Use(fire),"Close-wall fire accepted from clear eye.");
        var shot=FindAnyObjectByType<PowerProjectile>();Check(shot!=null&&shot.transform.position.z<.55f,"Projectile spawn cannot skip wall inside original 1.7m muzzle offset.");
        float until=Time.time+1;while(!probe.Hit&&Time.time<until)yield return null;
        Check(probe.Hit,"Close-wall projectile actually impacts intervening wall.");
        foreach(var p in W.Powers.Powers.Where(p=>p.Definition.Id=="ice"||p.Definition.Id=="telekinesis"))
        {
            if(!W.Powers.IsEquipped(p.Definition))continue;W.Powers.Select(p);W.Powers.Tick(100,true);
            var behind=Target(new Vector3(0,151.62f,2));Physics.SyncTransforms();
            Check(!W.Powers.Use(p)&&W.Powers.HeldBody==null&&behind.constraints!=RigidbodyConstraints.FreezeAll,"Wall CONTROL blocks "+p.Definition.Id+" target behind it.");Destroy(behind.gameObject);
        }
        Destroy(wall);Destroy(left);Destroy(right);Destroy(ceiling);yield return null;
    }
    IEnumerator Capture(string file)
    {
        var texture=new RenderTexture(1280,720,24);texture.Create();var hud=FindAnyObjectByType<GameHud>();
        Cam.targetTexture=texture;hud.Panel.targetTexture=texture;
        yield return null;yield return null;yield return null;
        var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Folder+file,image.EncodeToPNG());
        Destroy(image);RenderTexture.active=previous;Cam.targetTexture=null;hud.Panel.targetTexture=null;texture.Release();Destroy(texture);
    }
    IEnumerator Performance()
    {
        Place(W.City.Spawn+Vector3.up*8);W.AddHeat(3);W.ReconcilePolice();
        var c=W.Tuning.Camera;
        float matchedPitch=Mathf.Atan2(c.Offset.y-c.LookHeight,-c.Offset.z)*Mathf.Rad2Deg;
        var target=new RenderTexture(1280,720,24);target.Create();Cam.targetTexture=target;FindAnyObjectByType<GameHud>().Panel.targetTexture=target;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;double first=0,third=0;
        Log($"BENCH same city position={W.Hero.transform.position}, matched world pitch={matchedPitch:F3}, civilians={W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Civilian)}, cops={W.Npcs.Count(n=>n!=null&&n.Role==NpcRole.Cop)}; 1280x720 camera+HUD, single render/frame.");
        for(int phase=0;phase<4;phase++)
        {
            bool fp=phase==1||phase==2;if(Rig.FirstPerson!=fp)Rig.ToggleView();Rig.SetLook(0,fp?matchedPitch:0);
            yield return new WaitForSecondsRealtime(.7f);var watch=System.Diagnostics.Stopwatch.StartNew();int frames=0,draws=0;
            while(watch.Elapsed.TotalSeconds<5){yield return null;frames++;draws+=UnityStats.drawCalls;}
            double fps=frames/watch.Elapsed.TotalSeconds;if(fp)first+=fps/2;else third+=fps/2;
            Log($"MEASURED {(fp?"first":"third")}-person phase={phase}: FPS={fps:F2}, draw calls={draws/(float)frames:F1}.");
        }
        Check(!W.PlayerDead,"Benchmark remained alive with populated city simulation running.");
        Log($"PAIRED same-scene means: third-person={third:F2} FPS; first-person={first:F2} FPS; delta={(first/third-1)*100:F2}%. Perspective naturally changes visible geometry; no content or population cut.");
        Cam.targetTexture=null;FindAnyObjectByType<GameHud>().Panel.targetTexture=null;target.Release();Destroy(target);
        yield return Capture("city-third.png");
        Rig.ToggleView();Rig.SetLook(0,matchedPitch);yield return null;yield return Capture("city-first.png");
    }
}
public sealed class FirstPersonImpactProbe : MonoBehaviour
{
    public bool Hit;public Vector3 Point;
    void OnCollisionEnter(Collision collision)
    {if(collision.gameObject.GetComponent<PowerProjectile>()!=null){Hit=true;Point=collision.GetContact(0).point;}}
}
#endif
