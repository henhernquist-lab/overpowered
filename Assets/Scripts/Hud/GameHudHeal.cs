using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// Green "+N HP" numbers over the hero. The ONLY input is WorldSession.PlayerHealed, which carries the health actually
/// restored, so the number and the health bar can never disagree. Bounded pool (no growth), no raycast per number (the
/// anchor is the player, always the closest visible body) and no per-frame allocation.
public sealed partial class GameHud
{
    public const int HealNumberPoolSize = 8;
    public const float HealNumberSeconds = .9f, HealNumberRise = 40f, HealNumberSpacing = 26f;

    public sealed class HealNumberDisplay
    {
        public Label Element;
        public float Amount, StartedAt;
        public bool Active;
    }

    public readonly List<HealNumberDisplay> HealNumbers = new List<HealNumberDisplay>(HealNumberPoolSize);
    /// Every number actually started (verification/diagnostics).
    public int HealNumbersShown { get; private set; }
    public float LastHealShown { get; private set; }

    VisualElement healNumberLayer;
    WorldSession healBound;

    void BuildHealFeedback()
    {
        if (healNumberLayer != null) return;
        healNumberLayer = Box("heal-numbers");
        Absolute(healNumberLayer);
        healNumberLayer.style.left = healNumberLayer.style.top = healNumberLayer.style.right = healNumberLayer.style.bottom = 0;
        OverlayRoot.Add(healNumberLayer);
        var ink = C(CityColor.Lawn);
        var navy = C(CityColor.UiNavy);
        for (int i = 0; i < HealNumberPoolSize; i++)
        {
            var display = new HealNumberDisplay();
            var label = Text("", 20, ink);
            Absolute(label);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.width = 90; label.style.height = 28;
            label.style.backgroundColor = Alpha(navy, .82f);
            Round(label, 5); Border(label, Alpha(ink, .7f), 1);
            Show(label, false);
            display.Element = label; healNumberLayer.Add(label); HealNumbers.Add(display);
        }
    }

    void BindHealEvents()
    {
        var world = WorldSession.Instance;
        if (healBound == world) return;
        if (healBound != null) healBound.PlayerHealed -= OnPlayerHealed;
        healBound = world;
        if (healBound != null) healBound.PlayerHealed += OnPlayerHealed;
    }

    void UnbindHealEvents()
    {
        if (healBound == null) return;
        healBound.PlayerHealed -= OnPlayerHealed; healBound = null;
    }

    /// The event carries the APPLIED heal. Saturation reuses the oldest number instead of growing the pool.
    void OnPlayerHealed(float amount)
    {
        if (healNumberLayer == null || amount <= 0f) return;
        float now = Time.unscaledTime;
        HealNumberDisplay free = null, oldest = null;
        foreach (var display in HealNumbers)
        {
            if (!display.Active || now - display.StartedAt >= HealNumberSeconds) { free = display; break; }
            if (oldest == null || display.StartedAt < oldest.StartedAt) oldest = display;
        }
        var slot = free ?? oldest;
        if (slot == null) return;
        slot.Active = true; slot.Amount = amount; slot.StartedAt = now;
        slot.Element.text = "+" + CombatValue(amount) + " HP";
        HealNumbersShown++; LastHealShown = amount;
    }

    /// Called once per frame from GameHud.LateUpdate, after the enemy feedback.
    void TickHealFeedback()
    {
        if (healNumberLayer == null) BuildHealFeedback();
        BindHealEvents();
        var world = WorldSession.Instance; var camera = ViewCamera();
        bool running = world != null && world.Hero != null && camera != null && OverlayRoot != null &&
            OverlayRoot.layout.width > 1f && OverlayRoot.style.display != DisplayStyle.None;
        if (!running)
        {
            foreach (var display in HealNumbers) { display.Active = false; Show(display.Element, false); }
            return;
        }
        float now = Time.unscaledTime, width = OverlayRoot.layout.width, height = OverlayRoot.layout.height;
        Vector3 view = camera.WorldToViewportPoint(world.Hero.transform.position + Vector3.up * 2.3f);
        bool onScreen = view.z > 0f && view.x > .02f && view.x < .98f && view.y > .04f && view.y < .96f;
        int stack = 0;
        foreach (var display in HealNumbers)
        {
            if (!display.Active) continue;
            float age = now - display.StartedAt;
            if (age >= HealNumberSeconds) { display.Active = false; Show(display.Element, false); continue; }
            if (!onScreen) { Show(display.Element, false); continue; }
            float rise = age / HealNumberSeconds * HealNumberRise;
            display.Element.style.left = view.x * width - 45f;
            display.Element.style.top = (1f - view.y) * height - 40f - rise - stack++ * HealNumberSpacing;
            display.Element.style.opacity = Mathf.Clamp01((HealNumberSeconds - age) / .25f);
            Show(display.Element, true);
        }
    }
}
