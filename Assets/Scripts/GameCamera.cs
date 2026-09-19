using UnityEngine;
using UnityEngine.SceneManagement;

// A real scene-owned camera exists before Play; runtime setup reuses it rather than hiding it in a factory.
[RequireComponent(typeof(Camera), typeof(AudioListener))]
public sealed class GameCamera : MonoBehaviour
{
    public static Camera Ensure(Scene scene, bool gameplay)
    {
        GameCamera rig=null;
        foreach(var root in scene.GetRootGameObjects())
        {rig=root.GetComponentInChildren<GameCamera>(true);if(rig!=null)break;}
        if(rig==null)
        {
            var go=new GameObject("Main Camera");SceneManager.MoveGameObjectToScene(go,scene);
            rig=go.AddComponent<GameCamera>();
        }
        rig.gameObject.SetActive(true);rig.tag="MainCamera";
        var camera=rig.GetComponent<Camera>();camera.enabled=true;
        camera.targetTexture=null;camera.targetDisplay=0;camera.rect=new Rect(0,0,1,1);
        camera.clearFlags=gameplay?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.035f,.05f,.08f);camera.cullingMask=gameplay?~0:0;
        return camera;
    }
}
