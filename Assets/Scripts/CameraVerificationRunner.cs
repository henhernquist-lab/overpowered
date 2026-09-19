#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CameraVerificationRunner : MonoBehaviour
{
    public string Task;
    public Action<int> Finished;
    readonly List<string> lines=new List<string>();
    readonly List<string> errors=new List<string>();
    readonly Dictionary<Camera,int> renders=new Dictionary<Camera,int>();
    void Awake() {Application.logMessageReceived+=Error;Camera.onPostRender+=Rendered;}
    void OnDestroy() {Application.logMessageReceived-=Error;Camera.onPostRender-=Rendered;}
    void Error(string message,string stack,LogType type)
    {
        if(type!=LogType.Exception&&type!=LogType.Error&&type!=LogType.Assert)return;
        if(stack.Contains("UnityEditor.Search.SearchDatabase")&&!stack.Contains("Assets/Scripts/"))
        {lines.Add("EDITOR-ONLY (not camera code): "+message+"\n"+stack);return;}
        errors.Add(message+"\n"+stack);
    }
    void Rendered(Camera camera) {if(camera.gameObject.scene.IsValid())renders[camera]=renders.TryGetValue(camera,out int count)?count+1:1;}
    void Check(bool ok,string message) {if(!ok)throw new Exception(message);Log("PASS "+message);}
    void Log(string message) {lines.Add(message);Debug.Log("[CAMERA VERIFY] "+message);}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Flow());
        while(stack.Count>0)
        {
            bool more=false;object current=null;
            try {more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}
            catch(Exception e){Log("FAIL "+e);Finish(1);yield break;}
            if(!more){stack.Pop();continue;}if(current is IEnumerator next)stack.Push(next);else yield return current;
        }
        Finish(0);
    }
    void Finish(int code)
    {File.WriteAllLines("Verification/Cameras/"+Task+".txt",lines.Concat(errors));Finished(code);}
    IEnumerator WaitScene(string name)
    {
        float deadline=Time.realtimeSinceStartup+25;
        while(GameFlow.Instance==null||GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!=name)
        {if(Time.realtimeSinceStartup>deadline)throw new Exception("Scene timeout: "+name);yield return null;}
        for(int i=0;i<8;i++)yield return null;
    }
    IEnumerator Flow()
    {
        yield return WaitScene("Home");Capture("home",false);
        foreach(string mode in new[]{"hero","villain"})
        {
            Check(GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/"+mode)),"Home -> "+mode+" selection accepted.");
            yield return WaitScene("Prototype");Capture(mode,true);
            var world=WorldSession.Instance;
            Check(world!=null&&world.Mode.Definition.Id==mode,"Correct live mode: "+mode+".");
            world.Mode.SetPaused(true);yield return null;Capture(mode+"-paused",true);world.Mode.SetPaused(false);
            world.Mode.Finish(SessionOutcome.Abandoned,"Camera flow verification.");
            yield return WaitScene("Results");Capture(mode+"-results",false);
            GameFlow.Instance.Home();yield return WaitScene("Home");Capture(mode+"-return-home",false);
        }
        Check(errors.Count==0,"No game-code errors/exceptions during camera flow (Editor Search errors recorded separately).");
    }
    void Capture(string stage,bool gameplay)
    {
        var cameras=Camera.allCameras.Where(c=>c.cameraType==CameraType.Game&&c.gameObject.scene.IsValid()).ToArray();
        Check(cameras.Length==1,stage+": exactly one enabled scene Game camera; count="+cameras.Length+".");
        var camera=cameras[0];
        Check(camera.enabled&&camera.gameObject.activeInHierarchy&&camera.targetTexture==null&&camera.targetDisplay==0,
            stage+$": '{camera.name}', enabled={camera.enabled}, active={camera.gameObject.activeInHierarchy}, target=Display {camera.targetDisplay+1}, rect={camera.rect}.");
        if(gameplay)Check(Camera.main==camera&&camera.GetComponent<ThirdPersonCamera>()?.target==WorldSession.Instance.Hero.transform,
            stage+": MainCamera follows the current player (not a verification camera).");
        int prior=renders.TryGetValue(camera,out int count)?count:0;
        var target=new RenderTexture(640,360,24);var image=new Texture2D(640,360,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;
        try
        {
            target.Create();RenderTexture.active=target;GL.Clear(true,true,Color.magenta);
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,640,360),0,0);image.Apply();
            var pixels=image.GetPixels32();int changed=pixels.Count(p=>p.r<250||p.g>5||p.b<250);
            int colors=pixels.Select(p=>(p.r/8)*1024+(p.g/8)*32+p.b/8).Distinct().Count();
            Check(changed>pixels.Length*.95f&&(!gameplay||colors>8),stage+$": actual camera rendered {changed}/{pixels.Length} pixels; quantized colors={colors}, natural render callbacks before capture={prior}.");
            File.WriteAllBytes("Verification/Cameras/"+Task+"-"+stage+".png",image.EncodeToPNG());
        }
        finally {camera.targetTexture=null;RenderTexture.active=previous;target.Release();Destroy(target);Destroy(image);}
        // Negative control: the diagnostic must see a disabled camera as non-rendering.
        camera.enabled=false;
        Check(!Camera.allCameras.Contains(camera),stage+": disabled-camera CONTROL excluded from rendering camera list.");
        camera.enabled=true;
    }
}
#endif
