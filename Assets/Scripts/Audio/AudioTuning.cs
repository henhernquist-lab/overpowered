using System;
using UnityEngine;
using UnityEngine.Audio;

public enum AudioCue { Punch, Footstep, FlightStart, FlightLoop, Land, Destruction, Fire, Ice, Telekinesis, Gunshot, Hit, Death, Jump, UiClick, UiHover, CityBed, SirenBed, Music }
[Serializable]
public sealed class AudioCueDefinition
{
    public AudioCue Id;
    public AudioClip[] Clips;
    public AudioMixerGroup Group;
    [Range(0,1)] public float Volume=.5f, VolumeJitter=.08f;
    public Vector2 Pitch=new Vector2(.94f,1.06f);
    [Min(1)] public int MaxConcurrent=3;
    public bool Spatial=true;
    [Min(.1f)] public float MinDistance=3, MaxDistance=40;
    public AudioRolloffMode Rolloff=AudioRolloffMode.Linear;
    [Range(0,256)] public int Priority=128;
}
[Serializable] public sealed class PowerAudioBinding { public PowerEffect Effect;public AudioCue Cue; }
[CreateAssetMenu(menuName="Overpowered/Audio Tuning")]
public sealed class AudioTuning : ScriptableObject
{
    [Header("Fixed budget: first four voices are reserved for persistent beds")]
    [Min(8)] public int PoolSize=24;
    [Min(1)] public int MaximumTrackedActors=128;
    [Min(.02f)] public float ActorSampleInterval=.05f, ActorRefreshInterval=1;
    public float FootstepDistance=1.55f, MinimumFootstepSpeed=.5f, ActorAudibleDistance=35;
    public float MinimumLandingSpeed=3, LoopFadePerSecond=2, HeatResponse=2;
    public Vector2 CalmBedGain=new Vector2(1,.55f), SirenBedGain=new Vector2(0,1), MusicGain=new Vector2(.35f,1);
    public float MenuMusicGain=.4f;
    public AudioMixer Mixer;
    [Range(-80,6)] public float MasterDb=-3, MusicDb=-10, SfxDb=-3, UiDb=-5, AmbientDb=-9;
    public AudioCueDefinition[] Cues;
    public PowerAudioBinding[] Powers;
    public AudioCueDefinition Find(AudioCue id){foreach(var cue in Cues)if(cue.Id==id)return cue;return null;}
}
