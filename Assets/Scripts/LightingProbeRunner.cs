#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
public sealed class LightingProbeRunner : MonoBehaviour
{
    public Action<int> Finished; readonly List<string> lines=new List<string>();
    void Log(string t){lines.Add(t);Debug.Log("[LIGHT PROBE] "+t);}
    IEnumerator Start()
    {
        while(GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!="Home")yield return null;
        for(int i=0;i<5;i++)yield return null;
        Log($"HOME ambientMode={RenderSettings.ambientMode} ambientLight={RenderSettings.ambientLight} skybox={(RenderSettings.skybox?RenderSettings.skybox.name+"/"+RenderSettings.skybox.shader.name:"null")} fog={RenderSettings.fog}");
        GameFlow.Instance.Select(Resources.Load<GameModeDefinition>("Modes/hero"));
        while(GameFlow.Instance.Loading||SceneManager.GetActiveScene().name!="Prototype"||WorldSession.Instance==null)yield return null;
        for(int i=0;i<10;i++)yield return null;
        var sun=FindObjectsByType<Light>().Where(l=>l.type==LightType.Directional).ToArray();
        foreach(var s in sun)Log($"LIGHT {s.name} color={s.color} intensity={s.intensity} shadows={s.shadows} shadowStrength={s.shadowStrength} renderMode={s.renderMode} cullingMask={s.cullingMask} bias={s.shadowBias}/{s.shadowNormalBias} rot={s.transform.eulerAngles} colorTemp={s.useColorTemperature}/{s.colorTemperature}");
        Log($"RENDER ambientMode={RenderSettings.ambientMode} ambientLight={RenderSettings.ambientLight} sky={RenderSettings.ambientSkyColor} eq={RenderSettings.ambientEquatorColor} ground={RenderSettings.ambientGroundColor} intensity={RenderSettings.ambientIntensity} sun={(RenderSettings.sun?RenderSettings.sun.name:"null")} skybox={(RenderSettings.skybox?RenderSettings.skybox.name+"/"+RenderSettings.skybox.shader.name:"null")} reflMode={RenderSettings.defaultReflectionMode} reflInt={RenderSettings.reflectionIntensity} fog={RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogDensity} {RenderSettings.fogColor}");
        var dirs=new[]{Vector3.up,Vector3.down,Vector3.forward,Vector3.right};var res=new Color[4];RenderSettings.ambientProbe.Evaluate(dirs,res);
        Log($"PROBE up={res[0]} down={res[1]} fwd={res[2]} right={res[3]}");
        if(RenderSettings.skybox!=null){var m=RenderSettings.skybox;foreach(var p in new[]{"_SunDisk","_SunSize","_SunSizeConvergence","_AtmosphereThickness","_Exposure"})if(m.HasProperty(p))Log($"SKY {p}={m.GetFloat(p)}");foreach(var p in new[]{"_SkyTint","_GroundColor"})if(m.HasProperty(p))Log($"SKY {p}={m.GetColor(p)}");}
        var cam=Camera.main;Log($"CAMERA clear={cam.clearFlags} bg={cam.backgroundColor} hdr={cam.allowHDR} path={cam.actualRenderingPath} colorSpace={QualitySettings.activeColorSpace} pixelLights={QualitySettings.pixelLightCount} shadows={QualitySettings.shadows} dist={QualitySettings.shadowDistance} cascades={QualitySettings.shadowCascades}");
        var rt=new RenderTexture(1280,720,24);rt.Create();cam.targetTexture=rt;var follow=cam.GetComponent<ThirdPersonCamera>();
        follow.SetLook(0,20);yield return null;yield return null;cam.Render();Save(rt,"probe-street-hero");
        follow.enabled=false;cam.transform.position=WorldSession.Instance.City.Spawn+new Vector3(0,60,-90);cam.transform.rotation=Quaternion.Euler(12,0,0);cam.Render();Save(rt,"probe-long-hero");
        cam.transform.rotation=Quaternion.Euler(-30,0,0);cam.Render();Save(rt,"probe-sky-hero");
        cam.targetTexture=null;
        Directory.CreateDirectory("Verification/Lighting");File.WriteAllLines(Arg("-probeOut")??"Verification/Lighting/probe.txt",lines);Finished(0);
    }
    static string Arg(string name){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,name);return i>=0&&i+1<a.Length?a[i+1]:null;}
    void Save(RenderTexture rt,string name){var p=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();File.WriteAllBytes((Arg("-probeDir")??"Verification/Lighting")+"/"+name+".png",t.EncodeToPNG());RenderTexture.active=p;Destroy(t);}
}
#endif
