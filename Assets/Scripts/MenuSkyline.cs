using UnityEngine;

/// One cached capture of the actual seeded building factory, not a second gameplay world.
public sealed class MenuSkyline : MonoBehaviour
{
    public RenderTexture Texture {get;private set;}
    public int BuildingCount {get;private set;}
    public float CaptureMilliseconds {get;private set;}
    string captureKey;
    public RenderTexture Get(MenuPresentationTuning menu)
    {
        var tuning=Resources.Load<GameTuning>("GameTuning");var palette=Resources.Load<CityPalette>("CityPalette");
        string key=JsonUtility.ToJson(menu)+JsonUtility.ToJson(tuning.City)+JsonUtility.ToJson(palette)+JsonUtility.ToJson(Resources.Load<CityLayout>("CityLayout"))+JsonUtility.ToJson(Resources.Load<CityArtSettings>("CityArtSettings"));
        if(Texture!=null&&captureKey==key)return Texture;
        if(Texture!=null){Texture.Release();Destroy(Texture);}captureKey=key;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var root=new GameObject("Menu skyline capture (no actors or simulation)");
        var art=root.AddComponent<CityArt>();art.Initialize(tuning);
        var buildings=Resources.Load<CityLayout>("CityLayout").Generate(tuning.City);BuildingCount=buildings.Count;
        for(int i=0;i<buildings.Count;i++)art.Building(buildings[i],i);
        art.FinishStaticGeometry();
        // Extended neutral ground is a backdrop only: no floating diorama edge or new city layout.
        CityArt.Piece(root.transform,"City backdrop ground",new Vector3(0,-1,0),new Vector3(1000,1,1000),CityColor.Road);
        foreach(var transform in root.GetComponentsInChildren<Transform>())transform.gameObject.layer=31;
        foreach(var collider in root.GetComponentsInChildren<Collider>())collider.enabled=false;
        var light=new GameObject("Skyline capture sun").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.2f;light.color=palette.Colors[(int)CityColor.Cream];light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(45,-35,0);
        var camera=new GameObject("Skyline capture camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.enabled=false;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=palette.Colors[(int)CityColor.UiPurple];camera.fieldOfView=menu.SkylineFieldOfView;camera.transform.position=menu.SkylineCamera;camera.transform.LookAt(menu.SkylineLook);
        Texture=new RenderTexture(menu.SkylineWidth,menu.SkylineHeight,24){name="Actual seeded city skyline",filterMode=FilterMode.Bilinear};Texture.Create();camera.targetTexture=Texture;
        Color ambient=RenderSettings.ambientLight;
        try{RenderSettings.ambientLight=palette.Colors[(int)CityColor.Slate];camera.Render();}
        finally{RenderSettings.ambientLight=ambient;camera.targetTexture=null;root.SetActive(false);Destroy(root);}
        watch.Stop();CaptureMilliseconds=(float)watch.Elapsed.TotalMilliseconds;return Texture;
    }
    void OnDestroy(){if(Texture!=null){Texture.Release();Destroy(Texture);}}
}
