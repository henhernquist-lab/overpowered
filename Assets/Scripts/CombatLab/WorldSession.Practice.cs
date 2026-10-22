using UnityEngine;

public sealed partial class WorldSession
{
    public bool IsPractice { get; private set; }
    public void InitializePractice(GameTuning tuning, CityDistrict arena, SuperHeroController hero, ProgressSave snapshot, string snapshotPath)
    {
        IsPractice=true; Instance=this; Tuning=tuning; City=arena; Hero=hero;
        var definitions=Resources.LoadAll<PowerDefinition>("Powers");
        System.Array.Sort(definitions,(a,b)=>string.CompareOrdinal(a.Id,b.Id));
        Progression=hero.gameObject.AddComponent<PlayerProgression>();
        Progression.InitializeTransient(tuning.Progression,definitions,snapshot,snapshotPath);
        Difficulty=new SessionDifficulty(Progression.SelectedDifficulty);
        Powers=hero.gameObject.AddComponent<PowerUser>(); Powers.Initialize(hero,Progression,definitions,tuning.Movement);
        hero.Initialize(Powers,tuning.Movement); Health=MaxHealth;
        Civilians=gameObject.AddComponent<CivilianLedger>();
        Message="COMBAT LAB · F2 controls · practice progress is discarded";
    }
    public bool RestorePracticeHealth()
    {
        if(!IsPractice)return false;
        bool dead=PlayerDead; Health=MaxHealth; deathTimer=0;
        if(dead) { PlacePracticeHero(); PlayerRespawned?.Invoke(); }
        return true;
    }
    public void PlacePracticeHero()
    {
        if(!IsPractice)return;
        Powers.Phase?.End(); Powers.Release(false); Powers.EndChannel(); Powers.SynergyRunner?.Cancel();
        var body=Hero.GetComponent<CharacterController>(); body.enabled=false;
        Hero.transform.position=City.Spawn+Vector3.up*.1f; body.enabled=true; Hero.ResetMotion();
    }
    void TickPractice()
    {
        if(!PlayerDead&&Hero.transform.position.y<Tuning.Movement.KillPlane)DamagePlayer(Health,true);
        if(!PlayerDead)return;
        deathTimer+=Time.deltaTime;
        if(deathTimer>=Tuning.Movement.RespawnDelay)RestorePracticeHealth();
    }
}
