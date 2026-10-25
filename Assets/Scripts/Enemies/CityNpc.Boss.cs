using UnityEngine;

/// Boss hook for CityNpc. Everything here is inert for every existing enemy: `Boss` stays null and both resistances stay 1,
/// so the archetype attack cycle, freeze, root and damage behaviour are bit-for-bit unchanged.
public sealed partial class CityNpc
{
    PulsebreakerBoss boss;
    /// The Pulsebreaker brain driving this body, or null for an ordinary enemy.
    public PulsebreakerBoss Boss => boss;
    public bool IsBoss => boss != null;
    /// Incoming freeze duration multiplier. 1 = unchanged (every archetype except the boss).
    public float FreezeResistance = 1f;
    /// Incoming root duration multiplier. 1 = unchanged.
    public float RootResistance = 1f;

    internal void AttachBoss(PulsebreakerBoss value) { boss = value; }
    internal void ClearBossReference(PulsebreakerBoss value) { if (boss == value) boss = null; }
}
