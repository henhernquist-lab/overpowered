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
        var sessionWatch = System.Diagnostics.Stopwatch.StartNew();
        GameObject hero = new GameObject("Overpowered Hero");
        hero.transform.position = city.Spawn + Vector3.up * tuning.Movement.Height;
        CharacterController cc = hero.AddComponent<CharacterController>(); cc.height = tuning.Movement.Height; cc.radius = tuning.Movement.Radius; cc.center = Vector3.up * cc.height * .5f;
        SuperHeroController controller = hero.AddComponent<SuperHeroController>();
        Camera camera = GameCamera.Ensure(hero.scene,true);
        var follow=camera.GetComponent<ThirdPersonCamera>() ?? camera.gameObject.AddComponent<ThirdPersonCamera>();
        follow.target=hero.transform;camera.fieldOfView=tuning.Camera.FieldOfView;
        city.Art.ApplyRendering(camera); // far clip, per-layer cull distances, fog (CityArtSettings.Rendering)
        new GameObject("World Session").AddComponent<WorldSession>().Initialize(tuning, city, controller);
        new GameObject("Feel Director").AddComponent<FeelDirector>().Initialize(WorldSession.Instance);
        new GameObject("NPC LOD").AddComponent<NpcLod>().Initialize(WorldSession.Instance, layout.NpcLod, city.Art.Rendering.ActorLayer);
        HumanoidPresentation.Create(hero,cc.height,controller,null,WorldSession.Instance.Progression.SelectedHero);
        var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight = new Color(.45f,.5f,.6f);
        VisualPreset.ApplyTo(sun, city); // OFF unless Resources/VisualPreset is enabled: shadows, warm sun, trilight ambient, haze sky
        if (!Application.isBatchMode) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        new GameObject("Prototype HUD").AddComponent<PrototypeHUD>(); // F3 debug panel, pause + Tab menus, defeated notice
        new GameObject("Game HUD").AddComponent<GameHud>();
        city.Timings.Add(new System.Collections.Generic.KeyValuePair<string,double>("session: hero, camera, WorldSession + NPC spawn, HUD", sessionWatch.Elapsed.TotalMilliseconds));
    }
}

[DefaultExecutionOrder(100)]
public sealed class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public bool FirstPerson { get; private set; }
    public float FlightFov { get; private set; }
    float yaw, pitch, originalNearClip, kick, kickUntil, kickDuration;
    /// The follow camera of the running session (Feel sends impulses / FOV kicks here).
    public static ThirdPersonCamera Active { get; private set; }
    Camera view;
    Renderer[] body;
    UnityEngine.Rendering.ShadowCastingMode[] shadows;
    void Start()
    {
        var world=WorldSession.Instance;
        if(world==null||target==null)return;
        view=GetComponent<Camera>();originalNearClip=view.nearClipPlane;
        pitch=world.Tuning.Camera.Pitch;
        body=target.GetComponent<HumanoidPresentation>().VisualRoot.GetComponentsInChildren<Renderer>(true);
        shadows=new UnityEngine.Rendering.ShadowCastingMode[body.Length];
        for(int i=0;i<body.Length;i++)shadows[i]=body[i].shadowCastingMode;
        ApplyView(world.Progression.Data.FirstPerson);
    }
    public bool ToggleView()
    {
        var world=WorldSession.Instance;
        if(view==null||world==null||world.MenuOpen||world.PlayerDead||Time.timeScale==0||world.Mode!=null&&world.Mode.Ended)return false;
        ApplyView(!FirstPerson);world.Progression.SetFirstPerson(FirstPerson);return true;
    }
    void ApplyView(bool first)
    {
        FirstPerson=first;
        var c=WorldSession.Instance.Tuning.Camera;
        // Match the last rendered look when entering, rather than the orbit's implicit focus angle.
        if(first){yaw=transform.eulerAngles.y;pitch=Mathf.DeltaAngle(0,transform.eulerAngles.x);}
        else pitch=Mathf.Clamp(pitch,c.MinimumPitch,c.MaximumPitch);
        if(!first)FlightFov=0;
        view.nearClipPlane=first?c.FirstPersonNearClip:originalNearClip;
        for(int i=0;i<body.Length;i++)if(body[i]!=null)
            body[i].shadowCastingMode=first?UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:shadows[i];
    }
    /// Degrees the synergy kick (KickFov) currently adds; the Feel FOV kick de-stacks against this, not against flight FOV.
    public float SynergyFov=>kick*Mathf.Clamp01((kickUntil-Time.unscaledTime)/Mathf.Max(.01f,kickDuration));
    public void KickFov(float amount,float seconds)
    {kick=amount;kickDuration=Mathf.Max(.01f,seconds);kickUntil=Time.unscaledTime+kickDuration;}
    // Also useful for cinematics/tests; does not move the physics root or mutate the saved view preference.
    public void SetLook(float heading,float elevation){yaw=heading;pitch=elevation;}
    void LateUpdate()
    {
        var world=WorldSession.Instance;
        if (target == null || world==null || world.Tuning==null) return;
        var c = world.Tuning.Camera;
        if (!world.MenuOpen&&!world.PlayerDead&&Time.timeScale>0)
        {
            if(Input.GetKeyDown(c.ViewToggle))ToggleView();
            yaw += Input.GetAxis("Mouse X") * c.Sensitivity;
            pitch -= Input.GetAxis("Mouse Y") * c.Sensitivity;
        }
        pitch=Mathf.Clamp(pitch,FirstPerson?c.FirstPersonMinimumPitch:c.MinimumPitch,FirstPerson?c.FirstPersonMaximumPitch:c.MaximumPitch);
        if(FirstPerson)
        {
            // Stable capsule-relative eye: flight/bob/squash bones never drag the camera through geometry.
            var cc=target.GetComponent<CharacterController>();
            Vector3 center=target.TransformPoint(cc.center), eye=target.position+Vector3.up*c.EyeHeight;
            Vector3 up=eye-center;float height=up.magnitude;
            // Contain the near-plane corners, including wide aspect ratios and additive FOV feedback.
            float nearHalf=c.FirstPersonNearClip*Mathf.Tan((c.FieldOfView+c.FlightFovIncrease+Mathf.Abs(kick))*.5f*Mathf.Deg2Rad);
            float radius=Mathf.Max(c.EyeCollisionRadius,nearHalf*Mathf.Sqrt(1+view.aspect*view.aspect));
            foreach(var hit in Physics.SphereCastAll(center,radius,up.normalized,height,~0,QueryTriggerInteraction.Ignore))
                if(hit.transform.root!=target)height=Mathf.Min(height,Mathf.Max(0,hit.distance-c.CollisionInset));
            transform.SetPositionAndRotation(center+up.normalized*height,Quaternion.Euler(pitch,yaw,0));
            var state=world.Hero.PresentationState;
            Vector3 velocity=target.TransformDirection(state.LocalVelocity);
            float forward=Vector3.Dot(velocity,Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized);
            float desiredFov=state.Flying?c.FlightFovIncrease*Mathf.Clamp01(forward/Mathf.Max(.01f,c.FlightFovSpeed)):0;
            FlightFov=Mathf.Lerp(FlightFov,desiredFov,1-Mathf.Exp(-c.FlightFovResponse*Time.deltaTime));
            view.fieldOfView=c.FieldOfView+FlightFov+kick*Mathf.Clamp01((kickUntil-Time.unscaledTime)/Mathf.Max(.01f,kickDuration));
            return;
        }
        view.fieldOfView=c.FieldOfView+kick*Mathf.Clamp01((kickUntil-Time.unscaledTime)/Mathf.Max(.01f,kickDuration));
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
    Vector3 impulseDirection, restPosition; float restFov; bool applied;
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
        Vector3 offset = CurrentOffset; float feelKick = CurrentFovKick;
        if (offset == Vector3.zero && feelKick <= 0f) return;
        if (view == null) view = GetComponent<Camera>();
        restPosition = transform.position; restFov = view.fieldOfView; applied = true;
        transform.position = restPosition + offset;
        // A synergy may already be kicking this camera's FOV (KickFov, applied in LateUpdate): the two do not stack.
        // Flight FOV is a separate, sustained widening and is not de-stacked against.
        view.fieldOfView = restFov + Mathf.Max(0f, feelKick - SynergyFov);
    }
    void OnPostRender()
    {
        if (!applied) return;
        transform.position = restPosition; view.fieldOfView = restFov; applied = false;
    }
    void OnDisable()
    {
        if (Active == this) Active = null;
        if(body!=null)for(int i=0;i<body.Length;i++)if(body[i]!=null)body[i].shadowCastingMode=shadows[i];
        if(view!=null){view.nearClipPlane=originalNearClip;if(WorldSession.Instance!=null)view.fieldOfView=WorldSession.Instance.Tuning.Camera.FieldOfView;}
    }
    void OnEnable(){Active=this;if(view!=null&&WorldSession.Instance!=null)ApplyView(FirstPerson);}
}
