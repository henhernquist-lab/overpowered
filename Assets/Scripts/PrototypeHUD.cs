using UnityEngine;

public sealed class PrototypeHUD : MonoBehaviour
{
    SuperHeroController hero;
    GUIStyle title, label;
    void Awake() { title = new GUIStyle { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } }; label = new GUIStyle { fontSize = 16, normal = { textColor = Color.white } }; }
    void OnGUI()
    {
        if (hero == null) hero = Object.FindFirstObjectByType<SuperHeroController>(); if (hero == null) return;
        GUI.Box(new Rect(18, 18, 460, 210), "");
        GUI.Label(new Rect(34, 30, 420, 30), "OVERPOWERED — PHYSICS PLAYGROUND", title);
        GUI.Label(new Rect(34, 70, 420, 28), $"FLIGHT FUEL: {hero.FlightFuel:0.00} / {PrototypeTuning.FlightDuration:0.00} s", label);
        GUI.Label(new Rect(34, 98, 420, 28), $"PUNCH CHARGES: {hero.Charges} / {PrototypeTuning.PunchMaxCharges}    CD: {hero.Cooldown:0.00}s", label);
        GUI.Label(new Rect(34, 126, 420, 28), hero.LastPunchResult, label);
        GUI.Label(new Rect(34, 162, 420, 26), "WASD move · Shift run · Space jump · F flight · LMB/E punch", label);
        GUI.Label(new Rect(34, 188, 420, 26), "V runs verification telemetry in Console", label);
    }
}
