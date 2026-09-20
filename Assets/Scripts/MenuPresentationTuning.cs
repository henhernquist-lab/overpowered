using UnityEngine;
[CreateAssetMenu(menuName="Overpowered/Menu Presentation Tuning")]
public sealed class MenuPresentationTuning : ScriptableObject
{
    [Header("Unscaled UI motion")]
    public float HoverLift=8, HoverResponse=12, XpFillSeconds=1.8f, LevelPopSeconds=.45f, LevelPopScale=.15f;
    public int AmbientCount=28;
    public float AmbientDrift=12, AmbientSway=12, AmbientSize=2.2f;
    [Header("One-time skyline capture, existing CityLayout and CityArt")]
    public int SkylineWidth=768, SkylineHeight=432;
    public Vector3 SkylineCamera=new Vector3(-65,22,-105), SkylineLook=new Vector3(0,19,0);
    public float SkylineFieldOfView=48, SkylineTint=.52f;
}
