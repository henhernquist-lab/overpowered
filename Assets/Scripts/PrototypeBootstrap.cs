using UnityEngine;

public static class PrototypeBootstrap
{
    public static void BuildCity()
    {
        if (Object.FindAnyObjectByType<SuperHeroController>() != null) return;
        var tuning = Resources.Load<GameTuning>("GameTuning");
        var layout = Resources.Load<CityLayout>("CityLayout");
        if (tuning == null || layout == null) throw new System.InvalidOperationException("Generate project data via Overpowered > Create missing data assets.");
        Physics.gravity = Vector3.down * tuning.Movement.Gravity;
        var city = new GameObject("City District").AddComponent<CityDistrict>(); city.Build(tuning, layout);
        GameObject hero = new GameObject("Overpowered Hero");
        hero.transform.position = city.Spawn + Vector3.up * tuning.Movement.Height;
        CharacterController cc = hero.AddComponent<CharacterController>(); cc.height = tuning.Movement.Height; cc.radius = tuning.Movement.Radius; cc.center = Vector3.up * cc.height * .5f;
        SuperHeroController controller = hero.AddComponent<SuperHeroController>();
        Camera camera = GameCamera.Ensure(hero.scene,true);
        var follow=camera.GetComponent<ThirdPersonCamera>() ?? camera.gameObject.AddComponent<ThirdPersonCamera>();
        follow.target=hero.transform;camera.fieldOfView=tuning.Camera.FieldOfView;
        new GameObject("World Session").AddComponent<WorldSession>().Initialize(tuning, city, controller);
        new GameObject("Feel Director").AddComponent<FeelDirector>().Initialize(WorldSession.Instance);
        HumanoidPresentation.Create(hero,cc.height,controller,null,WorldSession.Instance.Progression.SelectedHero);
        var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight = new Color(.45f,.5f,.6f);
        if (!Application.isBatchMode) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        new GameObject("Prototype HUD").AddComponent<PrototypeHUD>(); // F3 debug panel, pause + Tab menus, defeated notice
        new GameObject("Game HUD").AddComponent<GameHud>();
    }
}

public sealed class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    float yaw, pitch;
    /// The follow camera of the running session (Feel sends impulses / FOV kicks here).
    public static ThirdPersonCamera Active { get; private set; }
    void OnEnable() { Active = this; }
    void OnDisable() { if (Active == this) Active = null; }
    void Start() { if(WorldSession.Instance!=null&&WorldSession.Instance.Tuning!=null) pitch = WorldSession.Instance.Tuning.Camera.Pitch; }
    void LateUpdate()
    {
        var world=WorldSession.Instance;
        if (target == null || world==null || world.Tuning==null) return;
        var c = world.Tuning.Camera;
        if (!world.MenuOpen) { yaw += Input.GetAxis("Mouse X") * c.Sensitivity; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * c.Sensitivity, c.MinimumPitch, c.MaximumPitch); }
        Vector3 focus = target.position + Vector3.up * c.LookHeight;
        Vector3 desired = target.position + Quaternion.Euler(pitch, yaw, 0) * c.Offset;
        Vector3 ray = desired - focus; float distance = ray.magnitude;
        foreach (var hit in Physics.SphereCastAll(focus, c.CollisionRadius, ray.normalized, distance))
            if (hit.transform.root != target && !hit.collider.isTrigger) distance = Mathf.Min(distance, Mathf.Max(c.CollisionInset, hit.distance - c.CollisionInset));
        transform.position = focus + ray.normalized * distance;
        transform.LookAt(focus);
    }

    // ---- Feel: additive camera impulse + FOV kick (values from GameTuning.Feel via FeelDirector), in UNSCALED time so
    // they animate through a hit pause. They are applied ONLY while this camera renders (OnPreCull -> OnPostRender) and
    // undone right after, so the follow pose above, the aim ray (ViewportPointToRay at screen centre) and the hero's
    // physics root never see them, and nothing can accumulate into drift.
    float impulseAt = -100f, impulseAmplitude, impulseSeconds, impulseFrequency, kickAt = -100f, kickDegrees, kickSeconds;
    Vector3 impulseDirection, restPosition; float restFov; bool applied; Camera view;
    public int ImpulsesAccepted { get; private set; }
    /// Offset added to the rendered camera position at unscaled time `t` (zero outside the impulse window).
    public Vector3 OffsetAt(float t)
    {
        float age = t - impulseAt; if (age < 0f || age >= impulseSeconds) return Vector3.zero;
        float k = 1f - age / impulseSeconds;
        return impulseDirection * (impulseAmplitude * k * k * Mathf.Cos(2f * Mathf.PI * impulseFrequency * age));
    }
    /// Degrees added to the rendered field of view at unscaled time `t` (before de-stacking with a synergy kick).
    public float FovKickAt(float t)
    {
        float age = t - kickAt; if (age < 0f || age >= kickSeconds) return 0f;
        float k = 1f - age / kickSeconds; return kickDegrees * k * k;
    }
    public Vector3 CurrentOffset => OffsetAt(Time.unscaledTime);
    public float CurrentFovKick => FovKickAt(Time.unscaledTime);
    public float ImpulseEndsAt => impulseAt + impulseSeconds;
    public float FovKickEndsAt => kickAt + kickSeconds;
    /// A new impulse replaces the running one only if it is at least as strong as what is left of it.
    public void Impulse(Vector3 direction, float amplitude, float seconds, float frequency)
    {
        if (amplitude <= 0f || seconds <= 0f) return;
        float age = Time.unscaledTime - impulseAt, left = age < impulseSeconds ? impulseAmplitude * (1f - age / impulseSeconds) * (1f - age / impulseSeconds) : 0f;
        if (amplitude < left) return;
        impulseDirection = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.down;
        impulseAmplitude = amplitude; impulseSeconds = seconds; impulseFrequency = frequency; impulseAt = Time.unscaledTime; ImpulsesAccepted++;
    }
    public void FovKick(float degrees, float seconds)
    {
        if (degrees <= 0f || seconds <= 0f || degrees < FovKickAt(Time.unscaledTime)) return;
        kickDegrees = degrees; kickSeconds = seconds; kickAt = Time.unscaledTime;
    }
    void OnPreCull()
    {
        if (applied) return;
        Vector3 offset = CurrentOffset; float kick = CurrentFovKick;
        if (offset == Vector3.zero && kick <= 0f) return;
        if (view == null) view = GetComponent<Camera>();
        restPosition = transform.position; restFov = view.fieldOfView; applied = true;
        transform.position = restPosition + offset;
        // A synergy may already be kicking this camera's FOV (SynergyRunner writes it directly): the two do not stack.
        var world = WorldSession.Instance; float baseFov = world != null && world.Tuning != null ? world.Tuning.Camera.FieldOfView : restFov;
        view.fieldOfView = restFov + Mathf.Max(0f, kick - Mathf.Max(0f, restFov - baseFov));
    }
    void OnPostRender()
    {
        if (!applied) return;
        transform.position = restPosition; view.fieldOfView = restFov; applied = false;
    }
}
