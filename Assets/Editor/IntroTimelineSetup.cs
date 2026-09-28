using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

/// One-time authoring of editable Timeline and AnimationClip assets; repeated runs preserve designer edits byte for byte.
public static class IntroTimelineSetup
{
    const string DirectoryName="Assets/Resources/Intro/";
    public static void Configure()
    {
        Directory.CreateDirectory(DirectoryName);AssetDatabase.Refresh();
        if(AssetDatabase.LoadAssetAtPath<TimelineAsset>(DirectoryName+"FirstLaunch.playable")!=null)
        {Debug.Log("First-launch Timeline already authored; preserving its clips and curves.");EditorApplication.Exit(0);return;}
        var entrance=new AnimationClip{name="First launch hero entrance",frameRate=30};
        entrance.SetCurve("Motion",typeof(Transform),"m_LocalPosition.y",new AnimationCurve(
            new Keyframe(0,4.5f),new Keyframe(2.4f,4.5f),new Keyframe(4.2f,2.8f),
            new Keyframe(5.7f,.1f),new Keyframe(6.1f,0),new Keyframe(6.4f,.18f),new Keyframe(6.9f,0),new Keyframe(9,0)));
        AssetDatabase.CreateAsset(entrance,DirectoryName+"HeroEntrance.anim");
        var camera=new AnimationClip{name="First launch camera push",frameRate=30};
        camera.SetCurve("Dolly",typeof(Transform),"m_LocalPosition.z",new AnimationCurve(
            new Keyframe(0,-9),new Keyframe(2.5f,-8.2f),new Keyframe(5.5f,-5.7f),new Keyframe(8,-4.8f),new Keyframe(9,-4.8f)));
        AssetDatabase.CreateAsset(camera,DirectoryName+"CameraPush.anim");
        var timeline=ScriptableObject.CreateInstance<TimelineAsset>();timeline.name="FirstLaunch";
        AssetDatabase.CreateAsset(timeline,DirectoryName+"FirstLaunch.playable");
        var heroTrack=timeline.CreateTrack<AnimationTrack>(null,"Hero entrance");var heroClip=heroTrack.CreateClip(entrance);heroClip.start=0;heroClip.duration=9;
        var cameraTrack=timeline.CreateTrack<AnimationTrack>(null,"Camera push");var cameraClip=cameraTrack.CreateClip(camera);cameraClip.start=0;cameraClip.duration=9;
        EditorUtility.SetDirty(timeline);AssetDatabase.SaveAssets();Debug.Log("First-launch 9-second Timeline authored with hero entrance and camera push tracks.");EditorApplication.Exit(0);
    }
}
