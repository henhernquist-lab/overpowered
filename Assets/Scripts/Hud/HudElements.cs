using UnityEngine;
using UnityEngine.UIElements;

/// Radial cooldown overlay: a shaded pie for the REMAINING fraction (clockwise from 12 o'clock) plus a thin accent
/// sweep on the rim. Repaints only when the fraction changes by a visible amount.
public sealed class HudRadial : VisualElement
{
    public Color Shade, Rim;
    public float Fraction { get; private set; }
    public HudRadial(Color shade, Color rim) { Shade = shade; Rim = rim; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
    public void Set(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        if (Mathf.Abs(fraction - Fraction) < .004f && (fraction > 0) == (Fraction > 0)) return;
        Fraction = fraction; MarkDirtyRepaint();
    }
    void Draw(MeshGenerationContext context)
    {
        if (Fraction <= 0f) return;
        var r = contentRect; if (r.width <= 0 || r.height <= 0) return;
        var p = context.painter2D; Vector2 c = r.center;
        // Radius reaches the corners; the rounded tile (overflow hidden) clips the pie to its own shape.
        float big = .5f * Mathf.Sqrt(r.width * r.width + r.height * r.height) + 1f, start = -90f, end = -90f + 360f * Fraction;
        p.fillColor = Shade;
        p.BeginPath();
        if (Fraction >= .999f) { p.MoveTo(new Vector2(r.xMin - 2, r.yMin - 2)); p.LineTo(new Vector2(r.xMax + 2, r.yMin - 2)); p.LineTo(new Vector2(r.xMax + 2, r.yMax + 2)); p.LineTo(new Vector2(r.xMin - 2, r.yMax + 2)); }
        else { p.MoveTo(c); p.Arc(c, big, Angle.Degrees(start), Angle.Degrees(end)); }
        p.ClosePath(); p.Fill();
        float ring = Mathf.Min(r.width, r.height) * .5f - 3f;
        p.strokeColor = Rim; p.lineWidth = 2.5f; p.lineCap = LineCap.Round;
        p.BeginPath(); p.Arc(c, ring, Angle.Degrees(start), Angle.Degrees(Mathf.Min(end, start + 359.5f))); p.Stroke();
    }
}

/// Filled five-point star (same outline as MenuGlyph.Star, which MenuIcon only strokes).
public sealed class HudStar : VisualElement
{
    public Color Fill, Outline;
    static readonly Vector2[] Points = { new Vector2(.5f,.13f), new Vector2(.61f,.37f), new Vector2(.88f,.4f), new Vector2(.68f,.58f), new Vector2(.73f,.86f),
        new Vector2(.5f,.73f), new Vector2(.27f,.86f), new Vector2(.32f,.58f), new Vector2(.12f,.4f), new Vector2(.39f,.37f) };
    public HudStar() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
    public void Set(Color fill, Color outline) { if (fill == Fill && outline == Outline) return; Fill = fill; Outline = outline; MarkDirtyRepaint(); }
    void Draw(MeshGenerationContext context)
    {
        var p = context.painter2D; float size = Mathf.Min(contentRect.width, contentRect.height); if (size <= 0) return;
        Vector2 origin = new Vector2((contentRect.width - size) * .5f, (contentRect.height - size) * .5f);
        p.BeginPath(); p.MoveTo(origin + Points[0] * size); for (int i = 1; i < Points.Length; i++) p.LineTo(origin + Points[i] * size); p.ClosePath();
        p.fillColor = Fill; p.Fill();
        p.strokeColor = Outline; p.lineWidth = Mathf.Max(1.2f, size * .06f); p.lineJoin = LineJoin.Round; p.Stroke();
    }
}

/// Screen-centre crosshair: four ticks and a dot with a dark outline so it reads on sky and asphalt alike.
public sealed class HudCrosshair : VisualElement
{
    public Color Ink, Outline;
    public HudCrosshair(Color ink, Color outline) { Ink = ink; Outline = outline; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
    void Draw(MeshGenerationContext context)
    {
        var p = context.painter2D; var c = contentRect.center; float half = Mathf.Min(contentRect.width, contentRect.height) * .5f; if (half <= 0) return;
        float gap = half * .32f, end = half * .92f;
        void Ticks(Color color, float width)
        {
            p.strokeColor = color; p.lineWidth = width; p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(c + new Vector2(0, -gap)); p.LineTo(c + new Vector2(0, -end)); p.Stroke();
            p.BeginPath(); p.MoveTo(c + new Vector2(0, gap)); p.LineTo(c + new Vector2(0, end)); p.Stroke();
            p.BeginPath(); p.MoveTo(c + new Vector2(-gap, 0)); p.LineTo(c + new Vector2(-end, 0)); p.Stroke();
            p.BeginPath(); p.MoveTo(c + new Vector2(gap, 0)); p.LineTo(c + new Vector2(end, 0)); p.Stroke();
        }
        Ticks(Outline, 4f); Ticks(Ink, 2f);
        p.fillColor = Outline; p.BeginPath(); p.Arc(c, 2.6f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
        p.fillColor = Ink; p.BeginPath(); p.Arc(c, 1.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
    }
}

/// Key hints. The synergy key is data (ForgeCatalog.SynergyKey). Flight (F) and Strength (E) are hardcoded in
/// SuperHeroController.Update, which is outside this change's scope; they are mirrored here in ONE place. Every other
/// equipped power is selected with its number key (its index in PowerUser.Powers, as SuperHeroController does) and
/// fired with LMB.
public static class HudBindings
{
    public const string FlightKey = "F", StrengthKey = "E", FireSelected = "LMB", BasicMelee = "E / RMB";
    /// Also hardcoded outside the HUD and mirrored here: Jump is the legacy "Jump" button (Space), backflip is Q in
    /// SuperHeroController.Update, the encounter hold is R in CrimeEncounter.Update.
    public const string JumpKey = "SPACE", BackflipKey = "Q", InteractKey = "R";
    /// The melee key the player actually has: Super Strength's E when equipped, else the basic melee on E / RMB.
    public static string MeleeKey(PowerUser user) => user != null && user.Strength != null && user.IsEquipped(user.Strength.Definition) ? StrengthKey : BasicMelee;
    public static bool FlightEquipped(PowerUser user) => user != null && user.Flight != null && user.IsEquipped(user.Flight.Definition);
    public static string PowerKey(PowerUser user, PowerRuntime power)
    {
        if (power.Definition.Effect != null && power.Definition.Effect.IsFlight) return FlightKey;
        if (power == user.Strength) return StrengthKey;
        int index = user.Powers.IndexOf(power);
        return index >= 0 && index < 9 ? (index + 1).ToString() : FireSelected;
    }
    public static string KeyName(KeyCode key)
    {
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
        switch (key)
        {
            case KeyCode.Mouse0: return "LMB";
            case KeyCode.Mouse1: return "RMB";
            case KeyCode.Mouse2: return "MMB";
            case KeyCode.LeftShift: case KeyCode.RightShift: return "SHIFT";
            case KeyCode.Space: return "SPACE";
            case KeyCode.None: return "";
            default: return key.ToString().ToUpperInvariant();
        }
    }
}

/// Arrow pointing UP in its own box (rotate the element to aim it): a filled chevron-headed arrow with a dark outline.
public sealed class HudArrow : VisualElement
{
    public Color Fill, Outline;
    public HudArrow(Color fill, Color outline) { Fill = fill; Outline = outline; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
    public void SetFill(Color fill) { if (fill == Fill) return; Fill = fill; MarkDirtyRepaint(); }
    void Draw(MeshGenerationContext context)
    {
        var p = context.painter2D; float w = contentRect.width, h = contentRect.height; if (w <= 0 || h <= 0) return;
        Vector2 P(float x, float y) => new Vector2(x * w, y * h);
        p.BeginPath(); p.MoveTo(P(.5f, .06f)); p.LineTo(P(.94f, .62f)); p.LineTo(P(.64f, .62f)); p.LineTo(P(.64f, .94f)); p.LineTo(P(.36f, .94f)); p.LineTo(P(.36f, .62f)); p.LineTo(P(.06f, .62f)); p.ClosePath();
        p.fillColor = Fill; p.Fill(); p.strokeColor = Outline; p.lineWidth = Mathf.Max(1.5f, w * .07f); p.lineJoin = LineJoin.Round; p.Stroke();
    }
}

/// Waypoint pin: a diamond with a hollow centre, drawn in the mode accent with a dark outline.
public sealed class HudDiamond : VisualElement
{
    public Color Fill, Outline;
    public HudDiamond(Color fill, Color outline) { Fill = fill; Outline = outline; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
    public void SetFill(Color fill) { if (fill == Fill) return; Fill = fill; MarkDirtyRepaint(); }
    void Draw(MeshGenerationContext context)
    {
        var p = context.painter2D; var c = contentRect.center; float r = Mathf.Min(contentRect.width, contentRect.height) * .5f - 2f; if (r <= 0) return;
        void Diamond(float radius) { p.BeginPath(); p.MoveTo(c + new Vector2(0, -radius)); p.LineTo(c + new Vector2(radius, 0)); p.LineTo(c + new Vector2(0, radius)); p.LineTo(c + new Vector2(-radius, 0)); p.ClosePath(); }
        Diamond(r); p.fillColor = Outline; p.Fill();
        Diamond(r - 2.5f); p.fillColor = Fill; p.Fill();
        Diamond(r * .38f); p.fillColor = Outline; p.Fill();
    }
}
