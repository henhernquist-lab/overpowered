using System.Collections.Generic;
using UnityEngine;

public enum AttackKind { Melee, Ranged, Slam }

/// Enemy BEHAVIOUR as data (Resources/Enemies). Role still decides hostility and body colour; the archetype decides
/// movement, the telegraphed attack and the silhouette. Health/damage are multipliers on the spawn's base values
/// (the standard Heat-star enemy formula, or an Endless wave's explicit stats), so both escalation curves stay intact.
[CreateAssetMenu(menuName="Overpowered/Enemy Archetype")]
public sealed class EnemyArchetype : ScriptableObject
{
    public string DisplayName="Enemy";
    [Tooltip("Free tags districts can prefer (DistrictGameplayProfile.EnemyTags), e.g. \"dockworker\". Empty = none.")] public string[] Tags=new string[0];
    [Header("Body")]
    [Tooltip("Hostile movement speed, m/s. Non-hostile NPCs keep their role speed.")] public float MoveSpeed=5.5f;
    [Tooltip("x standard enemy health (GameTuning CopHealth + Heat stars) or x the Endless wave health.")] public float HealthMultiplier=1f;
    [Tooltip("x standard enemy damage (GameTuning AttackDamage + Heat stars) or x the Endless wave damage.")] public float DamageMultiplier=1f;
    [Header("Telegraphed attack: Approach -> Windup (commit) -> Release (re-check) -> Recover")]
    public AttackKind Kind=AttackKind.Melee;
    [Tooltip("Melee/Slam: the NPC commits once the player is this close. Ranged: maximum distance to start a shot.")] public float TriggerDistance=1.6f;
    [Tooltip("Melee/Slam: centre of the committed disc, metres in front of the NPC at windup start.")] public float Reach=1f;
    [Tooltip("Melee/Slam: disc radius (the telegraph shows exactly this). Ranged: line half-width to the player's capsule surface.")] public float Radius=1.1f;
    [Tooltip("Ranged: length of the locked shot line from the muzzle.")] public float Range=22f;
    [Tooltip("Ranged: muzzle height above the feet (before VisualScale).")] public float MuzzleHeight=1.35f;
    [Tooltip("Melee/Slam: highest player-feet height above the disc that is still hit (a hovering hero is not).")] public float MaxHitHeight=1.2f;
    public float WindupSeconds=.45f, CooldownSeconds=.8f;
    [Tooltip("Metres the player is shoved away over KnockbackSeconds when a hit actually deals damage.")] public float Knockback=0f;
    public float KnockbackSeconds=.25f;
    [Header("Ranged: hold a band around the player instead of closing in")]
    public float PreferredDistance=10f;
    [Tooltip("Half-width of the preferred band (PreferredDistance +/- this).")] public float PreferredBand=2f;
    [Header("Telegraph (shared palette material only)")]
    [Tooltip("Ranged aim-line thickness at windup end (starts at 40%).")] public float TelegraphWidth=.12f;
    [Tooltip("Melee/Slam disc radius at windup START as a fraction of Radius; it grows to exactly Radius at release.")] [Range(0,1)] public float TelegraphStartFraction=.2f;
    [Header("Readability")]
    [Tooltip("Uniform scale of the VISUAL root only; collider and NavMeshAgent dimensions follow it. Never the physics root.")] public float VisualScale=1f;
    [Tooltip("Palette colour of the joints mesh (body colour still comes from the role).")] public CityColor Accent=CityColor.Metal;

    void OnValidate()
    {
        MoveSpeed=Mathf.Max(.1f,MoveSpeed);HealthMultiplier=Mathf.Max(.01f,HealthMultiplier);DamageMultiplier=Mathf.Max(0,DamageMultiplier);
        TriggerDistance=Mathf.Max(.1f,TriggerDistance);Radius=Mathf.Max(.05f,Radius);Range=Mathf.Max(.5f,Range);
        WindupSeconds=Mathf.Max(0,WindupSeconds);CooldownSeconds=Mathf.Max(0,CooldownSeconds);KnockbackSeconds=Mathf.Max(.01f,KnockbackSeconds);
        PreferredBand=Mathf.Max(.1f,PreferredBand);VisualScale=Mathf.Clamp(VisualScale,.5f,2f);
    }
}

/// Attack-token budget: at most Budget hostile NPCs may be engaged (committed approach, windup and release) at once,
/// so crowds attack in a readable rhythm. Tokens are released on release, cancel, death, freeze, disable and session end;
/// dead/destroyed/disabled holders are also pruned on every query so a leak can never silence the crowd.
/// Requests are served oldest-first so the NPCs updated first each frame cannot monopolise the budget.
public sealed class AttackTokenPool
{
    readonly List<CityNpc> holders=new List<CityNpc>(8);
    readonly List<CityNpc> queue=new List<CityNpc>(16);
    /// Verification CONTROL override; negative = use EnemyRoster.MaxConcurrentAttackers.
    public int BudgetOverride=-1;
    public int Budget=>BudgetOverride>=0?BudgetOverride:EnemyRoster.Current!=null?EnemyRoster.Current.MaxConcurrentAttackers:2;
    public int Count{get{Prune();return holders.Count;}}
    public int Waiting{get{Prune();return queue.Count;}}
    public int Granted{get;private set;}
    public float LastGrantTime{get;private set;}=-1;
    public CityNpc LastGrantee{get;private set;}
    public bool Holds(CityNpc npc)=>holders.Contains(npc);
    public bool TryAcquire(CityNpc npc)
    {
        if(holders.Contains(npc))return true;
        Prune();
        int position=queue.IndexOf(npc);
        if(position<0){queue.Add(npc);position=queue.Count-1;}
        int free=Budget-holders.Count;
        if(position>=free)return false;
        queue.RemoveAt(position);holders.Add(npc);Granted++;LastGrantTime=Time.time;LastGrantee=npc;return true;
    }
    /// The NPC is no longer asking (moved out of range, cooling down, lost line of sight).
    public void Withdraw(CityNpc npc){queue.Remove(npc);}
    public void Release(CityNpc npc){holders.Remove(npc);queue.Remove(npc);}
    void Prune()
    {
        for(int i=holders.Count-1;i>=0;i--)if(Gone(holders[i]))holders.RemoveAt(i);
        for(int i=queue.Count-1;i>=0;i--)if(Gone(queue[i]))queue.RemoveAt(i);
    }
    static bool Gone(CityNpc npc)=>npc==null||npc.Dead||!npc.isActiveAndEnabled;
}
