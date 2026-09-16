using UnityEngine;

public static class PrototypeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BuildArena()
    {
        if (Object.FindFirstObjectByType<SuperHeroController>() != null) return;
        Physics.gravity = Vector3.down * PrototypeTuning.Gravity;
        MakeGround();
        GameObject hero = new GameObject("Overpowered Hero");
        hero.transform.position = new Vector3(0, 1.1f, -10f);
        CharacterController cc = hero.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = .38f; cc.center = new Vector3(0, .9f, 0);
        SuperHeroController controller = hero.AddComponent<SuperHeroController>();
        BuildHeroVisuals(hero, controller);
        GameObject camera = new GameObject("Player Camera"); camera.tag = "MainCamera"; camera.AddComponent<Camera>(); camera.AddComponent<AudioListener>(); camera.AddComponent<ThirdPersonCamera>().target = hero.transform;
        BuildProps();
        new GameObject("Prototype HUD").AddComponent<PrototypeHUD>();
        new GameObject("Verification Harness").AddComponent<VerificationHarness>();
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
    float yaw = 0f, pitch = 16f;
    void LateUpdate()
    {
        if (target == null) return;
        yaw += Input.GetAxis("Mouse X") * 3.5f; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3.5f, -10, 55);
        transform.position = target.position + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 2.6f, -6.5f);
        transform.LookAt(target.position + Vector3.up * 1.05f);
    }
}
