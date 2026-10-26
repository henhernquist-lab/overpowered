using UnityEngine;
public sealed partial class CityNpc
{
    MirageDecoy chosenDecoy,committedDecoy;bool committedAtDecoy;float nextDecoyCheck;
    MirageDecoy CombatDecoy
    {
        get
        {
            if(Time.time>=nextDecoyCheck)
            {
                nextDecoyCheck=Time.time+.2f;chosenDecoy=null;var decoy=MirageDecoy.Current;
                if(!IsBoss&&Hostile&&decoy!=null&&decoy.Alive&&(decoy.transform.position-transform.position).sqrMagnitude<=14*14)
                {
                    Vector3 from=transform.position+Vector3.up,to=decoy.transform.position+Vector3.up;
                    if(!Blocked(from,to-from,Vector3.Distance(from,to)))chosenDecoy=decoy;
                }
            }
            return chosenDecoy!=null&&chosenDecoy.Alive?chosenDecoy:null;
        }
    }
    public bool TargetingDecoy=>CombatDecoy!=null;
    Vector3 CombatTargetPosition=>CombatDecoy!=null?CombatDecoy.transform.position:world.Hero.transform.position;
}
