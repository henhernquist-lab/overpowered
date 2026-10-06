#if UNITY_EDITOR
using System.Collections;
using UnityEngine;

/// Same hero, isolated real actor and three-second input window for each equipped power.
public sealed class LaserBalanceRunner : SessionVerificationRunner
{
    protected override string Folder=>"Verification/LaserPresentation/";
    protected override string ResultFile=>"balance.txt";
    protected override IEnumerator Checks()
    {
        Time.captureDeltaTime=1f/120f;
        foreach(var id in new[]{"laser-eyes","fire","lightning","poison","strength","basic-melee"})
            yield return Sample(id);
        Time.captureDeltaTime=0;
        Log("LIMIT: three game-seconds on stationary single criminal in the isolated fixture. Fire/Strength in-flight hits after the deadline are excluded; Poison's remaining DoT is excluded. Multi-target damage, reach, mobility and human feel are different advantages.");
    }
    IEnumerator Sample(string id)
    {
        bool basic=id=="basic-melee", melee=basic||id=="strength";
        yield return Enter(basic?"fire":id,basic?"ice":id=="strength"?"fire":"strength");
        Isolate();
        var target=Actor(new Vector3(0,150,melee?1.8f:10f),NpcRole.Criminal,1000);
        yield return new WaitForSeconds(.2f);
        Aim(Chest(target));
        var rt=basic?Runtime("strength"):Runtime(id);
        float health=target.Health,energy=W.Powers.Energy,start=Time.time;
        int attempts=0,casts=0;bool poisonCast=false;
        if(id=="laser-eyes"){Check(W.Powers.Use(rt),"Laser starts fixed-window comparison through its loadout gate.");casts++;}
        while(Time.time-start<3f)
        {
            if(id=="laser-eyes")W.Powers.Channel(true,Time.deltaTime);
            else if(melee)
            {
                attempts++;
                if(W.Hero.TryPunch())casts++;
            }
            else if(!poisonCast&&id=="poison" || id!="poison"&&rt.Cooldown<=0&&rt.Charges>0&&W.Powers.Energy>=rt.Definition.ResourceCost)
            {
                attempts++;
                if(W.Powers.Use(rt)){casts++;if(id=="poison")poisonCast=true;}
            }
            yield return null;
        }
        if(id=="laser-eyes")W.Powers.Channel(false,0);
        float seconds=Time.time-start,dealt=health-target.Health,spent=energy-W.Powers.Energy;
        Log($"MEASURED balance {id}: targetDistance={(melee?1.8f:10f):F1}m window={seconds:F3}s casts={casts} attempts={attempts} delivered={dealt:F2} DPS={dealt/seconds:F2} netEnergyChange={spent:F2} range={(melee?"close melee":W.Powers.Stats(rt).Range.ToString("F1")+"m")} targetAlive={!target.Dead}.");
        Check(casts>0&&dealt>0,"Real "+id+" hit the single stationary target in its fixed input window.");
    }
}
#endif
