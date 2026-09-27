using UnityEngine;

[CreateAssetMenu(menuName="Overpowered/Lighting/Cycle")]
public sealed class LightingCycle : ScriptableObject
{
    [Tooltip("Presets by time of day (0..1), in ascending Time order.")] public LightingKey[] Keys=new LightingKey[0];
    [Tooltip("Real (scaled) seconds for time of day to run 0 -> 1. 0 = static at StartTime.")] [Min(0)] public float DurationSeconds;
    [Range(0,1)] public float StartTime;
    [Tooltip("True: 1 wraps to 0 (a repeating day). False: time holds at 1 (e.g. dusk that settles into night).")] public bool Loop=true;
    public bool Advances=>DurationSeconds>0;
    public float Advance(float time,float seconds)
    {
        if(!Advances)return time;
        time+=seconds/DurationSeconds;
        return Loop?Mathf.Repeat(time,1f):Mathf.Min(time,1f);
    }
    public float Normalize(float time)=>Loop?Mathf.Repeat(time,1f):Mathf.Clamp01(time);
    /// The two presets around `time` and the smoothstep weight of the second. A single key (or none after it) returns it twice.
    public void Evaluate(float time,out LightingPreset from,out LightingPreset to,out float weight)
    {
        from=to=null;weight=0;int n=Keys==null?0:Keys.Length;if(n==0)return;
        time=Normalize(time);
        if(n==1){from=to=Keys[0].Preset;return;}
        int next=0;while(next<n&&Keys[next].Time<=time)next++;
        int prev=next-1;
        if(prev<0){if(!Loop){from=to=Keys[0].Preset;return;}prev=n-1;}
        if(next>=n){if(!Loop){from=to=Keys[n-1].Preset;return;}next=0;}
        float a=Keys[prev].Time,b=Keys[next].Time,span=b-a,at=time-a;
        if(prev>=next){span+=1f;if(at<0)at+=1f;} // wrapping segment (last key -> first key of the next day)
        from=Keys[prev].Preset;to=Keys[next].Preset;weight=span<=0?1:Mathf.SmoothStep(0,1,Mathf.Clamp01(at/span));
        if(from==to)weight=0;
    }
}
