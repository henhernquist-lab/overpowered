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
        HumanoidPresentation.Create(hero,cc.height,controller,null,WorldSession.Instance.Progression.SelectedHero);
        var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45,-35,0);
        RenderSettings.ambientLight = new Color(.45f,.5f,.6f);
        if (!Application.isBatchMode) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        new GameObject("Prototype HUD").AddComponent<PrototypeHUD>();
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
