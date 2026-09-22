using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class AudioSetup
{
    const string MixerPath="Assets/Audio/Overpowered.mixer", TuningPath="Assets/Resources/AudioTuning.asset";
    [MenuItem("Overpowered/Audio/Create missing audio assets")]
    public static void Create()
    {
        AssetDatabase.Refresh();
        foreach(string guid in AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/Audio"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var importer=(AudioImporter)AssetImporter.GetAtPath(path);
            bool music=path.EndsWith("/music.ogg"),bed=path.EndsWith("-bed.ogg");
            var settings=importer.defaultSampleSettings;settings.loadType=music?AudioClipLoadType.Streaming:AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.7f;
            importer.forceToMono=!bed&&!music;importer.loadInBackground=false;settings.preloadAudioData=true;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
        }
        var mixer=AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if(mixer==null)mixer=CreateMixer();
        if(AssetDatabase.LoadAssetAtPath<AudioTuning>(TuningPath)!=null){AssetDatabase.SaveAssets();return;}
        var tuning=ScriptableObject.CreateInstance<AudioTuning>();tuning.Mixer=mixer;
        AudioClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/"+name+".ogg")??throw new InvalidOperationException("Missing actual audio clip "+name);
        AudioCueDefinition Cue(AudioCue id,string[] files,float volume,int cap=3,bool spatial=true,string group="SFX",float max=40)
        {
            return new AudioCueDefinition{Id=id,Clips=files.Select(Clip).ToArray(),Group=mixer.FindMatchingGroups(group).Single(g=>g.name==group),Volume=volume,MaxConcurrent=cap,Spatial=spatial,MaxDistance=max,VolumeJitter=spatial?.04f:0,Pitch=spatial?new Vector2(.94f,1.06f):Vector2.one};
        }
        tuning.Cues=new[]{
            Cue(AudioCue.Punch,new[]{"punch-1","punch-2"},.65f),Cue(AudioCue.Footstep,new[]{"step-1","step-2","step-3"},.5f,4,true,"SFX",25),
            Cue(AudioCue.FlightStart,new[]{"flight-start"},.3f,1),Cue(AudioCue.FlightLoop,new[]{"flight-loop"},.16f,1),
            Cue(AudioCue.Land,new[]{"land"},.55f,2),Cue(AudioCue.Destruction,new[]{"debris"},.55f,3),Cue(AudioCue.Fire,new[]{"fire"},.55f,3),
            Cue(AudioCue.Ice,new[]{"ice"},.45f,3),Cue(AudioCue.Telekinesis,new[]{"telekinesis"},.35f,2),Cue(AudioCue.Gunshot,new[]{"gunshot"},.6f,4),
            Cue(AudioCue.Hit,new[]{"hit"},.5f,3),Cue(AudioCue.Death,new[]{"death"},.4f,2),Cue(AudioCue.Jump,new[]{"jump"},.18f,2),
            Cue(AudioCue.UiClick,new[]{"ui-click"},.6f,2,false,"UI"),Cue(AudioCue.UiHover,new[]{"ui-hover"},.16f,1,false,"UI"),
            Cue(AudioCue.CityBed,new[]{"city-bed"},.35f,1,false,"Ambient"),Cue(AudioCue.SirenBed,new[]{"siren-bed"},.24f,1,false,"Ambient"),Cue(AudioCue.Music,new[]{"music"},.18f,1,false,"Music")};
        tuning.Powers=new[]{new PowerAudioBinding{Effect=Resources.Load<PowerEffect>("Effects/FireBlast"),Cue=AudioCue.Fire},new PowerAudioBinding{Effect=Resources.Load<PowerEffect>("Effects/Ice"),Cue=AudioCue.Ice},new PowerAudioBinding{Effect=Resources.Load<PowerEffect>("Effects/Telekinesis"),Cue=AudioCue.Telekinesis}};
        AssetDatabase.CreateAsset(tuning,TuningPath);AssetDatabase.SaveAssets();
    }
    // Unity 6 exposes mixer authoring through internal Editor types, not the runtime AudioMixer API.
    // Reflection is confined to this explicit authoring command; shipped builds have no dependency.
    static AudioMixer CreateMixer()
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("UnityEditor.Audio.AudioMixerController")).First(t=>t!=null);
        var mixer=(AudioMixer)type.GetMethod("CreateMixerControllerAtPath",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{MixerPath});
        object master=type.GetProperty("masterGroup").GetValue(mixer);var groups=new object[5];groups[0]=master;
        string[] names={"Master","Music","SFX","UI","Ambient"};
        for(int i=1;i<names.Length;i++){groups[i]=type.GetMethod("CreateNewGroup").Invoke(mixer,new object[]{names[i],false});type.GetMethod("AddChildToParent").Invoke(mixer,new[]{groups[i],master});}
        var property=type.GetProperty("exposedParameters");var parameterType=property.PropertyType.GetElementType();var parameters=Array.CreateInstance(parameterType,names.Length);
        for(int i=0;i<names.Length;i++){object parameter=Activator.CreateInstance(parameterType);parameterType.GetField("name").SetValue(parameter,names[i]+"Volume");parameterType.GetField("guid").SetValue(parameter,groups[i].GetType().GetMethod("GetGUIDForVolume").Invoke(groups[i],null));parameters.SetValue(parameter,i);EditorUtility.SetDirty((UnityEngine.Object)groups[i]);}
        property.SetValue(mixer,parameters);EditorUtility.SetDirty(mixer);AssetDatabase.SaveAssets();return mixer;
    }
    public static void CreateBatch(){Create();EditorApplication.Exit(0);}
}
