#if UNITY_EDITOR
using System.Collections;
using UnityEngine;
public sealed partial class PowerExpansionVerificationRunner
{
    IEnumerator AimChecks()
    {
        foreach(string id in new[]{"aerokinesis","sonic-manipulation"})
        {
            yield return EnterExpansion(id);yield return ClearActors();var view=Camera.main.GetComponent<ThirdPersonCamera>();
            foreach(bool first in new[]{true,false})
            {
                if(view.FirstPerson!=first)view.ToggleView();view.SetLook(0,0);yield return null;yield return ClearActors();Ready();
                var npc=Npc("Rusher",new Vector3(0,.1f,.9f));yield return null;
                Check(W.Powers.FindTarget(W.Powers.Stats(W.Powers.Selected).Range,out var aimed)&&aimed.collider.GetComponentInParent<CityNpc>()==npc,id+": actual "+(first?"first":"third")+"-person center ray visibly intersects close hostile capsule");
                float hp=npc.CurrentHealth;Check(W.Powers.Use(W.Powers.Selected),"Close directional cast accepted");
                Check(npc.CurrentHealth<hp,id+": center-screen point-blank target receives real damage rather than missing an arbitrary torso point");
                yield return ClearActors();Ready();npc=Npc("Rusher",new Vector3(0,.1f,.9f));var wall=Wall(new Vector3(0,1.5f,.45f),new Vector3(4,3,.15f));yield return null;hp=npc.CurrentHealth;W.Powers.Use(W.Powers.Selected);
                Check(npc.CurrentHealth==hp,"CONTROL close intervening wall still blocks directional damage");Destroy(wall);yield return null;
                yield return ClearActors();Ready();npc=Npc("Rusher",new Vector3(3,.1f,.9f));hp=npc.CurrentHealth;W.Powers.Use(W.Powers.Selected);
                Check(npc.CurrentHealth==hp,"CONTROL capsule-volume test still excludes a target outside the forward cone");
            }
        }
    }
}
#endif
