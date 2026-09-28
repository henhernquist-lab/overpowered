#if UNITY_EDITOR
// Entry-point aliases so the static gauntlet verifiers have stable -executeMethod targets.
using UnityEditor;

public static class VerificationGauntletInventory
{
    [MenuItem("Overpowered/Verification/Gauntlet Inventory Report")]
    public static void Run() { _ = VerificationInventoryRunner.Run(); }

    [MenuItem("Overpowered/Verification/Hard-Code Audit")]
    public static void HardCodeAudit() { _ = HardCodeAuditRunner.Run(); }
}
#endif
