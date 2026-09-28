using UnityEngine;

/// The shipped challenge list (Resources/ChallengeCatalog). Enabled=false (the setup default) keeps challenges dormant: no
/// tracking and no rewards until the explicit content switch (menu "Overpowered/Challenges/Enable challenges").
[CreateAssetMenu(menuName = "Overpowered/Challenge catalog")]
public sealed class ChallengeCatalog : ScriptableObject
{
    public bool Enabled;
    public ChallengeDefinition[] Challenges = new ChallengeDefinition[0];
    public static ChallengeCatalog Current => Resources.Load<ChallengeCatalog>("ChallengeCatalog");
}
