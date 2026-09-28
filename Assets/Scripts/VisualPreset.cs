using UnityEngine;
using UnityEngine.Rendering;

/// City lighting look (Visual polish, audit items 1 + 2). OPTIONAL and OFF by default: without Resources/VisualPreset, or
/// with Enabled false, the city lights exactly as before (shadowless white sun at 1.2, scene skybox ambient, haze skirt).
/// When on it applies, for the city session only (VisualPresetApplier restores Quality/RenderSettings on teardown):
///   - the sun casts soft shadows (a Light added from code defaults to LightShadows.None, so the city never had any),
///     warm cream colour, lower intensity, shadow distance / cascades capped for performance (detail pieces already
///     never cast: CityArt.FlushBatches);
///   - gradient (trilight) ambient instead of the default skybox's bright blue fill that bleaches up-facing surfaces;
///   - a procedural skybox whose ground half is the fog Haze, so the haze-skirt walls (visible as flat slabs in the sky
///     from the park and quay) can be hidden;
///   - melee / slam attack telegraphs drawn as rings instead of an opaque disc (AttackTelegraph);
///   - a street-level facade pass on urban-height buildings (CityArt.StreetLevel), read when the city is generated;
///   - per-role enemy silhouettes and accents (HumanoidPresentation), e.g. no hero-cyan on a hostile Gunner;
///   - a comet tail on projectile powers (FireBlastEffect);
///   - structure recipe overrides using shared generated low-poly shapes (faceted canopy / pine trees), at generation.
/// Colours come from the shared CityPalette. Tune in Resources/VisualPreset (created by Overpowered > Visual > menus).
[CreateAssetMenu(menuName = "Overpowered/Visual preset")]
public sealed class VisualPreset : ScriptableObject
{
    public bool Enabled;
    [Header("Sun")]
    public CityColor SunColor = CityColor.Cream;
    [Range(0f, 1f)] public float SunWhiteBlend = .55f;
    public float SunIntensity = 1.05f;
    public Vector3 SunEuler = new Vector3(50f, -30f, 0f);
    public LightShadows Shadows = LightShadows.Soft;
    [Range(0f, 1f)] public float ShadowStrength = .55f;
    public float ShadowDistance = 80f;
    [Tooltip("1, 2 or 4.")] public int ShadowCascades = 2;
    [Header("Ambient (trilight)")]
    public CityColor AmbientSky = CityColor.Slate, AmbientEquator = CityColor.UiPurple, AmbientGround = CityColor.Metal;
    [Range(0f, 2f)] public float AmbientSkyScale = 1.25f, AmbientEquatorScale = 1.2f, AmbientGroundScale = 1.1f;
    [Header("Sky / horizon")]
    public bool ProceduralSky = true;
    public CityColor SkyTint = CityColor.Haze, SkyGround = CityColor.Haze;
    public float SkyExposure = 1.15f, SkyAtmosphere = .75f, SunSize = .025f;
    [Tooltip("Hide the haze-skirt walls (only when ProceduralSky paints the lower sky in the fog colour).")] public bool HideHazeSkirt = true;
    [Header("Buildings (read at city generation)")]
    [Tooltip("Street-level facade pass: shop glazing on every face, base plinth, corner pilasters, projecting cornice.")] public bool FacadeDetail = true;
    [Tooltip("Only buildings at least this tall get it (keeps low residential houses as they are).")] public float FacadeMinHeight = 10f;
    [Tooltip("Distant mainland as a continuous coastline of narrow stepped towers instead of isolated wide slabs.")] public bool SkylineBackdrop = true;
    [Tooltip("Structure recipes replaced by name at city generation (e.g. faceted canopy trees instead of stacked cubes). Pieces may use the shared generated PieceShape meshes.")]
    public StructureRecipe[] RecipeOverrides = DefaultRecipeOverrides();
    static RecipePiece Piece(PieceShape shape, PrimitiveType type, float x, float y, float z, float sx, float sy, float sz, CityColor c, bool solid = false, float yaw = 0)
        => new RecipePiece { Shape = shape, Type = type, Position = new Vector3(x, y, z), Size = new Vector3(sx, sy, sz), Euler = new Vector3(0, yaw, 0), Color = c, Solid = solid };
    /// Same trunks (and trunk colliders) as the shipped recipes; canopies become faceted low-poly balls / cones.
    static StructureRecipe[] DefaultRecipeOverrides()
    {
        const PieceShape P = PieceShape.Primitive, Ball = PieceShape.Canopy, Cone = PieceShape.Cone;
        const PrimitiveType Cyl = PrimitiveType.Cylinder, Box = PrimitiveType.Cube;
        return new[]
        {
            new StructureRecipe { Name = "Street tree", Pieces = new[] { Piece(P, Cyl, 0, 1.2f, 0, .32f, 2.4f, .32f, CityColor.Wood, true), Piece(Ball, Box, 0, 3.5f, 0, 2.8f, 2.4f, 2.8f, CityColor.Leaf, yaw: 30), Piece(Ball, Box, .35f, 4.6f, .2f, 1.8f, 1.6f, 1.8f, CityColor.Lawn, yaw: 70) } },
            new StructureRecipe { Name = "Park tree", Pieces = new[] { Piece(P, Cyl, 0, 1.6f, 0, .5f, 3.2f, .5f, CityColor.Wood, true), Piece(Ball, Box, 0, 4.6f, 0, 4.6f, 3.8f, 4.6f, CityColor.Leaf, yaw: 15), Piece(Ball, Box, .7f, 6.1f, .3f, 3.2f, 2.8f, 3.2f, CityColor.Lawn, yaw: 50), Piece(Ball, Box, -1.1f, 5.6f, -.7f, 2.6f, 2.2f, 2.6f, CityColor.Leaf, yaw: 80) } },
            new StructureRecipe { Name = "Pine tree", Pieces = new[] { Piece(P, Cyl, 0, 1.1f, 0, .42f, 2.2f, .42f, CityColor.Wood, true), Piece(Cone, Box, 0, 2.9f, 0, 3.8f, 2.6f, 3.8f, CityColor.Leaf, yaw: 10), Piece(Cone, Box, 0, 4.3f, 0, 2.8f, 2.3f, 2.8f, CityColor.Leaf, yaw: 32), Piece(Cone, Box, 0, 5.6f, 0, 1.8f, 2f, 1.8f, CityColor.Leaf, yaw: 55) } },
        };
    }
    [Header("Combat readability")]
    [Tooltip("Melee / slam telegraphs as a boundary ring + closing timing ring instead of a solid disc covering the ground.")] public bool RingTelegraphs = true;
    [Tooltip("Projectile powers (Fire Blast) trail a three-sphere comet tail in their palette colour.")] public bool ProjectileTail = true;

    [Header("Enemy roles (visual root only: collider, NavMeshAgent and physics root unchanged)")]
    public EnemyLook[] EnemyLooks = new EnemyLook[0];
    [System.Serializable] public sealed class EnemyLook
    {
        public EnemyArchetype Archetype;
        [Tooltip("Multiplies the archetype's uniform VisualScale on the visual root (x width, y height, z depth).")] public Vector3 Silhouette = Vector3.one;
        public bool OverrideAccent; public CityColor Accent = CityColor.Metal;
    }
    /// The active preset's look for `archetype`, or null (no preset / not listed).
    public static EnemyLook LookFor(EnemyArchetype archetype)
    {
        if (archetype == null) return null; var p = Current; if (!Active(p) || p.EnemyLooks == null) return null;
        foreach (var l in p.EnemyLooks) if (l != null && l.Archetype == archetype) return l;
        return null;
    }
    /// Verification / capture override for this process: null = the asset's Enabled flag.
    public static bool? ForceEnabled;
    public static VisualPreset Current
    {
        get
        {
            var p = Resources.Load<VisualPreset>("VisualPreset");
            if (ForceEnabled == true && p == null) { p = CreateInstance<VisualPreset>(); p.hideFlags = HideFlags.HideAndDontSave; }
            return p;
        }
    }
    public static bool Active(VisualPreset p) => p != null && (ForceEnabled ?? p.Enabled);
    /// Called once by PrototypeBootstrap after the sun and camera exist. No-op when inactive.
    public static void ApplyTo(Light sun, CityDistrict city)
    {
        var p = Current; if (!Active(p) || sun == null) return;
        sun.gameObject.AddComponent<VisualPresetApplier>().Apply(p, sun, city);
    }
}

/// Applies a VisualPreset and puts every global it touched back when the city session ends.
public sealed class VisualPresetApplier : MonoBehaviour
{
    float shadowDistance; int cascades; AmbientMode ambientMode; Color sky, equator, ground; Material skybox; bool saved;
    Material created; readonly System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
    public VisualPreset Preset { get; private set; }
    public void Apply(VisualPreset p, Light sun, CityDistrict city)
    {
        Preset = p; var palette = CityMaterials.Current != null ? CityMaterials.Current.Palette : Resources.Load<CityPalette>("CityPalette");
        Color C(CityColor c) => palette != null ? palette.Colors[(int)c] : Color.gray;
        shadowDistance = QualitySettings.shadowDistance; cascades = QualitySettings.shadowCascades;
        ambientMode = RenderSettings.ambientMode; sky = RenderSettings.ambientSkyColor; equator = RenderSettings.ambientEquatorColor; ground = RenderSettings.ambientGroundColor; skybox = RenderSettings.skybox; saved = true;

        sun.color = Color.Lerp(C(p.SunColor), Color.white, p.SunWhiteBlend); sun.intensity = p.SunIntensity; sun.transform.rotation = Quaternion.Euler(p.SunEuler);
        sun.shadows = p.Shadows; sun.shadowStrength = p.ShadowStrength;
        QualitySettings.shadowDistance = p.ShadowDistance; QualitySettings.shadowCascades = p.ShadowCascades;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = C(p.AmbientSky) * p.AmbientSkyScale;
        RenderSettings.ambientEquatorColor = C(p.AmbientEquator) * p.AmbientEquatorScale;
        RenderSettings.ambientGroundColor = C(p.AmbientGround) * p.AmbientGroundScale;

        if (p.ProceduralSky)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader != null)
            {
                created = new Material(shader) { name = "Visual preset sky" };
                created.SetColor("_SkyTint", C(p.SkyTint)); created.SetColor("_GroundColor", C(p.SkyGround));
                created.SetFloat("_Exposure", p.SkyExposure); created.SetFloat("_AtmosphereThickness", p.SkyAtmosphere); created.SetFloat("_SunSize", p.SunSize);
                RenderSettings.skybox = created; RenderSettings.sun = sun;
                if (p.HideHazeSkirt && city != null && city.Art != null && city.Art.BackdropRoot != null)
                    foreach (var r in city.Art.BackdropRoot.GetComponentsInChildren<Renderer>())
                        if (r.gameObject.name == CityArt.HazeSkirtName && r.enabled) { r.enabled = false; hidden.Add(r); }
            }
        }
    }
    void OnDestroy()
    {
        if (!saved) return;
        QualitySettings.shadowDistance = shadowDistance; QualitySettings.shadowCascades = cascades;
        RenderSettings.ambientMode = ambientMode; RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
        RenderSettings.skybox = skybox;
        foreach (var r in hidden) if (r != null) r.enabled = true;
        if (created != null) Destroy(created);
    }
}
