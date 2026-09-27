#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LightingVerificationRunner : MonoBehaviour
{
    public Action<int> Finished;const string Folder="Verification/Lighting/";
    readonly List<string> lines=new List<string>();string failure;
    Camera camera;ThirdPersonCamera follow;RenderTexture target;Vector3 cameraAt;Quaternion cameraRotation;
    WorldSession W=>WorldSession.Instance;WorldLightingDirector Lighting=>WorldLightingDirector.Instance;
    void Awake(){Application.logMessageReceived+=Observe;}
    void OnDestroy(){Application.logMessageReceived-=Observe;}
    void Observe(string message,string trace,LogType type){if((type==LogType.Exception||type==LogType.Error)&&trace.Contains("Assets/Scripts/"))failure=message;}
    void Log(string value){lines.Add(value);UnityEngine.Debug.Log("[LIGHTING] "+value);File.WriteAllLines(Folder+"results.txt",lines);}
    void Check(bool pass,string value){if(!pass)throw new Exception(value);Log("PASS "+value);}
    IEnumerator Start()
    {
        Directory.CreateDirectory(Folder);var stack=new Stack<IEnumerator>();stack.Push(Checks());
        while(stack.Count>0){object next=null;bool moved=false;try{if(failure!=null)throw new Exception(failure);moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}catch(Exception e){Log("FAIL "+e);Finished(1);yield break;}if(!moved){stack.Pop();continue;}if(next is IEnumerator child)stack.Push(child);else yield return next;}
        Finished(0);
    }
    IEnumerator Scene(string name){float until=Time.realtimeSinceStartup+60;while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name||(name==GameFlow.CityScene&&W==null)){if(Time.realtimeSinceStartup>until)throw new Exception("Scene timeout "+name);yield return null;}yield return null;}
    IEnumerator Home(){if(SceneManager.GetActiveScene().name!=GameFlow.HomeScene){GameFlow.Instance.Home();yield return Scene(GameFlow.HomeScene);}}
    IEnumerator Enter(string id){yield return Home();Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/"+id)),"Home launches "+id);yield return Scene(GameFlow.CityScene);Check(Lighting!=null&&Lighting.Cycle==W.Mode.Definition.Lighting,id+" uses its authored cycle asset.");}
    void CameraView()
    {
        camera=Camera.main;follow=camera.GetComponent<ThirdPersonCamera>();follow.enabled=false;
        cameraAt=W.City.Spawn+new Vector3(0,8,-22);cameraRotation=Quaternion.LookRotation(W.City.Spawn+new Vector3(0,3,18)-cameraAt,Vector3.up);
        camera.transform.SetPositionAndRotation(cameraAt,cameraRotation);target=new RenderTexture(1280,720,24){name="Lighting comparison"};target.Create();camera.targetTexture=target;camera.enabled=false;
    }
    IEnumerator Capture(string name)
    {
        camera.transform.SetPositionAndRotation(cameraAt,cameraRotation);
        for(int i=0;i<3;i++){camera.Render();yield return null;}
        var prior=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Folder+name+".png",image.EncodeToPNG());RenderTexture.active=prior;Destroy(image);
    }
    IEnumerator Measure(string label,bool stress)
    {
        var fire=Resources.Load<PowerDefinition>("Powers/fire").Light;var at=W.City.Spawn+new Vector3(0,2,12);var watch=Stopwatch.StartNew();int frames=0,draws=0,maxStreet=0,maxPower=0;
        while(watch.Elapsed.TotalSeconds<3)
        {
            if(stress){for(int i=0;i<4;i++)Lighting.FlashPower(fire,at+Vector3.right*i*6);}
            camera.Render();draws+=UnityStats.drawCalls;frames++;maxStreet=Mathf.Max(maxStreet,Lighting.StreetActive);maxPower=Mathf.Max(maxPower,Lighting.PowerActive);yield return null;
        }
        watch.Stop();Log($"MEASURED {label}: {frames/watch.Elapsed.TotalSeconds:F2} FPS, {watch.Elapsed.TotalMilliseconds/frames:F2} ms/frame, draw calls {draws/frames}, street={maxStreet}/{Lighting.StreetCapacity}, power={maxPower}/{Lighting.PowerCapacity}, 1280x720 Editor Play Mode single gameplay camera, same viewpoint, {frames} frames, {SystemInfo.graphicsDeviceName}.");
        Check(maxStreet<=Lighting.StreetCapacity&&maxPower<=Lighting.PowerCapacity,label+" respects both fixed point-light caps.");
        if(stress)Check(maxPower==Lighting.PowerCapacity&&Lighting.PowerCapacity==4,label+" keeps exactly four pooled power flashes active at the cap.");
    }
    IEnumerator Checks()
    {
        yield return Scene(GameFlow.HomeScene);
        var profile=FindAnyObjectByType<ModeScreens>().Profile;var forge=Resources.Load<ForgeCatalog>("ForgeCatalog");var fire=Resources.Load<PowerDefinition>("Powers/fire");var ice=Resources.Load<PowerDefinition>("Powers/ice");
        Check(profile.SetLoadout(forge.Heroes[0],fire,ice,forge.Heroes[0].Primary,forge.Heroes[0].Secondary),"Save Fire/Ice through Forge ownership gate for real power light control.");
        yield return Enter("hero");CameraView();
        Check(W.Progression.Data.Side==PlayerSide.Hero&&Lighting.TimeOfDay==0&&Mathf.Abs(Lighting.State.Intensity-1.2f)<.01f,"Hero starts at the unchanged 1.2-intensity daytime sun.");
        Check(Lighting.StreetCapacity<=4&&Lighting.PowerCapacity==4&&Lighting.StreetActive==0&&Lighting.PowerActive==0,"Day has zero active point lights; both pools are bounded.");
        yield return Capture("day-same-view");yield return Measure("day",false);
        Lighting.SetTime(.5f);yield return null;Check(Lighting.State.Glow>.4f&&Lighting.State.Glow<.7f,"Dusk raises shared palette emission to a partial glow.");yield return Capture("dusk-same-view");yield return Measure("dusk",false);
        Lighting.SetTime(.75f);yield return null;Check(Lighting.State.Glow>.9f&&Lighting.StreetActive>0,"Night keeps emissive windows/signs and nearby street lights active.");
        var mats=CityMaterials.Current.All.Count();yield return Capture("night-same-view");yield return Measure("night",false);
        yield return Measure("night-combat-max-lights",true);yield return Capture("night-max-power-lights-same-view");
        Check(CityMaterials.Current.All.Count()==mats,"Lighting cycle and pooled flashes reuse the same shared city palette materials.");
        var power=W.Powers.Powers.Find(p=>p.Definition==fire);W.Powers.Tick(100,true);Check(W.Powers.Use(power),"Real equipped Fire cast requests a pooled point light.");yield return null;Check(Lighting.PowerActive>0,"Fire leaves a visible short-lived point light while its projectile travels.");
        camera.targetTexture=null;target.Release();Destroy(target);camera.enabled=true;follow.enabled=true;
        yield return Enter("villain");Check(Lighting.TimeOfDay==.5f&&Lighting.State.Glow>.4f,"Villain starts at the authored dusk preset.");
        yield return Enter("free-play");Check(Lighting.Cycle.Advances&&Lighting.Cycle.Loop&&Lighting.Cycle.DurationSeconds>0,"Free Play cycles through day/dusk/night from data.");float t=Lighting.TimeOfDay;yield return new WaitForSeconds(.15f);Check(Lighting.TimeOfDay>t,"Free Play's live cycle advances in game time.");
        yield return Enter("endless-fight");Check(!Lighting.Cycle.Advances&&Lighting.State.Glow==0,"Hero Endless uses a stable daytime preset.");
        yield return Enter("endless-fight-villain");Check(!Lighting.Cycle.Advances&&Lighting.State.Glow>.9f,"Villain Endless uses a stable readable night preset.");
        Log("LIMIT: visual readability needs a human playtest; FPS is Editor Play Mode throughput, not player-build timing. Maximum power lights are a pool stress fixture, with Fire also exercised through its real cast.");
    }
}
#endif
