using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ModeScreens : MonoBehaviour
{
    GameModeDefinition[] modes;
    Vector2 scroll;
    void Awake() { modes=Resources.LoadAll<GameModeDefinition>("Modes"); Array.Sort(modes,(a,b)=>a.MenuOrder.CompareTo(b.MenuOrder)); }
    void OnGUI()
    {
        var flow=GameFlow.Instance; if(flow==null) return;
        GUI.skin.label.wordWrap=true;
        float width=Mathf.Min(620,Screen.width-32);
        GUILayout.BeginArea(new Rect((Screen.width-width)*.5f,Mathf.Max(16,Screen.height*.12f),width,Screen.height*.8f),GUI.skin.box);
        GUILayout.Label("OVERPOWERED",new GUIStyle(GUI.skin.label){fontSize=40,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter});
        if(SceneManager.GetActiveScene().name==GameFlow.ResultsScene)
        {
            var r=flow.Result;
            if(r!=null)
            {
                GUILayout.Label(r.ModeName+" — "+r.Outcome,new GUIStyle(GUI.skin.label){fontSize=26});
                GUILayout.Label(r.Reason);
                GUILayout.Label($"Score {r.Score}     XP earned {r.Xp}\nEncounters completed {r.Successes}     Failed {r.Failures}\nPlayer defeats {r.Defeats}     Time {TimeSpan.FromSeconds(r.Seconds):mm\\:ss}");
                GUILayout.Label("Your earned progression and upgrades are saved across modes.");
            }
            GUI.enabled=!flow.Loading; if(GUILayout.Button("BACK TO HOME",GUILayout.Height(48))) flow.Home(); GUI.enabled=true;
        }
        else
        {
            GUILayout.Label("CHOOSE YOUR SIDE",new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter});
            scroll=GUILayout.BeginScrollView(scroll);
            foreach(var mode in modes)
            {
                GUI.enabled=mode.Playable&&!flow.Loading;
                if(GUILayout.Button(mode.DisplayName+" — "+(mode.Playable?"PLAYABLE":"COMING SOON"),GUILayout.Height(55))) flow.Select(mode);
                GUI.enabled=true; GUILayout.Label(mode.Description); GUILayout.Space(12);
            }
            GUILayout.EndScrollView();
        }
        if(flow.Loading) GUILayout.Label("Loading…");
        GUILayout.EndArea();
    }
}
