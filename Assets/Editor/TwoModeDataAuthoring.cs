#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// One explicit native asset edit; does not reconstruct legacy modes or overwrite their saved identifiers.
public static class TwoModeDataAuthoring
{
    public static void Run()
    {
        foreach (var mode in Resources.LoadAll<GameModeDefinition>("Modes"))
        {
            mode.ShowOnHome = mode.Id == "endless-fight" || mode.Id == "free-play";
            if (mode.Id == "endless-fight")
            {
                mode.DisplayName = "Endless Fight"; mode.MenuOrder = 0;
                mode.MenuTagline = "PROGRESSIVE ROUNDS. A BOSS EVERY FIVE.";
                mode.Description = "Survive progressive combat rounds with your equipped hero. Defeat Pulsebreaker every fifth round and beat your best score.";
                mode.SideFromProfile = true;
                mode.BriefingGoal = "Survive combat rounds. Pulsebreaker arrives every fifth round.";
            }
            if (mode.Id == "free-play")
            {
                mode.MenuOrder = 1;
                mode.MenuTagline = "EXPLORE THE CITY. MAKE YOUR OWN RULES.";
            }
            EditorUtility.SetDirty(mode);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("PASS Home assets expose exactly Endless Fight and Free Play; legacy definitions remain playable internally.");
        EditorApplication.Exit(0);
    }
}
#endif
