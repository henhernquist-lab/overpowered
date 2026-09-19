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
        BuildHeroVisuals(hero, controller);
        Camera camera = GameCamera.Ensure(hero.scene,true);
        var follow=camera.GetComponent<ThirdPersonCamera>() ?? camera.gameObject.AddComponent<ThirdPersonCamera>();
        follow.target=hero.transform;camera.fieldOfView=tuning.Camera.FieldOfView;
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
        CityColor blue = CityColor.Blue;
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
                new Vector3(0f, -.62f, 0f), Vector3.one * .28f, CityColor.Amber);
            if (side > 0) punchShoulder = shoulder;
        }
        var tuning = Resources.Load<ProceduralAnimationTuning>("ProceduralAnimationTuning");
        hero.AddComponent<ProceduralHeroAnimation>().Initialize(controller, visuals, punchShoulder, tuning);
    }
    static void VisualPrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, CityColor color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Collider collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Object.Destroy(collider);
        part.GetComponent<Renderer>().sharedMaterial = CityMaterials.Get(color);
    }
}

public sealed class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    float yaw, pitch;
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
}
