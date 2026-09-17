using UnityEngine;

public static class PrototypeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BuildArena()
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
        BuildHeroVisuals(hero, controller);
        GameObject camera = new GameObject("Player Camera"); camera.tag = "MainCamera"; camera.AddComponent<Camera>(); camera.AddComponent<AudioListener>(); camera.AddComponent<ThirdPersonCamera>().target = hero.transform;
        camera.GetComponent<Camera>().fieldOfView = tuning.Camera.FieldOfView;
        new GameObject("World Session").AddComponent<WorldSession>().Initialize(tuning, city, controller);
        var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight = new Color(.45f,.5f,.6f);
        if (!Application.isBatchMode) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        new GameObject("Prototype HUD").AddComponent<PrototypeHUD>();
    }
    static void BuildHeroVisuals(GameObject hero, SuperHeroController controller)
    {
        Transform visuals = new GameObject("Hero Visuals (presentation only)").transform;
        visuals.SetParent(hero.transform, false);
        Color blue = new Color(.1f, .32f, .9f);
        VisualPrimitive("Capsule Body", PrimitiveType.Capsule, visuals,
            new Vector3(0f, 1f, 0f), new Vector3(.75f, 1f, .75f), blue);
        Transform punchShoulder = null;
        for (int side = -1; side <= 1; side += 2)
        {
            Transform shoulder = new GameObject(side > 0 ? "Punch Shoulder" : "Left Shoulder").transform;
            shoulder.SetParent(visuals, false);
            shoulder.localPosition = new Vector3(side * .48f, 1.45f, 0f);
            VisualPrimitive("Arm", PrimitiveType.Capsule, shoulder,
                new Vector3(0f, -.3f, 0f), new Vector3(.22f, .32f, .22f), blue);
            VisualPrimitive("Fist", PrimitiveType.Cube, shoulder,
                new Vector3(0f, -.62f, 0f), Vector3.one * .28f, new Color(1f, .65f, .12f));
            if (side > 0) punchShoulder = shoulder;
        }
        var tuning = Resources.Load<ProceduralAnimationTuning>("ProceduralAnimationTuning");
        hero.AddComponent<ProceduralHeroAnimation>().Initialize(controller, visuals, punchShoulder, tuning);
    }
    static void VisualPrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Collider collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Object.Destroy(collider);
        part.GetComponent<Renderer>().material.color = color;
    }
    static void MakeGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Arena Floor"; ground.transform.position = new Vector3(0, -.5f, 0); ground.transform.localScale = new Vector3(36, 1, 36); ground.GetComponent<Renderer>().material.color = new Color(.12f, .14f, .18f);
        for (int i = -2; i <= 2; i++) { GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube); stripe.transform.position = new Vector3(i * 5f, .02f, 3); stripe.transform.localScale = new Vector3(.08f, .03f, 25); stripe.GetComponent<Renderer>().material.color = new Color(.95f, .62f, .08f); }
    }
    static void BuildProps()
    {
        for (int x = -3; x <= 3; x += 2)
        for (int z = 0; z <= 8; z += 3)
        {
            bool barrel = (x + z) % 3 == 0;
            GameObject prop = GameObject.CreatePrimitive(barrel ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            prop.name = barrel ? "Physics Barrel" : "Breakable Crate"; prop.transform.position = new Vector3(x, barrel ? .75f : .55f, z);
            prop.transform.localScale = barrel ? new Vector3(.85f, 1.5f, .85f) : Vector3.one * 1.1f;
            prop.GetComponent<Renderer>().material.color = barrel ? new Color(.8f, .2f, .06f) : new Color(.47f, .25f, .08f);
            Rigidbody rb = prop.AddComponent<Rigidbody>(); rb.mass = barrel ? 2.5f : 3.5f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            prop.AddComponent<BreakableProp>();
        }
    }
}

public sealed class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    float yaw, pitch;
    void Start() { pitch = WorldSession.Instance.Tuning.Camera.Pitch; }
    void LateUpdate()
    {
        if (target == null) return;
        var c = WorldSession.Instance.Tuning.Camera;
        if (!WorldSession.Instance.MenuOpen) { yaw += Input.GetAxis("Mouse X") * c.Sensitivity; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * c.Sensitivity, c.MinimumPitch, c.MaximumPitch); }
        Vector3 focus = target.position + Vector3.up * c.LookHeight;
        Vector3 desired = target.position + Quaternion.Euler(pitch, yaw, 0) * c.Offset;
        Vector3 ray = desired - focus; float distance = ray.magnitude;
        foreach (var hit in Physics.SphereCastAll(focus, c.CollisionRadius, ray.normalized, distance))
            if (hit.transform.root != target && !hit.collider.isTrigger) distance = Mathf.Min(distance, Mathf.Max(c.CollisionInset, hit.distance - c.CollisionInset));
        transform.position = focus + ray.normalized * distance;
        transform.LookAt(focus);
    }
}
