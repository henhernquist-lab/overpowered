#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public sealed partial class CombatLabVerificationRunner
{
    void VerifyWaveData()
    {
        var waves=Resources.Load<EndlessWaveDirector>("ModeDirectors/EndlessWaves");
        foreach(string id in new[]{"endless-fight","endless-fight-villain"})Check(Resources.Load<GameModeDefinition>("Modes/"+id).Director==waves,id+" shares the expanded wave table");
        foreach(string id in new[]{"hero","villain","free-play"})Check(Resources.Load<GameModeDefinition>("Modes/"+id).Director==null,id+" receives no invented rounds or automatic director");
        Check(waves.Events==null,"No miniboss/events enabled by combat expansion");
        for(int wave=1;wave<=12;wave++)
        {
            var names=new HashSet<string>();
            for(int i=0;i<Mathf.Min(waves.WaveSize(wave),waves.MaxAlive);i++)
            {
                var hero=waves.Compose(wave,i,PlayerSide.Hero);var villain=waves.Compose(wave,i,PlayerSide.Villain);
                names.Add(hero.Archetype.name);
                Check(hero.Archetype==villain.Archetype&&hero.Role==NpcRole.Criminal&&villain.Role==NpcRole.Cop,"Wave "+wave+" composition preserves side-specific roles at index "+i);
                Check(Mathf.Abs(hero.Health-waves.HealthFor(wave)*hero.Archetype.HealthMultiplier)<.001f,"Wave stats contain archetype factor once (difficulty remains at spawn)");
            }
            Check(names.Contains("Skirmisher")== (wave>=3),"Skirmisher eligibility wave "+wave);
            Check(names.Contains("Bulwark")== (wave>=5),"Bulwark eligibility wave "+wave);
            Check(names.Contains("Rusher")&&(wave<2||names.Contains("Gunner"))&&(wave<3||names.Contains("Brute")),"Original types remain in wave "+wave);
            Check(waves.AliveTarget(wave)<=waves.MaxAlive,"Alive cap preserved in wave "+wave);
        }
        W.Progression.SetModeSide(PlayerSide.Villain);
        var police=CityNpc.Spawn(W,new Vector3(20,0,0),NpcRole.Cop,Resources.Load<EnemyArchetype>("Enemies/Bulwark"));
        if(police!=null) {police.AlwaysAggro=true;Check(police.Hostile,"Villain responder is hostile");W.Progression.SetModeSide(PlayerSide.Hero);Check(!police.Hostile,"Hero responder preserves friendly faction policy");Destroy(police.gameObject);}
        W.Progression.SetModeSide(PlayerSide.Hero);
    }
}
#endif
