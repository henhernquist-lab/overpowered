using UnityEngine;
using UnityEngine.SceneManagement;

public enum SessionOutcome { Won, Lost, TimedOut, Abandoned }
public sealed class SessionResult
{
    public string ModeId, ModeName, Reason;
    public SessionOutcome Outcome;
    public int Score, Xp, Successes, Failures, Defeats;
    public float Seconds;
    public PlayerSide Side;
    public int Rescues, StartLevel, StartXp, EndLevel, EndXp;
    public float PeakHeat, TimeLimit;
}
public sealed class GameFlow : MonoBehaviour
{
    public const string HomeScene="Home", CityScene="Prototype", ResultsScene="Results";
    public static GameFlow Instance { get; private set; }
    public static bool VerificationSandbox;
    public GameModeDefinition ActiveMode { get; private set; }
    public SessionResult Result { get; private set; }
    public bool Loading { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if(Instance!=null) return;
        var go=new GameObject("Game flow"); Instance=go.AddComponent<GameFlow>(); DontDestroyOnLoad(go);
        SceneManager.sceneLoaded+=Instance.SceneReady;
    }
    void SceneReady(Scene scene,LoadSceneMode mode)
    {
        Loading=false; Time.timeScale=1f;
        if(scene.name==CityScene)
        {
            if(ActiveMode==null&&!VerificationSandbox) { Home(); return; }
            PrototypeBootstrap.BuildCity(); return;
        }
        Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
        GameCamera.Ensure(scene,false);
        new GameObject("Menu").AddComponent<ModeScreens>();
    }
    public bool Select(GameModeDefinition definition)
    {
        if(Loading||definition==null||!definition.Playable||definition.Rules==null||definition.Encounters==null||definition.Encounters.Length==0) return false;
        ActiveMode=definition; Result=null; Load(CityScene); return true;
    }
    public void Results(SessionResult result,bool home=false) { if(Loading) return; Result=result; if(home) ActiveMode=null; Load(home?HomeScene:ResultsScene); }
    public void Home() { if(Loading) return; ActiveMode=null; Load(HomeScene); }
    void Load(string scene) { Loading=true; Time.timeScale=1; SceneManager.LoadSceneAsync(scene,LoadSceneMode.Single); }
    void OnDestroy() { SceneManager.sceneLoaded-=SceneReady; if(Instance==this) Instance=null; Time.timeScale=1; }
}
